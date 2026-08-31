using MelonLoader;

namespace DoEFriendsMod
{
    /// <summary>
    /// MelonPreferences-backed settings, so recon behaviour can be retuned between headset
    /// sessions by editing UserData/MelonPreferences.cfg instead of rebuilding.
    /// </summary>
    public static class ModConfig
    {
        public static MelonPreferences_Category Category;

        public static MelonPreferences_Entry<bool> ReconEnabled;
        public static MelonPreferences_Entry<bool> HotkeysEnabled;
        public static MelonPreferences_Entry<float> AvatarPollSeconds;
        public static MelonPreferences_Entry<int> HierarchyMaxDepth;
        public static MelonPreferences_Entry<float> RedumpDelaySeconds;
        public static MelonPreferences_Entry<int> MaxBlendShapesLogged;
        public static MelonPreferences_Entry<bool> LogPhotonEvents;
        public static MelonPreferences_Entry<bool> MirrorReconToConsole;
        public static MelonPreferences_Entry<string> PreviewAvatarName;

        // Spring-bone tuning. These are honest guesses at a mapping between VRChat's PhysBone
        // model and ours — they are meant to be edited. Change the .cfg and press F3 in game
        // to reload; a spawned avatar retunes immediately, no respawn needed.
        public static MelonPreferences_Entry<bool> SpringsEnabled;
        public static MelonPreferences_Entry<float> SpringStiffnessScale;
        public static MelonPreferences_Entry<float> SpringGravityScale;
        public static MelonPreferences_Entry<float> SpringDragBase;
        public static MelonPreferences_Entry<float> SpringDragFromSpring;
        public static MelonPreferences_Entry<bool> SpringCollidersEnabled;

        /// <summary>Set false to swap the model in WITHOUT VRIK — bisects "is it IK or placement?".</summary>
        public static MelonPreferences_Entry<bool> SwapUseVrik;
        /// <summary>Set false to leave the vanilla mesh visible alongside ours, as a position reference.</summary>
        public static MelonPreferences_Entry<bool> SwapHideVanillaMesh;

        public static void Load()
        {
            Category = MelonPreferences.CreateCategory("DoEFriendsMod");

            ReconEnabled = Category.CreateEntry("ReconEnabled", true);
            HotkeysEnabled = Category.CreateEntry("HotkeysEnabled", true);
            AvatarPollSeconds = Category.CreateEntry("AvatarPollSeconds", 1.0f);
            HierarchyMaxDepth = Category.CreateEntry("HierarchyMaxDepth", 12);
            // The merged character mesh is built a moment after AvatarPlayer exists, and
            // hotkeys don't reach the game window while you're in the headset — so every
            // avatar gets dumped a second time once its model has had time to appear.
            RedumpDelaySeconds = Category.CreateEntry("RedumpDelaySeconds", 10.0f);
            MaxBlendShapesLogged = Category.CreateEntry("MaxBlendShapesLogged", 400);
            LogPhotonEvents = Category.CreateEntry("LogPhotonEvents", true);
            // Hierarchy dumps are thousands of lines; the file is the real output and the
            // console gets a summary unless you explicitly want the firehose.
            MirrorReconToConsole = Category.CreateEntry("MirrorReconToConsole", false);
            // Which avatar F6 spawns. Empty = the first one found in
            // UserData/DoEFriendsMod/Avatars/, which is what you want with only one installed.
            PreviewAvatarName = Category.CreateEntry("PreviewAvatarName", "");

            SpringsEnabled = Category.CreateEntry("SpringsEnabled", true);
            // Restoring force toward the animated pose, in m/s at stiffness = 1.
            SpringStiffnessScale = Category.CreateEntry("SpringStiffnessScale", 2.0f);
            // Downward force in m/s at PhysBone gravity = 1. Raise it if nothing droops.
            // 0.6 is what a real avatar's tail wanted in testing, so start there rather than
            // at the value I originally guessed.
            SpringGravityScale = Category.CreateEntry("SpringGravityScale", 0.6f);
            // Damping at spring = 0. Lower it for a bouncier chain.
            SpringDragBase = Category.CreateEntry("SpringDragBase", 0.55f);
            // How much PhysBone `spring` removes from that damping.
            SpringDragFromSpring = Category.CreateEntry("SpringDragFromSpring", 0.35f);
            // Turn off to find out whether a collider is what's holding a chain out straight.
            SpringCollidersEnabled = Category.CreateEntry("SpringCollidersEnabled", true);
            SwapUseVrik = Category.CreateEntry("SwapUseVrik", true);
            SwapHideVanillaMesh = Category.CreateEntry("SwapHideVanillaMesh", true);
        }
    }
}
