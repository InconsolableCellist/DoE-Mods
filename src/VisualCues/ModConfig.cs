using MelonLoader;

namespace VisualCues
{
    /// <summary>
    /// Settings in UserData/MelonPreferences.cfg: [VisualCues] for what a player changes,
    /// [VisualCues_Dev] for diagnostics. Everything is read live every frame, so editing the
    /// file and pressing the reload key applies without a restart.
    /// </summary>
    public static class ModConfig
    {
        public static MelonPreferences_Category Main;
        public static MelonPreferences_Category Dev;

        // ---- [VisualCues] ---------------------------------------------------------------
        public static MelonPreferences_Entry<bool> Enabled;

        /// <summary>The "come here" call: press a stick in, everyone with the mod sees an arrow to you.</summary>
        public static MelonPreferences_Entry<bool> SummonEnabled;
        /// <summary>Which stick click sends the call: left, right, or either.</summary>
        public static MelonPreferences_Entry<string> SummonStick;
        /// <summary>How the click sends: "double" (two clicks within SummonDoubleClickSeconds, leaves the game's single click alone), "single", or "hold" (SummonHoldSeconds).</summary>
        public static MelonPreferences_Entry<string> SummonPress;
        /// <summary>Two clicks closer together than this are a double click.</summary>
        public static MelonPreferences_Entry<float> SummonDoubleClickSeconds;
        /// <summary>Hold the stick in this long before it sends (SummonPress = hold).</summary>
        public static MelonPreferences_Entry<float> SummonHoldSeconds;
        /// <summary>Show incoming calls (marker, notification). Off for a hearing player who only wants to send.</summary>
        public static MelonPreferences_Entry<bool> SummonShowIncoming;
        /// <summary>Flash "Call sent" on your own HUD when a call goes out.</summary>
        public static MelonPreferences_Entry<bool> SummonSentFlash;
        /// <summary>The caller's marker goes away once you are within this many metres of them and facing them. 0 = never.</summary>
        public static MelonPreferences_Entry<float> SummonDismissMeters;
        /// <summary>Degrees from straight ahead that count as facing the caller for the dismissal.</summary>
        public static MelonPreferences_Entry<float> SummonFacingAngle;
        /// <summary>No second call goes out within this many seconds of the last one.</summary>
        public static MelonPreferences_Entry<float> SummonCooldownSeconds;
        /// <summary>How long the arrow to the caller stays on the receiver's HUD.</summary>
        public static MelonPreferences_Entry<float> SummonDurationSeconds;
        /// <summary>Also show the game's own text notification ("<name> is calling you").</summary>
        public static MelonPreferences_Entry<bool> SummonToast;
        /// <summary>Also point the game's own revive-style arrow at the caller. Off by default: it is the same arrow the game uses for downed players.</summary>
        public static MelonPreferences_Entry<bool> SummonUseGameArrow;
        /// <summary>Buzz the controller that sent the call, so the sender knows it went out.</summary>
        public static MelonPreferences_Entry<bool> SummonHaptics;

        /// <summary>The noise cue: a marker toward every enemy that makes a sound you cannot see.</summary>
        public static MelonPreferences_Entry<bool> NoiseEnabled;
        /// <summary>Enemies farther than this (metres) are ignored.</summary>
        public static MelonPreferences_Entry<float> NoiseRangeMeters;
        /// <summary>An enemy within this many degrees of straight ahead, with a clear line of sight, counts as seen.</summary>
        public static MelonPreferences_Entry<float> NoiseSeenAngle;
        /// <summary>Inside this many degrees of straight ahead an enemy counts as seen with no line-of-sight test: you are facing it, the arrow has done its job (gates and bars block the ray but not your eyes).</summary>
        public static MelonPreferences_Entry<float> NoiseFacingAngle;
        /// <summary>Show the marker for seen enemies too (then it is a plain "who is making noise" display).</summary>
        public static MelonPreferences_Entry<bool> NoiseIncludeVisible;
        /// <summary>A marker that has been up this long fades even if the enemy keeps making noise. 0 = stays while noisy.</summary>
        public static MelonPreferences_Entry<float> NoiseMaxSeconds;
        /// <summary>After a marker times out, that enemy gets no new marker for this long.</summary>
        public static MelonPreferences_Entry<float> NoiseRepeatSeconds;
        /// <summary>How long a marker lingers after the enemy's last sound.</summary>
        public static MelonPreferences_Entry<float> NoiseDurationSeconds;
        /// <summary>Footsteps count as noise.</summary>
        public static MelonPreferences_Entry<bool> NoiseFootsteps;
        /// <summary>Enemy animation effects (attacks, growls, spell casts) count as noise.</summary>
        public static MelonPreferences_Entry<bool> NoiseAnimationFx;
        /// <summary>Any positioned sound the audio manager plays within NoiseAttributeRadius of an enemy counts as that enemy's noise.</summary>
        public static MelonPreferences_Entry<bool> NoiseAttributeSounds;
        /// <summary>Metres. A positioned sound closer than this to an enemy is that enemy's.</summary>
        public static MelonPreferences_Entry<float> NoiseAttributeRadius;
        /// <summary>Most markers on screen at once. The nearest win.</summary>
        public static MelonPreferences_Entry<int> NoiseMaxMarkers;

