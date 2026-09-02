using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using HarmonyLib;
using Il2CppInterop.Runtime;

namespace LootOverhaul.Recon
{
    /// <summary>
    /// Tiny Harmony helper for the recon patches. Every patch here is a logging prefix or
    /// postfix that never changes arguments, results, or control flow. One failed patch
    /// logs and moves on: half a recon pass is still worth the headset session.
    ///
    /// <b>The IL2CPP address-folding trap.</b> IL2CPP emits one native function for every
    /// method whose compiled body is identical, so an empty <c>protected virtual void
    /// OnLootCollected() {}</c> shares its address with ~3,200 other empty methods in this
    /// game (dump.cs RVA 0x35FC20). Patching it patches all of them; the postfix then runs
    /// with garbage <c>__instance</c> pointers and the process dies. The 0.1.0 crash was
    /// exactly this. So before patching, every candidate's native method pointer is read
    /// and compared against a known empty method and against everything already patched;
    /// a shared address is refused, and the refusal is reported.
    /// </summary>
    public static class Hooks
    {
        private static HarmonyLib.Harmony _harmony;
        private static readonly List<string> Installed = new List<string>();
        private static readonly List<string> Failed = new List<string>();
        private static readonly List<string> Refused = new List<string>();
        private static readonly Dictionary<IntPtr, string> PatchedPointers = new Dictionary<IntPtr, string>();
        private static IntPtr _stubPointer;
        private static bool _guardReady;

        public static void Init(HarmonyLib.Harmony harmony)
        {
            _harmony = harmony;
            try
            {
                // WeaponFactory.Init() is an empty body (dump.cs RVA 0x35FC20, the shared stub).
                _stubPointer = NativePointer(typeof(Il2Cpp.WeaponFactory), "Init", 0);
                _guardReady = _stubPointer != IntPtr.Zero;
                if (!_guardReady) Failed.Add("stub-address guard: could not resolve WeaponFactory.Init — refusing ALL patches (fail closed)");
            }
            catch (Exception e)
            {
                _guardReady = false;
                Failed.Add($"stub-address guard failed: {e.GetType().Name}: {e.Message} — refusing ALL patches (fail closed)");
            }
        }

        /// <summary>Native code address of an il2cpp method, or zero. First field of il2cpp's MethodInfo is methodPointer.</summary>
        public static IntPtr NativePointer(Type type, string name, int argc)
        {
            var klass = Il2CppClassPointerStore.GetNativeClassPointer(type);
            if (klass == IntPtr.Zero) return IntPtr.Zero;
            var mi = IL2CPP.il2cpp_class_get_method_from_name(klass, name, argc);
            return mi == IntPtr.Zero ? IntPtr.Zero : Marshal.ReadIntPtr(mi);
        }

        /// <summary>
        /// Patch every overload of <paramref name="methodName"/> on <paramref name="type"/>
        /// (optionally narrowed by parameter count) with the given static prefix/postfix,
        /// unless its native address is shared.
        /// </summary>
        public static int Patch(Type type, string methodName, MethodInfo prefix, MethodInfo postfix, int paramCount = -1)
        {
            var label = $"{type.Name}.{methodName}";
            if (!_guardReady) { Refused.Add($"{label} — guard unavailable"); return 0; }

            var patched = 0;
            var found = 0;
            try
            {
                foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    if (m.Name != methodName) continue;
                    var argc = m.GetParameters().Length;
                    if (paramCount >= 0 && argc != paramCount) continue;
                    found++;
                    var overload = $"{label}({argc})";

                    IntPtr ptr;
                    try { ptr = NativePointer(type, methodName, argc); }
                    catch (Exception e) { Refused.Add($"{overload} — pointer lookup threw {e.GetType().Name}"); continue; }

                    if (ptr == IntPtr.Zero) { Refused.Add($"{overload} — native method not found"); continue; }
                    if (ptr == _stubPointer) { Refused.Add($"{overload} — shares the universal empty-method address (would patch ~3,200 methods)"); continue; }
                    if (PatchedPointers.TryGetValue(ptr, out var other) && other != overload) { Refused.Add($"{overload} — shares native code with already-patched {other}"); continue; }

                    try
                    {
                        _harmony.Patch(m,
                            prefix: prefix == null ? null : new HarmonyMethod(prefix),
                            postfix: postfix == null ? null : new HarmonyMethod(postfix));
                        PatchedPointers[ptr] = overload;
                        patched++;
                    }
                    catch (Exception e)
                    {
                        Failed.Add($"{overload} — {e.GetType().Name}: {e.Message}");
                    }
                }
            }
            catch (Exception e)
            {
                Failed.Add($"{label} — {e.GetType().Name}: {e.Message}");
            }

            if (patched > 0) Installed.Add($"{label} ×{patched}");
            else if (found == 0) Failed.Add($"{label} — no method by that name");
            return patched;
        }

        public static MethodInfo Of(Type owner, string name) =>
            owner.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

        public static void Report()
        {
            ReconLog.Section("Harmony patches");
            ReconLog.Line($"- stub (empty-method) address: 0x{_stubPointer.ToInt64():X}");
            foreach (var s in Installed) ReconLog.Line($"- ok: {s}");
            foreach (var s in Refused) ReconLog.Line($"- REFUSED: {s}");
            foreach (var s in Failed) ReconLog.Line($"- FAILED: {s}");
            ReconLog.Headline($"Recon patches: {Installed.Count} installed, {Refused.Count} refused (shared address), {Failed.Count} failed.");
        }
    }
}
