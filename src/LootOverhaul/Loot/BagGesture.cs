using System;
using Il2Cpp;
using Il2CppValve.VR;
using UnityEngine;
using Interop = LootOverhaul.Recon.Interop;

namespace LootOverhaul.Loot
{
    /// <summary>
    /// Opening the bag without a keyboard. Reads the game's own SteamVR actions
    /// (<c>othergate_Thumbstick</c>, <c>othergate_HandTrigger</c>) for the right hand:
    ///  - "stick-hold" (default): right stick held straight up for <c>BagGestureHoldSeconds</c>.
    ///    The right stick only snap-turns left/right, so up is free.
    ///  - "back-grip": right hand behind and below the head, grip squeezed, stick up. The
    ///    back holsters have large grab zones, so this one fights the game; kept as an option.
    /// Only SteamVR/OpenVR rigs have these actions; on other rigs the reads throw once, the
    /// gesture logs that and stays off, and the keyboard key still works.
    /// </summary>
    public static class BagGesture
    {
        private static float _heldSince = -1f, _retryAt = -1f;
        private static bool _fired, _loggedOnce, _readyLogged;
        private static int _failures;

        public static void Tick()
        {
            var mode = (ModConfig.BagGesture.Value ?? "off").Trim().ToLowerInvariant();
            if (mode == "off") return;
            if (Time.unscaledTime < _retryAt) return;
            try
            {
                // The action set is null until SteamVR input is up (a NullReference in the main
                // menu on 2026-09-02), so a failure means "try again in a few seconds", not "off".
                var stickAction = SteamVR_Actions.othergate_Thumbstick;
                var gripAction = SteamVR_Actions.othergate_HandTrigger;
                if (stickAction == null || gripAction == null) { _retryAt = Time.unscaledTime + 3f; return; }
                var stick = stickAction.GetAxis(SteamVR_Input_Sources.RightHand);
                var grip = gripAction.GetAxis(SteamVR_Input_Sources.RightHand);
                if (!_readyLogged) { _readyLogged = true; Core.Log.Msg($"Bag gesture reading SteamVR actions ({mode})."); }
                var up = stick.y > 0.75f && Mathf.Abs(stick.x) < 0.5f;
                var active = mode == "stick-hold" ? up : up && grip > 0.6f && HandBehindBack();

                if (!active) { _heldSince = -1f; _fired = false; return; }
                if (_heldSince < 0f) _heldSince = Time.unscaledTime;
                if (!_fired && Time.unscaledTime - _heldSince >= Mathf.Max(0.15f, ModConfig.BagGestureHoldSeconds.Value))
                {
                    _fired = true;
                    BagPanel.Toggle();
                }
            }
            catch (Exception e)
            {
                _failures++;
                if (!_loggedOnce) { _loggedOnce = true; Core.Log.Warning($"Bag gesture: SteamVR actions not readable yet ({e.GetType().Name}); retrying every few seconds. The [ key always works."); }
                _retryAt = Time.unscaledTime + (_failures > 20 ? 30f : 3f);
            }
        }

        private static bool HandBehindBack()
        {
            try
            {
                var local = AvatarPlayer.LocalAvatar;
                if (!Interop.Alive(local)) return false;
                var head = local.Head; var hand = local.RightHand;
                if (!Interop.Alive(head) || !Interop.Alive(hand)) return false;
                var fwd = head.forward; fwd.y = 0f; fwd.Normalize();
                var rel = hand.position - head.position;
                var behind = Vector3.Dot(rel, fwd) < -0.05f;
                var low = rel.y < -0.25f;
                return behind && low;
            }
            catch { return false; }
        }
    }
}
