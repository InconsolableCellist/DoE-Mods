using MelonLoader;

namespace CustomAvatars
{
    /// <summary>
    /// Settings, in UserData/MelonPreferences.cfg.
    ///
    /// Three sections, because they are three different things and mixing them made the file
    /// hard to read:
    ///
    /// <list type="bullet">
    /// <item><b>[CustomAvatars]</b> — what a normal user changes. Which avatar, ports, on/off.</item>
    /// <item><b>[CustomAvatars_Tuning]</b> — per-avatar and per-rig dialling in. Safe to change,
    /// but the defaults are reasonable and you only come here when something looks wrong on
    /// YOUR avatar.</item>
    /// <item><b>[CustomAvatars_Dev]</b> — diagnostic switches for taking the mod apart. Every one
    /// of these has a correct value already set; changing them is how you find out which half
    /// of a problem is broken, not how you configure the mod. If your avatar is misbehaving in
    /// a way that seems impossible, check here first for something left over from a debugging
    /// session.</item>
    /// </list>
    ///
    /// Every entry carries a description, so the .cfg explains itself in comments rather than
    /// being a wall of names. Edit, save, press F3 in game — most settings apply immediately.
    /// </summary>
    public static class ModConfig
    {
        public static MelonPreferences_Category Main;
        public static MelonPreferences_Category Tuning;
        public static MelonPreferences_Category Dev;

        // ---- [CustomAvatars] ------------------------------------------------------------
        public static MelonPreferences_Entry<string> SelectedAvatar;
        public static MelonPreferences_Entry<bool> AutoWear;
        public static MelonPreferences_Entry<bool> HotkeysEnabled;
        public static MelonPreferences_Entry<bool> HologramSwapEnabled;
        public static MelonPreferences_Entry<bool> HandPosesEnabled;
        public static MelonPreferences_Entry<bool> SpringsEnabled;
        public static MelonPreferences_Entry<bool> FaceOscEnabled;
        public static MelonPreferences_Entry<int> FaceOscListenPort;
        public static MelonPreferences_Entry<int> FaceOscSendPort;
        public static MelonPreferences_Entry<bool> VoiceJawEnabled;

        // ---- [CustomAvatars_Tuning] -----------------------------------------------------
        public static MelonPreferences_Entry<float> SpringStiffnessScale;
        public static MelonPreferences_Entry<float> SpringGravityScale;
        public static MelonPreferences_Entry<float> SpringDragBase;
        public static MelonPreferences_Entry<float> SpringDragFromSpring;
        public static MelonPreferences_Entry<bool> SpringCollidersEnabled;
        public static MelonPreferences_Entry<float> SpringMaxAngleFallback;

        public static MelonPreferences_Entry<float> SwapHandOffsetLeftX;
        public static MelonPreferences_Entry<float> SwapHandOffsetLeftY;
        public static MelonPreferences_Entry<float> SwapHandOffsetLeftZ;
        public static MelonPreferences_Entry<float> SwapHandOffsetRightX;
        public static MelonPreferences_Entry<float> SwapHandOffsetRightY;
        public static MelonPreferences_Entry<float> SwapHandOffsetRightZ;

        public static MelonPreferences_Entry<bool> SelfHideHead;
        public static MelonPreferences_Entry<float> SelfHeadBoneScale;
        public static MelonPreferences_Entry<string> SelfHeadShrinkBones;
        public static MelonPreferences_Entry<string> SelfHeadKeepBones;

        public static MelonPreferences_Entry<float> HandCurlDegrees;
        public static MelonPreferences_Entry<float> ThumbCurlDegrees;
        public static MelonPreferences_Entry<float> HandCurlSmoothing;

        public static MelonPreferences_Entry<float> ArmStretch;
        public static MelonPreferences_Entry<float> ArmShoulderYieldDegrees;
        public static MelonPreferences_Entry<float> ArmTwistShare;

        public static MelonPreferences_Entry<float> FaceSmoothing;
        public static MelonPreferences_Entry<float> FaceShapeScale;
        public static MelonPreferences_Entry<float> FaceEyePitchDegrees;
        public static MelonPreferences_Entry<float> FaceEyeYawDegrees;
        public static MelonPreferences_Entry<float> FaceEyeLidOpenPoint;
        public static MelonPreferences_Entry<float> FaceStaleSeconds;
        public static MelonPreferences_Entry<float> VoiceJawScale;

