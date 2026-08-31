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
        /// <summary>Metres the swapped avatar may drift from your head before it's snapped back.</summary>
        public static MelonPreferences_Entry<float> SwapLeashMetres;
        public static MelonPreferences_Entry<float> SwapLocomotionWeight;

        // Wrist rotation offset between the game's hand IK targets and this avatar's wrists,
        // in degrees. Applied every frame, so edit the .cfg and press F3 to dial it in live.
        public static MelonPreferences_Entry<float> SwapHandOffsetLeftX;
        public static MelonPreferences_Entry<float> SwapHandOffsetLeftY;
        public static MelonPreferences_Entry<float> SwapHandOffsetLeftZ;
        public static MelonPreferences_Entry<float> SwapHandOffsetRightX;
        public static MelonPreferences_Entry<float> SwapHandOffsetRightY;
        public static MelonPreferences_Entry<float> SwapHandOffsetRightZ;

        // First-person head handling, the equivalent of VRChat's Head Chop. Applied only to
        // your own avatar and only locally — it must never change what peers see.
        public static MelonPreferences_Entry<bool> SelfHideHead;
        public static MelonPreferences_Entry<float> SelfHeadBoneScale;
        public static MelonPreferences_Entry<string> SelfHeadShrinkBones;
        public static MelonPreferences_Entry<string> SelfHeadKeepBones;

        // The first-person arms are a SEPARATE model on the SteamVR rig, not part of the
        // character body — hiding the body does nothing to them.
        public static MelonPreferences_Entry<bool> SwapHideFpsArms;
        public static MelonPreferences_Entry<string> SwapFpsArmPrefixes;
        /// <summary>Pin the avatar to the game's own body position every frame. Turn OFF to let
        /// VRIK's procedural locomotion own the root, which is what makes legs step.</summary>
        public static MelonPreferences_Entry<bool> SwapFollowVanillaRoot;

        // Finger curling from the controllers.
        public static MelonPreferences_Entry<bool> HandPosesEnabled;
        public static MelonPreferences_Entry<float> HandCurlDegrees;
        public static MelonPreferences_Entry<float> ThumbCurlDegrees;
        public static MelonPreferences_Entry<float> HandCurlSmoothing;
        public static MelonPreferences_Entry<bool> HandPoseDebug;

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
            SwapLeashMetres = Category.CreateEntry("SwapLeashMetres", 5.0f);
            // 0 = we position the body ourselves from the game's own root (reliable).
            // 1 = let VRIK walk the root around procedurally (what threw it 100 m away).
            SwapLocomotionWeight = Category.CreateEntry("SwapLocomotionWeight", 0f);

            // (-90, 0, 180) on both wrists was measured in-headset against a real VRChat
            // humanoid rig, and VRChat rigs are consistent enough that it's a much better
            // starting point than zero. Still per-avatar — expect to nudge it.
            SwapHandOffsetLeftX = Category.CreateEntry("SwapHandOffsetLeftX", -90f);
            SwapHandOffsetLeftY = Category.CreateEntry("SwapHandOffsetLeftY", 0f);
            SwapHandOffsetLeftZ = Category.CreateEntry("SwapHandOffsetLeftZ", 180f);
            SwapHandOffsetRightX = Category.CreateEntry("SwapHandOffsetRightX", -90f);
            SwapHandOffsetRightY = Category.CreateEntry("SwapHandOffsetRightY", 0f);
            SwapHandOffsetRightZ = Category.CreateEntry("SwapHandOffsetRightZ", 180f);

            SelfHideHead = Category.CreateEntry("SelfHideHead", true);
            // Not exactly zero: a zero-scale bone gives Unity a degenerate matrix to skin
            // through. Small enough to be invisible, large enough to stay well-defined.
            SelfHeadBoneScale = Category.CreateEntry("SelfHeadBoneScale", 0.0001f);
            // Humanoid bone names (from the manifest map) or literal transform names, comma
            // separated. "Head" collapses everything parented under the head bone.
            SelfHeadShrinkBones = Category.CreateEntry("SelfHeadShrinkBones", "Head");
            // Bones to scale back UP afterwards, cancelling their parent's shrink — this is how
            // you keep a snout visible while the rest of the head is gone. Needs the geometry
            // to be weighted to its own bone; leave empty until you know which bone that is.
            SelfHeadKeepBones = Category.CreateEntry("SelfHeadKeepBones", "");

            SwapHideFpsArms = Category.CreateEntry("SwapHideFpsArms", true);
            // Only the arm meshes. The weapon-stat and kill-counter panels are parented into
            // the same rig's forearm bones, and hiding those would take away real UI.
            SwapFpsArmPrefixes = Category.CreateEntry("SwapFpsArmPrefixes", "FPS_Arm");
            SwapFollowVanillaRoot = Category.CreateEntry("SwapFollowVanillaRoot", true);

            HandPosesEnabled = Category.CreateEntry("HandPosesEnabled", true);
            // How far a fully closed finger bends, per joint. Negate if the fingers bend
            // backwards — the bend direction is derived from the avatar's own bone geometry
            // and a rig can have it mirrored.
            HandCurlDegrees = Category.CreateEntry("HandCurlDegrees", 70f);
            ThumbCurlDegrees = Category.CreateEntry("ThumbCurlDegrees", 40f);
            HandCurlSmoothing = Category.CreateEntry("HandCurlSmoothing", 0.35f);
            // Prints the raw grip/trigger/curl values twice a second, so "nothing moves" can be
            // told apart from "the input is zero".
            HandPoseDebug = Category.CreateEntry("HandPoseDebug", false);
        }
    }
}
