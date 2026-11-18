using System;
using Il2CppInterop.Runtime.InteropTypes;

namespace CustomEnemies
{
    /// <summary>Liveness checks for interop proxies (the same rule as CustomAvatars' Recon/Interop).</summary>
    public static class Interop
    {
        /// <summary>
        /// True if the proxy still points at a live native object. A destroyed UnityEngine.Object
        /// can keep a valid pointer; Unity's own null check is <c>m_CachedPtr</c>.
        /// </summary>
        public static bool Alive(Il2CppObjectBase o)
        {
            if (ReferenceEquals(o, null)) return false;
            try
            {
                if (o.Pointer == IntPtr.Zero) return false;
                if (o is UnityEngine.Object u) return u.m_CachedPtr != IntPtr.Zero;
                return true;
            }
            catch { return false; }
        }

        public static string Name(UnityEngine.Object o)
        {
            if (!Alive(o)) return "<null>";
            try { return o.name; } catch { return "?"; }
        }
    }
}
