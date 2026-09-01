using System;

namespace CustomAvatars.Gate
{
    /// <summary>
    /// Capability bitfield advertised as <c>ca.caps</c>. Append only — the value is compared
    /// across builds, and a renumber would silently change what a peer thinks we can do.
    /// </summary>
    [Flags]
    public enum ModCaps
    {
        None = 0,
        Avatars = 1 << 0,
        FaceTracking = 1 << 1,
        Items = 1 << 2,
        Fbt = 1 << 3,
    }

    public static class ModCapsInfo
    {
        /// <summary>
        /// What this build actually implements — not what it aspires to. Custom avatars now
        /// sync between peers, so that capability is real and worth advertising.
        /// </summary>
        public static ModCaps Local => ModCaps.Avatars | ModCaps.FaceTracking | ModCaps.Fbt;

        public static string Describe(ModCaps caps) => caps == ModCaps.None ? "none" : caps.ToString();
    }
}
