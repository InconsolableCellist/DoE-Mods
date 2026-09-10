using System;
using Il2Cpp;
using UnityEngine;

namespace StayPutVR.Trigger
{
    /// <summary>
    /// The one gameplay patch: a read-only postfix on <c>AvatarPlayer.OnDamaged</c>, which the
    /// game calls on the client that owns the avatar every time a hit lands on it. The postfix
    /// reads the damage, the type and the resulting health and hands them to
    /// <see cref="ShockPolicy"/>; it never touches the return value, so the game's damage maths
    /// is untouched whether the link is armed or not.
    ///
    /// Two filters matter. <c>__result</c> is the game's own "the damage was applied" answer, so
    /// a hit shrugged off by invulnerability or a blocked frame never reaches the policy. And
    /// the instance is compared against <c>AvatarPlayer.LocalAvatar</c>, because in a party the
    /// same method runs for other people's avatars on this client too — only your own hits are
    /// yours to be shocked for.
    ///
    /// Severity is the fraction of max HP the hit removed, not the raw number: weapon and
    /// difficulty scaling move raw damage around between runs, while "a third of your health"
    /// means the same thing in every dungeon.
    /// </summary>
    public static class DamageWatch
    {
        private static bool _installed;
        private static int _seen, _foreign;
        private static bool _loggedFirst;

        public static void Install()
        {
            System.Reflection.MethodInfo target = null;
            try { target = typeof(AvatarPlayer).GetMethod("OnDamaged"); }
            catch (Exception e) { Core.Log.Error($"Looking up AvatarPlayer.OnDamaged threw {e.GetType().Name}: {e.Message}"); }
            _installed = Hooks.Patch(target, null, Hooks.Of(typeof(DamageWatch), nameof(OnDamaged_Postfix)), "AvatarPlayer.OnDamaged");
            if (!_installed)
                Core.Log.Error("AvatarPlayer.OnDamaged could not be patched — the mod will see no hits. Nothing will be sent.");
        }

        public static bool Installed => _installed;
        public static string Stats() => $"{_seen} local hit(s) seen, {_foreign} on other players ignored";

        private static void OnDamaged_Postfix(AvatarPlayer __instance, float __0, float __1, Vector3 __2, DamageType __3, bool __result)
        {
            try
            {
                if (!ModConfig.Enabled.Value) return;
                if (!__result) return;                       // the game did not apply it
                if (!IsLocal(__instance)) { _foreign++; return; }

                _seen++;
                var damage = __0;
                var maxHp = 0f;
                var downed = false;
                try
                {
                    var health = __instance.health;
                    maxHp = health.maxHP;
                    // "Downed" covers both outright death and the last-chance state the game
                    // puts you in when a party could still revive you; either one ends the run
                    // as far as being hit is concerned.
                    downed = !health.IsAlive || health.lastChance || health.waitingForRescue;
                }
                catch (Exception e)
                {
                    if (!_loggedFirst) Core.Log.Warning($"Could not read health on the damaged avatar: {e.GetType().Name}: {e.Message}");
                }

                var fraction = maxHp > 0.01f ? Mathf.Clamp01(damage / maxHp) : 0f;
                var type = "Other";
                try { type = __3.ToString(); } catch { }

                if (!_loggedFirst)
                {
                    _loggedFirst = true;
                    Core.Log.Msg($"First local hit seen: {damage:0.#} HP of {maxHp:0.#} max ({fraction * 100f:0}%), type {type}. The damage hook works.");
                }

                ShockPolicy.OnHit(damage, fraction, type, downed);
            }
            catch (Exception e)
            {
                // A throw out of a Harmony postfix on an il2cpp method is not survivable in
                // the general case, so nothing is ever allowed to leave here.
                try { Core.Log.Warning($"OnDamaged postfix threw: {e.GetType().Name}: {e.Message}"); } catch { }
            }
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
