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

        /// <summary>One stat per piece; a Legendary carries two.</summary>
        private static int StatCount(int cls) => cls >= 3 ? 2 : 1;

        /// <summary>
        /// The most armor can give for each stat, in whole percent: half of what the perk gives at
        /// its top level in the game (the game's exosuit table: Power 1.0 + 0.1 per level up to
        /// level 5 = +50%, so armor tops out at +25%). The regeneration stats are measured from
        /// the perk's first level, the value the buff works from when the perk is not equipped.
        /// </summary>
        public static readonly Dictionary<string, int> StatCap = new Dictionary<string, int>
        {
            { "Arms_Power", 25 }, { "Arms_Critical", 100 }, { "Arms_Knockback", 50 }, { "Arms_Distance", 25 }, { "Arms_Farshot", 25 },
            { "Chest_Armor", 25 }, { "Chest_Ricochet", 25 }, { "Chest_Dispel", 25 }, { "Chest_Blast", 40 }, { "Chest_Antidote", 40 },
            { "Chest_Antifreeze", 25 }, { "Chest_Heal", 40 }, { "Chest_Vitality", 100 }, { "Chest_Resilience", 25 },
            { "Mind_Stillness", 50 }, { "Mind_Mystify", 25 },
            { "Legs_Haste", 25 }, { "Legs_Jump", 25 }, { "Legs_Leap", 25 }, { "Legs_Absorb", 40 }, { "Mind_Fortune", 10 },
        };

        /// <summary>
        /// The share of a stat's cap a drop rolls in, by rarity: 1-5 / 4-8 / 7-14 / 13-25% of the
        /// perk's top level, so a Legendary drop reaches half the cap. Upgrading can double the
        /// rarity's best roll, which takes a Legendary to the cap.
        /// </summary>
        private static (float lo, float hi) BandShare(int cls) => Math.Max(0, Math.Min(3, cls)) switch
        {
            0 => (0.02f, 0.10f), 1 => (0.08f, 0.16f), 2 => (0.14f, 0.28f), _ => (0.26f, 0.50f),
        };

        public static int Cap(string stat) => StatCap.TryGetValue(stat, out var c) ? c : 25;

        private static int ShareOfCap(string stat, float share) => (int)Math.Round(Cap(stat) * share, MidpointRounding.AwayFromZero);

        /// <summary>The whole-percent range a stat drops with on a piece of this rarity, never below 1%.</summary>
        public static (int lo, int hi) Band(int cls, string stat)
        {
            var (a, b) = BandShare(cls);
            var lo = Math.Max(1, ShareOfCap(stat, a));
            var hi = Math.Max(lo, ShareOfCap(stat, b));
            return (lo, hi);
        }

        /// <summary>How far upgrading can take a stat on a piece of this rarity: twice its best drop.</summary>
        public static int UpgradeCeiling(int cls, string stat) => Math.Max(Band(cls, stat).hi, ShareOfCap(stat, BandShare(cls).hi * 2f));

        public static bool IsInvert(string stat) { var d = Buffs.Find(stat); return d != null && d.Invert; }

        /// <summary>Whole percent to the stored multiplier: +16% is ×1.16, a 12% cut on a ÷ stat is ÷(1/0.88).</summary>
        public static float PctToMult(string stat, int pct) => IsInvert(stat) ? 1f / (1f - Math.Min(95, pct) / 100f) : 1f + pct / 100f;

        public static int MultToPct(string stat, float m)
        {
            if (m <= 0f) return 0;
            var raw = IsInvert(stat) ? (1f - 1f / m) * 100f : (m - 1f) * 100f;
            return (int)Math.Round(raw, MidpointRounding.AwayFromZero);
        }

        /// <summary>"+16%" for a boost, "-12%" for a cut.</summary>
        public static string PctText(string stat, int pct) => IsInvert(stat) ? $"-{pct}%" : $"+{pct}%";
        public static string PctText(string stat, float m) => PctText(stat, MultToPct(stat, m));

        /// <summary>
        /// Which stats each slot rolls, so no stat can sit on two worn pieces: head is offense,
        /// chest is defense, legs are utility (the game's exosuit groups its perks the same way:
        /// arms, chest, legs). The mind perks have no slot of their own: enemy aim and standing-still
        /// regeneration are defensive, run coins is a utility.
        /// </summary>
        private static readonly string[][] SlotStats =
        {
            new[] { "Arms_Power", "Arms_Critical", "Arms_Knockback", "Arms_Distance", "Arms_Farshot" },
            new[] { "Chest_Armor", "Chest_Ricochet", "Chest_Dispel", "Chest_Blast", "Chest_Antidote", "Chest_Antifreeze",
                    "Chest_Heal", "Chest_Vitality", "Chest_Resilience", "Mind_Stillness", "Mind_Mystify" },
            new[] { "Legs_Haste", "Legs_Jump", "Legs_Leap", "Legs_Absorb", "Mind_Fortune" },
        };

        /// <summary>The slot's stats. Every one can roll, whether or not the player has the perk.</summary>
        public static List<string> EligibleStats(int slot)
        {
            var list = new List<string>();
            if (slot < 0 || slot >= SlotStats.Length) return list;
            foreach (var st in SlotStats[slot]) { var d = Buffs.Find(st); if (d != null && !d.Retired) list.Add(st); }
            return list;
        }

        public static LootItem Roll(int cls, int realm, System.Random rng)
        {
            var open = new List<int>();
            for (var s = 0; s < 3; s++) if (EligibleStats(s).Count > 0) open.Add(s);
            if (open.Count == 0) return null;
            cls = Math.Max(0, Math.Min(3, cls));
            var slot = open[rng.Next(open.Count)];
            var stats = EligibleStats(slot);
            var piece = Pieces[slot][rng.Next(Pieces[slot].Length)];
            var mat = Materials[cls][rng.Next(Materials[cls].Length)];
            var name = $"{mat.Item1} {piece}";
            if (cls >= 2 || (cls == 1 && rng.NextDouble() < 0.4)) name += " " + Suffixes[rng.Next(Suffixes.Length)];

            var chosen = new List<(string stat, float mult)>();
            var pool = new List<string>(stats);
            for (var i = 0; i < StatCount(cls) && pool.Count > 0; i++)
            {
                var st = pool[rng.Next(pool.Count)]; pool.Remove(st);
                var (lo, hi) = Band(cls, st);
                chosen.Add((st, PctToMult(st, rng.Next(lo, hi + 1))));
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
            foreach (var (st, m) in Decode(item.ArmorStats)) { var d = Buffs.Find(st); parts.Add($"{(d == null ? st : d.Flavor)} {PctText(st, m)}"); }
            return string.Join(", ", parts);
        }

        /// <summary>The stat's two-or-three-word label for comparison lines (report 2026-09-13: the full sentences ran off the row).</summary>
        private static string ShortLabel(string stat)
        {
            var d = Buffs.Find(stat);
            return d == null ? stat : string.IsNullOrEmpty(d.Short) ? d.Flavor : d.Short;
        }

        /// <summary>The stats in short labels, for the worn piece's line on the ARMOR tab.</summary>
        public static string DescribeStatsShort(LootItem item)
        {
            var parts = new List<string>();
            foreach (var (st, m) in Decode(item.ArmorStats)) parts.Add($"{ShortLabel(st)} {PctText(st, m)}");
            return string.Join(", ", parts);
        }

        /// <summary>
        /// A candidate's stats against the piece worn in the same slot, for the ARMOR tab: each
        /// stat with its percentage, and in brackets how it compares in points — green when better,
        /// red when worse, blue when the worn piece has no such stat — then, in red, any stat the
        /// worn piece has that the candidate lacks. A bigger percentage is always the better one.
        /// </summary>
        public static string Compare(LootItem candidate, LootItem worn)
        {
            var wornStats = new Dictionary<string, int>();
            if (worn != null) foreach (var (st, m) in Decode(worn.ArmorStats)) wornStats[st] = MultToPct(st, m);
            var parts = new List<string>();
            var seen = new HashSet<string>();
            foreach (var (st, m) in Decode(candidate.ArmorStats))
            {
                seen.Add(st);
                var p = MultToPct(st, m);
                var label = $"{ShortLabel(st)} {PctText(st, p)}";
                if (worn == null) { parts.Add(label); continue; }
                if (!wornStats.TryGetValue(st, out var w)) { parts.Add($"{label} <color=#7FD8FF>(new)</color>"); continue; }
                var diff = p - w;
                var color = diff > 0 ? "#5BD75B" : diff < 0 ? "#E06060" : "#9A9A9A";
                parts.Add($"{label} <color={color}>({(diff == 0 ? "=" : diff > 0 ? $"+{diff}" : $"{diff}")})</color>");
            }
            if (worn != null)
                foreach (var kv in wornStats)
                    if (!seen.Contains(kv.Key)) parts.Add($"<color=#E06060>loses {ShortLabel(kv.Key)}</color>");
            return string.Join(", ", parts);
        }

        /// <summary>One stat of the ARMOR table: what is worn, what the bag piece has, and what a swap changes.</summary>
        public struct CompareRow
        {
            public string Label;       // "Run speed"
            public string WornText;    // "Run speed +20%", or "-" when the worn piece lacks it
            public string NewText;     // same for the bag piece
            public string Net;         // the change only, coloured, in percentage points: "+21", "-15" (the stat name is already in the row); empty without a bag piece
            public int Verdict;        // 1 better or new, -1 worse or lost, 0 same
        }

        /// <summary>
        /// The ARMOR table's rows: every stat on either piece, the bag piece's stats first.
        /// Either piece may be null (nothing worn, or no piece in the bag). A bigger percentage
        /// is always the better one.
        /// </summary>
        public static List<CompareRow> CompareRows(LootItem candidate, LootItem worn)
        {
            var rows = new List<CompareRow>();
            var wornStats = new Dictionary<string, int>();
            var candStats = new Dictionary<string, int>();
            var order = new List<string>();
            if (candidate != null) foreach (var (st, m) in Decode(candidate.ArmorStats)) { candStats[st] = MultToPct(st, m); order.Add(st); }
            if (worn != null) foreach (var (st, m) in Decode(worn.ArmorStats)) { wornStats[st] = MultToPct(st, m); if (!order.Contains(st)) order.Add(st); }
            foreach (var st in order)
            {
                var label = ShortLabel(st);
                var row = new CompareRow { Label = label, WornText = "-", NewText = "-", Net = "" };
                var hasW = wornStats.TryGetValue(st, out var w);
                var hasC = candStats.TryGetValue(st, out var c);
                if (hasW) row.WornText = $"{label} {PctText(st, w)}";
                if (hasC) row.NewText = $"{label} {PctText(st, c)}";
                if (candidate != null)
                {
                    if (hasC && !hasW) { row.Net = $"<color=#5BD75B>+{c}</color>"; row.Verdict = 1; }
                    else if (hasW && !hasC) { row.Net = $"<color=#E06060>-{w}</color>"; row.Verdict = -1; }
                    else
                    {
                        var diff = c - w;
                        if (diff > 0) { row.Net = $"<color=#5BD75B>+{diff}</color>"; row.Verdict = 1; }
                        else if (diff < 0) { row.Net = $"<color=#E06060>{diff}</color>"; row.Verdict = -1; }
                        else row.Net = $"<color=#B0B0B0>=</color>";
                    }
                }
                rows.Add(row);
            }
            return rows;
        }
        // ---- upgrading ----------------------------------------------------------------------

        /// <summary>The strongest a stat can be on a piece of this rarity, in whole percent.</summary>
        public static int MaxPct(LootItem item, string stat) => UpgradeCeiling(item.WeaponClass, stat);

        public static bool AtMax(LootItem item, string stat, float m) => MultToPct(stat, m) >= MaxPct(item, stat);

        /// <summary>One percent up, never past the rarity's maximum.</summary>
        public static float NextMult(LootItem item, string stat, float m) => AtMax(item, stat, m) ? m : PctToMult(stat, MultToPct(stat, m) + 1);

        /// <summary>
        /// Token price of one upgrade step: fixed by the piece's rarity, the same for every step
        /// and every stat (300 / 600 / 1200 / 2400 × ShopPriceMultiplier × ArmorUpgradeCostMultiplier,
        /// the same base as an enchantment: 750 / 1,500 / 3,000 / 6,000 at the defaults).
        /// </summary>
        public static int UpgradePrice(LootItem item)
        {
            var baseCost = Math.Max(0, Math.Min(3, item.WeaponClass)) switch { 0 => 300, 1 => 600, 2 => 1200, _ => 2400 };
            return Math.Max(1, (int)Math.Round(baseCost * ModConfig.ShopPriceMultiplier.Value * Math.Max(0f, ModConfig.ArmorUpgradeCostMultiplier.Value)));
        }

        /// <summary>Raise one stat of a bag or worn piece by a step. The piece's sell value is not touched.</summary>
        public static bool Upgrade(LootItem item, int index)
        {
            var inv = BagManager.Inventory;
            if (!ModGate.Active) { BagManager.Toast("Not in a modded room."); return false; }
            var live = item == null ? null : inv.Find(item.Id);
            if (live == null || !live.IsArmor) { BagManager.Toast("That's gone."); return false; }
            var stats = Decode(live.ArmorStats);
            if (index < 0 || index >= stats.Count) return false;
            var (st, m) = stats[index];
            if (AtMax(live, st, m)) { BagManager.Toast("That stat is already at its best."); return false; }
            var next = NextMult(live, st, m);
            var price = UpgradePrice(live);
            if (inv.Gold < price) { BagManager.Toast($"Upgrading costs {price} tokens; you have {inv.Gold}."); return false; }
            inv.Gold -= price;
            stats[index] = (st, next);
            live.ArmorStats = Encode(stats);
            inv.Save();
            if (live.WornSlot >= 0) Buffs.RebuildWorn();
            ReconLog.Line($"armor: upgrade {live.Name} [{SlotNames[live.ArmorSlot]}] {st} {m:0.###} -> {next:0.###} for {price} -> tokens {inv.Gold}");
            BagManager.Toast($"{ShortLabel(st)} {PctText(st, m)} → {PctText(st, next)} for {price} tokens");
            BagPanel.Refresh(); Booth.Refresh();
            return true;
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
            if (Buffs.LegPerksGatedOff() && (live.ArmorStats ?? "").Contains("Legs_"))
                BagManager.Toast("The game has friendly fire on here (the sandbox does this): it ignores every run speed, jump and leap perk until it is off.");
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
