using System;
using System.Collections.Generic;
using System.Reflection;
using Il2Cpp;
using LootOverhaul.Gate;
using LootOverhaul.Recon;
using Interop = LootOverhaul.Recon.Interop;

namespace LootOverhaul.Loot
{
    /// <summary>
    /// One-run tonics on the game's own exosuit multiplier table. <c>AvatarPlayer.LocalExoSuit</c>
    /// has a public float per perk stat; the game recomputes them from the equipped perks
    /// (<c>ResetAll</c> then <c>Update</c> per module), so a tonic is a multiplier applied on
    /// top, re-applied a frame after every recompute, and forgotten when the lobby loads.
    /// Nothing persists to the profile. Only stats whose perk the player has already
    /// unlocked are offered (lock-step with the game's gating).
    /// </summary>
    public static class Buffs
    {
        public class Def
        {
            public string Stat;      // Exosuit field name
            public string Name;      // tonic name
            public string Flavor;    // what it does, plainly
            public float[] Mults = { 1.15f, 1.30f, 1.50f };
            public int[] Prices = { 60, 160, 400 };
            public float Weight = 0.4f;
            public bool Invert;
        }

        // One brew per exosuit stat, so whatever perks a player has unlocked, something is on offer.
        // Invert = the stat is a reduction factor (Antidote sits at 0.8 with the perk), so the tonic divides.
        public static readonly Def[] Catalogue =
        {
            new Def { Stat = "Arms_Critical",     Name = "Keen Edge Oil",      Flavor = "more criticals" },
            new Def { Stat = "Arms_Distance",     Name = "Long Arm Liniment",  Flavor = "throw weapons farther" },
            new Def { Stat = "Arms_Farshot",      Name = "Hawkeye Drops",      Flavor = "shoot farther" },
            new Def { Stat = "Arms_Impale",       Name = "Skewer Salve",       Flavor = "impale more often" },
            new Def { Stat = "Arms_Knockback",    Name = "Ram's Draught",      Flavor = "knock foes back" },
            new Def { Stat = "Arms_Might",        Name = "Ogre Blood",         Flavor = "mightier swings" },
            new Def { Stat = "Arms_Pierce",       Name = "Needle Tincture",    Flavor = "pierce deeper" },
            new Def { Stat = "Arms_Power",        Name = "Bruiser's Brew",     Flavor = "hit harder" },
            new Def { Stat = "Arms_Pullback",     Name = "Bowstring Balm",     Flavor = "draw faster" },
            new Def { Stat = "Arms_Stun",         Name = "Thunderclap Syrup",  Flavor = "stun more" },
            new Def { Stat = "Chest_Antidote",    Name = "Antidote Tonic",     Flavor = "poison bites less", Invert = true },
            new Def { Stat = "Chest_Armor",       Name = "Ironskin Tonic",     Flavor = "take less damage" },
            new Def { Stat = "Chest_Blast",       Name = "Powderkeg Brew",     Flavor = "bigger blasts" },
            new Def { Stat = "Chest_Dispel",      Name = "Cleansing Draught",  Flavor = "dispel better" },
            new Def { Stat = "Chest_Heal",        Name = "Mending Tonic",      Flavor = "heal faster" },
            new Def { Stat = "Chest_Resilience",  Name = "Stalwart Brew",      Flavor = "shrug off blows" },
            new Def { Stat = "Chest_Ricochet",    Name = "Mirror Elixir",      Flavor = "more ricochets" },
            new Def { Stat = "Chest_Vitality",    Name = "Hearty Draught",     Flavor = "more health" },
            new Def { Stat = "Chest_Antifreeze",  Name = "Ember Tea",          Flavor = "cold bites less", Invert = true },
            new Def { Stat = "Legs_Absorb",       Name = "Cushion Cordial",    Flavor = "absorb falls" },
            new Def { Stat = "Legs_Airtime",      Name = "Feather Tonic",      Flavor = "hang in the air" },
            new Def { Stat = "Legs_Endurance",    Name = "Marathon Brew",      Flavor = "tire slower" },
            new Def { Stat = "Legs_Haste",        Name = "Quicksilver",        Flavor = "move faster" },
            new Def { Stat = "Legs_Jump",         Name = "Springheel",         Flavor = "jump higher" },
            new Def { Stat = "Legs_Leap",         Name = "Grasshopper Gin",    Flavor = "leap farther" },
            new Def { Stat = "Legs_Shockwave",    Name = "Stomp Syrup",        Flavor = "bigger stomps" },
            new Def { Stat = "Legs_Swift",        Name = "Fleetfoot Salve",    Flavor = "swifter" },
            new Def { Stat = "Mind_Crafter",      Name = "Tinker's Tea",       Flavor = "craft cheaper" },
            new Def { Stat = "Mind_Fortune",      Name = "Lucky Coin Tea",     Flavor = "better fortune" },
            new Def { Stat = "Mind_Lucky",        Name = "Rabbit's Foot",      Flavor = "luckier" },
            new Def { Stat = "Mind_Mystify",      Name = "Mystic Draught",     Flavor = "mystify more" },
            new Def { Stat = "Mind_Perception",   Name = "Owl's Eye",          Flavor = "see more" },
            new Def { Stat = "Mind_Predator",     Name = "Wolf's Blood",       Flavor = "hunt better" },
            new Def { Stat = "Mind_Stillness",    Name = "Still Water",        Flavor = "steadier" },
            new Def { Stat = "Mind_Grounded",     Name = "Root Tea",           Flavor = "stand firm" },
        };
        public static readonly string[] TierNames = { "Minor", "Major", "Grand" };

