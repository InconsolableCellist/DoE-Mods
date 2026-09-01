using System;
using System.Text;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;

namespace DoEFriendsMod.Recon
{
    /// <summary>
    /// Small helpers for the parts of Il2CppInterop that bite. Notably: an interop proxy is
    /// a C# object wrapping a native pointer, so ordinary <c>== null</c> reasoning is unsafe
    /// on objects the engine has destroyed. Everything here is defensive on purpose.
    /// </summary>
    public static class Interop
    {
        /// <summary>
        /// True if the proxy still points at a live native object. Checks the managed
        /// reference and the native pointer; Unity's "destroyed but not collected" state is
        /// caught by the try/catch at the call site rather than guessed at here.
        /// </summary>
        public static bool Alive(Il2CppObjectBase o)
        {
            if (ReferenceEquals(o, null)) return false;
            try
            {
                if (o.Pointer == IntPtr.Zero) return false;

                // A UnityEngine.Object can be DESTROYED while its managed proxy still holds a
                // valid pointer. Unity's own null check is exactly this field, and skipping it
                // is what made stale AvatarPlayer and CharacterPrefab references survive a
                // scene change, pass every guard, and then throw on first use.
                if (o is UnityEngine.Object unityObject) return unityObject.m_CachedPtr != IntPtr.Zero;

                return true;
            }
            catch { return false; }
        }

        public static string Name(UnityEngine.Object o)
        {
            if (!Alive(o)) return "<null>";
            try { return o.name; }
            catch (Exception e) { return $"<name unavailable: {e.GetType().Name}>"; }
        }

        /// <summary>Full scene path of a transform, e.g. "Player/Rig/Head".</summary>
        public static string ScenePath(Transform t)
        {
            if (!Alive(t)) return "<null>";
            try
            {
                var sb = new StringBuilder(t.name);
                var p = t.parent;
                var guard = 0;
                while (Alive(p) && guard++ < 64)
                {
                    sb.Insert(0, p.name + "/");
                    p = p.parent;
                }
                return sb.ToString();
            }
            catch (Exception e) { return $"<path unavailable: {e.GetType().Name}>"; }
        }

        public static string Vec(Vector3 v) => $"({v.x:0.###}, {v.y:0.###}, {v.z:0.###})";
    }
}
