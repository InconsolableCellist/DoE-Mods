using System;

namespace LootOverhaul.Gate
{
    /// <summary>
    /// Capability bitfield advertised as <c>lo.caps</c>. Append only — the value is compared
    /// across builds, and a renumber would silently change what a peer thinks we can do.
    /// This is LootOverhaul's own field; it does not share numbering with CustomAvatars' caps.
    /// </summary>
    [Flags]
    public enum ModCaps
    {
        None = 0,
        /// <summary>Drops, bag, claims — the core loop.</summary>
        Loot = 1 << 0,
        /// <summary>Equip-from-bag with respawn persistence.</summary>
        Equip = 1 << 1,
        /// <summary>The lobby booth: sell, shop.</summary>
        Booth = 1 << 2,
    }

    public static class ModCapsInfo
    {
        /// <summary>What this build actually implements — not what it aspires to.</summary>
        public static ModCaps Local => ModCaps.Loot;

        public static string Describe(ModCaps caps) => caps == ModCaps.None ? "none" : caps.ToString();
    }
}
