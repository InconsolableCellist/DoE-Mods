using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using MelonLoader;
using MelonLoader.Utils;

namespace LootOverhaul
{
    /// <summary>
    /// Settings, in UserData/MelonPreferences.cfg, one section per topic so a new player can
    /// find what they are looking for. Fine tuning that nobody needs to touch lives in code
    /// (see <see cref="Fixed"/>). [LootOverhaul_Dev] holds diagnostics and testing switches.
    /// </summary>
    public static class ModConfig
    {
        public const int Layout = 11;

        public static MelonPreferences_Category Main, ChestArmor, Goblin, FloorLoot, Bag, Shop, TokenPay, BoothPos, Dev;

        public static MelonPreferences_Entry<bool> Enabled;

        public static MelonPreferences_Entry<bool> ChestArmorEnabled, ChestArmorScalesWithPlayers;
        public static MelonPreferences_Entry<float> ChestArmorSmall, ChestArmorMedium, ChestArmorEpic, ChestArmorBoss;

        public static MelonPreferences_Entry<bool> GoblinDropsEnabled, GoblinDropsScaleWithPlayers;
        public static MelonPreferences_Entry<int> GoblinDrops, GoblinGuaranteedClass;
        public static MelonPreferences_Entry<float> LootGoblinScale;

        public static MelonPreferences_Entry<bool> DropBeams, DropSparkles;
        public static MelonPreferences_Entry<int> BeamMinClass;
        public static MelonPreferences_Entry<string> DropLabels, DropSound;

        public static MelonPreferences_Entry<float> BagWeightCapacity;
        public static MelonPreferences_Entry<string> BagGesture;
        public static MelonPreferences_Entry<bool> AutoPickup;

        public static MelonPreferences_Entry<float> ShopPriceMultiplier, SellMultiplier, EnchantCostMultiplier, ArmorUpgradeCostMultiplier;
        public static MelonPreferences_Entry<int> ShopSlots, ShopRefreshMinutes;
        public static MelonPreferences_Entry<bool> LoadoutEnabled;

        public static MelonPreferences_Entry<float> GoldToTokenRate, BossKillBonus, MinibossKillBonus, HazardBonusPerLevel,
            TierBonusPerLevel, TokenPlayerBonusPerExtraPlayer, TokenChestBonusPerChest;

        public static MelonPreferences_Entry<float> BoothX, BoothY, BoothZ, BoothYaw;
        public static MelonPreferences_Entry<bool> BoothPlaced;

        public static MelonPreferences_Entry<bool> DevTools, VerboseLogging, RunLogging;
        public static MelonPreferences_Entry<int> DropPlayersOverride, ConfigVersion;

        /// <summary>What used to be settings and is now fixed.</summary>
        public static class Fixed
        {
            public const float ChestArmorDistance = 0.2f;
            public const float ChestArmorDelay = 2f;
            public const float ChestDropSpread = 35f;
            public const float ChestDropStagger = 0.35f;
            public const float AutoPickupMeters = 0.6f;
            public const float BagAutoCloseMeters = 2f;
            public const float BagGestureHoldSeconds = 0.7f;
            public const string PedestalFrameColor = "#FFD24A";
            public const float TokenToastSeconds = 5f;
            public const float TokenToastRepeatSeconds = 2f;
        }

        public static bool LabelsOn => !string.Equals(DropLabels.Value, "off", StringComparison.OrdinalIgnoreCase);
        public static bool LabelsOnHover => string.Equals(DropLabels.Value, "hover", StringComparison.OrdinalIgnoreCase);
        public static bool SoundsOn => !string.Equals(DropSound.Value, "off", StringComparison.OrdinalIgnoreCase);

