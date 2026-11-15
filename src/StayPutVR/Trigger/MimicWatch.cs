using System;
using System.Collections.Generic;
using Il2Cpp;
using UnityEngine;

namespace StayPutVR.Trigger
{
    /// <summary>
    /// Waking a mimic shocks the player who did it. The game never records who that was: its
    /// open is an RPC with no arguments, and its events carry no player. What it does have is
    /// three places that only run, or only count, on the PC of the player responsible, and
    /// those are what is hooked here:
    ///
    /// <list type="bullet">
    /// <item><c>TriggeredInteractable.OnButtonPressed</c>: the open prompt on the chest. It is
    /// driven by this client's own controller, so a press here is always the local player's.</item>
    /// <item><c>Lock.OnKeyTurned(Key, PropRoot)</c>: a key turned in the mimic's lock. It counts
    /// when the key is this client's (<c>IsMine</c>), which is the player holding it.</item>
    /// <item><c>Mimic_Trigger.OnTriggerEnter(Collider)</c>: a weapon touching the mimic, which
    /// wakes it. Only when the trigger actually fired (its <c>isTriggered</c> went from false to
    /// true) and the weapon is this client's.</item>
    /// </list>
    ///
    /// Only a mimic that is still alive and closed counts, so opening a dead one for its loot does
    /// not shock. One shock per mimic per <see cref="RepeatSeconds"/>, so pressing and then
    /// turning a key in the same mimic is one shock. Everything is read-only: the game's own
    /// behaviour is untouched, and every other player decides for themselves on their own PC.
    /// </summary>
    public static class MimicWatch
    {
        private const float RepeatSeconds = 5f;

        private static readonly Dictionary<IntPtr, float> LastShockAt = new Dictionary<IntPtr, float>();
        private static int _seen, _foreign;

        public static void Install()
        {
            Patch(typeof(TriggeredInteractable), "OnButtonPressed", 0, nameof(Button_Prefix), null, "TriggeredInteractable.OnButtonPressed");
            Patch(typeof(Lock), "OnKeyTurned", 2, nameof(KeyTurned_Prefix), null, "Lock.OnKeyTurned");
            Patch(typeof(Mimic_Trigger), "OnTriggerEnter", 1, nameof(Trigger_Prefix), nameof(Trigger_Postfix), "Mimic_Trigger.OnTriggerEnter");
        }

        public static string Stats() => $"{_seen} mimic wake(s) by you, {_foreign} by others ignored";

        public static void Clear() => LastShockAt.Clear();

        private static void Patch(Type type, string name, int argc, string prefix, string postfix, string label)
        {
            System.Reflection.MethodInfo target = null;
            try
            {
                foreach (var m in type.GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly))
                    if (m.Name == name && m.GetParameters().Length == argc) { target = m; break; }
            }
            catch (Exception e) { Core.Log.Warning($"Looking up {label} threw {e.GetType().Name}: {e.Message}"); }
            Hooks.Patch(target,
                prefix == null ? null : Hooks.Of(typeof(MimicWatch), prefix),
                postfix == null ? null : Hooks.Of(typeof(MimicWatch), postfix),
                label);
        }

        // ---- the open prompt -------------------------------------------------------------

        private static void Button_Prefix(TriggeredInteractable __instance)
        {
            try
            {
                if (!ModConfig.Enabled.Value || !ModConfig.MimicShock.Value) return;
                if (!Interop.Alive(__instance)) return;
                var mimic = __instance.GetComponentInParent<Mimic>();
                if (!Sleeping(mimic)) return;
                Shock(mimic, "pressed open");
            }
            catch (Exception e) { Swallow("button", e); }
        }

        // ---- a key in the lock -----------------------------------------------------------

        private static void KeyTurned_Prefix(Lock __instance, Key __0)
        {
            try
            {
                if (!ModConfig.Enabled.Value || !ModConfig.MimicShock.Value) return;
                if (!Interop.Alive(__instance)) return;
                var mimic = __instance.GetComponentInParent<Mimic>();
                if (!Sleeping(mimic)) return;
                if (!Interop.Alive(__0) || !__0.IsMine) { _foreign++; return; }
                Shock(mimic, "turned a key in it");
            }
            catch (Exception e) { Swallow("key", e); }
        }

        // ---- a weapon touching it --------------------------------------------------------

        private static void Trigger_Prefix(Mimic_Trigger __instance, out bool __state)
        {
            __state = true;
            try { if (Interop.Alive(__instance)) __state = __instance.isTriggered; }
            catch { }
        }

        private static void Trigger_Postfix(Mimic_Trigger __instance, Collider __0, bool __state)
        {
            try
            {
                if (__state) return;                                  // already woken before this touch
                if (!ModConfig.Enabled.Value || !ModConfig.MimicShock.Value) return;
                if (!Interop.Alive(__instance) || !__instance.isTriggered) return;   // this touch did not wake it
                if (!IsMine(__0)) { _foreign++; return; }
                var mimic = __instance.mimic;
                if (!Interop.Alive(mimic)) return;
                Shock(mimic, "hit it with a weapon");
            }
            catch (Exception e) { Swallow("weapon", e); }
        }

        /// <summary>Whether the weapon behind this collider is this client's: its Prop is ours, or it hangs off our avatar.</summary>
        private static bool IsMine(Collider c)
        {
            if (!Interop.Alive(c)) return false;
            var rb = c.attachedRigidbody;
            var from = Interop.Alive(rb) ? rb.transform : c.transform;
            var prop = from.GetComponentInParent<Prop>();
            if (Interop.Alive(prop)) return prop.IsMine;
            var local = AvatarPlayer.LocalAvatar;
            return Interop.Alive(local) && from.IsChildOf(local.transform);
        }

        // ---- shared ----------------------------------------------------------------------

        /// <summary>A live mimic that has not opened yet. A dead one is opened for its loot and does not count.</summary>
        private static bool Sleeping(Mimic mimic)
        {
            if (!Interop.Alive(mimic)) return false;
            try { return mimic.IsAlive && !mimic.IsOpen && !mimic.IsOpening; }
            catch { return false; }
        }

        private static void Shock(Mimic mimic, string how)
        {
            var now = Time.unscaledTime;
            var key = mimic.Pointer;
            if (LastShockAt.TryGetValue(key, out var at) && now - at < RepeatSeconds) return;
            LastShockAt[key] = now;
            _seen++;
            ShockPolicy.OnMimic($"mimic `{Interop.Name(mimic)}`: you {how}");
        }

        private static void Swallow(string where, Exception e)
        {
            // A throw out of a Harmony patch on an il2cpp method is not survivable in general.
            try { Core.Log.Warning($"Mimic {where} hook threw: {e.GetType().Name}: {e.Message}"); } catch { }
        }
    }
}
