using System;
using HarmonyLib;
using Il2Cpp;
using UnityEngine;

namespace StayPutVR.Trigger
{
    /// <summary>
    /// Sees you being healed and says by what, for the heal shield (<see cref="HealShield"/>).
    ///
    /// Every heal of the local player goes through <c>AvatarPlayer.Net_Heal</c>, on your own
    /// client: a healing potion drunk, thrown or splashed (<c>HealthPotion.OnDrinkPotion</c>,
    /// <c>HealthPotion.RPC_OnBreak</c>, <c>HealthPotionArea.ApplyToPlayer</c>), a healing staff's
    /// beam (<c>KineticBeam.BeamPlayer</c>, only for the Heal kinetic), life steal
    /// ("notification.stole.health": a shield's absorb, the vampire weapon perks, the heal on a
    /// kill), a revive, and the game's own regain in <c>LateUpdate</c>. <c>Net_Heal</c> alone
    /// cannot tell a potion from the regain (both pass an empty notification), so the potion and
    /// beam methods get a prefix that marks the frame, and the <c>Net_Heal</c> postfix reads it.
    ///
    /// The staff has a second, independent witness: the beam's RPC <c>WeaponStaff.BeamPlayer</c>
    /// arriving with you as the target (<see cref="StaffBeam"/>). The first heal within a moment of
    /// it is the staff's, one per beam, and a beam that no heal follows is logged, once per episode.
    ///
    /// Full health counts for every source. Potions, the staff and the vampire weapon perks call
    /// <c>Net_Heal</c> whatever your health, so the postfix sees them with nothing gained. Two kinds
    /// of life steal skip <c>Net_Heal</c> at full health: a shield's absorb
    /// (<c>Shield.TryShieldAbsorb</c>, only while <c>normalizedHP</c> is under 1, at most once a
    /// second) and the Bloodlust ring's heal on a kill (<c>AvatarPlayer.OnAIKilled</c>, only while HP
    /// is under max). Both are hooked directly: the prefix decides whether a steal was due, and if
    /// no "notification.stole.health" heal came out of the call, the postfix counts it itself.
    ///
    /// The staff cannot be seen at full health: <c>KineticBeam.CanBeamPlayer</c> refuses a heal
    /// beam on a player at max HP (unless poisoned or frozen), so the staff never targets you and
    /// nothing is sent. The shield starts on its first heal once you are hurt.
    /// </summary>
    public static class HealWatch
    {
        private static HealSource _source;
        private static int _sourceFrame = -1;
        private static bool _installed;
        private static int _sources;
        private static int _heals, _counted;

        // A life steal call in progress (shield absorb, kill): whether a steal was due, and
        // whether Net_Heal already counted one inside it.
        private static bool _inSteal, _stealDue, _stealCounted, _stealIsAbsorb;
        private static string _stealWhat = "";
        private static float _lastAbsorbSteal = -1000f;
        /// <summary><c>Shield.TryShieldAbsorb</c>'s own cooldown between steals.</summary>
        private const float AbsorbCooldown = 1f;

        private static HealSource _lastBreak = HealSource.MajorPotion;
        private static float _lastBreakAt = -1000f;

        public static readonly StaffBeam Beam = new StaffBeam();

        public static bool Installed => _installed;
        public static string Stats() => $"{_heals} heal(s) seen, {_counted} started or extended a heal shield";