        /// <summary>HUD plane distance from the eyes, metres. Closer walls and enemies can cover it unless HudOnTop works.</summary>
        public static MelonPreferences_Entry<float> HudDistance;
        /// <summary>Radius of the edge ring as an angle from straight ahead, degrees. Off-screen cues sit on this ring.</summary>
        public static MelonPreferences_Entry<float> HudRingDegrees;
        /// <summary>Overall size multiplier for the ring arcs, tips and labels.</summary>
        public static MelonPreferences_Entry<float> HudScale;
        /// <summary>Draw the HUD over everything (needs a shader with a depth-test switch; the log says whether it got one).</summary>
        public static MelonPreferences_Entry<bool> HudOnTop;
        /// <summary>Print the distance next to each marker.</summary>
        public static MelonPreferences_Entry<bool> HudShowDistance;

        // ---- [VisualCues_Dev] -----------------------------------------------------------
        public static MelonPreferences_Entry<bool> VerboseLogging;
        /// <summary>Write every positioned sound with its nearest enemy to the session log, for tuning the attribution.</summary>
        public static MelonPreferences_Entry<bool> LogSounds;
        public static MelonPreferences_Entry<bool> HotkeysEnabled;

        public static void Load()
        {
            Main = MelonPreferences.CreateCategory("VisualCues");
            Enabled = Main.CreateEntry("Enabled", true);

            SummonEnabled = Main.CreateEntry("SummonEnabled", true);
            SummonStick = Main.CreateEntry("SummonStick", "either", description: "left, right, or either. Clicking a stick in sends the call.");
            SummonPress = Main.CreateEntry("SummonPress", "double", description: "double = two clicks within SummonDoubleClickSeconds (the game's single click keeps working); single; hold.");
            SummonDoubleClickSeconds = Main.CreateEntry("SummonDoubleClickSeconds", 0.45f);
            SummonHoldSeconds = Main.CreateEntry("SummonHoldSeconds", 0.6f, description: "Seconds the stick must stay clicked before the call sends, when SummonPress = hold.");
            SummonShowIncoming = Main.CreateEntry("SummonShowIncoming", true, description: "Show other players' calls. A hearing player who only wants to send sets this false.");
            SummonSentFlash = Main.CreateEntry("SummonSentFlash", true, description: "Flash 'Call sent' on your own HUD.");
            SummonDismissMeters = Main.CreateEntry("SummonDismissMeters", 10f, description: "The caller's marker goes away once you are this close and facing them. 0 = never.");
            SummonFacingAngle = Main.CreateEntry("SummonFacingAngle", 25f);
            SummonCooldownSeconds = Main.CreateEntry("SummonCooldownSeconds", 1.5f);
            SummonDurationSeconds = Main.CreateEntry("SummonDurationSeconds", 4f);
            SummonToast = Main.CreateEntry("SummonToast", true);
            SummonUseGameArrow = Main.CreateEntry("SummonUseGameArrow", false, description: "Also use the game's revive arrow to point at the caller.");
            SummonHaptics = Main.CreateEntry("SummonHaptics", true);

            NoiseEnabled = Main.CreateEntry("NoiseEnabled", true);
            NoiseRangeMeters = Main.CreateEntry("NoiseRangeMeters", 18f);
            NoiseSeenAngle = Main.CreateEntry("NoiseSeenAngle", 40f, description: "Degrees from straight ahead. Inside this cone with clear line of sight = seen = no marker (unless NoiseIncludeVisible).");
            NoiseFacingAngle = Main.CreateEntry("NoiseFacingAngle", 15f, description: "Degrees. Facing an enemy this squarely counts as seen even if a gate blocks the ray.");
            NoiseIncludeVisible = Main.CreateEntry("NoiseIncludeVisible", false);
            NoiseMaxSeconds = Main.CreateEntry("NoiseMaxSeconds", 6f, description: "A marker fades after this long even if the enemy keeps making noise. 0 = never.");
            NoiseRepeatSeconds = Main.CreateEntry("NoiseRepeatSeconds", 4f, description: "After a marker times out, no new marker for that enemy for this long.");
            NoiseDurationSeconds = Main.CreateEntry("NoiseDurationSeconds", 2.5f);
            NoiseFootsteps = Main.CreateEntry("NoiseFootsteps", true);
            NoiseAnimationFx = Main.CreateEntry("NoiseAnimationFx", true);
            NoiseAttributeSounds = Main.CreateEntry("NoiseAttributeSounds", true);
            NoiseAttributeRadius = Main.CreateEntry("NoiseAttributeRadius", 2.0f);
            NoiseMaxMarkers = Main.CreateEntry("NoiseMaxMarkers", 8);

            HudDistance = Main.CreateEntry("HudDistance", 1.2f);
            HudRingDegrees = Main.CreateEntry("HudRingDegrees", 24f);
            HudScale = Main.CreateEntry("HudScale", 1.0f);
            HudOnTop = Main.CreateEntry("HudOnTop", true);
            HudShowDistance = Main.CreateEntry("HudShowDistance", true);

            Dev = MelonPreferences.CreateCategory("VisualCues_Dev");
            VerboseLogging = Dev.CreateEntry("VerboseLogging", false);
            LogSounds = Dev.CreateEntry("LogSounds", true, description: "Log every positioned sound with the nearest enemy and distance to UserData/VisualCues/logs/. Turn off once the noise cue is tuned.");
            HotkeysEnabled = Dev.CreateEntry("HotkeysEnabled", true,
                description: "Quote (') = send a call as if a stick was clicked; Period (.) = fake an enemy noise on the nearest enemy; Comma (,) = reload settings.");
        }
    }
}
