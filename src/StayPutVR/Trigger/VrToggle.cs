using System;
using Il2Cpp;
using UnityEngine;

namespace StayPutVR.Trigger
{
    /// <summary>
    /// Arming and disarming from inside the headset, on the game's own input abstraction
    /// (<c>XRInput.Instance</c>, the SteamVR or Oculus rig behind one interface; <c>L3</c> and
    /// <c>R3</c> are the stick buttons). Read as a level with the edge detected here, the same
    /// way VisualCues does it, because the rig's own <c>L3Down</c> flag is true only during the
    /// rig's Update and a mod's OnUpdate is not guaranteed to land in the same frame slot.
    ///
    /// The gesture is <b>both sticks clicked in at once</b>: quarter of a second disarms, a
    /// second and a half arms. One gesture, and how long you hold it decides which way it goes,
    /// so the quick panicky version is the one that gets you out. It is the only way to arm or
    /// disarm — there are no keyboard keys.
    ///
    /// It was a double click of a single stick in 0.1.0 and that was wrong. The game uses a
    /// single stick click itself, and VisualCues uses a *double* click of either stick to send
    /// its call — so sending a call disarmed the shock link, which is exactly the sort of silent
    /// state change this mod must not have. Both sticks together collides with nothing: no game
    /// action and no other mod here asks for it, and it cannot happen while you are simply
    /// walking around.
    /// </summary>
    public static class VrToggle
    {
        // Not settings: the quick half of the gesture has to stay quick and the deliberate half
        // has to stay deliberate, and nothing was learned by making either adjustable.
        private const float DisarmHoldSeconds = 0.25f;
        private const float ArmHoldSeconds = 1.5f;

        private static bool _lastBoth;
        private static float _bothSince = -1f;
        private static bool _fired;
        private static float _retryAt = -1f;
        private static bool _warned, _readyLogged;
        private static int _failures;

        public static void Tick()
        {
            if (Time.unscaledTime < _retryAt) return;
            try
            {
                var input = XRInput.Instance;
                if (!Interop.Alive(input)) { _retryAt = Time.unscaledTime + 2f; return; }

                var both = input.L3 && input.R3;
                if (!_readyLogged)
                {
                    _readyLogged = true;
                    Core.Log.Msg($"Stick clicks readable from {input.GetType().Name}: click BOTH sticks — {DisarmHoldSeconds:0.##} s disarms, {ArmHoldSeconds:0.#} s arms.");
                }

                var now = Time.unscaledTime;
                if (!both)
                {
                    // Released: ready for the next gesture.
                    _bothSince = -1f;
                    _fired = false;
                    _lastBoth = false;
                    return;
                }

                if (!_lastBoth) { _bothSince = now; _fired = false; }
                _lastBoth = true;

                if (_fired || _bothSince < 0f) return;
                var held = now - _bothSince;

                // The arm state is read at the moment a threshold passes, and the gesture then
                // latches until both sticks come up — so holding on past the disarm threshold
                // cannot walk straight back into arming.
                if (ShockPolicy.Armed)
                {
                    if (held >= DisarmHoldSeconds)
                    {
                        _fired = true;
                        ShockPolicy.SetArmed(false, "both sticks clicked");
                    }
                }
                else if (held >= ArmHoldSeconds)
                {
                    _fired = true;
                    ShockPolicy.SetArmed(true, "both sticks held in");
                }
            }
            catch (Exception e)
            {
                _failures++;
                if (!_warned)
                {
                    _warned = true;
                    Core.Log.Warning($"Stick clicks not readable ({e.GetType().Name}: {e.Message}); retrying. Until they are, the link cannot be armed or disarmed.");
                }
                _retryAt = Time.unscaledTime + (_failures > 20 ? 30f : 3f);
            }
        }
    }
}