        public static void Install()
        {
            _installed = Hooks.Patch(AccessTools.DeclaredMethod(typeof(AvatarPlayer), "Net_Heal"),
                Hooks.Of(typeof(HealWatch), nameof(NetHeal_Prefix)),
                Hooks.Of(typeof(HealWatch), nameof(NetHeal_Postfix)),
                "AvatarPlayer.Net_Heal");

            Source(typeof(HealthPotion), "OnDrinkPotion", nameof(Potion_Prefix));
            Source(typeof(HealthPotion), "RPC_OnBreak", nameof(Break_Prefix));
            Source(typeof(HealthPotionArea), "ApplyToPlayer", nameof(Area_Prefix));
            Source(typeof(KineticBeam), "BeamPlayer", nameof(Staff_Prefix));
            // The beam's RPC, on every client; the prefix runs before it calls KineticBeam.BeamPlayer.
            if (Hooks.Patch(AccessTools.DeclaredMethod(typeof(WeaponStaff), "BeamPlayer", new[] { typeof(int) }),
                            Hooks.Of(typeof(HealWatch), nameof(StaffRpc_Prefix)), null,
                            "WeaponStaff.BeamPlayer"))
                _sources++;

            // Life steal that skips Net_Heal at full health.
            if (Hooks.Patch(AccessTools.DeclaredMethod(typeof(Shield), "TryShieldAbsorb"),
                            Hooks.Of(typeof(HealWatch), nameof(Absorb_Prefix)),
                            Hooks.Of(typeof(HealWatch), nameof(Steal_Postfix)),
                            "Shield.TryShieldAbsorb"))
                _sources++;
            if (Hooks.Patch(AccessTools.DeclaredMethod(typeof(AvatarPlayer), "OnAIKilled"),
                            Hooks.Of(typeof(HealWatch), nameof(Kill_Prefix)),
                            Hooks.Of(typeof(HealWatch), nameof(Steal_Postfix)),
                            "AvatarPlayer.OnAIKilled"))
                _sources++;

            if (!_installed || _sources == 0)
                Core.Log.Warning("Heal shield unavailable: the heal hooks did not go in (see the patch lines). Hits after a heal shock as usual.");
        }

        private static void Source(Type type, string method, string prefix)
        {
            // DeclaredOnly: the override on this class, not the base Potion/PotionArea one.
            if (Hooks.Patch(AccessTools.DeclaredMethod(type, method),
                            Hooks.Of(typeof(HealWatch), prefix),
                            Hooks.Of(typeof(HealWatch), nameof(Source_Postfix)),
                            $"{type.Name}.{method}"))
                _sources++;
        }

        private static void Potion_Prefix(HealthPotion __instance) => Mark(PotionSize(__instance));

        private static void Break_Prefix(HealthPotion __instance)
        {
            // Remembered for the splash area the break leaves behind.
            _lastBreak = PotionSize(__instance);
            try { _lastBreakAt = Time.unscaledTime; } catch { }
            Mark(_lastBreak);
        }

        /// <summary>The splash area is as big as the last potion broken within its lifetime; major if none was.</summary>
        private static void Area_Prefix(HealthPotionArea __instance)
        {
            var size = HealSource.MajorPotion;
            try
            {
                var lifetime = Mathf.Max(__instance.duration, 1f) + 1f;
                if (Time.unscaledTime - _lastBreakAt <= lifetime) size = _lastBreak;
            }
            catch { }
            Mark(size);
        }

        private static void Staff_Prefix() => Mark(HealSource.Staff);
        private static void Source_Postfix() => _source = HealSource.None;

        private static HealSource PotionSize(HealthPotion potion)
        {
            try { return HealSources.FromPotionType((int)potion.type); }
            catch { return HealSource.MajorPotion; }
        }

        private static void StaffRpc_Prefix(WeaponStaff __instance, int playerViewID)
        {
            try
            {
                if (!ModConfig.Enabled.Value) return;
                var local = AvatarPlayer.LocalAvatar;
                if (!Interop.Alive(local)) return;
                var target = AvatarPlayer.Find(playerViewID);
                if (!Interop.Alive(target) || target.Pointer != local.Pointer) return;

                var actor = -1;
                try { actor = __instance.PVO.OwnerActorNr; } catch { }
                var kinetic = StaffBeam.UnknownKinetic;
                try
                {
                    var kb = __instance.beam?.TryCast<KineticBeam>();
                    if (kb == null) kinetic = -3;   // not a kinetic beam at all
                    else if (kb.kineticEffect != null) kinetic = (int)kb.kineticEffect.type;
                }
                catch { }

                if (Beam.OnBeam(Time.unscaledTime, actor, kinetic))
                    ShockLog.Line($"staff beam on you from actor {actor} (kinetic type {KineticName(kinetic)})");
            }
            catch (Exception e)
            {
                try { Core.Log.Warning($"WeaponStaff.BeamPlayer prefix threw: {e.GetType().Name}: {e.Message}"); } catch { }
            }
        }

