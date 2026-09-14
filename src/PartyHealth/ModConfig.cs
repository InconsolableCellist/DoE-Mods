using MelonLoader;

namespace PartyHealth
{
    /// <summary>
    /// Settings in UserData/MelonPreferences.cfg: [PartyHealth] for what a player changes,
    /// [PartyHealth_Dev] for diagnostics. Everything is read live every frame, so editing the
    /// file and pressing the reload key applies without a restart.
    /// </summary>
    public static class ModConfig
    {
        public static MelonPreferences_Category Main;
        public static MelonPreferences_Category Dev;

        // ---- [PartyHealth] ---------------------------------------------------------------
        public static MelonPreferences_Entry<bool> Enabled;

        /// <summary>Keep the bar up while a friend is at full health. Off: it appears on the first hit and goes away once they are back to full.</summary>
        public static MelonPreferences_Entry<bool> ShowWhenFull;
        /// <summary>After a friend is back to full health, the bar stays this many seconds before it fades.</summary>
        public static MelonPreferences_Entry<float> HoldAfterFullSeconds;
        /// <summary>Friends farther than this (metres) get no bar. The bar fades over the last fifth of the distance.</summary>
        public static MelonPreferences_Entry<float> MaxDistanceMeters;

        /// <summary>Bar width in metres, at arm's length.</summary>
        public static MelonPreferences_Entry<float> WidthMeters;
        /// <summary>Bar height in metres, at arm's length.</summary>
        public static MelonPreferences_Entry<float> HeightMeters;
        /// <summary>How far above the head the bar floats, metres.</summary>
        public static MelonPreferences_Entry<float> AboveHeadMeters;
        /// <summary>Beyond SizeFromMeters the bar grows with distance so it stays the same size on screen instead of shrinking to a sliver.</summary>
        public static MelonPreferences_Entry<bool> SizeWithDistance;
        /// <summary>Metres. Closer than this the bar is its real size; farther it grows with distance (SizeWithDistance).</summary>
        public static MelonPreferences_Entry<float> SizeFromMeters;
        /// <summary>How much of the shrinking with distance is undone. 1 = same size on screen at any range (too big), 0 = a real object; 0.5 = halfway.</summary>
        public static MelonPreferences_Entry<float> DistanceGrowth;
        /// <summary>The bar never grows past this many times its real size.</summary>
        public static MelonPreferences_Entry<float> MaxGrowth;
        /// <summary>0 to 1. How solid the bar is.</summary>
        public static MelonPreferences_Entry<float> Opacity;
        /// <summary>Seconds a bar takes to fade in or out.</summary>
        public static MelonPreferences_Entry<float> FadeSeconds;

        /// <summary>Write the friend's name above the bar.</summary>
        public static MelonPreferences_Entry<bool> ShowName;
        /// <summary>Write the health as a percentage beside the bar.</summary>
        public static MelonPreferences_Entry<bool> ShowPercent;
        /// <summary>Say DOWN, pulsing, when a friend is waiting for a rescue.</summary>
        public static MelonPreferences_Entry<bool> ShowDownedLabel;
        /// <summary>Draw the bar through walls and enemies (needs a shader with a depth-test switch; the log says whether it got one).</summary>
        public static MelonPreferences_Entry<bool> OnTop;

        // ---- [PartyHealth_Dev] -----------------------------------------------------------
        public static MelonPreferences_Entry<bool> VerboseLogging;
        /// <summary>Write every health change the mod sees, and where it came from, to the session log.</summary>
        public static MelonPreferences_Entry<bool> LogHealthChanges;
        public static MelonPreferences_Entry<bool> HotkeysEnabled;

        public static void Load()
        {
            Main = MelonPreferences.CreateCategory("PartyHealth");
            Enabled = Main.CreateEntry("Enabled", true);

            ShowWhenFull = Main.CreateEntry("ShowWhenFull", false, description: "Keep the bar up at full health. Off: it appears on the first hit and goes away once they are back to full.");
            HoldAfterFullSeconds = Main.CreateEntry("HoldAfterFullSeconds", 2.5f, description: "Seconds the bar stays after a friend is back to full health.");
            MaxDistanceMeters = Main.CreateEntry("MaxDistanceMeters", 40f, description: "No bar for friends farther than this. Fades over the last fifth.");

            WidthMeters = Main.CreateEntry("WidthMeters", 0.24f);
            HeightMeters = Main.CreateEntry("HeightMeters", 0.022f);
            AboveHeadMeters = Main.CreateEntry("AboveHeadMeters", 0.28f, description: "How far above the head the bar floats.");
            SizeWithDistance = Main.CreateEntry("SizeWithDistance", true, description: "Grow the bar with distance beyond SizeFromMeters so it stays readable across a room.");
            SizeFromMeters = Main.CreateEntry("SizeFromMeters", 3f);
            DistanceGrowth = Main.CreateEntry("DistanceGrowth", 0.5f, description: "0 to 1. How much of the shrinking with distance is undone: 1 keeps the bar the same size on screen at any range, 0 leaves it a real-sized object, 0.5 is halfway.");
            MaxGrowth = Main.CreateEntry("MaxGrowth", 3f, description: "The bar never grows past this many times its real size.");
            Opacity = Main.CreateEntry("Opacity", 0.8f, description: "0 to 1.");
            FadeSeconds = Main.CreateEntry("FadeSeconds", 0.35f);

            ShowName = Main.CreateEntry("ShowName", false, description: "Write the friend's name above the bar.");
            ShowPercent = Main.CreateEntry("ShowPercent", false, description: "Write the health percentage beside the bar.");
            ShowDownedLabel = Main.CreateEntry("ShowDownedLabel", true, description: "Say DOWN, pulsing, when a friend is waiting for a rescue.");
            OnTop = Main.CreateEntry("OnTop", true, description: "Draw the bar through walls and enemies.");

            Dev = MelonPreferences.CreateCategory("PartyHealth_Dev");
            VerboseLogging = Dev.CreateEntry("VerboseLogging", false, description: "Echo the session log to the console.");
            LogHealthChanges = Dev.CreateEntry("LogHealthChanges", true, description: "Log every health change the mod sees, with its source, to UserData/PartyHealth/logs/.");
            HotkeysEnabled = Dev.CreateEntry("HotkeysEnabled", true,
                description: "H = show or hide all bars; J = a demo bar in front of you that drains, goes down and heals, to check the look alone; K = reload settings.");
        }
    }
}
