using System;

namespace Descent.Gate
{
    /// <summary>
    /// Capability bitfield advertised as <c>dd.caps</c>. Append only — the value is compared
    /// across builds, and a renumber would silently change what a peer thinks we can do.
    /// This is Descent's own field; it shares no numbering with the other mods' caps.
    /// </summary>
    [Flags]
    public enum ModCaps
    {
        None = 0,
        /// <summary>Runs, floors, the descend hand-off, per-floor banking.</summary>
        Descent = 1 << 0,
    }

    public static class ModCapsInfo
    {
        /// <summary>What this build actually implements — not what it aspires to.</summary>
        public static ModCaps Local => ModCaps.Descent;

        public static string Describe(ModCaps caps) => caps == ModCaps.None ? "none" : caps.ToString();
    }
}