        public static void Load()
        {
            var old = ReadOldFile();

            Main = MelonPreferences.CreateCategory("LootOverhaul");
            Enabled = Main.CreateEntry("Enabled", true, description: "Master switch for the whole mod.");

            ChestArmor = MelonPreferences.CreateCategory("LootOverhaul_ChestArmor");
            ChestArmorEnabled = ChestArmor.CreateEntry("ChestArmorEnabled", true, description: "Chests drop armor on the floor when they open. Mimics count as a chest of their size. Never in the crypt.");
            ChestArmorSmall = ChestArmor.CreateEntry("ChestArmorSmall", 0.10f, description: "Chance, 0-1, that a small chest drops a Common piece.");
            ChestArmorMedium = ChestArmor.CreateEntry("ChestArmorMedium", 0.25f, description: "Chance, 0-1, that a medium chest drops a Unique piece.");
            ChestArmorEpic = ChestArmor.CreateEntry("ChestArmorEpic", 1.0f, description: "Chance, 0-1, that an epic chest drops a Rare piece.");
            ChestArmorBoss = ChestArmor.CreateEntry("ChestArmorBoss", 1.0f, description: "Chance, 0-1, that a boss chest drops a Legendary piece.");
            ChestArmorScalesWithPlayers = ChestArmor.CreateEntry("ChestArmorScalesWithPlayers", true, description: "Roll once for every player in the room, so more players find more pieces. The pieces are shared: anyone can pick them up.");

            Goblin = MelonPreferences.CreateCategory("LootOverhaul_Goblin");
            GoblinDropsEnabled = Goblin.CreateEntry("GoblinDropsEnabled", true, description: "The loot goblin drops a pile of armor when it dies.");
            GoblinDrops = Goblin.CreateEntry("GoblinDrops", 2, description: "Armor pieces in each goblin pile.");
            GoblinGuaranteedClass = Goblin.CreateEntry("GoblinGuaranteedClass", 1, description: "Lowest rarity of each pile's first piece: 0 Common, 1 Unique, 2 Rare, 3 Legendary.");
            GoblinDropsScaleWithPlayers = Goblin.CreateEntry("GoblinDropsScaleWithPlayers", true, description: "One pile for every player in the room. The piles are shared: anyone can pick them up.");
            LootGoblinScale = Goblin.CreateEntry("LootGoblinScale", 1.1f, description: "Size of the loot goblin (1 = the game's size).");

            FloorLoot = MelonPreferences.CreateCategory("LootOverhaul_FloorLoot");
            DropBeams = FloorLoot.CreateEntry("DropBeams", true, description: "A rarity-coloured beam of light over armor on the floor.");
            BeamMinClass = FloorLoot.CreateEntry("BeamMinClass", 2, description: "Lowest rarity that gets a beam: 0 Common, 1 Unique, 2 Rare, 3 Legendary.");
            DropLabels = FloorLoot.CreateEntry("DropLabels", "off", description: "Name over loot on the floor: off, hover (only while you look at it or reach for it), or always.");
            DropSparkles = FloorLoot.CreateEntry("DropSparkles", true, description: "The coin pile's sparkle over loot on the floor.");
            DropSound = FloorLoot.CreateEntry("DropSound", "finish", description: "Sound when armor lands: start, finish, collected (three coin-pile sounds), or off for silence.");

            Bag = MelonPreferences.CreateCategory("LootOverhaul_Bag");
            BagWeightCapacity = Bag.CreateEntry("BagWeightCapacity", 60f, description: "How much the bag holds, in weight units.");
            BagGesture = Bag.CreateEntry("BagGesture", "stick-hold", description: "How the bag opens in VR: stick-hold (hold the right stick up), back-grip (right hand behind your back, grip and push the stick up), or off.");
            AutoPickup = Bag.CreateEntry("AutoPickup", false, description: "Walk over tonics, trinkets and armor to put them in the bag without grabbing them.");

            Shop = MelonPreferences.CreateCategory("LootOverhaul_Shop");
            ShopPriceMultiplier = Shop.CreateEntry("ShopPriceMultiplier", 2.5f, description: "Scales every kobold price: shop stock, tonics, enchanting and armor upgrades.");
            SellMultiplier = Shop.CreateEntry("SellMultiplier", 1.0f, description: "Tokens per point of an item's value when you sell it to the kobold.");
            ShopSlots = Shop.CreateEntry("ShopSlots", 6, description: "How many weapons the kobold keeps in stock.");
            ShopRefreshMinutes = Shop.CreateEntry("ShopRefreshMinutes", 60, description: "Minutes before the stock is rolled again for free.");
            EnchantCostMultiplier = Shop.CreateEntry("EnchantCostMultiplier", 1.0f, description: "Enchanting price on top of ShopPriceMultiplier.");
            ArmorUpgradeCostMultiplier = Shop.CreateEntry("ArmorUpgradeCostMultiplier", 1.0f, description: "Armor upgrade price on top of ShopPriceMultiplier. Every +1% costs 750 / 1,500 / 3,000 / 6,000 tokens by rarity at the defaults.");
            LoadoutEnabled = Shop.CreateEntry("LoadoutEnabled", true, description: "Keep the loadout picked at the booth in your holsters.");

            TokenPay = MelonPreferences.CreateCategory("LootOverhaul_Tokens");
            GoldToTokenRate = TokenPay.CreateEntry("GoldToTokenRate", 0.5f, description: "Tokens per gold earned in a run, before the bonuses below.");
            BossKillBonus = TokenPay.CreateEntry("BossKillBonus", 0.5f, description: "Extra payout per boss killed. 0.5 = +50%.");
            MinibossKillBonus = TokenPay.CreateEntry("MinibossKillBonus", 0.1f, description: "Extra payout per mini-boss killed.");
            HazardBonusPerLevel = TokenPay.CreateEntry("HazardBonusPerLevel", 0.25f, description: "Extra payout per hazard level of the run (0-3).");
            TierBonusPerLevel = TokenPay.CreateEntry("TierBonusPerLevel", 0.1f, description: "Extra payout per dungeon tier above the first.");
            TokenPlayerBonusPerExtraPlayer = TokenPay.CreateEntry("TokenPlayerBonusPerExtraPlayer", 0.5f, description: "Extra payout per party member beyond you.");
            TokenChestBonusPerChest = TokenPay.CreateEntry("TokenChestBonusPerChest", 0.02f, description: "Extra payout per chest you opened.");

            BoothPos = MelonPreferences.CreateCategory("LootOverhaul_Booth");
            BoothX = BoothPos.CreateEntry("BoothX", Loot.Booth.DefaultPosition.x);
            BoothY = BoothPos.CreateEntry("BoothY", Loot.Booth.DefaultPosition.y);
            BoothZ = BoothPos.CreateEntry("BoothZ", Loot.Booth.DefaultPosition.z);
            BoothYaw = BoothPos.CreateEntry("BoothYaw", Loot.Booth.DefaultYaw, description: "Set by pressing = in the lobby: the booth moves to where you stand, facing you.");
            BoothPlaced = BoothPos.CreateEntry("BoothPlaced", false, description: "False: the booth stands at its built-in spot.");

            Dev = MelonPreferences.CreateCategory("LootOverhaul_Dev");
            DevTools = Dev.CreateEntry("DevTools", false, description: "Recon transcript in UserData/LootOverhaul/recon/, the profile-write watch and the survey hotkeys.");
            VerboseLogging = Dev.CreateEntry("VerboseLogging", false, description: "More detail in the MelonLoader console, the recon transcript included.");
            RunLogging = Dev.CreateEntry("RunLogging", false, description: "One row per run in UserData/LootOverhaul/runs.csv.");
            DropPlayersOverride = Dev.CreateEntry("DropPlayersOverride", 0, description: "Testing: count this many players for chest armor and goblin piles. 0 = the real room size.");
            ConfigVersion = Dev.CreateEntry("ConfigVersion", 0, description: "Internal. Do not edit.");

            if (ConfigVersion.Value < Layout) Migrate(old);
        }

