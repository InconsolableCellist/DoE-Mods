using System;
using System.Collections.Generic;
using Il2Cpp;

namespace HazardTweaks
{
    /// <summary>
    /// The hazard bonus table, scaled in memory.
    ///
    /// <c>HazardData.difficultyValues</c> holds one row per reward tier (the "+25%" and the
    /// "+40%" hazards), each with seven boosts, one per dungeon tier. Read from the game's data
    /// on 2026-11-15: 0.05 0.07 0.10 0.13 0.17 0.21 0.25 and 0.08 0.12 0.17 0.22 0.28 0.34 0.40.
    /// Everything that shows or pays a hazard bonus reads this table, so scaling it once is
    /// the one change that keeps the board, the per-hazard lines and the payout in agreement,
    /// and it cannot double-count the way patching both the per-hazard lookup and the sum could.
    /// The original values are kept, so the multiplier can change or be undone in place.
    /// </summary>
    public static class Bonus
    {
        private static IntPtr _asset = IntPtr.Zero;
        private static List<float[]> _original;
        private static float _appliedMult = 1f;
        private static int _appliedStep;

        /// <summary>Bring the table to the multiplier and rounding the current settings and lobby call for.</summary>
        public static void Sync(string why)
        {
            try
            {
                var data = GameManager.hazardData;
                if (data == null) return;
                var rows = data.difficultyValues;
                if (rows == null) return;

                if (data.Pointer != _asset || _original == null || _original.Count != rows.Count)
                {
                    _asset = data.Pointer;
                    _original = new List<float[]>();
                    for (int r = 0; r < rows.Count; r++)
                    {
                        var b = rows[r].boosts;
                        var copy = new float[b == null ? 0 : b.Count];
                        for (int i = 0; i < copy.Length; i++) copy[i] = b[i];
                        _original.Add(copy);
                    }
                    _appliedMult = 1f;
                    _appliedStep = 0;
                    Core.Log.Msg("Hazard bonus table found: " + Describe(_original, 1f, 0));
                }

                bool on = Gate.Evaluate(out _) && Gate.Effective != null;
                float mult = on ? Gate.Effective.Multiplier : 1f;
                int step = on ? Gate.Effective.RoundUpTo : 0;
                if (Math.Abs(mult - 1f) < 0.0001f) step = 0; // vanilla stays exactly vanilla
                if (Math.Abs(mult - _appliedMult) < 0.0001f && step == _appliedStep) return;

                for (int r = 0; r < rows.Count; r++)
                {
                    var b = rows[r].boosts;
                    if (b == null) continue;
                    for (int i = 0; i < b.Count && i < _original[r].Length; i++) b[i] = Scaled(_original[r][i], mult, step);
                }
                _appliedMult = mult;
                _appliedStep = step;
                Core.Log.Msg($"Hazard bonus x{mult:0.##}{(step > 0 ? $", rounded up to {step}%" : "")}{(why == null ? "" : " (" + why + ")")}: " + Describe(_original, mult, step));
            }
            catch (Exception e) { Core.Log.Warning($"Hazard bonus table: {e.GetType().Name}: {e.Message}"); }
        }

        /// <summary>
        /// One boost (a fraction, 0.25 = +25%) times the multiplier, rounded UP to the next multiple
        /// of <paramref name="step"/> percent. The small epsilon keeps float noise from pushing an
        /// exact multiple (0.25 x 1.6 = 40.0000006%) up a whole step.
        /// </summary>
        public static float Scaled(float boost, float mult, int step)
        {
            double pct = boost * (double)mult * 100.0;
            if (step > 0) pct = Math.Ceiling(pct / step - 1e-4) * step;
            return (float)(pct / 100.0);
        }

        private static string Describe(List<float[]> rows, float mult, int step)
        {
            var parts = new List<string>();
            foreach (var row in rows)
            {
                var v = new List<string>();
                foreach (var f in row) v.Add($"{Scaled(f, mult, step) * 100f:0.#}%");
                parts.Add("[" + string.Join(" ", v) + "]");
            }
            return "tiers 1-7 " + string.Join(" / ", parts);
        }
    }
}