        public static MelonPreferences_Entry<float> HandSyncHz;
        public static MelonPreferences_Entry<float> FaceSyncHz;
        public static MelonPreferences_Entry<int> FaceSyncMaxShapes;
        public static MelonPreferences_Entry<float> FaceSyncEpsilon;

        // ---- [CustomAvatars_Dev] --------------------------------------------------------
        public static MelonPreferences_Entry<bool> ReconEnabled;
        public static MelonPreferences_Entry<float> AvatarPollSeconds;
        public static MelonPreferences_Entry<int> HierarchyMaxDepth;
        public static MelonPreferences_Entry<float> RedumpDelaySeconds;
        public static MelonPreferences_Entry<int> MaxBlendShapesLogged;
        public static MelonPreferences_Entry<bool> LogPhotonEvents;
        public static MelonPreferences_Entry<bool> MirrorReconToConsole;
        public static MelonPreferences_Entry<bool> HandPoseDebug;

        public static MelonPreferences_Entry<bool> SwapUseVrik;
        public static MelonPreferences_Entry<bool> SwapHideVanillaMesh;
        public static MelonPreferences_Entry<bool> SwapHideFpsArms;
        public static MelonPreferences_Entry<string> SwapFpsArmKeepPrefixes;
        public static MelonPreferences_Entry<bool> SwapFollowVanillaRoot;
        public static MelonPreferences_Entry<float> SwapLeashMetres;
        public static MelonPreferences_Entry<float> SwapLocomotionWeight;
        public static MelonPreferences_Entry<string> SwapPoseSource;
        public static MelonPreferences_Entry<string> SwapArmSource;
        public static MelonPreferences_Entry<string> SwapArmTargetSource;
        public static MelonPreferences_Entry<bool> SwapForceVanillaIK;
        public static MelonPreferences_Entry<float> RetargetHipsFollow;
        public static MelonPreferences_Entry<bool> RetargetAlignAtCapture;
        public static MelonPreferences_Entry<bool> FaceForceRelevant;

