using System;
using System.Collections.Generic;

namespace LootOverhaul.Loot
{
    /// <summary>
    /// Thousands of trinkets from a dozen bodies: adjective × material × noun × provenance,
    /// pooled by tier, with value multipliers riding on the material and the adjective.
    /// "Tarnished Pewter Tankard", "Engraved Silver Goblet of the Vile Halls", "Runic
    /// Moonstone Reliquary of the Bone King". Names are rolled on the master and travel in
    /// the loot record, so every client shows the same one.
    /// </summary>
    public static class JunkNamer
    {
        private static readonly Dictionary<string, string[]> Nouns = new Dictionary<string, string[]>
        {
            { "Mug_01",               new[] { "Mug", "Tankard", "Stein", "Cup" } },
            { "Mug_02",               new[] { "Tankard", "Mug", "Flagon", "Cup" } },
            { "Chalice",              new[] { "Chalice", "Goblet", "Cup", "Grail" } },
            { "Tools/Chalice_Silver", new[] { "Chalice", "Goblet", "Cup" } },
            { "Dice",                 new[] { "Dice", "Knucklebones", "Gaming Dice" } },
            { "Wolf_Treat",           new[] { "Bone", "Marrow Bone", "Knuckle" } },
            { "Rocks_01",             new[] { "Stone", "Rock", "Pebble", "Geode" } },
            { "Hockey_Puck",          new[] { "Disc", "Token", "Seal", "Medallion" } },
            { "Tools/Spoon_02",       new[] { "Spoon", "Ladle" } },
            { "Tools/Metalbar_01",    new[] { "Ingot", "Bar" } },
            { "Tools/Hammer_01",      new[] { "Hammer", "Mallet" } },
            { "Tools/Pincers_01",     new[] { "Pincers", "Tongs" } },
            { "Tools/Shovel_01",      new[] { "Shovel", "Spade" } },
            { "Xmas_Ornament",        new[] { "Bauble", "Orb", "Glass Ball" } },
            { "Trophy_SkullCrown",    new[] { "Skull Crown", "Crowned Skull" } },
            { "Trophy_NovaGuild",     new[] { "Guild Trophy", "Icon", "Idol" } },
            { "Trophy_Chest_01",      new[] { "Reliquary", "Casket", "Coffer" } },
            { "Trophy_Zombie_01",     new[] { "Death Mask", "Effigy" } },
            { "Rune_01",              new[] { "Rune Stone", "Rune Tablet", "Runestone" } },
        };

        // (word, value multiplier)
        private static readonly (string, float)[][] Adjectives =
        {
            new[] { ("Chipped", 0.8f), ("Cracked", 0.7f), ("Tarnished", 0.9f), ("Dented", 0.8f), ("Grimy", 0.7f), ("Worn", 0.9f), ("Bent", 0.8f), ("Rusty", 0.7f), ("Stained", 0.8f), ("Plain", 1.0f), ("Crude", 0.8f), ("Old", 1.0f), ("Chewed", 0.6f), ("Muddy", 0.7f) },
            new[] { ("Polished", 1.1f), ("Engraved", 1.3f), ("Etched", 1.2f), ("Fine", 1.1f), ("Ornate", 1.4f), ("Sturdy", 1.0f), ("Gleaming", 1.2f), ("Elegant", 1.3f), ("Carved", 1.2f), ("Inlaid", 1.4f), ("Lacquered", 1.2f), ("Burnished", 1.2f) },
            new[] { ("Gilded", 1.3f), ("Ancient", 1.5f), ("Jeweled", 1.8f), ("Enchanted", 1.6f), ("Royal", 1.7f), ("Sacred", 1.5f), ("Blessed", 1.4f), ("Cursed", 1.4f), ("Runic", 1.6f), ("Dwarven", 1.5f), ("Elven", 1.5f), ("Forgotten", 1.4f), ("Sovereign", 1.9f) },
        };

        private static readonly (string, float)[][] Materials =
        {
            new[] { ("Clay", 0.8f), ("Tin", 0.9f), ("Pewter", 1.1f), ("Wooden", 0.8f), ("Bone", 1.0f), ("Iron", 1.2f), ("Copper", 1.3f), ("Lead", 0.9f), ("", 1.0f) },
            new[] { ("Silver", 1.5f), ("Brass", 1.1f), ("Bronze", 1.2f), ("Jade", 1.6f), ("Ivory", 1.5f), ("Steel", 1.2f), ("Ebony", 1.4f), ("Amber", 1.5f) },
            new[] { ("Gold", 1.8f), ("Platinum", 2.2f), ("Obsidian", 1.6f), ("Moonstone", 2.0f), ("Mithril", 2.5f), ("Dragonbone", 2.3f), ("Crystal", 1.7f), ("Starmetal", 2.4f) },
        };

        private static readonly string[] RealmSuffix =
            { "of the Underworld", "of the Sandstorm", "of the Vile Halls", "of the Lava Forge", "of Frostbound", "of Stormgrave" };

        private static readonly string[] GenericSuffix =
        {
            "of a Fallen Knight", "of the Nova Guild", "from a Drowned Tomb", "of the Bone King", "from the Goblin Hoard",
            "of a Nameless Mage", "of the Last Abbot", "from a Sunken Vault", "of the Sewer Kings", "of the Crystal Court",
            "of a Wandering Merchant", "from the Mimic's Belly", "of the First Expedition", "of the Wasp Queen", "of the Ossuary",
        };

        /// <summary>Roll a name and a value multiplier for a body at a tier. Realm is the game's Realm value or -1.</summary>
        public static (string name, float valueMult) Roll(string prefab, int tier, int realm, System.Random rng)
        {
            tier = Math.Max(0, Math.Min(2, tier));
            var nouns = Nouns.TryGetValue(prefab, out var n) ? n : new[] { "Trinket" };
            var noun = nouns[rng.Next(nouns.Length)];
            var adj = Adjectives[tier][rng.Next(Adjectives[tier].Length)];
            var mat = Materials[tier][rng.Next(Materials[tier].Length)];

            // The body already says what it is made of for some; skip a clashing material.
            if (prefab == "Tools/Chalice_Silver" && tier < 2) mat = ("Silver", 1.5f);
            if (prefab == "Wolf_Treat" || prefab == "Rocks_01") mat = ("", 1.0f);
            if (prefab == "Xmas_Ornament") mat = tier == 2 ? ("Crystal", 1.7f) : ("Glass", 1.0f);

            var parts = new List<string> { adj.Item1 };
            if (mat.Item1.Length > 0) parts.Add(mat.Item1);
            parts.Add(noun);
            var name = string.Join(" ", parts);

            var suffixChance = tier == 0 ? 0.08 : tier == 1 ? 0.35 : 0.85;
            if (rng.NextDouble() < suffixChance)
            {
                var useRealm = realm >= 0 && realm < RealmSuffix.Length && rng.NextDouble() < 0.4;
                name += " " + (useRealm ? RealmSuffix[realm] : GenericSuffix[rng.Next(GenericSuffix.Length)]);
            }
            return (name, adj.Item2 * mat.Item2);
        }
    }
}
