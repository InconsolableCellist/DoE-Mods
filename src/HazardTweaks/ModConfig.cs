using MelonLoader;

namespace HazardTweaks
{
    /// <summary>
    /// Settings in UserData/MelonPreferences.cfg under [HazardTweaks]. Read when a dungeon's
    /// hazards are rolled and when a run starts, so an edit applies to the next mission board
    /// after pressing the reload key (F9) or restarting.
    /// </summary>
    public static class ModConfig
    {
        public static MelonPreferences_Category Main;

        public static MelonPreferences_Entry<bool> Enabled;
        /// <summary>Drop every hazard that takes your own weapons away (Bows Only, Axes Only, Random Weapons, ...).</summary>
        public static MelonPreferences_Entry<bool> RemoveWeaponRestrictions;
        /// <summary>More hazards to drop, by their game name, comma separated. Example: Death, NoRevives.</summary>
        public static MelonPreferences_Entry<string> AlsoRemove;
        /// <summary>Every hazard's XP and gold bonus is multiplied by this. 1.5 = Death +25% becomes +37.5% at tier 7.</summary>
        public static MelonPreferences_Entry<float> BonusMultiplier;
        /// <summary>Round every scaled bonus up to the next multiple of this many percent. 5 = 37.5% shows and pays 40%. 0 = no rounding.</summary>
        public static MelonPreferences_Entry<int> RoundUpTo;
        public static MelonPreferences_Entry<bool> VerboseLogging;

        public static void Load()
        {
            Main = MelonPreferences.CreateCategory("HazardTweaks");
            Enabled = Main.CreateEntry("Enabled", true);
            RemoveWeaponRestrictions = Main.CreateEntry("RemoveWeaponRestrictions", true,
                description: "Drop every hazard that takes your own weapons away: Bows Only, Axes Only, Swords & Shields, Daggers Only, Fireball Staffs Only, Random Weapons (and any other *Only weapon hazard the game adds).");
            AlsoRemove = Main.CreateEntry("AlsoRemove", "",
                description: "More hazards to drop, comma separated, by game name: Death, NoPotions, Potions50Pct, NoRevives, CreatureSwarms, Damage150Pct, Damage200Pct, DragonBattle, MiniBossBattle, BossBattle. The log lists every name at startup.");
            BonusMultiplier = Main.CreateEntry("BonusMultiplier", 1.5f,
                description: "Multiplies every hazard's XP/gold bonus. 1 = vanilla, 1.5 = half again (Death +25% -> +37.5% at tier 7), 2 = double.");
            RoundUpTo = Main.CreateEntry("RoundUpTo", 5,
                description: "Round every scaled bonus UP to the next multiple of this many percent, so there are no decimals. 5: 37.5% -> 40%. 1: 37.5% -> 38%. 0: no rounding. Only applies when BonusMultiplier is not 1.");
            VerboseLogging = Main.CreateEntry("VerboseLogging", true,
                description: "Log every hazard list the mod changes.");
        }
    }
}