        private static readonly string[] OldMainKeys =
        {
            "EnemyDropsEnabled", "BaseDropChance", "BagWeightCapacity", "BossDropChance", "BossDrops", "MiniBossDrops", "BossGuaranteedClass",
            "BossDropsPerExtraPlayer", "MiniBossDropsPerExtraPlayer", "BossDropsPerExtraHealthBar", "EliteBossExtraDrops", "EliteDrops", "LegendDrops",
            "GoblinDrops", "GoblinGuaranteedClass", "GoblinJunkRolls", "GoblinJunk", "ArmorTint", "LootGoblinScale", "SandboxDrops", "PedestalFrame",
            "PedestalFrameColor", "LegendaryPityKills", "DropChancePerExtraPlayer", "BeamMinClass", "DropBeams", "DropLabels", "DropLabelsOnHover",
            "DropSparkles", "DropOutline", "DropSounds", "DropChime", "BagAutoCloseMeters", "JunkAutoPickup", "JunkAutoPickupMeters", "AutoPickupArmor",
            "TossJunkWhenFull", "EnchantCostMultiplier", "ArmorUpgradeCostMultiplier", "PanelLaser", "BagGesture", "BagGestureHoldSeconds",
            "WeightCommon", "WeightUnique", "WeightRare", "WeightLegendary", "JunkDropChance", "TierUpChance", "ArmorShare", "ChestArmorEnabled",
            "ChestArmorSmall", "ChestArmorMedium", "ChestArmorEpic", "ChestArmorBoss", "ChestArmorDistance", "ChestArmorDelay",
            "ChestArmorScalesWithPlayers", "ChestDropSpread", "ChestDropStagger", "GoblinDropsEnabled", "GoblinDropsScaleWithPlayers",
            "SellMultiplier", "ShopPriceMultiplier", "ShopSlots", "ShopRefreshMinutes", "BoothX", "BoothY", "BoothZ", "BoothYaw", "BoothPlaced",
            "LoadoutEnabled", "GoldToTokenRate", "BossKillBonus", "MinibossKillBonus", "HazardBonusPerLevel", "TierBonusPerLevel",
            "TokenPlayerBonusPerExtraPlayer", "TokenChestBonusPerChest", "TokenToastSeconds", "TokenToastRepeatSeconds",
        };

