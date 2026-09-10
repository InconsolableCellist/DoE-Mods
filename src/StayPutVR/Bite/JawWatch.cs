using System;
using UnityEngine;

namespace StayPutVR.Bite
{
    /// <summary>
    /// Turns a stream of jaw-open values into discrete bites.
    ///
    /// A bite is a <b>chomp</b>: the mouth opens past a threshold, stays open briefly, and then
    /// shuts quickly. All three parts are needed, because the mouth opens all day for reasons
    /// that are not bites — talking never crosses the open threshold for long, a yawn stays open
    /// far too long and closes slowly, and a laugh drifts shut rather than snapping. So the
    /// gesture is: open ≥ <c>BiteOpenThreshold</c>, held for between <c>BiteMinOpenSeconds</c>
    /// and <c>BiteMaxOpenSeconds</c>, then down to <c>BiteCloseThreshold</c> within
    /// <c>BiteSnapSeconds</c> of starting to close.
    ///
    /// Reported once per chomp on the closing edge, never while the mouth is open, so a bite
    /// lands when your teeth meet rather than when they part.
    /// </summary>
    public static class JawWatch
    {
        private enum Phase { Shut, Open, Closing }

        private static Phase _phase = Phase.Shut;
        private static float _openedAt, _fellAt, _peak;
        private static float _lastBiteAt = -1000f;
        private static float _lastValue;

        public static int Chomps { get; private set; }
        /// <summary>The last jaw value read, for the panel.</summary>
        public static float Jaw => _lastValue;
        /// <summary>Where the gesture is right now, as one word for the panel.</summary>
        public static string PhaseName => _phase.ToString().ToLowerInvariant();
        public static float LastBiteAt => _lastBiteAt;

        /// <summary>Feed one frame. True on the frame a chomp completes.</summary>
        public static bool Tick()
        {
            if (!FaceLink.TryJaw(out var value)) { _phase = Phase.Shut; _lastValue = 0f; return false; }
            _lastValue = value;

            var now = Time.unscaledTime;
            var open = Mathf.Clamp01(ModConfig.BiteOpenThreshold.Value);
            var shut = Mathf.Clamp01(ModConfig.BiteCloseThreshold.Value);
            if (shut >= open) shut = open * 0.5f;   // a nonsense config must not make every frame a bite

            switch (_phase)
            {
                case Phase.Shut:
                    if (value >= open) { _phase = Phase.Open; _openedAt = now; _peak = value; }
                    break;

                case Phase.Open:
                    if (value > _peak) _peak = value;
                    if (now - _openedAt > Mathf.Max(0.1f, ModConfig.BiteMaxOpenSeconds.Value))
                    {
                        // Held open too long to be a bite — a yawn, or a jaw resting open.
                        _phase = Phase.Shut;
                    }
                    else if (value < open)
                    {
                        _phase = Phase.Closing;
                        _fellAt = now;
                    }
                    break;

                case Phase.Closing:
                    if (value >= open)
                    {
                        // Opened again without shutting: not a chomp, restart the timing.
                        _phase = Phase.Open;
                        _openedAt = now;
                        _peak = value;
                    }
                    else if (value <= shut)
                    {
                        var held = _fellAt - _openedAt;
                        var snap = now - _fellAt;
                        _phase = Phase.Shut;
                        if (held >= Mathf.Max(0.02f, ModConfig.BiteMinOpenSeconds.Value)
                            && snap <= Mathf.Max(0.05f, ModConfig.BiteSnapSeconds.Value)
                            && now - _lastBiteAt >= Mathf.Max(0.1f, ModConfig.BiteGestureCooldownSeconds.Value))
                        {
                            _lastBiteAt = now;
                            Chomps++;
                            ShockLog.Line($"chomp #{Chomps}: open {held:0.00} s to peak {_peak:0.00}, shut in {snap:0.00} s");
                            return true;
                        }
                    }
                    else if (now - _fellAt > Mathf.Max(0.05f, ModConfig.BiteSnapSeconds.Value))
                    {
                        // Closing too slowly to be a snap — speech, or a drifting jaw.
                        _phase = Phase.Shut;
                    }
                    break;
            }
            return false;
        }

        public static string Describe() => $"{Chomps} chomp(s) seen; jaw {(FaceLink.Ready ? $"{_lastValue:0.00} via {FaceLink.Status}" : "unreadable — " + FaceLink.Status)}";
    }
}
