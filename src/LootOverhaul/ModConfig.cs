using MelonLoader;

namespace LootOverhaul
{
    /// <summary>
    /// Settings, in UserData/MelonPreferences.cfg, under their own sections so they sit next
    /// to CustomAvatars' without mixing. [LootOverhaul] for what a player changes,
    /// [LootOverhaul_Dev] for diagnostics.
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
        /// <summary>Install the read-only recon hooks and write a transcript. The v0.1 build is nothing but this.</summary>
        public static MelonPreferences_Entry<bool> ReconEnabled;
        /// <summary>Log every PlayerProfile write (the PlayFab watchdog). Off only if it turns out to be noisy.</summary>
        public static MelonPreferences_Entry<bool> ProfileWatchEnabled;
        public static MelonPreferences_Entry<bool> HotkeysEnabled;
        /// <summary>Echo every transcript line to the MelonLoader console as well.</summary>
        public static MelonPreferences_Entry<bool> MirrorReconToConsole;
        /// <summary>One-shot coin restoration for the 0.1.0 test-button bug. Applied once in the lobby, then reset to 0.</summary>
        public static MelonPreferences_Entry<int> CoinRepairAmount;

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
            ReconEnabled = Dev.CreateEntry("ReconEnabled", true,
                description: "Read-only recon hooks + transcript in UserData/LootOverhaul/recon/. The 0.1 build does nothing else.");
            ProfileWatchEnabled = Dev.CreateEntry("ProfileWatchEnabled", true,
                description: "Log every PlayerProfile (PlayFab) write so the mod's read-only boundary can be checked.");
            HotkeysEnabled = Dev.CreateEntry("HotkeysEnabled", true,
                description: "Insert = generator survey, Delete = spawn test weapon, Backslash = lobby survey, Scroll Lock = button test.");
            MirrorReconToConsole = Dev.CreateEntry("MirrorReconToConsole", false);
            CoinRepairAmount = Dev.CreateEntry("CoinRepairAmount", 0,
                description: "One-shot: added to Coins once in the lobby, then reset to 0. Exists only to undo the 0.1.0 test-button charge.");
        }
    }
}