        private static readonly string[] OldDevKeys = { "ReconEnabled", "ProfileWatchEnabled", "HotkeysEnabled", "MirrorReconToConsole" };

        /// <summary>
        /// Layout 11 (0.11.0): every value a player kept moves from the old flat [LootOverhaul]
        /// section into its new section, the merged switches are worked out from the old pairs,
        /// and the keys that no longer exist are removed from the file.
        /// </summary>
        private static void Migrate(Dictionary<string, Dictionary<string, string>> old)
        {
            old.TryGetValue("LootOverhaul", out var m);
            old.TryGetValue("LootOverhaul_Dev", out var d);
            m ??= new Dictionary<string, string>();
            d ??= new Dictionary<string, string>();
            var carried = 0;

            void Carry<T>(MelonPreferences_Entry<T> entry, Dictionary<string, string> from)
            {
                if (!from.TryGetValue(entry.Identifier, out var raw) || !TryParse(raw, out T value)) return;
                entry.Value = value;
                carried++;
            }

            foreach (var e in new[] { ChestArmorEnabled, ChestArmorScalesWithPlayers, GoblinDropsEnabled, GoblinDropsScaleWithPlayers, DropBeams, DropSparkles, LoadoutEnabled, BoothPlaced })
                Carry(e, m);
            foreach (var e in new[] { ChestArmorSmall, ChestArmorMedium, ChestArmorEpic, ChestArmorBoss, LootGoblinScale, BagWeightCapacity, ShopPriceMultiplier,
                                      SellMultiplier, EnchantCostMultiplier, ArmorUpgradeCostMultiplier, GoldToTokenRate, BossKillBonus, MinibossKillBonus,
                                      HazardBonusPerLevel, TierBonusPerLevel, TokenPlayerBonusPerExtraPlayer, TokenChestBonusPerChest,
                                      BoothX, BoothY, BoothZ, BoothYaw })
                Carry(e, m);
            foreach (var e in new[] { GoblinDrops, GoblinGuaranteedClass, BeamMinClass, ShopSlots, ShopRefreshMinutes })
                Carry(e, m);
            Carry(BagGesture, m);

            if (m.TryGetValue("DropLabels", out var lr) && TryParse(lr, out bool labels))
            {
                var hover = !m.TryGetValue("DropLabelsOnHover", out var hr) || !TryParse(hr, out bool h) || h;
                DropLabels.Value = !labels ? "off" : hover ? "hover" : "always";
                carried++;
            }
            if (m.TryGetValue("DropSounds", out var sr) && TryParse(sr, out bool sounds))
            {
                var chime = m.TryGetValue("DropChime", out var cr) && TryParse(cr, out string c) ? c : "start";
                DropSound.Value = sounds ? chime : "off";
                carried++;
            }
            if (m.TryGetValue("JunkAutoPickup", out var ar) && TryParse(ar, out bool auto)) { AutoPickup.Value = auto; carried++; }
            if (d.TryGetValue("ReconEnabled", out var rr) && TryParse(rr, out bool recon)) { DevTools.Value = recon; carried++; }
            if (d.TryGetValue("MirrorReconToConsole", out var mr) && TryParse(mr, out bool mirror) && mirror) VerboseLogging.Value = true;

            var removed = 0;
            foreach (var key in OldMainKeys) if (m.ContainsKey(key) && SafeDelete(Main, key)) removed++;
            foreach (var key in OldDevKeys) if (d.ContainsKey(key) && SafeDelete(Dev, key)) removed++;

            ConfigVersion.Value = Layout;
            MelonPreferences.Save();
            MelonLogger.Msg(m.Count == 0
                ? $"LootOverhaul config layout {Layout}: fresh settings written."
                : $"LootOverhaul config migrated to layout {Layout}: {carried} value(s) moved into the new sections, {removed} old key(s) removed.");
        }

