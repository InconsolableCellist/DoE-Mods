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
        public static MelonPreferences_Entry<float> SpringMaxAngleFallback;

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
        public static MelonPreferences_Entry<string> SwapFpsArmKeepPrefixes;
        /// <summary>Pin the avatar to the game's own body position every frame. Turn OFF to let
        /// VRIK's procedural locomotion own the root, which is what makes legs step.</summary>
        public static MelonPreferences_Entry<bool> SwapFollowVanillaRoot;

        // Finger curling from the controllers.
        public static MelonPreferences_Entry<bool> HandPosesEnabled;
        public static MelonPreferences_Entry<float> HandCurlDegrees;
        public static MelonPreferences_Entry<float> ThumbCurlDegrees;
        public static MelonPreferences_Entry<float> HandCurlSmoothing;
        public static MelonPreferences_Entry<bool> HandPoseDebug;

        /// <summary>"VanillaRig" copies the game's own animated pose; "VRIK" solves our own.</summary>
        public static MelonPreferences_Entry<string> SwapPoseSource;
        public static MelonPreferences_Entry<float> RetargetHipsFollow;
        public static MelonPreferences_Entry<bool> RetargetAlignAtCapture;
        /// <summary>How much wrist roll is passed back to the forearm, 0..1.</summary>
        public static MelonPreferences_Entry<float> ArmTwistShare;
        /// <summary>How far past its natural length an arm may stretch to reach the hand target.</summary>
        public static MelonPreferences_Entry<float> ArmStretch;
        /// <summary>"IKTargets" (the game's smoothed targets) or "Controllers" (no lag).</summary>
        public static MelonPreferences_Entry<string> SwapArmTargetSource;
        public static MelonPreferences_Entry<float> HandSyncHz;
        public static MelonPreferences_Entry<bool> HologramSwapEnabled;

        // Face tracking over OSC from VRCFaceTracking.
        public static MelonPreferences_Entry<bool> FaceOscEnabled;
        public static MelonPreferences_Entry<int> FaceOscListenPort;
        public static MelonPreferences_Entry<int> FaceOscSendPort;
        public static MelonPreferences_Entry<bool> FaceForceRelevant;
        public static MelonPreferences_Entry<bool> FaceOscDebug;
        public static MelonPreferences_Entry<float> FaceSmoothing;
        public static MelonPreferences_Entry<float> FaceShapeScale;
        public static MelonPreferences_Entry<float> FaceEyePitchDegrees;
        public static MelonPreferences_Entry<float> FaceEyeYawDegrees;
        public static MelonPreferences_Entry<float> FaceStaleSeconds;
        public static MelonPreferences_Entry<bool> SwapForceVanillaIK;
        /// <summary>"Retarget" copies the vanilla arms; "IKTargets" solves them to your controllers.</summary>
        public static MelonPreferences_Entry<string> SwapArmSource;

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
            // Cone limit for chains whose PhysBone set none. 0 disables; 75 stops a tail
            // folding back through the body without looking stiff.
            SpringMaxAngleFallback = Category.CreateEntry("SpringMaxAngleFallback", 75f);
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
            // Everything under the first-person arms rig is hidden EXCEPT paths matching one
            // of these. Keeping the UI: the weapon-stat and kill-counter panels are parented
            // into the same rig's forearm bones and are real UI, not part of the arms.
            SwapFpsArmKeepPrefixes = Category.CreateEntry("SwapFpsArmKeepPrefixes",
                "ui_counter,_StatsPanel,TMP,Holster");
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

            // The game already solves a correct pose for every player, including legs. Copying
            // it beats re-deriving it: no targets to get wrong, nothing to run away, and remote
            // players animate properly. Set to "VRIK" to go back to solving our own.
            SwapPoseSource = Category.CreateEntry("SwapPoseSource", "VanillaRig");
            RetargetHipsFollow = Category.CreateEntry("RetargetHipsFollow", 1.0f);
            RetargetAlignAtCapture = Category.CreateEntry("RetargetAlignAtCapture", true);
            // Real forearms share pronation between elbow and wrist. Putting all of it on the
            // wrist pinches the mesh into a straw when you turn your palm up.
            ArmTwistShare = Category.CreateEntry("ArmTwistShare", 0.5f);
            // A custom avatar's arms are rarely the game character's length, and a shorter arm
            // can't reach the hand target, so the hand stops short of the weapon.
            ArmStretch = Category.CreateEntry("ArmStretch", 0.08f);
            // Left on the game's IK targets by default so existing wrist offsets keep working.
            // "Controllers" removes the lag that slides a held weapon out of your hand while
            // you move with the stick, but the two have different orientations, so the wrist
            // offsets will need retuning if you switch.
            SwapArmTargetSource = Category.CreateEntry("SwapArmTargetSource", "IKTargets");
            // Ten bytes per tick, sent only when a finger actually moved.
            HandSyncHz = Category.CreateEntry("HandSyncHz", 12f);
            // Show custom avatars on the equipment-room mannequins as well as on the players.
            HologramSwapEnabled = Category.CreateEntry("HologramSwapEnabled", true);

            FaceOscEnabled = Category.CreateEntry("FaceOscEnabled", true);
            // VRCFaceTracking's OSCOutPort. 9000 is its default and VRChat's too, so if VRChat
            // is running at the same time one of them has to move.
            FaceOscListenPort = Category.CreateEntry("FaceOscListenPort", 9000);
            // VRCFaceTracking's OSCInPort, where we ask it to send everything.
            FaceOscSendPort = Category.CreateEntry("FaceOscSendPort", 9001);
            FaceForceRelevant = Category.CreateEntry("FaceForceRelevant", true);
            FaceOscDebug = Category.CreateEntry("FaceOscDebug", false);
            FaceSmoothing = Category.CreateEntry("FaceSmoothing", 0.5f);
            // Multiplier on every blendshape. Some faces want the whole set toned down.
            FaceShapeScale = Category.CreateEntry("FaceShapeScale", 1.0f);
            FaceEyePitchDegrees = Category.CreateEntry("FaceEyePitchDegrees", 20f);
            FaceEyeYawDegrees = Category.CreateEntry("FaceEyeYawDegrees", 25f);
            // Relax the face after this long with no OSC, so it doesn't freeze mid-expression
            // when VRCFaceTracking closes or the headset goes to sleep.
            FaceStaleSeconds = Category.CreateEntry("FaceStaleSeconds", 3f);
            // Only meaningful when the arms are copied from the vanilla body. Off by default:
            // the game disables that body's IK deliberately, and re-enabling it didn't produce
            // usable arms anyway, so there's no reason to interfere with it.
            SwapForceVanillaIK = Category.CreateEntry("SwapForceVanillaIK", false);
            // Measured 2026-08-31: the game DISABLES the VRIK component on your own
            // third-person body (`VRIK.enabled=False`), so its arms are pure locomotion
            // animation and never solved to your controllers. Copying them can't work, whatever
            // we force. Solving the arms from the hand targets is the only correct option
            // locally, so it's the default. Remote bodies are solved normally and keep the
            // copied pose.
            SwapArmSource = Category.CreateEntry("SwapArmSource", "IKTargets");
        }
    }
}
