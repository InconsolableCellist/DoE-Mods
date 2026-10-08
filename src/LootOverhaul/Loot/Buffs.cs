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
    /// One-run tonics and worn armor on the game's own exosuit stats. Since the 2026-09-27 game
    /// update the exosuit keeps one perk slot per body part (arms, chest, legs, mind: perk type,
    /// stat, probability) and every stat is a getter-only property that tail-calls
    /// <c>Exosuit.GetExosuitStat(PerkType, default)</c>: the slot's stat when that slot holds the
    /// perk, else the default (1, or 0 for regeneration). There are no float fields to write any
    /// more, so the mod's multiplier is applied in a postfix on <c>GetExosuitStat</c>, on the
    /// local player's exosuit only. Nothing is stored in the game's objects, so nothing can
    /// compound and a recompute can never undo it; the tonic table is forgotten when the lobby
    /// loads. Only stats whose perk the player has already unlocked are offered.
    /// </summary>
    public static class Buffs
    {
        public class Def
        {
            public string Stat;      // Exosuit field name
            public string Name;      // tonic name
            public string Flavor;    // what it does, plainly
            public string Short;     // the same in two or three words, for comparison lines
            public float[] Mults = { 1.15f, 1.30f, 1.50f };
            public int[] Prices = { 60, 160, 400 };
            public float Weight = 0.4f;
            public bool Invert;
            /// <summary>
            /// The value the multiplier works from when the game's own value is below it: the
            /// regeneration stats read 0 without their perk (the perk's level-1 value is used), and
            /// run speed has the game's 0.9 factor to clear (see <see cref="HasteFloor"/>).
            /// Zero = the getter's default (1).
            /// </summary>
            public float Floor;
            /// <summary>Kept so tonics and armor already in a bag still read and work, but no longer sold or rolled.</summary>
            public bool Retired;
        }

        /// <summary>
        /// <c>VRControllerInput.Update</c> (2026-09-27 build): speed = base × max(1, 0.9 × Legs_Haste
        /// × Legs_Juggernaut_Speed × GetEquippableMoveSpeedMult()). A haste value below 1/0.9 does
        /// nothing, so a ×1.15 on the default 1.0 would have been a 3.5% change. Working from
        /// 1/0.9 makes ×1.15 on the label ×1.15 on the ground (with no juggernaut or ring aura).
        /// </summary>
        public const float HasteFloor = 1f / 0.9f;

        // One brew per exosuit stat the game actually reads (readers found in the assembly
        // 2026-09-09, re-checked on the 2026-09-27 build; the perk table in the bundles gives the
        // direction: a perk whose multiplier falls below 1 per level is a "less is better" stat). Invert = the game multiplies
        // something bad (incoming damage, a drain, a bad duration) by the stat, so the tonic or
        // armor DIVIDES. Chest_Armor has a perk again since the 2026-09-27 update (-0.1 per level); 0.9.14 and earlier multiplied it upward, which
        // was MORE damage (report 2026-09-09). OnDamaged by damage type: Melee × Chest_Armor,
        // Projectile × Chest_Ricochet, Magic × Chest_Dispel, Fire × Chest_Blast. Stats nothing reads (Arms_Stun, Legs_Airtime,
        // Legs_Shockwave, Legs_Swift, Mind_Crafter/Lucky/Perception/Predator) and the on/off
        // perks (Grounded, Gemini, Unburdened, Juggernaut) are not offered.
        public static readonly Def[] Catalogue =
        {
            new Def { Stat = "Arms_Critical",     Name = "Keen Edge Oil",      Flavor = "stronger critical hits", Short = "crits" },
            new Def { Stat = "Arms_Distance",     Name = "Long Arm Liniment",  Flavor = "throw farther", Short = "throw range" },
            new Def { Stat = "Arms_Farshot",      Name = "Hawkeye Drops",      Flavor = "shoot farther", Short = "shot range" },
            new Def { Stat = "Arms_Impale",       Name = "Skewer Salve",       Flavor = "stronger impales", Short = "impales", Retired = true },
            new Def { Stat = "Arms_Knockback",    Name = "Ram's Draught",      Flavor = "stronger knockbacks", Short = "knockback" },
            new Def { Stat = "Arms_Might",        Name = "Ogre Blood",         Flavor = "more axe/spear damage", Short = "axe/spear dmg", Retired = true },
            new Def { Stat = "Arms_Pierce",       Name = "Needle Tincture",    Flavor = "more pierce damage", Short = "pierce dmg", Retired = true },
            new Def { Stat = "Arms_Power",        Name = "Bruiser's Brew",     Flavor = "more weapon damage", Short = "weapon dmg" },
            new Def { Stat = "Arms_Pullback",     Name = "Bowstring Balm",     Flavor = "more crossbow/staff damage", Short = "xbow/staff dmg", Retired = true },
            new Def { Stat = "Chest_Antidote",    Name = "Antidote Tonic",     Flavor = "poison does less damage", Short = "poison taken", Invert = true },
            new Def { Stat = "Chest_Armor",       Name = "Ironskin Tonic",     Flavor = "melee hits do less damage", Short = "melee taken", Invert = true },
            new Def { Stat = "Chest_Blast",       Name = "Powderkeg Brew",     Flavor = "fire does less damage", Short = "fire taken", Invert = true },
            new Def { Stat = "Chest_Dispel",      Name = "Cleansing Draught",  Flavor = "magic does less damage", Short = "magic taken", Invert = true },
            new Def { Stat = "Chest_Heal",        Name = "Mending Tonic",      Flavor = "potions heal more", Short = "potion heal" },
            new Def { Stat = "Chest_Resilience",  Name = "Stalwart Brew",      Flavor = "self-effects last longer", Short = "self-effects" },
            new Def { Stat = "Chest_Ricochet",    Name = "Mirror Elixir",      Flavor = "arrows and bolts do less damage", Short = "arrows taken", Invert = true },
            new Def { Stat = "Chest_Vitality",    Name = "Hearty Draught",     Flavor = "health regenerates faster", Short = "regen", Floor = 0.10f },
            new Def { Stat = "Chest_Antifreeze",  Name = "Ember Tea",          Flavor = "freezing wears off sooner", Short = "freeze time", Invert = true },
            new Def { Stat = "Legs_Absorb",       Name = "Cushion Cordial",    Flavor = "less fall damage", Short = "fall dmg", Invert = true },
            new Def { Stat = "Legs_Endurance",    Name = "Marathon Brew",      Flavor = "stamina drains slower", Short = "stamina drain", Invert = true, Retired = true },
            new Def { Stat = "Legs_Haste",        Name = "Quicksilver",        Flavor = "run faster", Short = "run speed", Floor = HasteFloor },
            new Def { Stat = "Legs_Jump",         Name = "Springheel",         Flavor = "jump higher", Short = "jump" },
            new Def { Stat = "Legs_Leap",         Name = "Grasshopper Gin",    Flavor = "leap farther", Short = "leap" },
            new Def { Stat = "Mind_Fortune",      Name = "Lucky Coin Tea",     Flavor = "more coins at the end of a run", Short = "run coins" },
            new Def { Stat = "Mind_Mystify",      Name = "Mystic Draught",     Flavor = "enemy shots miss more", Short = "enemy aim" },
            new Def { Stat = "Mind_Stillness",    Name = "Still Water",        Flavor = "health regenerates while standing still", Short = "still regen", Floor = 0.20f },
        };
        public static readonly string[] TierNames = { "Minor", "Major", "Grand" };

        /// <summary>Active for this run: stat -> multiplier (stacking takes the best, not the product).</summary>
        private static readonly Dictionary<string, float> Active = new Dictionary<string, float>();
        /// <summary>Armor: stat -> product of worn multipliers. Permanent while worn.</summary>
        private static Dictionary<string, float> Worn = new Dictionary<string, float>();
        /// <summary>What the postfix applies: perk type -> (catalogue entry, combined multiplier). Rebuilt on every change.</summary>
        private static Dictionary<int, (Def def, float mult)> _byPerk = new Dictionary<int, (Def, float)>();
        private static IntPtr _localExo = IntPtr.Zero;
        private static bool _bypass;

        public static bool AnyActive => Active.Count > 0;
        public static bool AnyWorn => Worn.Count > 0;

        public static void RebuildWorn()
        {
            Worn = Armor.WornMultipliers();
            Apply("armor changed");
        }

        public static void Install()
        {
            // GetExosuitStat is the one routine every stat getter jumps to (read from the assembly
            // 2026-09-28: 39 getters end in `jmp GetExosuitStat`, Health.UpdateLocal calls it for
            // Vitality and Stillness). Its RVA is not shared.
            Hooks.Patch(typeof(AvatarPlayer.Exosuit), "GetExosuitStat", null, Hooks.Of(typeof(Buffs), nameof(OnStat)));
        }

        private static void OnStat(AvatarPlayer.Exosuit __instance, ExosuitModule.PerkType type, float defaultValue, ref float __result)
        {
            if (_bypass || _byPerk.Count == 0 || __instance == null) return;
            try
            {
                if (__instance.Pointer != _localExo) return;
                if (!_byPerk.TryGetValue((int)type, out var e)) return;
                __result = Multiplied(e.def, e.mult, __result, defaultValue);
            }
            catch { }
        }

        /// <summary>The game's value times ours (divided, for a "less is better" stat). A perk's own value is kept: a ÷ on a 0.8 antidote gives less than 0.8.</summary>
        private static float Multiplied(Def def, float mult, float game, float defaultValue)
        {
            var from = game;
            if (def != null && from < def.Floor) from = def.Floor;
            if (from <= 0f) from = defaultValue > 0f ? defaultValue : 1f;
            return def != null && def.Invert ? from / mult : from * mult;
        }

        public static void Tick()
        {
            // The postfix compares against this instead of reading the static every call.
            try { var exo = AvatarPlayer.LocalExoSuit; _localExo = exo == null ? IntPtr.Zero : exo.Pointer; }
            catch { _localExo = IntPtr.Zero; }
        }

        public static Def Find(string stat) { foreach (var d in Catalogue) if (d.Stat == stat) return d; return null; }

        /// <summary>Tonics the player may buy: catalogue entries whose perk is unlocked.</summary>
        public static List<Def> Offered()
        {
            var list = new List<Def> { LightPotion.Def };   // no perk behind it
            foreach (var d in Catalogue) if (!d.Retired && Unlocks.PerkUnlocked(d.Stat)) list.Add(d);
            return list;
        }

        /// <summary>What a tonic does, for bag rows and toasts: "run faster ×1.15", "light around you, 9 m".</summary>
        public static string Effect(string stat, float mult)
        {
            if (stat == LightPotion.Stat) return $"{LightPotion.Def.Flavor}, {mult:0.#} m";
            var d = Find(stat);
            return $"{(d == null ? stat : d.Flavor)} {(d != null && d.Invert ? "÷" : "×")}{mult:0.00}";
        }

        /// <summary>The three tiers as the shop lists them.</summary>
        public static string Tiers(Def d) => d.Stat == LightPotion.Stat
            ? $"{d.Mults[0]:0.#} m / {d.Mults[1]:0.#} m / {d.Mults[2]:0.#} m"
            : $"×{d.Mults[0]:0.00} / ×{d.Mults[1]:0.00} / ×{d.Mults[2]:0.00}";

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
            if (item.BuffStat == LightPotion.Stat) { LightPotion.Drink(item); return; }
            Active.TryGetValue(item.BuffStat, out var current);
            Active[item.BuffStat] = Math.Max(current, item.BuffMult);
            inv.Remove(item.Id);
            inv.Save();
            Apply($"drank {item.Name}");
            BagManager.Toast($"Drank {item.ColoredName}: {Effect(item.BuffStat, item.BuffMult)} until you return to the lobby.");
            BagPanel.Refresh();
        }

        public static void ClearAll(string why)
        {
            if (Active.Count == 0) return;
            Active.Clear();
            ReconLog.Line($"buffs cleared: {why}");
            Apply($"tonics cleared: {why}");
        }

        private static PropertyInfo Prop(string stat) =>
            typeof(AvatarPlayer.Exosuit).GetProperty(stat, BindingFlags.Public | BindingFlags.Instance);

        /// <summary>
        /// Rebuild the perk-type table the postfix reads: tonic (best of) × armor (product), per
        /// stat. Idempotent; logs the game's value and ours for every stat touched.
        /// </summary>
        private static void Apply(string why)
        {
            Tick();
            var combined = new Dictionary<string, float>(Worn);
            foreach (var kv in Active) { combined.TryGetValue(kv.Key, out var w); combined[kv.Key] = (w <= 0f ? 1f : w) * kv.Value; }
            var table = new Dictionary<int, (Def, float)>();
            var parts = new List<string>();
            var read = new List<(string stat, PropertyInfo p, float game, float mult, Def def)>();
            var exo = AvatarPlayer.LocalExoSuit;
            foreach (var kv in combined)
            {
                var def = Find(kv.Key);
                if (!Enum.TryParse<ExosuitModule.PerkType>(kv.Key.Replace("_", ""), out var perk)) { parts.Add($"{kv.Key}: no such perk"); continue; }
                table[(int)perk] = (def, kv.Value);
                var p = Prop(kv.Key);
                if (exo == null || p == null) { parts.Add($"{kv.Key} ×{kv.Value:0.###}"); continue; }
                try { _bypass = true; read.Add((kv.Key, p, Convert.ToSingle(p.GetValue(exo)), kv.Value, def)); }
                catch (Exception e) { parts.Add($"{kv.Key}: {e.GetType().Name}"); }
                finally { _bypass = false; }
            }
            _byPerk = table;
            // Read back through the hook: "live" is what the game sees now, so a hook that never
            // fires shows as live == game.
            foreach (var (stat, p, game, mult, def) in read)
            {
                var live = float.NaN;
                try { live = Convert.ToSingle(p.GetValue(exo)); } catch { }
                parts.Add($"{stat} {game:0.###}->{live:0.###} ({(def != null && def.Invert ? "÷" : "×")}{mult:0.###})");
            }
            // To the MelonLoader log as well: a tester's log without the transcript said nothing (2026-09-09).
            Core.Log.Msg($"buffs applied ({why}): {(parts.Count == 0 ? "nothing to apply" : string.Join(", ", parts))}{(exo == null ? "; no local exosuit yet (applies when it exists)" : "")}{GameGate()}");
        }

        /// <summary>
        /// The game's own switch on the leg multipliers: <c>VRPlayerControl.UpdateJumping</c> and
        /// <c>GetInputVelocity</c> multiply by <c>Legs_Jump</c> / <c>Legs_Leap</c> only while
        /// <c>GameManager.FriendlyFireEnabled</c> is false (read from the assembly 2026-09-09), and
        /// the sandbox sets that flag from its hazard choice (<c>SandboxUI.DelayedFriendlyFire</c>).
        /// Since the 2026-09-27 update <c>VRControllerInput.Update</c> skips <c>Legs_Haste</c> behind
        /// the same flag. With it on, no run, jump or leap perk works, the game's own included.
        /// </summary>
        public static bool LegPerksGatedOff()
        {
            try { return GameManager.FriendlyFireEnabled; } catch { return false; }
        }

        private static string GameGate()
        {
            var scene = "?"; try { scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name; } catch { }
            return LegPerksGatedOff()
                ? $"; scene {scene}; GameManager.FriendlyFireEnabled=true — the game skips ALL run-speed/jump/leap multipliers while this is on"
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