        private static string KineticName(int kinetic) => kinetic switch
        {
            StaffBeam.UnknownKinetic => "unreadable",
            -3 => "none, not a kinetic beam",
            StaffBeam.HealKinetic => "3, Heal",
            _ => kinetic.ToString(),
        };

        /// <summary>Once a frame: the staff beam's "no heal followed" and "stopped" lines.</summary>
        public static void Tick()
        {
            if (!Beam.InEpisode) return;
            float now;
            try { now = Time.unscaledTime; } catch { return; }
            var (missed, ended) = Beam.Poll(now);
            if (missed)
                ShockLog.Line($"staff beam on you from actor {Beam.FromActor} (kinetic type {KineticName(Beam.Kinetic)}) but no heal followed within {StaffBeam.Window:0.##} s — KineticBeam.BeamPlayer did not reach Net_Heal");
            if (ended)
                ShockLog.Line($"staff beam on you stopped: {Beam.Beams} beam(s), {Beam.Heals} heal(s) put down to the staff");
        }

        public static void Clear() => Beam.Clear();

        private static void Mark(HealSource what)
        {
            _source = what;
            try { _sourceFrame = Time.frameCount; } catch { _sourceFrame = -1; }
        }

        private static HealSource MarkedSource()
        {
            if (_source == HealSource.None) return HealSource.None;
            try { return _sourceFrame == Time.frameCount ? _source : HealSource.None; }
            catch { return HealSource.None; }
        }

        private static void NetHeal_Prefix(AvatarPlayer __instance, out float __state)
        {
            __state = -1f;
            try { if (IsLocal(__instance)) __state = Hp(__instance); } catch { }
        }

        private static void NetHeal_Postfix(AvatarPlayer __instance, string __2, float __state)
        {
            try
            {
                if (!ModConfig.Enabled.Value || __state < 0f || !IsLocal(__instance)) return;
                // Back on your feet: the next downed hit is a new death and shocks again.
                if ((__2 ?? "") == "notification.revived") ShockPolicy.ClearDown();
                var after = Hp(__instance);
                if (after < 0f) return;
                var gained = Mathf.Max(after - __state, 0f);
                var stole = (__2 ?? "") == "notification.stole.health";

                // One staff tick per beam: the KineticBeam frame mark and the beam RPC's window
                // both point at the same heal, and the mark claims the beam so the window cannot
                // take it again. An unmarked heal inside the window counts only if the beam is
                // still unclaimed, so the game's own regain cannot pass for a second tick.
                var source = MarkedSource();
                var now = Time.unscaledTime;
                if (source == HealSource.Staff) Beam.Claim();
                else if (source == HealSource.None && !stole && Beam.Covers(now) && Beam.Claim()) source = HealSource.Staff;
                if (source == HealSource.Staff) Beam.OnHeal();
                if (source == HealSource.None && stole) source = HealSource.LifeSteal;

                if (source == HealSource.LifeSteal && _inSteal)
                {
                    _stealCounted = true;
                    if (_stealIsAbsorb) _lastAbsorbSteal = now;
                }

                // Potions, the staff and life steal count at full health; the rest only if health went up.
                if (source == HealSource.None && gained <= 0.0001f) return;
                _heals++;

                var what = source != HealSource.None ? HealSources.Name(source)
                    : (__2 ?? "") == "notification.revived" ? "revive" : "not a potion, staff or life steal";
                if (ShockPolicy.OnHealed(source, what, gained)) _counted++;
            }
            catch (Exception e)
            {
                try { Core.Log.Warning($"Net_Heal postfix threw: {e.GetType().Name}: {e.Message}"); } catch { }
            }
        }

