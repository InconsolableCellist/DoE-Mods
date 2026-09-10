using System;
using System.Collections.Generic;
using System.Globalization;
using LootOverhaul.Gate;
using LootOverhaul.Recon;

namespace LootOverhaul.Loot
{
    /// <summary>
    /// Armor: a worn piece that carries one to three exosuit-stat bonuses, permanent while
    /// worn (tonics are the same mechanism for one run). Three slots: head, chest, legs.
    /// Stats are drawn only from perks the player has unlocked (lock-step). The floor body
    /// is a placeholder prop until cosmetic meshes are wired in. Names are rolled like junk:
    /// material × piece × suffix by rarity.
    /// </summary>
    public static class Armor
    {
        public const int Head = 0, Chest = 1, Legs = 2;
        public static readonly string[] SlotNames = { "Head", "Chest", "Legs" };
        private static readonly string[][] Pieces =
        {
            new[] { "Helm", "Coif", "Cap", "Hood", "Circlet", "Sallet", "Barbute" },
            new[] { "Cuirass", "Jerkin", "Hauberk", "Brigandine", "Breastplate", "Tunic", "Vest" },
            new[] { "Greaves", "Leggings", "Chausses", "Boots", "Sabatons", "Breeches", "Tassets" },
        };
        private static readonly (string, float)[][] Materials =
        {
            new[] { ("Leather", 1.0f), ("Padded", 0.9f), ("Hide", 0.9f), ("Studded Leather", 1.1f), ("Bronze", 1.1f) },
            new[] { ("Chain", 1.2f), ("Scale", 1.3f), ("Iron", 1.2f), ("Boiled Leather", 1.1f), ("Bone", 1.2f) },
            new[] { ("Steel", 1.4f), ("Silvered", 1.5f), ("Elven", 1.6f), ("Dwarven", 1.6f), ("Blackiron", 1.5f) },
            new[] { ("Mithril", 2.0f), ("Dragonscale", 2.2f), ("Moonsteel", 2.0f), ("Runeforged", 2.1f), ("Starmetal", 2.3f) },
        };
        private static readonly string[] Suffixes =
        {
            "of the Owl", "of the Bear", "of the Fox", "of the Ox", "of the Hawk", "of the Wolf", "of the Stag",
            "of Quiet Steps", "of the Deep Vault", "of the Last Abbot", "of the Bone King", "of the First Expedition",
            "of Warding", "of Vigour", "of the Unbroken", "of the Sewer Kings",
        };
        private static readonly float[] BaseWeight = { 1.5f, 4.0f, 3.0f };
        private static readonly int[] BaseValue = { 40, 120, 300, 800 };

        /// <summary>Number of stat bonuses by rarity and their strength band.</summary>
        private static int StatCount(int cls) => cls switch { 0 => 1, 1 => 1, 2 => 2, _ => 3 };
        private static (float lo, float hi) Band(int cls) => cls switch { 0 => (1.04f, 1.08f), 1 => (1.06f, 1.12f), 2 => (1.10f, 1.18f), _ => (1.15f, 1.28f) };

        /// <summary>Exosuit stats armor may carry: the unlocked perks that are not "reduction" stats.</summary>
        public static List<string> EligibleStats()
        {
            var list = new List<string>();
            // Inverted stats (less damage, slower drain) are fine on armor: the buff divides for those.
            foreach (var d in Buffs.Catalogue) if (Unlocks.PerkUnlocked(d.Stat)) list.Add(d.Stat);
            return list;
        }

        public static LootItem Roll(int cls, int realm, System.Random rng)
        {
            var stats = EligibleStats();
            if (stats.Count == 0) return null;
            cls = Math.Max(0, Math.Min(3, cls));
            var slot = rng.Next(3);
            var piece = Pieces[slot][rng.Next(Pieces[slot].Length)];
            var mat = Materials[cls][rng.Next(Materials[cls].Length)];
            var name = $"{mat.Item1} {piece}";
            if (cls >= 2 || (cls == 1 && rng.NextDouble() < 0.4)) name += " " + Suffixes[rng.Next(Suffixes.Length)];

            var chosen = new List<(string stat, float mult)>();
            var pool = new List<string>(stats);
            var (lo, hi) = Band(cls);
            for (var i = 0; i < StatCount(cls) && pool.Count > 0; i++)
            {
                var st = pool[rng.Next(pool.Count)]; pool.Remove(st);
                var m = (float)Math.Round(lo + rng.NextDouble() * (hi - lo), 3);
                chosen.Add((st, m));
            }
            var item = new LootItem
            {
                Kind = "armor",
                PrefabName = "Trophy_Chest_01",   // placeholder body: a bundle on the floor
                Name = name,
                ColoredName = $"<color={ClassColor(cls)}>{name}</color>",
                WeaponClass = cls,
                ArmorSlot = slot,
                ArmorStats = Encode(chosen),
                Weight = BaseWeight[slot] * (cls >= 2 ? 1.2f : 1f),
                Value = (int)Math.Round(BaseValue[cls] * mat.Item2 * (0.8 + rng.NextDouble() * 0.4)),
                PropType = -1,
                FoundInRealm = realm,
            };
            return item;
        }

