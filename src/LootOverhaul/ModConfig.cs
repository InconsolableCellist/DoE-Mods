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
        /// <summary>Drop chance, 0–1, when a boss dies.</summary>
        public static MelonPreferences_Entry<float> BossDropChance;
        /// <summary>After this many kills without a Legendary, the next drop is one. 0 disables.</summary>
        public static MelonPreferences_Entry<int> LegendaryPityKills;
        /// <summary>Rarity-coloured beam over floor loot (off: the floating name label is the default).</summary>
        public static MelonPreferences_Entry<bool> DropBeams;
        /// <summary>Floating rarity-coloured name over floor loot.</summary>
        public static MelonPreferences_Entry<bool> DropLabels;
        /// <summary>Show the floor label only while you look at the item or reach for it.</summary>
        public static MelonPreferences_Entry<bool> DropLabelsOnHover;
        /// <summary>The coin pile's own sparkle, borrowed and placed over floor loot.</summary>
        public static MelonPreferences_Entry<bool> DropSparkles;
        /// <summary>An invisible pointer target over each panel so the laser shows across the whole window.</summary>
        public static MelonPreferences_Entry<bool> PanelLaser;
        /// <summary>How the bag opens in VR: "back-grip" (right hand behind you, grip + stick up), "stick-hold" (right stick held up), or "off".</summary>
        public static MelonPreferences_Entry<string> BagGesture;
        public static MelonPreferences_Entry<float> BagGestureHoldSeconds;
        /// <summary>Rarity weights for weapon drops, relative. Bosses and pity push upward from here.</summary>
        public static MelonPreferences_Entry<float> WeightCommon, WeightUnique, WeightRare, WeightLegendary;
        /// <summary>Chance, 0–1, that a real kill drops a trinket instead of nothing (rolled after the weapon roll fails).</summary>
        public static MelonPreferences_Entry<float> JunkDropChance;
        /// <summary>Chance that a drop is one tier above the game's loot tier for your level.</summary>
        public static MelonPreferences_Entry<float> TierUpChance;
        /// <summary>Share of successful weapon rolls that become armor instead, 0–1.</summary>
        public static MelonPreferences_Entry<float> ArmorShare;
        /// <summary>Gold per point of the game's salvage value when selling at the booth.</summary>
        public static MelonPreferences_Entry<float> SellMultiplier;
        /// <summary>Shop asking price = the game's cost figure × this.</summary>
        public static MelonPreferences_Entry<float> ShopPriceMultiplier;
        /// <summary>How many weapons the broker keeps in stock.</summary>
        public static MelonPreferences_Entry<int> ShopSlots;
        /// <summary>Stock older than this is rolled again for free on the next visit.</summary>
        public static MelonPreferences_Entry<int> ShopRefreshMinutes;
        /// <summary>Booth placement in the lobby, world units. Press = in the lobby to set it where you stand.</summary>
        public static MelonPreferences_Entry<float> BoothX, BoothY, BoothZ, BoothYaw;
        /// <summary>Re-apply the booth loadout after every holster fill. Off means the booth only sells.</summary>
        public static MelonPreferences_Entry<bool> LoadoutEnabled;

        // ---- [LootOverhaul_Dev] ---------------------------------------------------------
        public static MelonPreferences_Entry<bool> VerboseLogging;
        /// <summary>Install the read-only recon hooks and write a transcript. The v0.1 build is nothing but this.</summary>
        public static MelonPreferences_Entry<bool> ReconEnabled;
        /// <summary>Log every PlayerProfile write (the PlayFab watchdog). Off only if it turns out to be noisy.</summary>
        public static MelonPreferences_Entry<bool> ProfileWatchEnabled;
        public static MelonPreferences_Entry<bool> HotkeysEnabled;
        /// <summary>Echo every transcript line to the MelonLoader console as well.</summary>
        public static MelonPreferences_Entry<bool> MirrorReconToConsole;

        public static void Load()
        {
            Main = MelonPreferences.CreateCategory("LootOverhaul");
            Enabled = Main.CreateEntry("Enabled", true);
            EnemyDropsEnabled = Main.CreateEntry("EnemyDropsEnabled", true);
            BaseDropChance = Main.CreateEntry("BaseDropChance", 0.035f);
            BagWeightCapacity = Main.CreateEntry("BagWeightCapacity", 60f);
            BossDropChance = Main.CreateEntry("BossDropChance", 1.0f);
            LegendaryPityKills = Main.CreateEntry("LegendaryPityKills", 120);
            DropBeams = Main.CreateEntry("DropBeams", false);
            DropLabels = Main.CreateEntry("DropLabels", true);
            DropLabelsOnHover = Main.CreateEntry("DropLabelsOnHover", true);
            DropSparkles = Main.CreateEntry("DropSparkles", true);
            PanelLaser = Main.CreateEntry("PanelLaser", true);
            BagGesture = Main.CreateEntry("BagGesture", "stick-hold",
                description: "back-grip = reach behind your back with the right hand, squeeze grip and push the stick up; stick-hold = hold the right stick up; off.");
            BagGestureHoldSeconds = Main.CreateEntry("BagGestureHoldSeconds", 0.7f);
            WeightCommon = Main.CreateEntry("WeightCommon", 70f);
            WeightUnique = Main.CreateEntry("WeightUnique", 22f);
            WeightRare = Main.CreateEntry("WeightRare", 7f);
            WeightLegendary = Main.CreateEntry("WeightLegendary", 1f);
            JunkDropChance = Main.CreateEntry("JunkDropChance", 0.35f);
            TierUpChance = Main.CreateEntry("TierUpChance", 0.12f);
            ArmorShare = Main.CreateEntry("ArmorShare", 0.35f);
            SellMultiplier = Main.CreateEntry("SellMultiplier", 1.0f);
            ShopPriceMultiplier = Main.CreateEntry("ShopPriceMultiplier", 2.5f);
            ShopSlots = Main.CreateEntry("ShopSlots", 6);
            ShopRefreshMinutes = Main.CreateEntry("ShopRefreshMinutes", 60);
            LoadoutEnabled = Main.CreateEntry("LoadoutEnabled", true);
            // Defaults: 2.5 m in front of where the lobby spawned the player on 2026-09-02, facing back.
            BoothX = Main.CreateEntry("BoothX", 51.45f);
            BoothY = Main.CreateEntry("BoothY", -1.98f);
            BoothZ = Main.CreateEntry("BoothZ", 20.75f);
            BoothYaw = Main.CreateEntry("BoothYaw", 105.6f, description: "Degrees. Press = in the lobby to place the booth where you stand, facing you.");

            Dev = MelonPreferences.CreateCategory("LootOverhaul_Dev");
            VerboseLogging = Dev.CreateEntry("VerboseLogging", false);
            ReconEnabled = Dev.CreateEntry("ReconEnabled", true,
                description: "Read-only recon hooks + transcript in UserData/LootOverhaul/recon/. The 0.1 build does nothing else.");
            ProfileWatchEnabled = Dev.CreateEntry("ProfileWatchEnabled", true,
                description: "Log every PlayerProfile (PlayFab) write so the mod's read-only boundary can be checked.");
            HotkeysEnabled = Dev.CreateEntry("HotkeysEnabled", true,
                description: "Insert = generator survey, Delete = spawn test weapon, Backslash = lobby survey, Scroll Lock = button test.");
            MirrorReconToConsole = Dev.CreateEntry("MirrorReconToConsole", false);
        }
    }
}
