using System;

namespace StayPutVR.Trigger
{
    /// <summary>
    /// How hard a hit should shock, as a magnitude from 0 to 1 for the StayPutVR app's float
    /// Shock parameter (app 1.5.2 and up). The app scales the shock between its configured
    /// intensity, at 0, and its configured max, at 1.
    ///
    /// The measure is the share of what you had left: a hit for a tenth of your health at full
    /// health is a tenth, the same hit with a fifth of your health left is half, and any hit
    /// that takes the rest is one. That single ratio orders every case the way it feels — the
    /// same blow hurts more the closer to death it leaves you, a bigger blow hurts more at the
    /// same health, and the killing blow is the worst — without special cases. Two knobs on
    /// top: a curve exponent below one lifts small hits so a chip at full health is still felt,
    /// and fall damage has a floor because a tumble should read as a serious hit whatever the
    /// number was. No UnityEngine dependency, so the tests can compile it.
    /// </summary>
    public static class Severity
    {
        /// <summary>The lightest magnitude ever sent for a hit. The app fires on any float above zero and zero is the release.</summary>
        public const float Least = 0.02f;

        /// <summary>The share of the health you had that the hit took: damage over health before the hit, 0..1.</summary>
        public static float Share(float fraction, float remaining)
        {
            var before = fraction + remaining;
            if (before <= 0.0001f) return 1f;
            return Clamp01(fraction / before);
        }

        /// <param name="fraction">Share of max HP the hit removed.</param>
        /// <param name="remaining">Share of max HP left after it.</param>
        /// <param name="damageType">The game's damage type name.</param>
        /// <param name="downed">Whether the hit killed you or put you in last chance.</param>
        /// <param name="curve">Exponent on the share; below one lifts small hits. Anything under 0.05 is treated as 1.</param>
        /// <param name="fallFloor">Fall damage counts as at least this share.</param>
        public static float Compute(float fraction, float remaining, string damageType, bool downed, float curve, float fallFloor)
        {
            var share = downed ? 1f : Share(fraction, remaining);
            if (string.Equals(damageType, "Fall", StringComparison.OrdinalIgnoreCase)) share = Math.Max(share, Clamp01(fallFloor));
            var exponent = curve > 0.05f ? curve : 1f;
            var magnitude = (float)Math.Pow(share, exponent);
            if (magnitude < Least) magnitude = Least;
            if (magnitude > 1f) magnitude = 1f;
            return magnitude;
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