        /// <summary>
        /// <c>Shield.TryShieldAbsorb(impactIndex)</c> runs on the shield owner's client after an
        /// absorb, with the perk on; it heals the local player by the perk's amount if
        /// <c>impactIndex</c> is 1 or more, health is under full, and a second has passed.
        /// Here: everything but the health.
        /// </summary>
        private static void Absorb_Prefix(Shield __instance, int __0)
        {
            BeginSteal(absorb: true);
            try
            {
                if (!ModConfig.Enabled.Value || __0 < 1 || !IsLocal(AvatarPlayer.LocalAvatar)) return;
                if (!__instance.IsMine) return;
                var perk = __instance.perk;
                if (!perk.ShieldAbsorb_Enabled || perk.ShieldAbsorb <= 0f) return;
                _stealDue = Time.unscaledTime - _lastAbsorbSteal >= AbsorbCooldown;
                _stealWhat = "life steal: shield absorb";
            }
            catch { _stealDue = false; }
        }

        /// <summary>
        /// <c>AvatarPlayer.OnAIKilled(enemy, killerActorNr)</c>: on the local player, a kill of yours
        /// with the Bloodlust ring heals by the ring's stat, unless the enemy is on the players'
        /// side or a kamikaze that blew itself up, and only while alive and under max HP. Here:
        /// everything but the HP.
        /// </summary>
        private static void Kill_Prefix(AvatarPlayer __instance, Il2CppSauron.AI __0, int __1)
        {
            BeginSteal(absorb: false);
            try
            {
                if (!ModConfig.Enabled.Value || !IsLocal(__instance) || __0 == null) return;
                if ((int)__0.faction == 0) return;
                if (__0.isKamikaze && Blasted(__0)) return;
                var mine = -1;
                try { mine = __instance.PVO.OwnerActorNr; } catch { }
                if (mine < 0 || __1 != mine) return;
                if (__instance.Ring_AuraBloodlust <= 0f) return;
                var health = __instance.health;
                if (health == null || !health.IsAlive) return;
                _stealDue = true;
                _stealWhat = "life steal: Bloodlust ring, on a kill";
            }
            catch { _stealDue = false; }
        }

        private static bool Blasted(Il2CppSauron.AI ai)
        {
            var modules = ai.modules;
            if (modules == null) return false;
            for (var i = 0; i < modules.Length; i++)
            {
                var k = modules[i]?.TryCast<Il2CppSauron.AIKamikaze>();
                if (k != null) return k.wasBlasted;
            }
            return false;
        }

        private static void BeginSteal(bool absorb)
        {
            _inSteal = true;
            _stealIsAbsorb = absorb;
            _stealDue = false;
            _stealCounted = false;
        }

        /// <summary>A steal that was due but made no Net_Heal call: you were at full health. Counted here, once.</summary>
        private static void Steal_Postfix()
        {
            var due = _stealDue && !_stealCounted;
            _inSteal = _stealDue = _stealCounted = false;
            if (!due) return;
            try
            {
                if (_stealIsAbsorb) _lastAbsorbSteal = Time.unscaledTime;
                _heals++;
                if (ShockPolicy.OnHealed(HealSource.LifeSteal, _stealWhat, 0f)) _counted++;
            }
            catch (Exception e)
            {
                try { Core.Log.Warning($"life steal postfix threw: {e.GetType().Name}: {e.Message}"); } catch { }
            }
        }

        /// <summary>Health as a share of the base max, unclamped (the overheal reading), or -1.</summary>
        private static float Hp(AvatarPlayer avatar)
        {
            try
            {
                var health = avatar.health;
                if (health == null) return -1f;
                try { return health.normalizedHP_Overheal; }
                catch { return health.normalizedHP; }
            }
            catch { return -1f; }
        }

        private static bool IsLocal(AvatarPlayer avatar)
        {
            try
            {
                if (!Interop.Alive(avatar)) return false;
                var local = AvatarPlayer.LocalAvatar;
                return Interop.Alive(local) && avatar.Pointer == local.Pointer;
            }
            catch { return false; }
        }
    }
}
