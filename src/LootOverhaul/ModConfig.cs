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
        /// <summary>Chance, 0–1, for each piece of a boss's pile after the guaranteed first one.</summary>
        public static MelonPreferences_Entry<float> BossDropChance;
        /// <summary>Pieces of loot (weapon or armor) a boss drops; the first is guaranteed.</summary>
        public static MelonPreferences_Entry<int> BossDrops;
        /// <summary>Pieces of loot a mini-boss drops; the first is guaranteed.</summary>
        public static MelonPreferences_Entry<int> MiniBossDrops;
        /// <summary>Rarity floor of the guaranteed first piece: 0 Common, 1 Unique, 2 Rare, 3 Legendary.</summary>
        public static MelonPreferences_Entry<int> BossGuaranteedClass;
        /// <summary>Extra pile pieces per player beyond the first (boss / mini-boss), fractions rounded down.</summary>
        public static MelonPreferences_Entry<float> BossDropsPerExtraPlayer, MiniBossDropsPerExtraPlayer;
        /// <summary>Extra pile pieces per health bar beyond the first (the game's Specs.stages).</summary>
        public static MelonPreferences_Entry<int> BossDropsPerExtraHealthBar;
        /// <summary>Extra pile pieces for a boss of the Elite strength type; a Legend boss gets twice this.</summary>
        public static MelonPreferences_Entry<int> EliteBossExtraDrops;
        /// <summary>Pieces an Elite-type enemy always drops (0 = roll like the rest).</summary>
        public static MelonPreferences_Entry<int> EliteDrops;
        /// <summary>Pieces a Legend-type enemy always drops.</summary>
        public static MelonPreferences_Entry<int> LegendDrops;
        /// <summary>The loot goblin: pieces, rarity floor of the first, guaranteed trinket rolls, and its size.</summary>
        public static MelonPreferences_Entry<int> GoblinDrops, GoblinGuaranteedClass, GoblinJunkRolls;
        public static MelonPreferences_Entry<float> LootGoblinScale;
        /// <summary>Roll drops for kills in the sandbox (the practice arena). Off: nothing drops there.</summary>
        public static MelonPreferences_Entry<bool> SandboxDrops;
        /// <summary>A bright frame around loot tiles at the fabricator and armory, and its colour.</summary>
        public static MelonPreferences_Entry<bool> PedestalFrame;
        public static MelonPreferences_Entry<string> PedestalFrameColor;
        /// <summary>After this many kills without a Legendary, the next drop is one. 0 disables.</summary>
        public static MelonPreferences_Entry<int> LegendaryPityKills;
        /// <summary>Drop-chance multiplier per player beyond the first.</summary>
        public static MelonPreferences_Entry<float> DropChancePerExtraPlayer;
        /// <summary>Lowest rarity that gets a beam.</summary>
        public static MelonPreferences_Entry<int> BeamMinClass;
        /// <summary>Rarity-coloured beam over floor loot (off: the floating name label is the default).</summary>
        public static MelonPreferences_Entry<bool> DropBeams;
        /// <summary>Floating rarity-coloured name over floor loot.</summary>
        public static MelonPreferences_Entry<bool> DropLabels;
        /// <summary>Show the floor label only while you look at the item or reach for it.</summary>
        public static MelonPreferences_Entry<bool> DropLabelsOnHover;
        /// <summary>The coin pile's own sparkle, borrowed and placed over floor loot.</summary>
        public static MelonPreferences_Entry<bool> DropSparkles;
        /// <summary>The game's own pickup glow (the outline coins and vanilla drops carry) kept lit on floor loot.</summary>
        public static MelonPreferences_Entry<bool> DropOutline;
        /// <summary>Throw/spin audio on the drop (networked by the game) and a chime when a weapon or armor lands.</summary>
        public static MelonPreferences_Entry<bool> DropSounds;
        /// <summary>Which coin-pile sound is the chime: start, finish, collected, or off.</summary>
        public static MelonPreferences_Entry<string> DropChime;
        /// <summary>The bag panel closes by itself when you walk this far from it (metres; 0 disables).</summary>
        public static MelonPreferences_Entry<float> BagAutoCloseMeters;
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
        /// <summary>Tokens per point of the game's salvage value when selling to the kobold.</summary>
        public static MelonPreferences_Entry<float> SellMultiplier;
        /// <summary>Shop asking price = the game's cost figure × this.</summary>
        public static MelonPreferences_Entry<float> ShopPriceMultiplier;
        /// <summary>How many weapons the broker keeps in stock.</summary>
        public static MelonPreferences_Entry<int> ShopSlots;
        /// <summary>Stock older than this is rolled again for free on the next visit.</summary>
        public static MelonPreferences_Entry<int> ShopRefreshMinutes;
        /// <summary>Booth placement in the lobby, world units. Press = in the lobby to set it where you stand.</summary>
        public static MelonPreferences_Entry<float> BoothX, BoothY, BoothZ, BoothYaw;
        /// <summary>True once the player has placed the booth with =; until then the built-in position is used.</summary>
        public static MelonPreferences_Entry<bool> BoothPlaced;
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
            BaseDropChance = Main.CreateEntry("BaseDropChance", 0.015f);
            BagWeightCapacity = Main.CreateEntry("BagWeightCapacity", 30f);
            BossDropChance = Main.CreateEntry("BossDropChance", 1.0f, description: "Chance, 0–1, for each piece of a boss's or mini-boss's pile after the guaranteed first one.");
            BossDrops = Main.CreateEntry("BossDrops", 3, description: "Pieces of loot (weapon or armor) a boss drops. The first always drops at BossGuaranteedClass or better; the rest each roll BossDropChance.");
            MiniBossDrops = Main.CreateEntry("MiniBossDrops", 2, description: "Pieces of loot a mini-boss drops, same rules as BossDrops.");
            BossGuaranteedClass = Main.CreateEntry("BossGuaranteedClass", 2, description: "Rarity floor of a boss's or mini-boss's first piece: 0 Common, 1 Unique, 2 Rare, 3 Legendary.");
            BossDropsPerExtraPlayer = Main.CreateEntry("BossDropsPerExtraPlayer", 1.0f, description: "Extra boss pile pieces per player beyond the first (rounded down).");
            MiniBossDropsPerExtraPlayer = Main.CreateEntry("MiniBossDropsPerExtraPlayer", 0.5f, description: "Extra mini-boss pile pieces per player beyond the first (rounded down).");
            BossDropsPerExtraHealthBar = Main.CreateEntry("BossDropsPerExtraHealthBar", 1, description: "Extra pile pieces per health bar beyond the first, for bosses that come with several.");
            EliteBossExtraDrops = Main.CreateEntry("EliteBossExtraDrops", 1, description: "Extra pile pieces for a boss or mini-boss of the Elite strength type; a Legend-type one gets twice this.");
            EliteDrops = Main.CreateEntry("EliteDrops", 1, description: "Pieces of loot an Elite-type enemy always drops, on the normal rarity curve. 0 = roll like everyone else.");
            LegendDrops = Main.CreateEntry("LegendDrops", 2, description: "Pieces of loot a Legend-type enemy always drops.");
            GoblinDrops = Main.CreateEntry("GoblinDrops", 3, description: "Pieces of loot the loot goblin drops; the first is at least GoblinGuaranteedClass.");
            GoblinGuaranteedClass = Main.CreateEntry("GoblinGuaranteedClass", 1, description: "Rarity floor of the goblin's first piece: 0 Common, 1 Unique, 2 Rare, 3 Legendary.");
            GoblinJunkRolls = Main.CreateEntry("GoblinJunkRolls", 3, description: "Trinkets the loot goblin always drops on top of its pile.");
            LootGoblinScale = Main.CreateEntry("LootGoblinScale", 1.5f, description: "Size multiplier for the loot goblin (1 = the game's size). Applied on every modded client.");
            SandboxDrops = Main.CreateEntry("SandboxDrops", false, description: "Roll drops for sandbox (practice arena) kills. Off by default: the arena despawns its enemies and would be free loot.");
            PedestalFrame = Main.CreateEntry("PedestalFrame", true, description: "Draw a bright frame around loot weapons on the fabricator and armory tiles.");
            PedestalFrameColor = Main.CreateEntry("PedestalFrameColor", "#FFD24A", description: "Frame colour as #RRGGBB.");
            LegendaryPityKills = Main.CreateEntry("LegendaryPityKills", 120, description: "Bad-luck protection: after this many kills by anyone in the room (counted in the host's own file) without a Legendary, the next drop is forced Legendary. 0 disables.");
            DropChancePerExtraPlayer = Main.CreateEntry("DropChancePerExtraPlayer", 0.35f, description: "Weapon and junk chances are multiplied by 1 + this × (players − 1).");
            BeamMinClass = Main.CreateEntry("BeamMinClass", 2, description: "Beam only over items of at least this rarity: 0 Common, 1 Unique, 2 Rare, 3 Legendary (junk: 2 = artifact).");
            DropBeams = Main.CreateEntry("DropBeams", false);
            DropLabels = Main.CreateEntry("DropLabels", true);
            DropLabelsOnHover = Main.CreateEntry("DropLabelsOnHover", true);
            DropSparkles = Main.CreateEntry("DropSparkles", true);
            PanelLaser = Main.CreateEntry("PanelLaser", true);
            DropOutline = Main.CreateEntry("DropOutline", true);
            DropSounds = Main.CreateEntry("DropSounds", true);
            DropChime = Main.CreateEntry("DropChime", "start", description: "Coin-pile sound played where a weapon or armor lands: start, finish, collected, or off.");
            BagAutoCloseMeters = Main.CreateEntry("BagAutoCloseMeters", 2.0f, description: "Walk this far from the open bag panel and it closes. 0 = never.");
            BagGesture = Main.CreateEntry("BagGesture", "stick-hold",
                description: "back-grip = reach behind your back with the right hand, squeeze grip and push the stick up; stick-hold = hold the right stick up; off.");
            BagGestureHoldSeconds = Main.CreateEntry("BagGestureHoldSeconds", 0.7f);
            WeightCommon = Main.CreateEntry("WeightCommon", 70f);
            WeightUnique = Main.CreateEntry("WeightUnique", 22f);
            WeightRare = Main.CreateEntry("WeightRare", 7f);
            WeightLegendary = Main.CreateEntry("WeightLegendary", 1f);
            JunkDropChance = Main.CreateEntry("JunkDropChance", 0.18f);
            TierUpChance = Main.CreateEntry("TierUpChance", 0.12f);
            ArmorShare = Main.CreateEntry("ArmorShare", 0.35f);
            SellMultiplier = Main.CreateEntry("SellMultiplier", 1.0f);
            ShopPriceMultiplier = Main.CreateEntry("ShopPriceMultiplier", 2.5f);
            ShopSlots = Main.CreateEntry("ShopSlots", 6);
            ShopRefreshMinutes = Main.CreateEntry("ShopRefreshMinutes", 60);
            LoadoutEnabled = Main.CreateEntry("LoadoutEnabled", true);
            BoothX = Main.CreateEntry("BoothX", Loot.Booth.DefaultPosition.x);
            BoothY = Main.CreateEntry("BoothY", Loot.Booth.DefaultPosition.y);
            BoothZ = Main.CreateEntry("BoothZ", Loot.Booth.DefaultPosition.z);
            BoothYaw = Main.CreateEntry("BoothYaw", Loot.Booth.DefaultYaw, description: "Degrees. Press = in the lobby to place the booth where you stand, facing you.");
            BoothPlaced = Main.CreateEntry("BoothPlaced", false, description: "Set by =. When false the mod's built-in lobby spot is used regardless of BoothX/Y/Z/Yaw.");

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
