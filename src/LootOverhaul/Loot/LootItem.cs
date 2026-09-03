using System;

namespace LootOverhaul.Loot
{
    /// <summary>
    /// One item in the bag. Plain managed data, no game types, so the inventory file can be
    /// read and written without the game running.
    ///
    /// The weapon itself is stored as the fields of the game's <c>PlayerData.WeaponModuleDTO</c>.
    /// Recon 2026-09-02: <c>WeaponModule.GetSaveString()</c> is only <c>#guid</c>, a reference
    /// into the PlayFab armory, so it cannot describe a weapon the profile has never seen;
    /// the DTO round-trip (<c>new WeaponModuleDTO(wm)</c> → <c>new WeaponModule(dto)</c>) was
    /// exact for every type. Everything else here is derived at pickup time and cached so the
    /// bag UI never has to touch the generator to sort a list.
    /// </summary>
    public class LootItem
    {
        /// <summary>Stable id for claims, trades and the loadout slots. Not the game's weapon GUID.</summary>
        public string Id = Guid.NewGuid().ToString("N");

        /// <summary>"weapon" (a generated weapon; DTO fields below apply) or "junk" (a trinket: PrefabName is the vanilla prop it rides on, Name/Value/Weight come from the junk table).</summary>
        public string Kind = "weapon";
        public bool IsWeapon => Kind == "weapon";
        public bool IsBuff => Kind == "buff";
        /// <summary>Tonics: the exosuit stat name and the multiplier applied for one run.</summary>
        public string BuffStat;
        public float BuffMult;

        // ---- WeaponModuleDTO fields, verbatim ----------------------------------------------
        /// <summary>e.g. <c>Sword_Gen1</c>, <c>Staff_Heal_Gen1</c> — the networked prefab name.</summary>
        public string PrefabName;
        public int GenV;
        /// <summary><c>WeaponFactory.WeaponClass</c>: 0 Common, 1 Unique, 2 Rare, 3 Legendary, 4 Mythic.</summary>
        public int WeaponClass;
        /// <summary><c>WeaponFactory.WeaponTier</c>, 0-based (Tier1 = 0 … Tier7 = 6).</summary>
        public int WeaponTier;
        public int RandomSeed;
        /// <summary>The game's own weapon GUID string, generated with the module.</summary>
        public string WeaponGuid;
        public int WeaponStyle;
        public int[] NameIDs;
        /// <summary>The DTO's own <c>name</c> field, carried verbatim.</summary>
        public string DtoName;
        /// <summary>The DTO's module name / type, so a mythic or manual weapon rebuilds through the right subclass.</summary>
        public string ModuleName;
        public int ModuleType;

        // ---- derived, cached -------------------------------------------------------------
        /// <summary>The game's <c>Prop.Type</c> value (Sword=3, Axe=2, …), kept as an int.</summary>
        public int PropType;

        /// <summary>Uncoloured display name from <c>WeaponModule.GetDisplayName(false)</c>.</summary>
        public string Name;
        /// <summary>The same with the game's rarity colour tags, for toasts and the bag UI.</summary>
        public string ColoredName;

        /// <summary>Weight units, from the mod's table by type and tier.</summary>
        public float Weight;
        /// <summary>Sell value, from <c>WeaponCost.GetSalvageValue</c> times the mod multiplier.</summary>
        public int Value;

        /// <summary>The game's <c>Realm</c> value where it dropped, or -1.</summary>
        public int FoundInRealm = -1;
        public DateTime FoundAt = DateTime.UtcNow;
        /// <summary>Nickname of whoever's kill (or chest) produced it — for the trade log.</summary>
        public string FoundBy;

        /// <summary>"loot" for bag items, "armory" for a vanilla weapon read from the profile.</summary>
        public string Source = "loot";
        /// <summary>-1 when not equipped; otherwise the loadout slot (0 left hip, 1 right hip, 2 back).</summary>
        public int EquippedSlot = -1;
    }
}
