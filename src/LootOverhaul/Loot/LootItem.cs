using System;

namespace LootOverhaul.Loot
{
    /// <summary>
    /// One item in the bag. Plain managed data, no game types, so the inventory file can be
    /// read and written without the game running.
    ///
    /// <see cref="SaveString"/> is the game's own <c>WeaponModule.GetSaveString()</c> output —
    /// the six values (prefab, generator version, class, tier, style, seed) plus GUID that
    /// fully describe a generated weapon. Everything else here is derived from it at pickup
    /// time and cached so the bag UI never has to touch the generator to sort a list.
    /// </summary>
    public class LootItem
    {
        /// <summary>Stable id for claims, trades and the loadout slots. Not the game's weapon GUID.</summary>
        public string Id = Guid.NewGuid().ToString("N");

        public string SaveString;

        /// <summary>The game's <c>Prop.Type</c> value (Sword=3, Axe=2, …), kept as an int.</summary>
        public int PropType;
        /// <summary><c>WeaponFactory.WeaponClass</c>: 0 Common, 1 Unique, 2 Rare, 3 Legendary, 4 Mythic.</summary>
        public int WeaponClass;
        /// <summary><c>WeaponFactory.WeaponTier</c>, 0-based (Tier1 = 0 … Tier7 = 6).</summary>
        public int WeaponTier;

        /// <summary>Uncoloured display name from <c>WeaponModule.GetDisplayName(false)</c>.</summary>
        public string Name;

        /// <summary>Weight units, from the mod's table by type and tier.</summary>
        public float Weight;
        /// <summary>Sell value, from <c>WeaponCost.GetSalvageValue</c> times the mod multiplier.</summary>
        public int Value;

        /// <summary>The game's <c>Realm</c> value where it dropped, or -1.</summary>
        public int FoundInRealm = -1;
        public DateTime FoundAt = DateTime.UtcNow;
        /// <summary>Nickname of whoever's kill (or chest) produced it — for the trade log.</summary>
        public string FoundBy;
    }
}
