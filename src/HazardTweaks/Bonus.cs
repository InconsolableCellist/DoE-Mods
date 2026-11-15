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
        private static float _applied = 1f;

        /// <summary>Bring the table to the multiplier the current settings and lobby call for.</summary>
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
                    _applied = 1f;
                    Core.Log.Msg("Hazard bonus table found: " + Describe(_original, 1f));
                }

                float want = Gate.Evaluate(out _) ? Math.Max(0f, ModConfig.BonusMultiplier.Value) : 1f;
                if (Math.Abs(want - _applied) < 0.0001f) return;

                for (int r = 0; r < rows.Count; r++)
                {
                    var b = rows[r].boosts;
                    if (b == null) continue;
                    for (int i = 0; i < b.Count && i < _original[r].Length; i++) b[i] = _original[r][i] * want;
                }
                _applied = want;
                Core.Log.Msg($"Hazard bonus x{want:0.##}{(why == null ? "" : " (" + why + ")")}: " + Describe(_original, want));
            }
            catch (Exception e) { Core.Log.Warning($"Hazard bonus table: {e.GetType().Name}: {e.Message}"); }
        }

        private static string Describe(List<float[]> rows, float mult)
        {
            var parts = new List<string>();
            foreach (var row in rows)
            {
                var v = new List<string>();
                foreach (var f in row) v.Add($"{f * mult * 100f:0}%");
                parts.Add("[" + string.Join(" ", v) + "]");
            }
            return "tiers 1-7 " + string.Join(" / ", parts);
        }
    }
}