        private static bool SafeDelete(MelonPreferences_Category cat, string key)
        {
            try { return cat.DeleteEntry(key); } catch { return false; }
        }

        private static bool TryParse<T>(string raw, out T value)
        {
            value = default;
            raw = raw.Trim();
            object parsed = null;
            if (typeof(T) == typeof(bool)) { if (bool.TryParse(raw, out var b)) parsed = b; }
            else if (typeof(T) == typeof(int)) { if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) parsed = i; }
            else if (typeof(T) == typeof(float)) { if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) parsed = f; }
            else if (typeof(T) == typeof(string)) parsed = raw.Length >= 2 && (raw[0] == '"' || raw[0] == '\'') ? raw.Substring(1, raw.Length - 2) : raw;
            if (parsed == null) return false;
            value = (T)parsed;
            return true;
        }

        /// <summary>The cfg as it was on disk at start, by section, before this layout's entries exist.</summary>
        private static Dictionary<string, Dictionary<string, string>> ReadOldFile()
        {
            var result = new Dictionary<string, Dictionary<string, string>>();
            try
            {
                var path = Path.Combine(MelonEnvironment.UserDataDirectory, "MelonPreferences.cfg");
                if (!File.Exists(path)) return result;
                Dictionary<string, string> section = null;
                foreach (var line in File.ReadAllLines(path))
                {
                    var t = line.Trim();
                    if (t.Length == 0 || t[0] == '#') continue;
                    if (t[0] == '[' && t[t.Length - 1] == ']')
                    {
                        var name = t.Substring(1, t.Length - 2).Trim().Trim('"');
                        if (!result.TryGetValue(name, out section)) result[name] = section = new Dictionary<string, string>();
                        continue;
                    }
                    var eq = t.IndexOf('=');
                    if (section == null || eq <= 0) continue;
                    section[t.Substring(0, eq).Trim().Trim('"')] = t.Substring(eq + 1).Trim();
                }
            }
            catch (Exception e) { MelonLogger.Warning($"LootOverhaul could not read the old settings: {e.Message}"); }
            return result;
        }
    }
}
