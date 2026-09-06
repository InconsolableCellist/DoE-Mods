using System;
using Il2Cpp;
using UnityEngine;

namespace VisualCues.Controls
{
    /// <summary>
    /// Stick clicks, read from the game's own input abstraction (<c>XRInput.Instance</c>, which
    /// is the SteamVR or Oculus rig behind one interface; <c>L3</c>/<c>R3</c> are the stick
    /// buttons). The level is read and the edge detected here rather than trusting the rig's
    /// own <c>L3Down</c>: that flag is true for the one frame of the rig's Update, and a mod's
    /// OnUpdate is not guaranteed to run in the same frame slot.
    ///
    /// The game itself uses a single click (the item outline, locomotion on some devices),
    /// so the default is a double click: two clicks within <c>SummonDoubleClickSeconds</c>.
    /// The first click still does its vanilla thing. <c>SummonPress</c> switches to a single
    /// click or a hold of <c>SummonHoldSeconds</c>.
    /// </summary>
    public static class StickPress
    {
        /// <summary>"left" or "right".</summary>
        public static event Action<string> Pressed;

        private static bool _lastL, _lastR, _firedL, _firedR, _warned, _readyLogged;
        private static float _sinceL = -1f, _sinceR = -1f, _retryAt = -1f;
        private static float _prevClickL = -100f, _prevClickR = -100f;
        private static int _failures;

        public static void Tick()
        {
            if (Time.unscaledTime < _retryAt) return;
            try
            {
                var input = XRInput.Instance;
                if (!Interop.Alive(input)) { _retryAt = Time.unscaledTime + 2f; return; }
                var l = input.L3;
                var r = input.R3;
                if (!_readyLogged) { _readyLogged = true; Core.Log.Msg($"Stick clicks readable from {input.GetType().Name}."); }

                var which = (ModConfig.SummonStick.Value ?? "either").Trim().ToLowerInvariant();
                var mode = (ModConfig.SummonPress.Value ?? "double").Trim().ToLowerInvariant();
                if (which == "left" || which == "either") Edge("left", l, ref _lastL, ref _sinceL, ref _firedL, ref _prevClickL, mode);
                if (which == "right" || which == "either") Edge("right", r, ref _lastR, ref _sinceR, ref _firedR, ref _prevClickR, mode);
                _lastL = l; _lastR = r;
            }
            catch (Exception e)
            {
                _failures++;
                if (!_warned) { _warned = true; Core.Log.Warning($"Stick clicks not readable ({e.GetType().Name}: {e.Message}); retrying. The ' key sends a call from the keyboard."); }
                _retryAt = Time.unscaledTime + (_failures > 20 ? 30f : 3f);
            }
        }

        private static void Edge(string name, bool down, ref bool last, ref float since, ref bool fired, ref float prevClick, string mode)
        {
            var now = Time.unscaledTime;
            if (!down) { since = -1f; fired = false; return; }
            var edge = !last;
            if (edge) since = now;
            if (fired) return;
            switch (mode)
            {
                case "single":
                    if (edge) { fired = true; Pressed?.Invoke(name); }
                    break;
                case "hold":
                    if (since >= 0f && now - since >= Mathf.Max(0.1f, ModConfig.SummonHoldSeconds.Value)) { fired = true; Pressed?.Invoke(name); }
                    break;
                default: // double
                    if (!edge) break;
                    if (now - prevClick <= Mathf.Max(0.15f, ModConfig.SummonDoubleClickSeconds.Value))
                    {
                        prevClick = -100f;
                        fired = true;
                        Pressed?.Invoke(name);
                    }
                    else prevClick = now;
                    break;
            }
        }
    }
}
