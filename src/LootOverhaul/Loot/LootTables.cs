using System;
using System.Collections.Generic;

namespace LootOverhaul.Loot
{
    /// <summary>
    /// The mod's own numbers: which weapon types can drop, what they weigh, how enemy class
    /// scales the drop chance. Everything the game already knows (damage, cost, salvage,
    /// rarity odds per realm) is read from the game instead.
    /// </summary>
    public static class LootTables
    {
        // Prop.Type values. LongAxe (22) is excluded: the generator throws on it (recon 2026-09-02).
        public const int Shield = 1, Axe = 2, Sword = 3, Dagger = 4, Spear = 5, Bow = 6, Staff = 14,
                         Crossbow = 20, Longsword = 21, LongAxe = 22, Hammer = 23;

        public static readonly int[] DroppableTypes = { Sword, Axe, Dagger, Spear, Bow, Staff, Crossbow, Longsword, Hammer, Shield };

        private static readonly Dictionary<int, float> BaseWeight = new Dictionary<int, float>
        {
            { Dagger, 1f }, { Bow, 2f }, { Sword, 3f }, { Axe, 3f }, { Staff, 3f },
            { Hammer, 4f }, { Crossbow, 4f }, { Spear, 4f }, { Shield, 5f }, { Longsword, 5f }, { LongAxe, 6f },
        };

        /// <summary>Weight units for a weapon: base by type plus half a unit per tier above the first.</summary>
        public static float Weight(int propType, int tier0)
        {
            BaseWeight.TryGetValue(propType, out var w);
            if (w <= 0f) w = 3f;
            return w + 0.5f * Math.Max(0, tier0);
        }

        /// <summary>Drop-chance multiplier by the game's <c>AI.Type</c> (Light=0 … Gold=8).</summary>
        public static float EnemyMultiplier(int aiType) => aiType switch
        {
            0 => 1.0f,   // Light
            1 => 1.5f,   // Medium
            2 => 2.5f,   // Heavy
            3 => 4.0f,   // Elite
            4 => 6.0f,   // Legend
            5 => 2.0f,   // Fire
            6 => 2.0f,   // Ice
            7 => 2.0f,   // Poison
            8 => 3.0f,   // Gold
            _ => 1.0f,
        };

        /// <summary>
        /// A trinket the broker will buy. Rides on a harmless vanilla prop for its floor body.
        /// Tiers: 0 trinket (grey), 1 curio (white), 2 artifact (gold).
        /// </summary>
        public class Junk
        {
            public string Name; public string Prefab; public int Tier; public int MinValue, MaxValue; public float Weight;
            public Junk(string name, string prefab, int tier, int min, int max, float weight) { Name = name; Prefab = prefab; Tier = tier; MinValue = min; MaxValue = max; Weight = weight; }
        }

        // Prefab names are the pool keys seen in the game's strings; which of them the
        // networked pool will actually instantiate is learned at runtime (SpawnLoot logs a
        // failure and the roller stops picking that prefab for the session).
        // Names match the model they ride on. `Bone` and `Bones` refused to spawn (2026-09-02);
        // Wolf_Treat spawned. Dice and the two trophies are unconfirmed — the `-` probe tests
        // more candidates and the roller retires any prefab that refuses.
        // Prefab names are the game's Resources paths (docs/RESOURCE-PATHS.txt). All of these
        // spawn and can be picked up (runs of 2026-09-02/03). Retired: Hockey_Puck (a flat disc
        // that sinks into the floor and cannot be grabbed) and Xmas_Ornament (a BreakableProp,
        // not grabbable). The roller still retires a prefab that refuses, and the `-` probe
        // tests candidates one by one. The namer picks the words; these rows pick body, tier,
        // value range and weight. Rocks_01 retired 2026-09-05: it spawned with nothing visible.
        public static readonly Junk[] JunkTable =
        {
            // trinkets (grey)
            new Junk("Dog Treat",           "Wolf_Treat",           0, 2,   8,   0.3f),
            new Junk("Chipped Dice",        "Dice",                 0, 4,   14,  0.2f),
            new Junk("Clay Mug",            "Mug_01",               0, 3,   12,  0.5f),
            new Junk("Cracked Tankard",     "Mug_02",               0, 3,   12,  0.5f),
            new Junk("Tarnished Chalice",   "Chalice",              0, 8,   25,  0.6f),
            new Junk("Bent Spoon",          "Tools/Spoon_02",       0, 2,   9,   0.1f),
            // curios (white)
            new Junk("Weighted Dice",       "Dice",                 1, 25,  70,  0.2f),
            new Junk("Engraved Tankard",    "Mug_02",               1, 30,  90,  0.6f),
            new Junk("Silver Chalice",      "Tools/Chalice_Silver", 1, 50,  140, 0.7f),
            new Junk("Silver Spoon",        "Tools/Spoon_02",       1, 20,  60,  0.1f),
            new Junk("Iron Ingot",          "Tools/Metalbar_01",    1, 30,  80,  2.0f),
            new Junk("Smith's Hammer",      "Tools/Hammer_01",      1, 25,  70,  1.2f),
            new Junk("Skull Crown",         "Trophy_SkullCrown",    1, 60,  160, 1.5f),
            new Junk("Guild Trophy",        "Trophy_NovaGuild",     1, 50,  140, 1.2f),
            new Junk("Marrow Charm",        "Wolf_Treat",           1, 30,  80,  0.4f),
            // artifacts (gold)
            new Junk("Jeweled Chalice",     "Chalice",              2, 250, 600, 0.6f),
            new Junk("Rune Stone",          "Rune_01",              2, 200, 550, 0.8f),
            new Junk("Gilded Skull Crown",  "Trophy_SkullCrown",    2, 200, 500, 1.5f),
            new Junk("Religious Icon",      "Trophy_NovaGuild",     2, 150, 400, 1.2f),
            new Junk("Reliquary Chest",     "Trophy_Chest_01",      2, 180, 450, 2.0f),
            new Junk("Gambler's Relic",     "Dice",                 2, 180, 450, 0.2f),
        };

        public static string JunkTierName(int tier) => tier switch { 0 => "trinket", 1 => "curio", _ => "artifact" };
        public static string JunkColor(int tier) => tier switch { 0 => "#9A9A9A", 1 => "#E8E8E8", _ => "#F5C542" };

        /// <summary>Junk tier roll: mostly trinkets.</summary>
        public static int RollJunkTier(double r) => r < 0.70 ? 0 : r < 0.94 ? 1 : 2;

        /// <summary>The game's <c>AI.Type</c> names, the strength ranks the skulls over an enemy stand for.</summary>
        public static string AiTypeName(int aiType) => aiType switch
        {
            0 => "Light", 1 => "Medium", 2 => "Heavy", 3 => "Elite", 4 => "Legend", 5 => "Fire", 6 => "Ice", 7 => "Poison", 8 => "Gold", _ => $"type{aiType}",
        };

        public static string ClassName(int weaponClass) => weaponClass switch
        {
            0 => "Common", 1 => "Unique", 2 => "Rare", 3 => "Legendary", 4 => "Mythic", _ => "?",
        };

        public static string TypeName(int propType) => propType switch
        {
            Shield => "Shield", Axe => "Axe", Sword => "Sword", Dagger => "Dagger", Spear => "Spear", Bow => "Bow",
            Staff => "Staff", Crossbow => "Crossbow", Longsword => "Longsword", LongAxe => "LongAxe", Hammer => "Hammer",
            _ => $"type{propType}",
        };
    }
}