        /// <summary>Active for this run: stat -> multiplier (stacking takes the best, not the product).</summary>
        private static readonly Dictionary<string, float> Active = new Dictionary<string, float>();
        /// <summary>Armor: stat -> product of worn multipliers. Permanent while worn.</summary>
        private static Dictionary<string, float> Worn = new Dictionary<string, float>();
        private static readonly Dictionary<string, PropertyInfo> Props = new Dictionary<string, PropertyInfo>();
        private static bool _reapplyPending;
        private static int _applied;

        public static bool AnyActive => Active.Count > 0;
        public static bool AnyWorn => Worn.Count > 0;

        public static void RebuildWorn()
        {
            Worn = Armor.WornMultipliers();
            Apply("armor changed");
        }

        public static void Install()
        {
            var t = typeof(Buffs);
            Hooks.Patch(typeof(AvatarPlayer.Exosuit), "ResetAll", null, Hooks.Of(t, nameof(OnRecompute)));
            Hooks.Patch(typeof(AvatarPlayer.Exosuit), "Update", null, Hooks.Of(t, nameof(OnRecompute)));
        }

        private static void OnRecompute() => _reapplyPending = true;

        public static void Tick()
        {
            if (!_reapplyPending) return;
            _reapplyPending = false;
            if (Active.Count > 0 || Worn.Count > 0) Apply("exosuit recompute");
        }

        public static Def Find(string stat) { foreach (var d in Catalogue) if (d.Stat == stat) return d; return null; }

        /// <summary>Tonics the player may buy: catalogue entries whose perk is unlocked.</summary>
        public static List<Def> Offered()
        {
            var list = new List<Def>();
            foreach (var d in Catalogue) if (Unlocks.PerkUnlocked(d.Stat)) list.Add(d);
            return list;
        }

        public static LootItem MakeItem(Def d, int tier)
        {
            tier = Math.Max(0, Math.Min(2, tier));
            var name = $"{TierNames[tier]} {d.Name}";
            return new LootItem
            {
                Kind = "buff",
                PrefabName = "Potion_S",
                Name = name,
                ColoredName = $"<color=#7FD8FF>{name}</color>",
                WeaponClass = tier,
                BuffStat = d.Stat,
                BuffMult = d.Mults[tier],
                Value = d.Prices[tier],
                Weight = d.Weight,
                PropType = -1,
                FoundBy = "Loot Broker",
            };
        }

