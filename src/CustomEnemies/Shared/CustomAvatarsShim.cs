// Adapters that let CustomAvatars' bundle, manifest and retargeting code compile into
// CustomEnemies as linked source (see CustomEnemies.csproj), without changing a line of it.
//
// Those files live in namespace CustomAvatars.Avatars and reach for three things of their own
// mod: CustomAvatars.Core.Log, CustomAvatars.Recon.Interop and two CustomAvatars.ModConfig
// entries. These stand-ins supply exactly those members and forward them to CustomEnemies.
// They are types of this assembly only; the CustomAvatars DLL keeps its own, and the two never
// meet (types are told apart by assembly at runtime).
using System;
using Il2CppInterop.Runtime.InteropTypes;
using MelonLoader;
using UnityEngine;

namespace CustomAvatars
{
    internal static class Core
    {
        public static MelonLogger.Instance Log => CustomEnemies.Core.Log;
    }

    internal static class ModConfig
    {
        public static MelonPreferences_Entry<float> RetargetHipsFollow => CustomEnemies.ModConfig.RetargetHipsFollow;
        public static MelonPreferences_Entry<bool> RetargetAlignAtCapture => CustomEnemies.ModConfig.RetargetAlignAtCapture;
        // SpringBones: CustomAvatars' own defaults, as fixed values (tune per chain in the sidecar).
        public static readonly Fixed<float> SpringStiffnessScale = new Fixed<float>(2.0f);
        public static readonly Fixed<float> SpringGravityScale = new Fixed<float>(0.6f);
        public static readonly Fixed<float> SpringDragBase = new Fixed<float>(0.55f);
        public static readonly Fixed<float> SpringDragFromSpring = new Fixed<float>(0.35f);
        public static readonly Fixed<bool> SpringCollidersEnabled = new Fixed<bool>(true);
        public static readonly Fixed<float> SpringMaxAngleFallback = new Fixed<float>(75f);
    }

    /// <summary>A constant that reads like a preference entry (<c>.Value</c>).</summary>
    internal sealed class Fixed<T>
    {
        public Fixed(T v) { Value = v; }
        public T Value { get; }
    }
}

namespace CustomAvatars.Recon
{
    internal static class Interop
    {
        public static bool Alive(Il2CppObjectBase o) => CustomEnemies.Interop.Alive(o);
        public static string Vec(Vector3 v) => $"({v.x:0.###}, {v.y:0.###}, {v.z:0.###})";
        public static string ScenePath(Transform t)
        {
            if (!Alive(t)) return "<null>";
            var s = t.name;
            for (var p = t.parent; Alive(p); p = p.parent) s = p.name + "/" + s;
            return s;
        }
    }
}
