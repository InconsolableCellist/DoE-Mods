using MelonLoader;

namespace LootOverhaul
{
    /// <summary>
    /// Settings, in UserData/MelonPreferences.cfg, under their own sections so they sit next
    /// to CustomAvatars' without mixing. Two sections: [LootOverhaul] for what a player
    /// changes, [LootOverhaul_Dev] for diagnostics.
    /// </summary>
    public static class ModConfig
    {
        public static MelonPreferences_Category Main;
        public static MelonPreferences_Category Dev;

        // ---- [LootOverhaul] -------------------------------------------------------------
        public static MelonPreferences_Entry<bool> Enabled;
        /// <summary>Master switch for the enemy-death drop roll. Off means chests only.</summary>
        public static MelonPreferences_Entry<bool> EnemyDropsEnabled;
        /// <summary>Base chance, 0–1, that a regular enemy drops a weapon. Elites and bosses multiply it.</summary>
        public static MelonPreferences_Entry<float> BaseDropChance;
        /// <summary>Bag capacity in weight units. The weight table lives in code, by weapon type and tier.</summary>
        public static MelonPreferences_Entry<float> BagWeightCapacity;
        /// <summary>Auto-bag commons on pickup without the beam and sting, to keep the floor clean.</summary>
        public static MelonPreferences_Entry<bool> AutoBagCommons;

        // ---- [LootOverhaul_Dev] ---------------------------------------------------------
        public static MelonPreferences_Entry<bool> VerboseLogging;

        public static void Load()
        {
            Main = MelonPreferences.CreateCategory("LootOverhaul");
            Enabled = Main.CreateEntry("Enabled", true);
            EnemyDropsEnabled = Main.CreateEntry("EnemyDropsEnabled", true);
            BaseDropChance = Main.CreateEntry("BaseDropChance", 0.08f);
            BagWeightCapacity = Main.CreateEntry("BagWeightCapacity", 60f);
            AutoBagCommons = Main.CreateEntry("AutoBagCommons", false);

            Dev = MelonPreferences.CreateCategory("LootOverhaul_Dev");
            VerboseLogging = Dev.CreateEntry("VerboseLogging", false);
        }
    }
}
