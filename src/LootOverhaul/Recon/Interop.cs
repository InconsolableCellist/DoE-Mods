using System;
using System.Text;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;

namespace LootOverhaul.Recon
{
    /// <summary>Defensive helpers for Il2CppInterop proxies (carried over from CustomAvatars).</summary>
    public static class Interop
    {
        /// <summary>True if the proxy still points at a live native object, including Unity's destroyed state.</summary>
        public static bool Alive(Il2CppObjectBase o)
        {
            if (ReferenceEquals(o, null)) return false;
            try
            {
                if (o.Pointer == IntPtr.Zero) return false;
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

        /// <summary>Collapse multi-line rich text (stats blocks) to one log line.</summary>
        public static string OneLine(string s) =>
            string.IsNullOrEmpty(s) ? "" : s.Replace("\r", "").Replace("\n", " | ");
    }
}