        public static void Load()
        {
            Main = MelonPreferences.CreateCategory("CustomAvatars", "Custom Avatars");
            Tuning = MelonPreferences.CreateCategory("CustomAvatars_Tuning", "Custom Avatars — per-avatar tuning");
            Dev = MelonPreferences.CreateCategory("CustomAvatars_Dev", "Custom Avatars — diagnostics (leave these alone)");

            // ---- everyday ---------------------------------------------------------------
            SelectedAvatar = Main.CreateEntry("Avatar", "", description:
                "Which avatar you wear. Empty picks the first one installed. F2 in game cycles " +
                "through them and writes your choice back here.");
            AutoWear = Main.CreateEntry("AutoWear", true, description:
                "Put your avatar on by yourself, as soon as you're in a private lobby with your " +
                "friends. Turn off if you'd rather press F4 each time.");
            HotkeysEnabled = Main.CreateEntry("HotkeysEnabled", true, description:
                "The F-keys. They only work while the desktop game window has focus.");
            HologramSwapEnabled = Main.CreateEntry("HologramSwapEnabled", true, description:
                "Show custom avatars on the equipment-room mannequins as well as on the players.");
            HandPosesEnabled = Main.CreateEntry("HandPosesEnabled", true, description:
                "Curl the avatar's fingers to match your grip and trigger.");
            SpringsEnabled = Main.CreateEntry("SpringsEnabled", true, description:
                "Let tails, ears and hair swing. This is our stand-in for VRChat's PhysBones.");
            FaceOscEnabled = Main.CreateEntry("FaceOscEnabled", true, description:
                "Listen for face tracking from VRCFaceTracking. Costs nothing if it isn't running.");
            FaceOscListenPort = Main.CreateEntry("FaceOscListenPort", 9000, description:
                "VRCFaceTracking's OSCOutPort. 9000 is its default and VRChat's too, so if " +
                "VRChat is running at the same time one of them has to move.");
            FaceOscSendPort = Main.CreateEntry("FaceOscSendPort", 9001, description:
                "VRCFaceTracking's OSCInPort, where we ask it to send everything.");
            VoiceJawEnabled = Main.CreateEntry("VoiceJawEnabled", true, description:
                "Move the mouth from how loudly someone is talking. This is what gives a face to " +
                "friends who have no face tracking hardware, and it costs no network traffic.");

            // ---- tuning -----------------------------------------------------------------
            SpringStiffnessScale = Tuning.CreateEntry("SpringStiffnessScale", 2.0f, description:
                "Pull back toward the resting pose. Raise if a tail flops about; lower if it's stiff.");
            SpringGravityScale = Tuning.CreateEntry("SpringGravityScale", 0.6f, description:
                "Droop, at PhysBone gravity = 1. Raise if nothing hangs down.");
            SpringDragBase = Tuning.CreateEntry("SpringDragBase", 0.55f, description:
                "Damping at PhysBone spring = 0. Lower for a bouncier chain.");
            SpringDragFromSpring = Tuning.CreateEntry("SpringDragFromSpring", 0.35f, description:
                "How much a PhysBone's own `spring` value takes away from that damping.");
            SpringCollidersEnabled = Tuning.CreateEntry("SpringCollidersEnabled", true, description:
                "Keep chains out of the body. Turn off to find out whether a collider is what's " +
                "holding a chain out straight.");
            SpringMaxAngleFallback = Tuning.CreateEntry("SpringMaxAngleFallback", 75f, description:
                "Cone limit for chains whose PhysBone set none. 0 disables it; 75 stops a tail " +
                "folding back through the body without looking stiff.");

            SwapHandOffsetLeftX = Tuning.CreateEntry("SwapHandOffsetLeftX", -90f, description:
                "Wrist rotation offset between the game's hand targets and this avatar's wrists, " +
                "in degrees. (-90, 0, 180) fits most VRChat humanoid rigs; expect to nudge it. " +
                "Applied every frame, so edit and press F3 to dial it in live.");
            SwapHandOffsetLeftY = Tuning.CreateEntry("SwapHandOffsetLeftY", 0f);
            SwapHandOffsetLeftZ = Tuning.CreateEntry("SwapHandOffsetLeftZ", 180f);
            SwapHandOffsetRightX = Tuning.CreateEntry("SwapHandOffsetRightX", -90f);
            SwapHandOffsetRightY = Tuning.CreateEntry("SwapHandOffsetRightY", 0f);
            SwapHandOffsetRightZ = Tuning.CreateEntry("SwapHandOffsetRightZ", 180f);

            SelfHideHead = Tuning.CreateEntry("SelfHideHead", true, description:
                "Get your own head out of your own view, the way VRChat's Head Chop does. Only " +
                "ever applies to your own eyes; peers always see your whole head.");
            SelfHeadBoneScale = Tuning.CreateEntry("SelfHeadBoneScale", 0.0001f, description:
                "How small the hidden head is shrunk. Not exactly zero, because a zero-scale bone " +
                "gives Unity a broken matrix to skin through.");
            SelfHeadShrinkBones = Tuning.CreateEntry("SelfHeadShrinkBones", "Head", description:
                "Which bones get shrunk, comma separated. Humanoid names or literal bone names. " +
                "\"Head\" collapses everything parented under the head bone.");
            SelfHeadKeepBones = Tuning.CreateEntry("SelfHeadKeepBones", "", description:
                "Bones to scale back up afterwards, cancelling their parent's shrink. This is how " +
                "you keep a snout visible while the rest of the head is gone. Needs that geometry " +
                "weighted to its own bone, so leave empty until you know which bone that is.");

            HandCurlDegrees = Tuning.CreateEntry("HandCurlDegrees", 70f, description:
                "How far a fully closed finger bends, per joint. Negate if the fingers bend " +
                "backwards on your rig.");
            ThumbCurlDegrees = Tuning.CreateEntry("ThumbCurlDegrees", 40f, description:
                "The same for thumbs, which bend less than fingers do.");
            HandCurlSmoothing = Tuning.CreateEntry("HandCurlSmoothing", 0.35f, description:
                "How quickly fingers follow the controller. Lower is smoother and laggier.");

            ArmStretch = Tuning.CreateEntry("ArmStretch", 0.08f, description:
                "How far past its natural length an arm may stretch to reach your hand, as a " +
                "fraction. A custom avatar's arms are rarely the same length as the game " +
                "character's, and an arm that can't reach leaves the hand short of the weapon.");
            ArmShoulderYieldDegrees = Tuning.CreateEntry("ArmShoulderYieldDegrees", 25f, description:
                "How far the collarbone may rotate toward a hand that's out of reach, in degrees. " +
                "This is what your own shoulder does when you reach for something far away, and " +
                "it buys real reach without stretching bones. 0 turns it off.");
            ArmTwistShare = Tuning.CreateEntry("ArmTwistShare", 0.5f, description:
                "How much wrist roll is passed back up to the forearm, 0 to 1. Real forearms " +
                "share the twist; putting all of it on the wrist pinches the mesh into a straw " +
                "when you turn your palm up.");

            FaceSmoothing = Tuning.CreateEntry("FaceSmoothing", 0.5f, description:
                "How quickly the face follows tracking. Lower is smoother and laggier.");
            FaceShapeScale = Tuning.CreateEntry("FaceShapeScale", 1.0f, description:
                "Multiplier on every blendshape. Some faces want the whole set toned down.");
            FaceEyePitchDegrees = Tuning.CreateEntry("FaceEyePitchDegrees", 50f, description:
                "How far the eyes rotate up and down at full deflection.");
            FaceEyeYawDegrees = Tuning.CreateEntry("FaceEyeYawDegrees", 60f, description:
                "How far the eyes rotate left and right at full deflection.");
            FaceEyeLidOpenPoint = Tuning.CreateEntry("FaceEyeLidOpenPoint", 0.75f, description:
                "The eyelid value that counts as fully open. VRCFaceTracking's own templates rest " +
                "here rather than at 1.0, so an avatar built against them looks half asleep " +
                "without this.");
            FaceStaleSeconds = Tuning.CreateEntry("FaceStaleSeconds", 3f, description:
                "Relax the face after this long with no tracking, so it doesn't freeze " +
                "mid-expression when VRCFaceTracking closes or the headset sleeps.");
            VoiceJawScale = Tuning.CreateEntry("VoiceJawScale", 1.5f, description:
                "How wide the mouth opens for a given speaking volume.");

            HandSyncHz = Tuning.CreateEntry("HandSyncHz", 12f, description:
                "How often finger positions go out to friends. Ten bytes a message, and only " +
                "when a finger actually moved.");
            FaceSyncHz = Tuning.CreateEntry("FaceSyncHz", 10f, description:
                "How often face tracking goes out to friends. One message per tick, never more, " +
                "whatever rate your tracking runs at. Photon relays everything through the " +
                "game's own servers, so this is the number that decides what we cost them.");
            FaceSyncMaxShapes = Tuning.CreateEntry("FaceSyncMaxShapes", 24, description:
                "Most face shapes in one message. A hard ceiling, so the worst case is bounded " +
                "rather than growing with the avatar.");
            FaceSyncEpsilon = Tuning.CreateEntry("FaceSyncEpsilon", 0.012f, description:
                "How far a shape must move to be worth sending. A still face sends nothing at all.");

            // ---- diagnostics ------------------------------------------------------------
            ReconEnabled = Dev.CreateEntry("ReconEnabled", true, description:
                "Write technical dumps about the game to UserData/CustomAvatars/recon/.");
            AvatarPollSeconds = Dev.CreateEntry("AvatarPollSeconds", 1.0f, description:
                "How often to look for players who have appeared or left.");
            HierarchyMaxDepth = Dev.CreateEntry("HierarchyMaxDepth", 12, description:
                "How deep the object-tree dumps go.");
            RedumpDelaySeconds = Dev.CreateEntry("RedumpDelaySeconds", 10.0f, description:
                "A player's body is finished building a moment after the player exists, so every " +
                "one gets dumped a second time this long after first sight.");
            MaxBlendShapesLogged = Dev.CreateEntry("MaxBlendShapesLogged", 400, description:
                "Cap on blendshape names per mesh in a dump.");
            LogPhotonEvents = Dev.CreateEntry("LogPhotonEvents", true, description:
                "Note unfamiliar Photon events in the log.");
            MirrorReconToConsole = Dev.CreateEntry("MirrorReconToConsole", false, description:
                "Repeat the dump files to the console. They're thousands of lines; the file is " +
                "the real output.");
            HandPoseDebug = Dev.CreateEntry("HandPoseDebug", false, description:
                "Log raw finger-curl values every frame.");

            SwapUseVrik = Dev.CreateEntry("SwapUseVrik", true, description:
                "Set false to swap the model in with no IK at all, which answers \"is it the " +
                "solver or the placement?\". A false here is why an avatar T-poses.");
            SwapHideVanillaMesh = Dev.CreateEntry("SwapHideVanillaMesh", true, description:
                "Hide the game character underneath. Set false to leave it visible next to the " +
                "avatar as a position reference. A false here is why your old body shows through.");
            SwapHideFpsArms = Dev.CreateEntry("SwapHideFpsArms", true, description:
                "Hide the game's first-person arms, which are a separate model on the headset " +
                "rig — hiding the body does nothing to them. Set false to compare the two.");
            SwapFpsArmKeepPrefixes = Dev.CreateEntry("SwapFpsArmKeepPrefixes",
                "ui_counter,_StatsPanel,TMP,Holster", description:
                "Everything under the first-person arm rig is hidden except paths matching one of " +
                "these. The weapon-stat and kill-counter panels hang off those forearm bones and " +
                "are real UI, not arms.");
            SwapFollowVanillaRoot = Dev.CreateEntry("SwapFollowVanillaRoot", true, description:
                "Pin the avatar to the game's own body position every frame. Turning this off " +
                "hands the root to procedural locomotion, which is what threw an avatar 100 m.");
            SwapLeashMetres = Dev.CreateEntry("SwapLeashMetres", 5.0f, description:
                "How far the avatar may drift from your head before it's snapped back.");
            SwapLocomotionWeight = Dev.CreateEntry("SwapLocomotionWeight", 0f, description:
                "0 = we place the body ourselves from the game's own root, which is reliable. " +
                "1 = let the solver walk the root around, which is what makes legs step and also " +
                "what ran away.");
            SwapPoseSource = Dev.CreateEntry("SwapPoseSource", "VanillaRig", description:
                "\"VanillaRig\" copies the pose the game already solved, legs included. \"VRIK\" " +
                "solves our own instead. Copying wins: no targets to get wrong and nothing to " +
                "run away.");
            SwapArmSource = Dev.CreateEntry("SwapArmSource", "IKTargets", description:
                "\"IKTargets\" solves the arms to your controllers; \"Retarget\" copies the " +
                "vanilla arms. The game disables the IK on your own third-person body, so its " +
                "arms are pure walking animation and copying them can't work for you. Remote " +
                "players are solved normally and keep the copied pose.");
            SwapArmTargetSource = Dev.CreateEntry("SwapArmTargetSource", "IKTargets", description:
                "\"IKTargets\" follows the game's smoothed hand targets; \"Controllers\" follows " +
                "your controllers with no lag but a different orientation, so the wrist offsets " +
                "above need retuning if you switch.");
            SwapForceVanillaIK = Dev.CreateEntry("SwapForceVanillaIK", false, description:
                "Re-enable the IK the game deliberately turned off on your own body. It doesn't " +
                "produce usable arms, so there's no reason to interfere.");
            RetargetHipsFollow = Dev.CreateEntry("RetargetHipsFollow", 1.0f, description:
                "How closely the avatar's hips follow the game character's.");
            RetargetAlignAtCapture = Dev.CreateEntry("RetargetAlignAtCapture", true, description:
                "Line each avatar bone up with the game's rig at the moment of the swap. Without " +
                "it, an avatar whose rest pose isn't a T-pose ends up in one.");
            FaceForceRelevant = Dev.CreateEntry("FaceForceRelevant", true, description:
                "Ask VRCFaceTracking to send every parameter. Without it, it only sends what a " +
                "VRChat avatar asked for, and there is no VRChat avatar here.");
        }
    }
}