        /// <summary>Drink a bag tonic: active until the lobby next loads.</summary>
        public static void Drink(LootItem item)
        {
            var inv = BagManager.Inventory;
            if (item == null || inv.Find(item.Id) == null) { BagManager.Toast("That's gone."); return; }
            if (!ModGate.Active) { BagManager.Toast("Not in a modded room."); return; }
            Active.TryGetValue(item.BuffStat, out var current);
            Active[item.BuffStat] = Math.Max(current, item.BuffMult);
            inv.Remove(item.Id);
            inv.Save();
            Apply($"drank {item.Name}");
            var d = Find(item.BuffStat);
            BagManager.Toast($"Drank {item.ColoredName}: {(d == null ? item.BuffStat : d.Flavor)} ×{item.BuffMult:0.00} until you return to the lobby.");
            BagPanel.Refresh();
        }

        public static void ClearAll(string why)
        {
            if (Active.Count == 0) return;
            Active.Clear();
            ReconLog.Line($"buffs cleared: {why}");
        }

        private static PropertyInfo Prop(string stat)
        {
            if (Props.TryGetValue(stat, out var p)) return p;
            p = typeof(AvatarPlayer.Exosuit).GetProperty(stat, BindingFlags.Public | BindingFlags.Instance);
            Props[stat] = p;
            return p;
        }

        /// <summary>
        /// Multiply the live values. The game's recompute always starts from ResetAll, so
        /// re-applying after each recompute never compounds: each application sees fresh
        /// base values.
        /// </summary>
        private static void Apply(string why)
        {
            var exo = AvatarPlayer.LocalExoSuit;
            if (exo == null) return;
            var parts = new List<string>();
            // Combined: tonic (best of) × armor (product), per stat.
            var combined = new Dictionary<string, float>(Worn);
            foreach (var kv in Active) { combined.TryGetValue(kv.Key, out var w); combined[kv.Key] = (w <= 0f ? 1f : w) * kv.Value; }
            foreach (var kv in combined)
            {
                try
                {
                    var p = Prop(kv.Key);
                    if (p == null) { parts.Add($"{kv.Key}: no such stat"); continue; }
                    var before = Convert.ToSingle(p.GetValue(exo));
                    // A stat at 0 means the perk is not equipped; a multiplier does nothing there,
                    // so give it a floor so the tonic is felt (the game's own level 1 is ~1.05–1.2).
                    var baseValue = before <= 0f ? 1f : before;
                    var def = Find(kv.Key);
                    var after = def != null && def.Invert ? baseValue / kv.Value : baseValue * kv.Value;
                    p.SetValue(exo, after);
                    parts.Add($"{kv.Key} {before:0.###}->{after:0.###}");
                }
                catch (Exception e) { parts.Add($"{kv.Key}: {e.GetType().Name}"); }
            }
            _applied++;
            ReconLog.Line($"buffs applied ({why}): {string.Join(", ", parts)}");
        }

        public static string DescribeActive()
        {
            if (Active.Count == 0) return "";
            var parts = new List<string>();
            foreach (var kv in Active) { var d = Find(kv.Key); parts.Add($"{(d == null ? kv.Key : d.Flavor)} ×{kv.Value:0.00}"); }
            return string.Join(", ", parts);
        }

        /// <summary>Recon: the whole multiplier table as the game has it right now.</summary>
        public static void Snapshot(string when)
        {
            var exo = AvatarPlayer.LocalExoSuit;
            if (exo == null) { ReconLog.Line($"exosuit snapshot ({when}): LocalExoSuit is null"); return; }
            var parts = new List<string>();
            foreach (var p in typeof(AvatarPlayer.Exosuit).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (p.PropertyType != typeof(float)) continue;
                try { var v = Convert.ToSingle(p.GetValue(exo)); if (v != 0f) parts.Add($"{p.Name}={v:0.###}"); } catch { }
            }
            ReconLog.Line($"exosuit snapshot ({when}): {(parts.Count == 0 ? "all zero" : string.Join(" ", parts))}");
            ReconLog.Line($"unlocks: {Unlocks.Describe()}");
        }
    }
}
