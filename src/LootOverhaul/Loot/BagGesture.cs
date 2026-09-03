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
    ///  - "back-grip": right hand behind and below the head (reaching to the small of the
    ///    back), grip squeezed, stick pushed up, held for <c>BagGestureHoldSeconds</c>.
    ///  - "stick-hold": right stick held up for the same time.
    /// Only SteamVR/OpenVR rigs have these actions; on other rigs the reads throw once, the
    /// gesture logs that and stays off, and the keyboard key still works.
    /// </summary>
    public static class BagGesture
    {
        private static float _heldSince = -1f;
        private static bool _fired, _disabled, _loggedOnce;

        public static void Tick()
        {
            if (_disabled) return;
            var mode = (ModConfig.BagGesture.Value ?? "off").Trim().ToLowerInvariant();
            if (mode == "off") return;
            try
            {
                var stick = SteamVR_Actions.othergate_Thumbstick.GetAxis(SteamVR_Input_Sources.RightHand);
                var grip = SteamVR_Actions.othergate_HandTrigger.GetAxis(SteamVR_Input_Sources.RightHand);
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
                if (!_loggedOnce) { _loggedOnce = true; Core.Log.Warning($"Bag gesture unavailable on this rig ({e.GetType().Name}: {e.Message}); use the [ key."); }
                _disabled = true;
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
