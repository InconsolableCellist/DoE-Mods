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
    /// A setting gets a comment in the .cfg only when its name doesn't already say what it is.
    /// Anything that is only interesting to whoever edits THIS file — why a default is what it
    /// is, what went wrong the last time it was changed — is a C# comment below, not a
    /// description, because a settings file nobody can skim is a settings file nobody reads.
    ///
    /// Edit, save, press F3 in game — most settings apply immediately.
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
        public static MelonPreferences_Entry<bool> FbtEnabled;
        public static MelonPreferences_Entry<bool> TrackerSyncEnabled;
        public static MelonPreferences_Entry<bool> HeightScalingEnabled;
        public static MelonPreferences_Entry<bool> HeightFromAvatar;
        public static MelonPreferences_Entry<float> HeightScale;

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

        public static MelonPreferences_Entry<bool> FbtAudioCues;
        public static MelonPreferences_Entry<float> FbtTriggerHoldSeconds;
        public static MelonPreferences_Entry<float> FbtTposeHoldSeconds;
        public static MelonPreferences_Entry<float> FbtPelvisRotationWeight;
        public static MelonPreferences_Entry<float> FbtFootRotationWeight;
        public static MelonPreferences_Entry<string> FbtCalibration;
        public static MelonPreferences_Entry<float> TrackerSyncHz;
        public static MelonPreferences_Entry<float> TrackerPosEpsilonMm;
        public static MelonPreferences_Entry<float> TrackerRotEpsilonDegrees;
        public static MelonPreferences_Entry<float> FbtRemoteSmoothing;
        public static MelonPreferences_Entry<float> FbtStaleSeconds;

        public static MelonPreferences_Entry<float> HeightEyeHeightOverride;
        public static MelonPreferences_Entry<float> HeightMinScale;
        public static MelonPreferences_Entry<float> HeightMaxScale;
        public static MelonPreferences_Entry<float> HeightMoveSpeedBlend;
        public static MelonPreferences_Entry<bool> HeightScaleNearClip;
        public static MelonPreferences_Entry<bool> HeightRestorePropScale;

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

        public static MelonPreferences_Entry<bool> FbtDebug;
        public static MelonPreferences_Entry<bool> FbtDisableGrounder;
        public static MelonPreferences_Entry<bool> FbtAnchorHips;

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
            // Empty picks the first one installed. F2 cycles and writes the choice back here.
            SelectedAvatar = Main.CreateEntry("Avatar", "");
            AutoWear = Main.CreateEntry("AutoWear", true);
            HotkeysEnabled = Main.CreateEntry("HotkeysEnabled", true, description:
                "The F-keys. They only work while the desktop game window has focus.");
            HologramSwapEnabled = Main.CreateEntry("HologramSwapEnabled", true);
            HandPosesEnabled = Main.CreateEntry("HandPosesEnabled", true);
            SpringsEnabled = Main.CreateEntry("SpringsEnabled", true);
            FaceOscEnabled = Main.CreateEntry("FaceOscEnabled", true);
            // VRCFaceTracking's OSCOutPort. 9000 is its default and VRChat's too, so if VRChat
            // is running at the same time one of them has to move.
            FaceOscListenPort = Main.CreateEntry("FaceOscListenPort", 9000);
            // VRCFaceTracking's OSCInPort, where we ask it to send everything.
            FaceOscSendPort = Main.CreateEntry("FaceOscSendPort", 9001);
            // What gives a face to friends with no tracking hardware, and it costs no traffic.
            VoiceJawEnabled = Main.CreateEntry("VoiceJawEnabled", true, description:
                "Mouth movement based on mic volume");
            // Remembered across sessions so trackers come back on at launch once calibrated.
            // F10 flips it; the trackers themselves are read straight from SteamVR.
            FbtEnabled = Main.CreateEntry("FbtEnabled", false, description:
                "Full-body tracking from SteamVR trackers (hip + feet). F10 toggles.");
            TrackerSyncEnabled = Main.CreateEntry("TrackerSyncEnabled", true, description:
                "Stream your tracker poses to modded peers so they see your legs");

            // Off by default because it changes how the game plays, not how it looks: your
            // hitbox, your reach and your weapons all come with you. PageUp/PageDown turn it
            // on and trim it live.
            HeightScalingEnabled = Main.CreateEntry("HeightScalingEnabled", false, description:
                "Be the size of your avatar. Small avatars are small players — smaller hitbox, " +
                "shorter reach, bigger world. PageUp/PageDown adjust, F12 back to vanilla.");
            // The thing people actually want: wear a 1.2 m character, be 1.2 m tall.
            HeightFromAvatar = Main.CreateEntry("HeightFromAvatar", true, description:
                "Take the height from the avatar you're wearing rather than from HeightScale alone");
            HeightScale = Main.CreateEntry("HeightScale", 1.0f, description:
                "Multiplier on top of that. 0.5 is half your real height; 1 leaves it alone.");

            // ---- tuning -----------------------------------------------------------------
            // Restoring force toward the resting pose.
            SpringStiffnessScale = Tuning.CreateEntry("SpringStiffnessScale", 2.0f);
            // Droop at PhysBone gravity = 1. 0.6 is what a real avatar's tail wanted in testing.
            SpringGravityScale = Tuning.CreateEntry("SpringGravityScale", 0.6f);
            // Damping at PhysBone spring = 0. Lower for a bouncier chain.
            SpringDragBase = Tuning.CreateEntry("SpringDragBase", 0.55f);
            SpringDragFromSpring = Tuning.CreateEntry("SpringDragFromSpring", 0.35f);
            // Turn off to find out whether a collider is holding a chain out straight.
            SpringCollidersEnabled = Tuning.CreateEntry("SpringCollidersEnabled", true);
            // Cone limit for chains whose PhysBone set none. 0 disables; 75 stops a tail folding
            // back through the body without looking stiff.
            SpringMaxAngleFallback = Tuning.CreateEntry("SpringMaxAngleFallback", 75f);

            // (-90, 0, 180) was measured in-headset against a real VRChat humanoid rig, and those
            // are consistent enough to be a much better starting point than zero. Applied every
            // frame, so edit and press F3 to dial it in live.
            SwapHandOffsetLeftX = Tuning.CreateEntry("SwapHandOffsetLeftX", -90f, description:
                "Wrist rotation offsets");
            SwapHandOffsetLeftY = Tuning.CreateEntry("SwapHandOffsetLeftY", 0f);
            SwapHandOffsetLeftZ = Tuning.CreateEntry("SwapHandOffsetLeftZ", 180f);
            SwapHandOffsetRightX = Tuning.CreateEntry("SwapHandOffsetRightX", -90f);
            SwapHandOffsetRightY = Tuning.CreateEntry("SwapHandOffsetRightY", 0f);
            SwapHandOffsetRightZ = Tuning.CreateEntry("SwapHandOffsetRightZ", 180f);

            // Only ever applies to your own eyes; peers always see your whole head.
            SelfHideHead = Tuning.CreateEntry("SelfHideHead", true);
            // Not exactly zero: a zero-scale bone gives Unity a degenerate matrix to skin through.
            SelfHeadBoneScale = Tuning.CreateEntry("SelfHeadBoneScale", 0.0001f);
            // Humanoid bone names or literal transform names, comma separated.
            SelfHeadShrinkBones = Tuning.CreateEntry("SelfHeadShrinkBones", "Head");
            // Scaled back up to cancel the parent's shrink. Needs that geometry weighted to its
            // own bone, so leave empty until you know which bone that is.
            SelfHeadKeepBones = Tuning.CreateEntry("SelfHeadKeepBones", "", description:
                "Keep bones (like snout) to see your own muzzle");

            // Negate if the fingers bend backwards — the direction comes from the avatar's own
            // bone geometry and a rig can have it mirrored.
            HandCurlDegrees = Tuning.CreateEntry("HandCurlDegrees", 70f);
            ThumbCurlDegrees = Tuning.CreateEntry("ThumbCurlDegrees", 40f);
            HandCurlSmoothing = Tuning.CreateEntry("HandCurlSmoothing", 0.35f, description:
                "How quickly fingers follow the controller. Lower is smoother and laggier.");

            // A custom avatar's arms are rarely the game character's length, and an arm that
            // can't reach leaves the hand short of the weapon.
            ArmStretch = Tuning.CreateEntry("ArmStretch", 0.08f);
            // 0 turns it off.
            ArmShoulderYieldDegrees = Tuning.CreateEntry("ArmShoulderYieldDegrees", 25f, description:
                "How far the collarbone may rotate toward a hand that's out of reach, in degrees");
            // Real forearms share pronation between elbow and wrist. All of it on the wrist
            // pinches the mesh into a straw when you turn your palm up.
            ArmTwistShare = Tuning.CreateEntry("ArmTwistShare", 0.5f, description:
                "How much wrist roll is passed back up to the forearm, 0 to 1");

            FaceSmoothing = Tuning.CreateEntry("FaceSmoothing", 0.5f);
            // Some faces want the whole set toned down.
            FaceShapeScale = Tuning.CreateEntry("FaceShapeScale", 1.0f);
            // Measured in the headset against a real avatar rather than guessed: 20/25 was
            // visibly under-driven.
            FaceEyePitchDegrees = Tuning.CreateEntry("FaceEyePitchDegrees", 50f);
            FaceEyeYawDegrees = Tuning.CreateEntry("FaceEyeYawDegrees", 60f);
            // The eyelid value that counts as fully open. VRCFaceTracking's templates rest here
            // rather than at 1.0, so an avatar built against them looks half asleep without it.
            FaceEyeLidOpenPoint = Tuning.CreateEntry("FaceEyeLidOpenPoint", 0.75f);
            // Relax the face after this long with no tracking, so it doesn't freeze
            // mid-expression when VRCFaceTracking closes or the headset sleeps.
            FaceStaleSeconds = Tuning.CreateEntry("FaceStaleSeconds", 3f);
            VoiceJawScale = Tuning.CreateEntry("VoiceJawScale", 1.5f);

            // You're in a headset during calibration; sound is the only feedback that reaches you.
            FbtAudioCues = Tuning.CreateEntry("FbtAudioCues", true, description:
                "Beeps for FBT arming, locking, and refusing");
            // How long both triggers must stay squeezed to lock calibration in. Long enough
            // that gripping a weapon two-handed doesn't do it by accident.
            FbtTriggerHoldSeconds = Tuning.CreateEntry("FbtTriggerHoldSeconds", 1.0f);
            // How long the T-pose must hold before the pucks appear and calibration arms.
            FbtTposeHoldSeconds = Tuning.CreateEntry("FbtTposeHoldSeconds", 1.5f);
            // 1 = hips follow the tracker's tilt too (lean, sitting); lower if a hip puck on a
            // loose waistband makes the pelvis twitch.
            FbtPelvisRotationWeight = Tuning.CreateEntry("FbtPelvisRotationWeight", 1.0f);
            FbtFootRotationWeight = Tuning.CreateEntry("FbtFootRotationWeight", 1.0f);
            // Written by calibration: serial|role|pos|rot per tracker. Delete to force a
            // fresh calibration; never edit by hand.
            FbtCalibration = Tuning.CreateEntry("FbtCalibration", "");
            // Three poses ≈ 32 bytes a message. Same Photon-relay economics as FaceSyncHz.
            TrackerSyncHz = Tuning.CreateEntry("TrackerSyncHz", 15f);
            // Standing truly still sends nothing at all.
            TrackerPosEpsilonMm = Tuning.CreateEntry("TrackerPosEpsilonMm", 5f);
            TrackerRotEpsilonDegrees = Tuning.CreateEntry("TrackerRotEpsilonDegrees", 1.5f);
            FbtRemoteSmoothing = Tuning.CreateEntry("FbtRemoteSmoothing", 0.35f, description:
                "How quickly a peer's legs follow their stream. Lower is smoother and laggier.");
            // After this long without tracker data a peer's legs go back to the game's own
            // walking animation rather than freezing mid-stride.
            FbtStaleSeconds = Tuning.CreateEntry("FbtStaleSeconds", 1.0f);

            // Measured automatically from where your headset actually is, taking the tallest
            // plausible reading of the session. Set it if you play seated, or if you want to be
            // sized against a height you didn't happen to be standing at.
            HeightEyeHeightOverride = Tuning.CreateEntry("HeightEyeHeightOverride", 0f, description:
                "Your own eye height in metres. 0 measures it.");
            // Below a quarter size the dungeon stops being playable — you can't reach chests or
            // climb anything — and above three you don't fit through doors.
            HeightMinScale = Tuning.CreateEntry("HeightMinScale", 0.25f);
            HeightMaxScale = Tuning.CreateEntry("HeightMaxScale", 3f);
            // 0 keeps the game's own metres per second, so everyone crosses a room together.
            // 1 makes movement feel right for your size and leaves you behind the party.
            HeightMoveSpeedBlend = Tuning.CreateEntry("HeightMoveSpeedBlend", 0f, description:
                "How much stick movement and jumping shrink with you, 0 to 1");
            // Off only to prove that a clipping problem is something else.
            HeightScaleNearClip = Tuning.CreateEntry("HeightScaleNearClip", true);
            // Puts a weapon's own scale back when it leaves your hands, so a small player
            // doesn't leave small axes lying around a full-size dungeon.
            HeightRestorePropScale = Tuning.CreateEntry("HeightRestorePropScale", true);

            // Ten bytes a message, and only when a finger actually moved.
            HandSyncHz = Tuning.CreateEntry("HandSyncHz", 12f);
            // One message per tick, never more, whatever rate tracking runs at. Photon relays
            // everything through the game's own servers, so this decides what we cost them.
            FaceSyncHz = Tuning.CreateEntry("FaceSyncHz", 10f);
            // A hard ceiling, so the worst case is bounded rather than growing with the avatar.
            FaceSyncMaxShapes = Tuning.CreateEntry("FaceSyncMaxShapes", 24);
            // A still face sends nothing at all.
            FaceSyncEpsilon = Tuning.CreateEntry("FaceSyncEpsilon", 0.012f, description:
                "How far a shape must move to be worth sending");

            // ---- diagnostics ------------------------------------------------------------
            ReconEnabled = Dev.CreateEntry("ReconEnabled", true, description:
                "Write technical dumps about the game to UserData/CustomAvatars/recon/.");
            AvatarPollSeconds = Dev.CreateEntry("AvatarPollSeconds", 1.0f);
            HierarchyMaxDepth = Dev.CreateEntry("HierarchyMaxDepth", 12);
            // A player's body finishes building a moment after the player exists, so every one
            // gets dumped a second time this long after first sight.
            RedumpDelaySeconds = Dev.CreateEntry("RedumpDelaySeconds", 10.0f);
            MaxBlendShapesLogged = Dev.CreateEntry("MaxBlendShapesLogged", 400);
            // Dumps are thousands of lines; the file is the real output.
            MirrorReconToConsole = Dev.CreateEntry("MirrorReconToConsole", false);
            HandPoseDebug = Dev.CreateEntry("HandPoseDebug", false, description:
                "Log raw finger-curl values every frame.");
            LogPhotonEvents = Dev.CreateEntry("LogPhotonEvents", true);

            FbtDebug = Dev.CreateEntry("FbtDebug", false, description:
                "Log tracker poses and VRIK weights every frame while FBT is on.");
            // The grounder plants feet on the floor procedurally; real foot trackers and a
            // foot-planter fighting over the same feet is visible as toe jitter.
            FbtDisableGrounder = Dev.CreateEntry("FbtDisableGrounder", true);
            // Escape hatch for risk #2 in the FBT plan: anchor the self avatar to the solved
            // hips (the ragdoll branch) instead of the head, if head-anchoring fights hip drive.
            FbtAnchorHips = Dev.CreateEntry("FbtAnchorHips", false);

            // Answers "is it the solver or the placement?". A false here is why an avatar T-poses.
            SwapUseVrik = Dev.CreateEntry("SwapUseVrik", true, description:
                "Set false to swap the model in with no IK at all");
            // A false here is why your old body shows through the new one.
            SwapHideVanillaMesh = Dev.CreateEntry("SwapHideVanillaMesh", true);
            // The game's first-person arms are a separate model on the headset rig — hiding the
            // body does nothing to them.
            SwapHideFpsArms = Dev.CreateEntry("SwapHideFpsArms", true);
            SwapFpsArmKeepPrefixes = Dev.CreateEntry("SwapFpsArmKeepPrefixes",
                "ui_counter,_StatsPanel,TMP,Holster", description:
                "Everything under the first-person arm rig is hidden except paths matching one of " +
                "these. The weapon-stat and kill-counter panels hang off those forearm bones and " +
                "are real UI, not arms.");
            // Turning this off hands the root to procedural locomotion, which threw an avatar 100 m.
            SwapFollowVanillaRoot = Dev.CreateEntry("SwapFollowVanillaRoot", true);
            // How far the avatar may drift from your head before it's snapped back.
            SwapLeashMetres = Dev.CreateEntry("SwapLeashMetres", 5.0f);
            // 0 = we place the body ourselves from the game's own root, which is reliable.
            // 1 = let the solver walk the root around, which makes legs step and also ran away.
            SwapLocomotionWeight = Dev.CreateEntry("SwapLocomotionWeight", 0f);
            // Copying the pose the game already solved wins: no targets to get wrong, nothing to
            // run away, and remote players animate properly.
            SwapPoseSource = Dev.CreateEntry("SwapPoseSource", "VanillaRig", description:
                "\"VanillaRig\" or \"VRIK\" for our own solver");
            SwapArmSource = Dev.CreateEntry("SwapArmSource", "IKTargets", description:
                "\"IKTargets\" solves the arms to your controllers; \"Retarget\" copies the vanilla " +
                "arms. The game disables the IK on your own third-person body, so its arms are " +
                "pure walking animation and copying them can't work for you. Remote players are " +
                "solved normally and keep the copied pose.");
            SwapArmTargetSource = Dev.CreateEntry("SwapArmTargetSource", "IKTargets", description:
                "\"IKTargets\" follows the game's smoothed hand targets; \"Controllers\" follows " +
                "your controllers with no lag but a different orientation, so the wrist offsets " +
                "above need retuning if you switch.");
            SwapForceVanillaIK = Dev.CreateEntry("SwapForceVanillaIK", false, description:
                "Re-enable the IK the game deliberately turned off on your own body. It doesn't " +
                "produce usable arms, so there's no reason to interfere.");
            RetargetHipsFollow = Dev.CreateEntry("RetargetHipsFollow", 1.0f);
            // Without it, an avatar whose rest pose isn't a T-pose ends up in one.
            RetargetAlignAtCapture = Dev.CreateEntry("RetargetAlignAtCapture", true);
            // Without it, VRCFaceTracking only sends what a VRChat avatar asked for, and there is
            // no VRChat avatar here.
            FaceForceRelevant = Dev.CreateEntry("FaceForceRelevant", true, description:
                "Ask VRCFaceTracking to send every parameter");
        }
    }
}
