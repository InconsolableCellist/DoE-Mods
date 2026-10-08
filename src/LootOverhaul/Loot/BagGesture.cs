using System;
using Il2Cpp;
using Il2CppValve.VR;
using UnityEngine;
using Interop = LootOverhaul.Recon.Interop;

namespace LootOverhaul.Loot
{
    /// <summary>
    /// Opening the bag without a keyboard. Reads the right stick and grip through the game's
    /// <c>XRInput</c> singleton, or its SteamVR actions (<c>othergate_Thumbstick</c>,
    /// <c>othergate_HandTrigger</c>) while that is not up yet:
    ///  - "stick-hold" (default): right stick held straight up for <c>BagGestureHoldSeconds</c>.
    ///    The right stick only snap-turns left/right, so up is free.
    ///  - "back-grip": right hand behind and below the head, grip squeezed, stick up. The
    ///    back holsters have large grab zones, so this one fights the game; kept as an option.
    /// A failed read logs once and retries; the keyboard key still works.
    /// </summary>
    public static class BagGesture
    {
        private static float _heldSince = -1f, _retryAt = -1f;
        private static bool _fired, _loggedOnce, _readyLogged;
        private static int _failures;

        // The setting, parsed once and again only when its text changes (not every frame).
        private static string _modeRaw, _mode = "off";

        private static string Mode()
        {
            var raw = ModConfig.BagGesture.Value;
            if (!ReferenceEquals(raw, _modeRaw)) { _modeRaw = raw; _mode = (raw ?? "off").Trim().ToLowerInvariant(); }
            return _mode;
        }

        public static void Tick()
        {
            var mode = Mode();
            if (mode == "off") return;
            if (Time.unscaledTime < _retryAt) return;
            try
            {
                // The game's own input layer first: XRInput is the singleton every backend
                // implements (OpenVRInput, and since the 2026-09-27 update SteamOpenXRInput and
                // SteamFrameInput, where the SteamVR actions below do not exist).
                Vector2 stick; float grip; string source;
                var xr = XRInput.IsValid ? XRInput.Instance : null;
                if (xr != null)
                {
                    stick = xr.rightThumbstick; grip = xr.rightHandTrigger;
                    // The type name is only for the one-time log line; reading it every frame cost an interop call and a string.
                    source = _readyLogged ? null : xr.GetIl2CppType().Name;
                }
                else
                {
                    // The action set is null until SteamVR input is up (a NullReference in the main
                    // menu on 2026-09-02), so a failure means "try again in a few seconds", not "off".
                    var stickAction = SteamVR_Actions.othergate_Thumbstick;
                    var gripAction = SteamVR_Actions.othergate_HandTrigger;
                    if (stickAction == null || gripAction == null) { _retryAt = Time.unscaledTime + 3f; return; }
                    stick = stickAction.GetAxis(SteamVR_Input_Sources.RightHand);
                    grip = gripAction.GetAxis(SteamVR_Input_Sources.RightHand);
                    source = "SteamVR actions";
                }
                if (!_readyLogged) { _readyLogged = true; Core.Log.Msg($"Bag gesture reading {source} ({mode})."); }
                var up = stick.y > 0.75f && Mathf.Abs(stick.x) < 0.5f;
                var active = mode == "stick-hold" ? up : up && grip > 0.6f && HandBehindBack();

                if (!active) { _heldSince = -1f; _fired = false; return; }
                if (_heldSince < 0f) _heldSince = Time.unscaledTime;
                if (!_fired && Time.unscaledTime - _heldSince >= Mathf.Max(0.15f, ModConfig.BagGestureHoldSeconds.Value))
                {
                    _fired = true;
                    Core.Log.Msg($"Bag gesture: stick held up — bag {(BagPanel.IsOpen ? "closing" : "opening")}.");
                    BagPanel.Toggle();
                }
            }
            catch (Exception e)
            {
                _failures++;
                if (!_loggedOnce) { _loggedOnce = true; Core.Log.Warning($"Bag gesture: controller input not readable yet ({e.GetType().Name}); retrying every few seconds. The [ key always works."); }
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
