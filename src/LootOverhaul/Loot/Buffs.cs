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

        // One brew per exosuit stat the game actually reads (readers found in the assembly
        // 2026-09-09; the perk table in the bundles gives the direction: a perk whose multiplier
        // falls below 1 per level is a "less is better" stat). Invert = the game multiplies
        // something bad (incoming damage, a drain, a bad duration) by the stat, so the tonic or
        // armor DIVIDES. Chest_Armor has no perk in the current game but AvatarPlayer.OnDamaged
        // still multiplies ordinary hits by it; 0.9.14 and earlier multiplied it upward, which
        // was MORE damage (report 2026-09-09). OnDamaged by damage type: Melee × Chest_Armor,
        // Projectile × Chest_Ricochet, Magic × Chest_Dispel, Fire × Chest_Blast. Stats nothing reads (Arms_Stun, Legs_Airtime,
        // Legs_Shockwave, Legs_Swift, Mind_Crafter/Lucky/Perception/Predator) and the on/off
        // perks (Grounded, Gemini, Unburdened, Juggernaut) are not offered.
        public static readonly Def[] Catalogue =
        {
            new Def { Stat = "Arms_Critical",     Name = "Keen Edge Oil",      Flavor = "stronger critical hits" },
            new Def { Stat = "Arms_Distance",     Name = "Long Arm Liniment",  Flavor = "throw farther" },
            new Def { Stat = "Arms_Farshot",      Name = "Hawkeye Drops",      Flavor = "shoot farther" },
            new Def { Stat = "Arms_Impale",       Name = "Skewer Salve",       Flavor = "stronger impales" },
            new Def { Stat = "Arms_Knockback",    Name = "Ram's Draught",      Flavor = "stronger knockbacks" },
            new Def { Stat = "Arms_Might",        Name = "Ogre Blood",         Flavor = "more axe/spear damage" },
            new Def { Stat = "Arms_Pierce",       Name = "Needle Tincture",    Flavor = "more pierce damage" },
            new Def { Stat = "Arms_Power",        Name = "Bruiser's Brew",     Flavor = "more weapon damage" },
            new Def { Stat = "Arms_Pullback",     Name = "Bowstring Balm",     Flavor = "more crossbow/staff damage" },
            new Def { Stat = "Chest_Antidote",    Name = "Antidote Tonic",     Flavor = "poison does less damage", Invert = true },
            new Def { Stat = "Chest_Armor",       Name = "Ironskin Tonic",     Flavor = "melee hits do less damage", Invert = true },
            new Def { Stat = "Chest_Blast",       Name = "Powderkeg Brew",     Flavor = "fire does less damage", Invert = true },
            new Def { Stat = "Chest_Dispel",      Name = "Cleansing Draught",  Flavor = "magic does less damage", Invert = true },
            new Def { Stat = "Chest_Heal",        Name = "Mending Tonic",      Flavor = "potions heal more" },
            new Def { Stat = "Chest_Resilience",  Name = "Stalwart Brew",      Flavor = "self-effects last longer" },
            new Def { Stat = "Chest_Ricochet",    Name = "Mirror Elixir",      Flavor = "arrows and bolts do less damage", Invert = true },
            new Def { Stat = "Chest_Vitality",    Name = "Hearty Draught",     Flavor = "health regenerates faster" },
            new Def { Stat = "Chest_Antifreeze",  Name = "Ember Tea",          Flavor = "freezing wears off sooner", Invert = true },
            new Def { Stat = "Legs_Absorb",       Name = "Cushion Cordial",    Flavor = "less fall damage", Invert = true },
            new Def { Stat = "Legs_Endurance",    Name = "Marathon Brew",      Flavor = "stamina drains slower", Invert = true },
            new Def { Stat = "Legs_Haste",        Name = "Quicksilver",        Flavor = "run faster" },
            new Def { Stat = "Legs_Jump",         Name = "Springheel",         Flavor = "jump higher" },
            new Def { Stat = "Legs_Leap",         Name = "Grasshopper Gin",    Flavor = "leap farther" },
            new Def { Stat = "Mind_Fortune",      Name = "Lucky Coin Tea",     Flavor = "more coins from piles" },
            new Def { Stat = "Mind_Mystify",      Name = "Mystic Draught",     Flavor = "mystify (staff magic) stronger" },
            new Def { Stat = "Mind_Stillness",    Name = "Still Water",        Flavor = "stillness (slowed time) stronger" },
        };
        public static readonly string[] TierNames = { "Minor", "Major", "Grand" };

        /// <summary>Active for this run: stat -> multiplier (stacking takes the best, not the product).</summary>
        private static readonly Dictionary<string, float> Active = new Dictionary<string, float>();
        /// <summary>Armor: stat -> product of worn multipliers. Permanent while worn.</summary>
        private static Dictionary<string, float> Worn = new Dictionary<string, float>();
        private static readonly Dictionary<string, PropertyInfo> Props = new Dictionary<string, PropertyInfo>();
        /// <summary>
        /// The game's own value of every stat we have multiplied, on the exosuit instance we
        /// multiplied it on. Every application starts by putting these back, so a second
        /// application (a second armor piece, a tonic, a perk refresh) multiplies the game's
        /// value and never our own. 0.9.13 and earlier multiplied whatever was live: two
        /// "leap farther" pieces at 1.14 and 1.16 compounded past 3× within a session
        /// (report 2026-09-09).
        /// </summary>
        private static readonly Dictionary<string, float> Base = new Dictionary<string, float>();
        private static IntPtr _basePointer = IntPtr.Zero;
        private static bool _appliedAny;
        private static bool _reapplyPending;
        private static int _applied;
        private static float _reapplyAt = -1f;

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
            // Before the game recomputes, its own values go back (Update(module) writes only that
            // module's stats, absolutely; read from the assembly 2026-09-09); after, ours go on again.
            Hooks.Patch(typeof(AvatarPlayer.Exosuit), "ResetAll", Hooks.Of(t, nameof(BeforeRecompute)), Hooks.Of(t, nameof(OnRecompute)));
            Hooks.Patch(typeof(AvatarPlayer.Exosuit), "Update", Hooks.Of(t, nameof(BeforeRecompute)), Hooks.Of(t, nameof(OnRecompute)));
            // A fresh local avatar: whatever it computed for its exosuit, the worn armor goes on a
            // moment later (the first lobby of a session spawned before the bag was loaded).
            Hooks.Patch(typeof(AvatarPlayer), "RespawnLocalPlayer", null, Hooks.Of(t, nameof(OnLocalRespawn)));
        }

        private static void BeforeRecompute() { try { Restore("game recompute"); } catch { } }
        private static void OnRecompute() => _reapplyPending = true;
        private static void OnLocalRespawn() => _reapplyAt = UnityEngine.Time.unscaledTime + 1.0f;

        /// <summary>Gate opened or the bag loaded: the worn set may have changed under us.</summary>
        public static void RequestReapply(float delaySeconds = 0f) => _reapplyAt = UnityEngine.Time.unscaledTime + Math.Max(0f, delaySeconds);

        public static void Tick()
        {
            var now = UnityEngine.Time.unscaledTime;
            if (_reapplyAt >= 0f && now >= _reapplyAt) { _reapplyAt = -1f; _reapplyPending = true; }
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
                FoundBy = "Kobold Traveler",
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

        /// <summary>The exosuit the base table belongs to, or null; a new instance forgets the old table.</summary>
        private static AvatarPlayer.Exosuit Exo()
        {
            var exo = AvatarPlayer.LocalExoSuit;
            if (exo == null) return null;
            var ptr = exo.Pointer;
            if (ptr != _basePointer)
            {
                if (Base.Count > 0) ReconLog.Line($"buffs: new exosuit instance; forgetting {Base.Count} base value(s)");
                Base.Clear(); _appliedAny = false; _basePointer = ptr;
            }
            return exo;
        }

        /// <summary>Put the game's own values back on every stat we touched. Idempotent.</summary>
        private static void Restore(string why)
        {
            if (!_appliedAny) return;
            var exo = Exo();
            _appliedAny = false;
            if (exo == null) { Base.Clear(); return; }
            var n = 0;
            foreach (var kv in Base)
            {
                try { var p = Prop(kv.Key); if (p != null) { p.SetValue(exo, kv.Value); n++; } } catch { }
            }
            if (ModConfig.VerboseLogging.Value) ReconLog.Line($"buffs restored ({why}): {n} stat(s) back to the game's values");
        }

        /// <summary>
        /// Multiply the game's values. The game's values are put back first, then read as the
        /// base of every stat with a multiplier, then multiplied once. Calling this any number
        /// of times, for any reason, yields the same result.
        /// </summary>
        private static void Apply(string why)
        {
            var exo = Exo();
            if (exo == null) { Core.Log.Msg($"buffs not applied ({why}): no local exosuit yet"); return; }
            Restore(why);
            var parts = new List<string>();
            // Combined: tonic (best of) × armor (product), per stat.
            var combined = new Dictionary<string, float>(Worn);
            foreach (var kv in Active) { combined.TryGetValue(kv.Key, out var w); combined[kv.Key] = (w <= 0f ? 1f : w) * kv.Value; }
            Base.Clear();
            foreach (var kv in combined)
            {
                try
                {
                    var p = Prop(kv.Key);
                    if (p == null) { parts.Add($"{kv.Key}: no such stat"); continue; }
                    var before = Convert.ToSingle(p.GetValue(exo));
                    Base[kv.Key] = before;
                    // A stat at 0 means the perk is not equipped; a multiplier does nothing there,
                    // so give it a floor so the tonic is felt (the game's own level 1 is ~1.05–1.2).
                    var baseValue = before <= 0f ? 1f : before;
                    var def = Find(kv.Key);
                    var after = def != null && def.Invert ? baseValue / kv.Value : baseValue * kv.Value;
                    p.SetValue(exo, after);
                    _appliedAny = true;
                    parts.Add($"{kv.Key} {before:0.###}->{after:0.###} (×{kv.Value:0.###})");
                }
                catch (Exception e) { parts.Add($"{kv.Key}: {e.GetType().Name}"); }
            }
            _applied++;
            // To the MelonLoader log as well: a tester's log without the transcript said nothing (2026-09-09).
            Core.Log.Msg($"buffs applied ({why}): {(parts.Count == 0 ? "nothing to apply" : string.Join(", ", parts))}{GameGate()}");
        }

        /// <summary>
        /// The game's own switch on the leg multipliers: <c>VRPlayerControl.UpdateJumping</c> and
        /// <c>GetInputVelocity</c> multiply by <c>Legs_Jump</c> / <c>Legs_Leap</c> only while
        /// <c>GameManager.FriendlyFireEnabled</c> is false (read from the assembly 2026-09-09), and
        /// the sandbox sets that flag from its hazard choice (<c>SandboxUI.DelayedFriendlyFire</c>).
        /// With it on, no jump or leap perk works, the game's own included.
        /// </summary>
        public static bool LegPerksGatedOff()
        {
            try { return GameManager.FriendlyFireEnabled; } catch { return false; }
        }

        private static string GameGate()
        {
            var scene = "?"; try { scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name; } catch { }
            return LegPerksGatedOff()
                ? $"; scene {scene}; GameManager.FriendlyFireEnabled=true — the game skips ALL jump/leap multipliers while this is on"
                : $"; scene {scene}; FriendlyFireEnabled=false";
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
            ReconLog.Line($"exosuit snapshot ({when}): {(parts.Count == 0 ? "all zero" : string.Join(" ", parts))}{GameGate()}");
            ReconLog.Line($"unlocks: {Unlocks.Describe()}");
        }
    }
}
