using MelonLoader;

namespace Descent
{
    /// <summary>
    /// Settings, in UserData/MelonPreferences.cfg, under their own sections so they sit next
    /// to the other mods' without mixing. [Descent] for what a player changes, [Descent_Dev]
    /// for diagnostics and the first-session switches.
    /// </summary>
    public static class ModConfig
    {
        public static MelonPreferences_Category Main;
        public static MelonPreferences_Category Dev;

        // ---- [Descent] --------------------------------------------------------------------
        public static MelonPreferences_Entry<bool> Enabled;
        /// <summary>Floors in a run. Four realms, four floors each, by default.</summary>
        public static MelonPreferences_Entry<int> FloorsPerRun;
        /// <summary>Consecutive floors in the same realm before the next band.</summary>
        public static MelonPreferences_Entry<int> FloorsPerBand;
        /// <summary>Enemy tier of floor 1, 1–7 as the scanner shows it (the game's TierOverride is this minus one).</summary>
        public static MelonPreferences_Entry<int> StartTier;
        /// <summary>The tier rises by one every this many floors, capped at 7.</summary>
        public static MelonPreferences_Entry<int> TierRampEvery;
        /// <summary>The last floor carries a boss battle.</summary>
        public static MelonPreferences_Entry<bool> FinalFloorBoss;
        /// <summary>Seconds between pressing Enter on the board and the load. Anyone can cancel.</summary>
        public static MelonPreferences_Entry<float> CountdownSeconds;
        /// <summary>Bank XP, gold, achievements and leaderboards on every descent through the game's own end-of-mission code.</summary>
        public static MelonPreferences_Entry<bool> BankRewardsPerFloor;
        /// <summary>Run the game's layout pass on a floor's seed before launching it, stepping the seed if the builder fails.</summary>
        public static MelonPreferences_Entry<bool> ValidateFloors;
        /// <summary>Board placement in the lobby, world units. Press / (slash) in the lobby to set it where you stand.</summary>
        public static MelonPreferences_Entry<float> BoardX, BoardY, BoardZ, BoardYaw;
        public static MelonPreferences_Entry<bool> BoardPlaced;
        /// <summary>An invisible pointer target over the board so the laser shows across the whole panel.</summary>
        public static MelonPreferences_Entry<bool> PanelLaser;
        /// <summary>Relabel the exit teleporter's pads with the next floor.</summary>
        public static MelonPreferences_Entry<bool> RelabelExit;

        // ---- [Descent_Dev] ----------------------------------------------------------------
        public static MelonPreferences_Entry<bool> HotkeysEnabled;
        /// <summary>"vanilla" = GameManager.LoadDungeon (the game's own launch coroutine); "manual" = the mod's copy of its steps. Falls back to manual if the callback delegate cannot be converted.</summary>
        public static MelonPreferences_Entry<string> LaunchMode;
        /// <summary>Seconds of avatar dissolve passed to the launch coroutine.</summary>
        public static MelonPreferences_Entry<float> DissolveSeconds;
        /// <summary>Delay between the exit firing and the next floor's load, so the success music and lock have their moment.</summary>
        public static MelonPreferences_Entry<float> DescendDelaySeconds;
        /// <summary>Turn on the game's own [DungeonBuilder] logging for every generation while a run is active.</summary>
        public static MelonPreferences_Entry<bool> BuilderLogging;
        /// <summary>Write the recon transcript under UserData/Descent/recon/.</summary>
        public static MelonPreferences_Entry<bool> ReconEnabled;
        public static MelonPreferences_Entry<bool> MirrorReconToConsole;
        /// <summary>Solo test switch: stay on the vanilla path (no descend, no banking); the mod only watches and logs.</summary>
        public static MelonPreferences_Entry<bool> ObserveOnly;
        /// <summary>Copy Unity exceptions and errors, with stack traces, into the recon transcript.</summary>
        public static MelonPreferences_Entry<bool> UnityLogTap;
        /// <summary>Detour the C++ runtime's throw to name the GameAssembly frames of every IL2CPP exception in the transcript.</summary>
        public static MelonPreferences_Entry<bool> NativeThrowTrace;

        public static void Load()
        {
            Main = MelonPreferences.CreateCategory("Descent", "Descent");
            Enabled = Main.CreateEntry("Enabled", true, description: "Master switch. Off means the mod does nothing at all.");
            FloorsPerRun = Main.CreateEntry("FloorsPerRun", 16, description: "Floors in a run.");
            FloorsPerBand = Main.CreateEntry("FloorsPerBand", 4, description: "Consecutive floors in the same realm.");
            StartTier = Main.CreateEntry("StartTier", 1, description: "Enemy tier of floor 1, 1-7.");
            TierRampEvery = Main.CreateEntry("TierRampEvery", 2, description: "Tier +1 every this many floors.");
            FinalFloorBoss = Main.CreateEntry("FinalFloorBoss", true, description: "Boss battle on the last floor.");
            CountdownSeconds = Main.CreateEntry("CountdownSeconds", 10f, description: "Board countdown before the load.");
            BankRewardsPerFloor = Main.CreateEntry("BankRewardsPerFloor", true, description: "XP/gold banked on every descent via the game's own end-of-mission code.");
            ValidateFloors = Main.CreateEntry("ValidateFloors", false, description: "Layout-check a floor's seed in the hub before launching; step the seed on failure. Off: the 2026-09-07 session showed NullReferenceExceptions in the Unity log after the hub pass, cause not yet known.");
            BoardX = Main.CreateEntry("BoardX", 0f);
            BoardY = Main.CreateEntry("BoardY", 0f);
            BoardZ = Main.CreateEntry("BoardZ", 0f);
            BoardYaw = Main.CreateEntry("BoardYaw", 0f);
            BoardPlaced = Main.CreateEntry("BoardPlaced", false, description: "True once the board was placed with /; until then the built-in spot is used.");
            PanelLaser = Main.CreateEntry("PanelLaser", true);
            RelabelExit = Main.CreateEntry("RelabelExit", true, description: "Exit pads read DESCEND with the next floor.");

            Dev = MelonPreferences.CreateCategory("Descent_Dev", "Descent (diagnostics)");
            HotkeysEnabled = Dev.CreateEntry("HotkeysEnabled", true, description: "Backspace / End / Slash hotkeys (game window focused).");
            LaunchMode = Dev.CreateEntry("LaunchMode", "vanilla", description: "vanilla = GameManager.LoadDungeon; manual = the mod's own copy of its steps.");
            DissolveSeconds = Dev.CreateEntry("DissolveSeconds", 2f);
            DescendDelaySeconds = Dev.CreateEntry("DescendDelaySeconds", 3f);
            BuilderLogging = Dev.CreateEntry("BuilderLogging", true);
            ReconEnabled = Dev.CreateEntry("ReconEnabled", true);
            MirrorReconToConsole = Dev.CreateEntry("MirrorReconToConsole", false);
            ObserveOnly = Dev.CreateEntry("ObserveOnly", false, description: "Watch and log only; never redirect the exit or bank rewards.");
            UnityLogTap = Dev.CreateEntry("UnityLogTap", true, description: "Unity exceptions and errors, with stack traces, into the recon transcript.");
            NativeThrowTrace = Dev.CreateEntry("NativeThrowTrace", true, description: "Name the GameAssembly frames of every IL2CPP exception (needs UserData/Descent/methods.tsv).");
        }
    }
}
