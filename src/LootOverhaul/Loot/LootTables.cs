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
