using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using MelonLoader.NativeUtils;

namespace Descent.Recon
{
    /// <summary>
    /// Release IL2CPP logs an exception as a bare message, and Unity's "full" stack trace is
    /// empty in this build. Every IL2CPP exception is a C++ throw of an
    /// <c>Il2CppExceptionWrapper</c>, and every C++ throw goes through the runtime's exported
    /// <c>_CxxThrowException</c>. This detours it, captures the native return addresses,
    /// and names the GameAssembly frames through <c>UserData/Descent/methods.tsv</c>
    /// (RVA → method, generated from dump/script.json). The first distinct traces go to the
    /// transcript with a count; repeats only bump the count. Everything here is best effort
    /// and guarded: if the hook cannot attach, the transcript says so and nothing changes.
    /// </summary>
    public static class NativeThrowTrace
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void CxxThrow(IntPtr exceptionObject, IntPtr throwInfo);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandleW(string name);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)] private static extern IntPtr GetProcAddress(IntPtr module, string name);
        [DllImport("kernel32.dll")] private static extern ushort RtlCaptureStackBackTrace(uint framesToSkip, uint framesToCapture, IntPtr[] backTrace, IntPtr backTraceHash);

        private static NativeHook<CxxThrow> _hook;
        private static CxxThrow _detour;
        private static IntPtr _gaBase;
        private static long _gaSize;
        private static long[] _addrs;
        private static string[] _names;
        private static bool _tableTried;
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, int> Seen = new Dictionary<string, int>();
        private static int _distinct;
        private const int MaxDistinct = 40;
        private static long _total;
        [ThreadStatic] private static bool _inside;

        public static bool Installed { get; private set; }
        public static string Summary() => Installed ? $"{_total} throw(s), {Seen.Count} distinct" : "not installed";

        public static void Install()
        {
            if (Installed) return;
            try
            {
                IntPtr target = IntPtr.Zero;
                foreach (var dll in new[] { "vcruntime140.dll", "vcruntime140_1.dll", "ucrtbase.dll" })
                {
                    var m = GetModuleHandleW(dll);
                    if (m == IntPtr.Zero) continue;
                    target = GetProcAddress(m, "_CxxThrowException");
                    if (target != IntPtr.Zero) { Core.Log.Msg($"Native throw trace: _CxxThrowException found in {dll}."); break; }
                }
                if (target == IntPtr.Zero) { Core.Log.Warning("Native throw trace: _CxxThrowException not found; exceptions stay untraced."); return; }
                foreach (ProcessModule pm in Process.GetCurrentProcess().Modules)
                {
                    if (!string.Equals(pm.ModuleName, "GameAssembly.dll", StringComparison.OrdinalIgnoreCase)) continue;
                    _gaBase = pm.BaseAddress; _gaSize = pm.ModuleMemorySize; break;
                }
                if (_gaBase == IntPtr.Zero) { Core.Log.Warning("Native throw trace: GameAssembly.dll module not found."); return; }
                _detour = Detour;
                _hook = new NativeHook<CxxThrow>(target, Marshal.GetFunctionPointerForDelegate(_detour));
                _hook.Attach();
                Installed = true;
                Core.Log.Msg($"Native throw trace installed (GameAssembly at 0x{_gaBase.ToInt64():X}, {_gaSize / 1048576} MB).");
            }
            catch (Exception e) { Core.Log.Warning($"Native throw trace failed ({e.GetType().Name}: {e.Message}); exceptions stay untraced."); }
        }

        private static void Detour(IntPtr exceptionObject, IntPtr throwInfo)
        {
            if (!_inside)
            {
                _inside = true;
                try { Capture(exceptionObject); } catch { }
                _inside = false;
            }
            _hook.Trampoline(exceptionObject, throwInfo);
        }

        private static void Capture(IntPtr wrapper)
        {
            _total++;
            var frames = new IntPtr[40];
            var n = RtlCaptureStackBackTrace(1, 40, frames, IntPtr.Zero);
            var key = new StringBuilder();
            var rvas = new List<long>();
            for (var i = 0; i < n; i++)
            {
                var a = frames[i].ToInt64();
                var off = a - _gaBase.ToInt64();
                if (off < 0 || off >= _gaSize) continue;
                rvas.Add(off);
                if (rvas.Count <= 6) key.Append(off.ToString("x")).Append('|');
            }
            var k = key.ToString();
            lock (Gate)
            {
                if (Seen.TryGetValue(k, out var c))
                {
                    Seen[k] = c + 1;
                    if (c + 1 == 10 || c + 1 == 100 || (c + 1) % 1000 == 0) ReconLog.Line($"native throw ×{c + 1}: {Top(rvas, 2)}");
                    return;
                }
                if (_distinct >= MaxDistinct) return;
                _distinct++;
                Seen[k] = 1;
            }
            string what = "?";
            try
            {
                // Il2CppExceptionWrapper { Il2CppException* ex; } — the managed exception object.
                var ex = Marshal.ReadIntPtr(wrapper);
                if (ex != IntPtr.Zero)
                {
                    var wrapped = new Il2CppSystem.Exception(ex);
                    what = $"{wrapped.GetType().FullName}: {wrapped.Message}";
                }
            }
            catch (Exception e) { what = $"(unreadable: {e.GetType().Name})"; }
            var sb = new StringBuilder();
            sb.Append($"native throw: {what}\n```\n");
            var shown = 0;
            foreach (var rva in rvas) { sb.Append($"  GameAssembly+0x{rva:X}  {Name(rva)}\n"); if (++shown >= 14) break; }
            if (rvas.Count == 0) sb.Append("  (no GameAssembly frames; thrown from the engine or another module)\n");
            sb.Append("```");
            ReconLog.Line(sb.ToString());
        }

        private static string Top(List<long> rvas, int count)
        {
            var parts = new List<string>();
            for (var i = 0; i < rvas.Count && i < count; i++) parts.Add(Name(rvas[i]));
            return string.Join(" <- ", parts);
        }

        private static string Name(long rva)
        {
            EnsureTable();
            if (_addrs == null || _addrs.Length == 0) return "?";
            var idx = Array.BinarySearch(_addrs, rva);
            if (idx < 0) idx = ~idx - 1;
            if (idx < 0) return "?";
            return $"{_names[idx]}+0x{rva - _addrs[idx]:X}";
        }

        private static void EnsureTable()
        {
            if (_tableTried) return;
            _tableTried = true;
            try
            {
                var path = Path.Combine(ModPaths.Root, "methods.tsv");
                if (!File.Exists(path)) { Core.Log.Warning($"Native throw trace: {path} missing; frames stay unnamed (generate it from dump/script.json)."); return; }
                var addrs = new List<long>(210000); var names = new List<string>(210000);
                foreach (var line in File.ReadLines(path))
                {
                    var tab = line.IndexOf('\t');
                    if (tab <= 0) continue;
                    if (!long.TryParse(line.AsSpan(0, tab), System.Globalization.NumberStyles.HexNumber, null, out var a)) continue;
                    addrs.Add(a); names.Add(line.Substring(tab + 1));
                }
                _addrs = addrs.ToArray(); _names = names.ToArray();
            }
            catch (Exception e) { Core.Log.Warning($"Native throw trace: method table unreadable ({e.GetType().Name})."); }
        }
    }
}