        public static string ClassColor(int cls) => cls switch { 0 => "#4EA2FF", 1 => "#31BC2F", 2 => "#1020FF", _ => "#4010FF" };

        public static string Encode(List<(string stat, float mult)> stats)
        {
            var parts = new List<string>();
            foreach (var (st, m) in stats) parts.Add($"{st}:{m.ToString("R", CultureInfo.InvariantCulture)}");
            return string.Join(";", parts);
        }

        public static List<(string stat, float mult)> Decode(string s)
        {
            var list = new List<(string, float)>();
            if (string.IsNullOrEmpty(s)) return list;
            foreach (var part in s.Split(';'))
            {
                var i = part.IndexOf(':');
                if (i <= 0) continue;
                if (float.TryParse(part.Substring(i + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var m)) list.Add((part.Substring(0, i), m));
            }
            return list;
        }

        public static string DescribeStats(LootItem item)
        {
            var parts = new List<string>();
            foreach (var (st, m) in Decode(item.ArmorStats)) { var d = Buffs.Find(st); parts.Add($"{(d == null ? st : d.Flavor)} {(d != null && d.Invert ? "÷" : "×")}{m:0.00}"); }
            return string.Join(", ", parts);
        }

        // ---- wearing ------------------------------------------------------------------------

        public static LootItem Worn(int slot)
        {
            var inv = BagManager.Inventory;
            foreach (var i in inv.Items) if (i.Kind == "armor" && i.WornSlot == slot) return i;
            return null;
        }

        public static void Wear(LootItem item)
        {
            var inv = BagManager.Inventory;
            var live = inv.Find(item.Id);
            if (live == null || live.Kind != "armor") { BagManager.Toast("That's gone."); return; }
            var old = Worn(live.ArmorSlot);
            if (old != null) old.WornSlot = -1;
            live.WornSlot = live.ArmorSlot;
            inv.Save();
            Buffs.RebuildWorn();
            BagManager.Toast($"Wearing {live.ColoredName}: {DescribeStats(live)}");
            if (Buffs.LegPerksGatedOff() && (live.ArmorStats ?? "").Contains("Legs_"))
                BagManager.Toast("The game has friendly fire on here (the sandbox does this): it ignores every jump and leap perk until it is off.");
            ReconLog.Line($"armor: wear {live.Name} [{SlotNames[live.ArmorSlot]}] {DescribeStats(live)}");
            BagPanel.Refresh(); Booth.Refresh();
        }

        public static void Remove(LootItem item)
        {
            var inv = BagManager.Inventory;
            var live = inv.Find(item.Id);
            if (live == null) return;
            live.WornSlot = -1;
            inv.Save();
            Buffs.RebuildWorn();
            BagManager.Toast($"Took off {live.ColoredName}");
            BagPanel.Refresh(); Booth.Refresh();
        }

        /// <summary>Stat → product of all worn multipliers.</summary>
        public static Dictionary<string, float> WornMultipliers()
        {
            var result = new Dictionary<string, float>();
            var inv = BagManager.Inventory;
            foreach (var i in inv.Items)
            {
                if (i.Kind != "armor" || i.WornSlot < 0) continue;
                foreach (var (st, m) in Decode(i.ArmorStats))
                {
                    result.TryGetValue(st, out var cur);
                    result[st] = (cur <= 0f ? 1f : cur) * m;
                }
            }
            return result;
        }

        public static string DescribeWorn()
        {
            var parts = new List<string>();
            for (var s = 0; s < 3; s++) { var w = Worn(s); if (w != null) parts.Add(w.Name); }
            return parts.Count == 0 ? "" : string.Join(", ", parts);
        }
    }
}
