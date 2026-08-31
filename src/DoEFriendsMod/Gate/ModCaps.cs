using System;

namespace DoEFriendsMod.Gate
{
    /// <summary>
    /// Capability bitfield advertised as <c>dfm.caps</c>. Append only — the value is compared
    /// across builds, and a renumber would silently change what a peer thinks we can do.
    /// </summary>
    [Flags]
    public enum ModCaps
    {
        None = 0,
        Avatars = 1 << 0,
        FaceTracking = 1 << 1,
        Items = 1 << 2,
    }

    public static class ModCapsInfo
    {
        /// <summary>
        /// What this build actually implements — not what it aspires to. Phase 1 ships the
        /// gate and nothing else, so we advertise nothing.
        /// </summary>
        public static ModCaps Local => ModCaps.None;

        public static string Describe(ModCaps caps) => caps == ModCaps.None ? "none" : caps.ToString();
    }
}
