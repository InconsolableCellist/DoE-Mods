using System;
using MelonLoader;

namespace LootOverhaul.Loot
{
    /// <summary>
    /// The ring settings of CustomAvatars, for the kobold's cogwheel tab. They live in
    /// CustomAvatars' own `[CustomAvatars]` preferences as `Ring{Left|Right}*` and
    /// `RingsOthersVisible`; this reads and writes them by name, so neither mod needs the
    /// other. CustomAvatars re-reads them twice a second, so a change shows on the hand at
    /// once. They apply to your own avatar only and change only what you see.
    /// </summary>
    public static class RingSettings
    {
        public const string Category = "CustomAvatars";

        public class Setting
        {
            public string Key, Label, Help, Format;
            public float Default, Min, Max, Step, BigStep;
        }

        public static readonly Setting[] All =
        {
            new Setting { Key = "Size",   Label = "SIZE",    Format = "x{0:0.00}",         Default = 1f, Min = 0.05f, Max = 10f,  Step = 0.05f, BigStep = 0.25f, Help = "bigger or smaller" },
            new Setting { Key = "Offset", Label = "UP/DOWN", Format = "{0:+0.0;-0.0;0}",   Default = 0f, Min = -5f,   Max = 5f,   Step = 0.1f,  BigStep = 0.5f,  Help = "+ lifts it off the back of the hand" },
            new Setting { Key = "Slide",  Label = "SLIDE",   Format = "{0:+0.00;-0.00;0}", Default = 0f, Min = -1f,   Max = 1f,   Step = 0.02f, BigStep = 0.1f,  Help = "+ moves it toward the fingertip" },
            new Setting { Key = "Tilt",   Label = "TILT",    Format = "{0:+0;-0;0}°",      Default = 0f, Min = -180f, Max = 180f, Step = 5f,    BigStep = 30f,   Help = "+ tips its top toward the fingertip" },
            new Setting { Key = "Turn",   Label = "TURN",    Format = "{0:+0;-0;0}°",      Default = 0f, Min = -180f, Max = 180f, Step = 5f,    BigStep = 30f,   Help = "+ points it toward the thumb" },
            new Setting { Key = "Roll",   Label = "ROLL",    Format = "{0:+0;-0;0}°",      Default = 0f, Min = -180f, Max = 180f, Step = 5f,    BigStep = 30f,   Help = "+ rolls its top toward the little finger" },
        };

        /// <summary>"Left" or "Right": the part of the entry name for that hand.</summary>
        public static string Side(bool left) => left ? "Left" : "Right";

        /// <summary>True when CustomAvatars is loaded and has the per-ring settings (0.42.14 or later).</summary>
        public static bool Available => FloatEntry("RingLeftSize") != null && BoolEntry("RingLeftVisible") != null;

        private static MelonPreferences_Entry<float> FloatEntry(string key)
        {
            try { return MelonPreferences.GetEntry<float>(Category, key); } catch { return null; }
        }

        private static MelonPreferences_Entry<bool> BoolEntry(string key)
        {
            try { return MelonPreferences.GetEntry<bool>(Category, key); } catch { return null; }
        }

        private static string Key(bool left, Setting s) => $"Ring{Side(left)}{s.Key}";

        public static float Get(bool left, Setting s)
        {
            var e = FloatEntry(Key(left, s));
            if (e == null) return s.Default;
            var v = e.Value;
            return float.IsFinite(v) ? v : s.Default;
        }

        public static void Nudge(bool left, Setting s, float by)
        {
            var e = FloatEntry(Key(left, s));
            if (e == null) return;
            var v = Get(left, s) + by;
            // Snap to the step, so ten presses of +0.1 read 1.0 and not 0.99999.
            v = (float)Math.Round(v / s.Step) * s.Step;
            e.Value = Math.Max(s.Min, Math.Min(s.Max, v));
            Save();
        }

        public static bool Visible(bool left)
        {
            var e = BoolEntry($"Ring{Side(left)}Visible");
            return e != null && e.Value;
        }

        public static void SetVisible(bool left, bool on)
        {
            var e = BoolEntry($"Ring{Side(left)}Visible");
            if (e == null) return;
            e.Value = on;
            Save();
        }

        public static bool OthersVisible
        {
            get { var e = BoolEntry("RingsOthersVisible"); return e != null && e.Value; }
            set { var e = BoolEntry("RingsOthersVisible"); if (e == null) return; e.Value = value; Save(); }
        }

        /// <summary>
        /// Back to the defaults for one ring: normal size, no offsets or angles. Whether it is
        /// shown stays as it is, so the reset can be watched on the hand.
        /// </summary>
        public static void Reset(bool left)
        {
            foreach (var s in All) { var e = FloatEntry(Key(left, s)); if (e != null) e.Value = s.Default; }
            Save();
        }

        public static string Show(bool left, Setting s) => string.Format(s.Format, Get(left, s));

        private static void Save()
        {
            try { MelonPreferences.Save(); }
            catch (Exception e) { Core.Log.Warning($"Ring settings: could not save: {e.GetType().Name}: {e.Message}"); }
        }
    }
}
