using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using HarmonyLib;
using Il2CppInterop.Runtime;

namespace StayPutVR
{
    /// <summary>
    /// Guarded Harmony patching, carried over from LootOverhaul. IL2CPP folds every method
    /// with an identical body into one native function, so an empty method shares its address
    /// with thousands of others (dump.cs RVA 0x35FC20); patching one patches all of them and
    /// the process dies. Every target's native pointer is therefore read and compared against
    /// a known empty method and against everything already patched before the patch goes in.
    ///
    /// Like the VisualCues copy this one takes an exact <see cref="MethodInfo"/> rather than a
    /// name and a parameter count, so a target with several same-name overloads resolves to
    /// the one that was asked for. The pointer comes from the interop-generated method-info
    /// field, which belongs to that overload and not to the first one with that name.
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
                _stubPointer = NativePointerByName(typeof(Il2Cpp.WeaponFactory), "Init", 0);
                _guardReady = _stubPointer != IntPtr.Zero;
                if (!_guardReady) Failed.Add("stub-address guard: could not resolve WeaponFactory.Init — refusing ALL patches (fail closed)");
            }
            catch (Exception e)
            {
                _guardReady = false;
                Failed.Add($"stub-address guard failed: {e.GetType().Name}: {e.Message} — refusing ALL patches (fail closed)");
            }
        }

        /// <summary>Native code address of an il2cpp method looked up by name, or zero. First field of il2cpp's MethodInfo is methodPointer.</summary>
        public static IntPtr NativePointerByName(Type type, string name, int argc)
        {
            var klass = Il2CppClassPointerStore.GetNativeClassPointer(type);
            if (klass == IntPtr.Zero) return IntPtr.Zero;
            var mi = IL2CPP.il2cpp_class_get_method_from_name(klass, name, argc);
            return mi == IntPtr.Zero ? IntPtr.Zero : Marshal.ReadIntPtr(mi);
        }

        /// <summary>
        /// Native code address of exactly this interop-generated method. Il2CppInterop keeps
        /// the il2cpp MethodInfo* of every generated method in a static field; the runtime
        /// exposes the lookup as <c>Il2CppInteropUtils.GetIl2CppMethodInfoPointerFieldForGeneratedMethod</c>.
        /// Found by reflection so a renamed namespace degrades to the by-name lookup instead of
        /// a missing-type crash at load.
        /// </summary>
        public static IntPtr NativePointer(MethodInfo method)
        {
            try
            {
                var utils = typeof(IL2CPP).Assembly.GetType("Il2CppInterop.Runtime.Il2CppInteropUtils");
                var lookup = utils?.GetMethod("GetIl2CppMethodInfoPointerFieldForGeneratedMethod", BindingFlags.Public | BindingFlags.Static);
                var field = lookup?.Invoke(null, new object[] { method }) as FieldInfo;
                if (field != null)
                {
                    var methodInfoPtr = (IntPtr)field.GetValue(null);
                    if (methodInfoPtr != IntPtr.Zero) return Marshal.ReadIntPtr(methodInfoPtr);
                }
            }
            catch (Exception e) { Failed.Add($"exact pointer lookup for {method.DeclaringType?.Name}.{method.Name} threw {e.GetType().Name}; using by-name lookup"); }
            return NativePointerByName(method.DeclaringType, method.Name, method.GetParameters().Length);
        }

        public static bool Patch(MethodInfo target, MethodInfo prefix, MethodInfo postfix, string label)
        {
            if (target == null) { Failed.Add($"{label} — method not found"); return false; }
            if (!_guardReady) { Refused.Add($"{label} — guard unavailable"); return false; }

            IntPtr ptr;
            try { ptr = NativePointer(target); }
            catch (Exception e) { Refused.Add($"{label} — pointer lookup threw {e.GetType().Name}"); return false; }

            if (ptr == IntPtr.Zero) { Refused.Add($"{label} — native method not found"); return false; }
            if (ptr == _stubPointer) { Refused.Add($"{label} — shares the universal empty-method address"); return false; }
            if (PatchedPointers.TryGetValue(ptr, out var other) && other != label) { Refused.Add($"{label} — shares native code with already-patched {other}"); return false; }

            try
            {
                _harmony.Patch(target,
                    prefix: prefix == null ? null : new HarmonyMethod(prefix),
                    postfix: postfix == null ? null : new HarmonyMethod(postfix));
                PatchedPointers[ptr] = label;
                Installed.Add($"{label} @0x{ptr.ToInt64():X}");
                return true;
            }
            catch (Exception e)
            {
                Failed.Add($"{label} — {e.GetType().Name}: {e.Message}");
                return false;
            }
        }

        public static MethodInfo Of(Type owner, string name) =>
            owner.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

        public static void Report()
        {
            foreach (var s in Installed) Core.Log.Msg($"patch ok: {s}");
            foreach (var s in Refused) Core.Log.Warning($"patch REFUSED: {s}");
            foreach (var s in Failed) Core.Log.Warning($"patch FAILED: {s}");
            Core.Log.Msg($"Patches: {Installed.Count} installed, {Refused.Count} refused, {Failed.Count} failed.");
        }
    }
}
