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
        public static MelonPreferences_Entry<float> AvatarSize;

        // ---- [CustomAvatars_Tuning] -----------------------------------------------------
        public static MelonPreferences_Entry<float> SpringStiffnessScale;
        public static MelonPreferences_Entry<float> SpringGravityScale;
        public static MelonPreferences_Entry<float> SpringDragBase;
        public static MelonPreferences_Entry<float> SpringDragFromSpring;
        public static MelonPreferences_Entry<bool> SpringCollidersEnabled;
        public static MelonPreferences_Entry<float> SpringMaxAngleFallback;

        public static MelonPreferences_Entry<float> SwapHandTrimLeftX;
        public static MelonPreferences_Entry<float> SwapHandTrimLeftY;
        public static MelonPreferences_Entry<float> SwapHandTrimLeftZ;
        public static MelonPreferences_Entry<float> SwapHandTrimRightX;
        public static MelonPreferences_Entry<float> SwapHandTrimRightY;
        public static MelonPreferences_Entry<float> SwapHandTrimRightZ;

        public static MelonPreferences_Entry<bool> SelfHideHead;
        public static MelonPreferences_Entry<float> SelfHeadBoneScale;
        public static MelonPreferences_Entry<string> SelfHeadShrinkBones;
        public static MelonPreferences_Entry<string> SelfHeadKeepBones;

        public static MelonPreferences_Entry<float> HandCurlDegrees;
        public static MelonPreferences_Entry<float> ThumbCurlDegrees;
        public static MelonPreferences_Entry<float> HandCurlSmoothing;
        public static MelonPreferences_Entry<bool> HandCurlFlipLeft;
        public static MelonPreferences_Entry<bool> HandCurlFlipRight;

        public static MelonPreferences_Entry<float> ArmStretch;
        public static MelonPreferences_Entry<float> ArmStretchUpperShare;
        public static MelonPreferences_Entry<float> ArmShoulderYieldDegrees;
        public static MelonPreferences_Entry<float> ArmTwistShare;
        public static MelonPreferences_Entry<float> ArmWristTwistLimitDegrees;
        public static MelonPreferences_Entry<bool> ArmLockHands;
        public static MelonPreferences_Entry<float> ArmPoleHandWeight;
        public static MelonPreferences_Entry<float> ArmPoleFollowRate;
        public static MelonPreferences_Entry<float> ArmElbowHeadClearance;
        public static MelonPreferences_Entry<bool> LegIkEnabled;
        public static MelonPreferences_Entry<float> LegStretch;
        public static MelonPreferences_Entry<bool> LegLockFeet;
        public static MelonPreferences_Entry<float> RebindOnTposeSeconds;
        public static MelonPreferences_Entry<float> SizeMoveSpeedBlend;

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
        public static MelonPreferences_Entry<bool> SizeDebug;
        public static MelonPreferences_Entry<bool> FbtDisableGrounder;
        public static MelonPreferences_Entry<bool> FbtStrictTracking;
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
        public static MelonPreferences_Entry<bool> RetargetHipsGuard;
        public static MelonPreferences_Entry<bool> PeerSpineToHead;
        public static MelonPreferences_Entry<float> PeerSpineToHeadMaxDegrees;
        public static MelonPreferences_Entry<float> DiagPeerPoseSeconds;
        public static MelonPreferences_Entry<string> SwapHideVanillaMeshMode;
        public static MelonPreferences_Entry<bool> SwapKeepVanillaMeshInView;
        public static MelonPreferences_Entry<bool> SwapSolvePeerArms;
        public static MelonPreferences_Entry<float> RetargetSettleSeconds;
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
            HotkeysEnabled = Main.CreateEntry("HotkeysEnabled", true);
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
            VoiceJawEnabled = Main.CreateEntry("VoiceJawEnabled", true);
            // Remembered across sessions so trackers come back on at launch once calibrated.
            // F10 flips it; the trackers themselves are read straight from SteamVR.
            FbtEnabled = Main.CreateEntry("FbtEnabled", false);
            TrackerSyncEnabled = Main.CreateEntry("TrackerSyncEnabled", true);
            // How big you are, as a multiple of vanilla. 1 means the mod never touches your
            // size — no reference held, nothing written. PageUp/PageDown step it, Home puts
            // it back to 1; each key writes the file. See Avatars/PlayerSize.cs.
            AvatarSize = Main.CreateEntry("AvatarSize", 1.0f, description:
                "How big you are, as a multiple of normal (0.3 to 3). 1 = the mod leaves your size alone. " +
                "PageUp/PageDown change it in game, Home sets it back to 1.");

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

            // Extra wrist rotation on top of the one measured from the avatar's own hand bones
            // (see ArmIK.HandFromTargetFrame). These replace SwapHandOffset*, whose (-90, 0, 180)
            // was one rig's bone convention tuned in by hand and rolled every other rig's palms
            // ninety degrees; that rotation is now derived per avatar, so the right trim for a
            // rig with finger bones is zero. Applied every frame, so edit and press F3 to dial
            // it in live — mainly for SwapArmTargetSource=Controllers, whose frame differs from
            // the IK targets'.
            SwapHandTrimLeftX = Tuning.CreateEntry("SwapHandTrimLeftX", 0f, description:
                "Extra wrist rotation, degrees, on top of the one measured from the avatar's hand bones");
            SwapHandTrimLeftY = Tuning.CreateEntry("SwapHandTrimLeftY", 0f);
            SwapHandTrimLeftZ = Tuning.CreateEntry("SwapHandTrimLeftZ", 0f);
            SwapHandTrimRightX = Tuning.CreateEntry("SwapHandTrimRightX", 0f);
            SwapHandTrimRightY = Tuning.CreateEntry("SwapHandTrimRightY", 0f);
            SwapHandTrimRightZ = Tuning.CreateEntry("SwapHandTrimRightZ", 0f);

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

            // How far a full grip closes the fingers. Applies to both hands; the bend
            // direction comes from the avatar's own bone geometry, per hand.
            HandCurlDegrees = Tuning.CreateEntry("HandCurlDegrees", 70f);
            ThumbCurlDegrees = Tuning.CreateEntry("ThumbCurlDegrees", 40f);
            HandCurlSmoothing = Tuning.CreateEntry("HandCurlSmoothing", 0.35f, description:
                "How quickly fingers follow the controller. Lower is smoother and laggier.");
            // Escape hatch for a rig the palm detection gets wrong: one hand's fingers bend
            // backwards while the other is fine. The log line `hand poses: right palm from …`
            // says what it decided and why; please report the rig so the detection can learn.
            HandCurlFlipLeft = Tuning.CreateEntry("HandCurlFlipLeft", false, description:
                "Reverse the left hand's finger curl if it bends backwards");
            HandCurlFlipRight = Tuning.CreateEntry("HandCurlFlipRight", false, description:
                "Reverse the right hand's finger curl if it bends backwards");

            // A custom avatar's arms are rarely the game character's length. The hand goes on
            // the target regardless; this is how much of the shortfall the arm may cover by
            // getting longer before the wrist skin covers the rest. High, because in first
            // person a long forearm is far less visible than a wrist pulled off the end of it.
            ArmStretch = Tuning.CreateEntry("ArmStretch", 0.5f, description:
                "How much longer the arm may get to reach the hand, 0 to 1 (0.5 = half again)");
            // A stretched forearm is the one thing you look at all day in first person; the
            // upper arm is mostly out of view. Most of the stretch goes there.
            ArmStretchUpperShare = Tuning.CreateEntry("ArmStretchUpperShare", 0.7f, description:
                "How much of that stretch the upper arm takes, 0 to 1 (1 = all of it, the forearm keeps its shape)");
            // 0 turns it off.
            ArmShoulderYieldDegrees = Tuning.CreateEntry("ArmShoulderYieldDegrees", 25f, description:
                "How far the collarbone may rotate toward a hand that's out of reach, in degrees");
            // Real forearms share pronation between elbow and wrist. All of it on the wrist
            // pinches the mesh into a straw when you turn your palm up.
            ArmTwistShare = Tuning.CreateEntry("ArmTwistShare", 0.5f, description:
                "How much wrist roll is passed back up to the forearm, 0 to 1");
            // Past this the wrist mesh collapses whatever the share says, so the forearm takes
            // the rest. Measured from the bind pose, which is the one wrist that skins right.
            ArmWristTwistLimitDegrees = Tuning.CreateEntry("ArmWristTwistLimitDegrees", 75f, description:
                "The most the wrist may twist away from its rest pose before the forearm turns instead");
            // The hand goes exactly where the controller is, even if the arm couldn't get it
            // there. False shows the solver's honest miss, for diagnosing it.
            ArmLockHands = Tuning.CreateEntry("ArmLockHands", true);
            // Where the elbow points is chosen by the solver (see ArmIK.RollElbow): a body-
            // relative rest direction plus a hint from the hand's finger direction, which is
            // what puts a bow-drawing elbow out to the side instead of through the headset.
            // 0 leaves only the rest direction; 1 weights the hint equally with it.
            ArmPoleHandWeight = Tuning.CreateEntry("ArmPoleHandWeight", 1f, description:
                "How much the hand's orientation steers the elbow, 0 to 2 (0 = elbows always hang the same way)");
            // A time constant, per second. Higher follows faster; 0 is no smoothing at all.
            ArmPoleFollowRate = Tuning.CreateEntry("ArmPoleFollowRate", 20f, description:
                "How quickly the elbow direction follows a change, per second (0 = instantly)");
            // 0 turns it off.
            ArmElbowHeadClearance = Tuning.CreateEntry("ArmElbowHeadClearance", 0.15f, description:
                "Keep the elbow at least this far from the head bone, in metres, by rolling it away");
            // The feet go where the game's own feet are — on the floor, stepping, or on the
            // trackers — instead of hanging off the head at a fixed leg length.
            LegIkEnabled = Tuning.CreateEntry("LegIkEnabled", true, description:
                "Solve each leg to the vanilla body's foot. False leaves the legs to the copied pose.");
            LegStretch = Tuning.CreateEntry("LegStretch", 0.25f, description:
                "How much longer a leg may get to reach the game's foot, 0 to 1");
            LegLockFeet = Tuning.CreateEntry("LegLockFeet", true);
            // F4 twice, without F4: hold a T-pose this long and the avatar is taken off and
            // put straight back on. 0 turns it off.
            RebindOnTposeSeconds = Tuning.CreateEntry("RebindOnTposeSeconds", 1.5f, description:
                "Hold a T-pose this many seconds to re-bind the avatar to your body. 0 disables.");
            // Stick speed and jump height are world metres, so a small player crosses a room
            // fast. 0 leaves the game alone; 1 makes a half-size player half as fast.
            SizeMoveSpeedBlend = Tuning.CreateEntry("SizeMoveSpeedBlend", 0f, description:
                "0 to 1: how much your movement speed and jump follow your size (0 = vanilla speed at any size).");

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
            // After this long without usable data, a limb is let go rather than left frozen:
            // your own hip or foot target fades out to the game's own placement when its puck
            // has been untracked this long (and back in when it returns), and a peer's legs
            // go back to the game's walking animation when their stream has gone quiet.
            FbtStaleSeconds = Tuning.CreateEntry("FbtStaleSeconds", 1.0f);

            // Measured automatically from where your headset actually is, taking the tallest
            // plausible reading of the session. Set it if you play seated, or if you want to be
            // sized against a height you didn't happen to be standing at.
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

            // Every transform between the headset and the rig root, at every size change and
            // for eight seconds after. This is what found the spawn-while-scaled bug.
            SizeDebug = Dev.CreateEntry("SizeDebug", false);
            FbtDebug = Dev.CreateEntry("FbtDebug", false, description:
                "Log tracker poses and VRIK weights every frame while FBT is on.");
            // The grounder plants feet on the floor procedurally; real foot trackers and a
            // foot-planter fighting over the same feet is visible as toe jitter.
            FbtDisableGrounder = Dev.CreateEntry("FbtDisableGrounder", true);
            // A puck that has lost sight of its base stations stays "valid" to SteamVR while
            // it coasts on its IMU and freezes wherever it drifted to. On, only a pose SteamVR
            // itself reports as Running_OK drives a limb; the rest count as dropouts and the
            // limb is held, then faded out. Off is the pre-0.42.3 behaviour, for comparison.
            FbtStrictTracking = Dev.CreateEntry("FbtStrictTracking", true);
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
            SwapHideVanillaMeshMode = Dev.CreateEntry("SwapHideVanillaMeshMode", "ShadowsOnly", description:
                "How the vanilla body is hidden. \"ShadowsOnly\" leaves the renderer enabled so the " +
                "game still counts the player as on screen and keeps solving their pose — it costs " +
                "a leftover human-shaped shadow. \"ForceOff\" and \"Disable\" both stop the game " +
                "solving peers, and are here to compare against, not to use.");
            SwapKeepVanillaMeshInView = Dev.CreateEntry("SwapKeepVanillaMeshInView", true, description:
                "Give the hidden body bounds large enough that it never leaves the camera, so the " +
                "game keeps solving that player even when their old body would have been culled. " +
                "Without it a peer freezes mid-stride whenever their vanilla body goes off screen, " +
                "which is not the same moment their avatar does.");
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
                "your controllers with no lag but a different orientation, so the SwapHandTrim " +
                "entries need setting if you switch; \"FpsHands\" follows the game's own " +
                "first-person hand bones, which it snaps to weapon grips and holds on a bow's " +
                "string at full draw — the other two sources know nothing of that.");
            SwapForceVanillaIK = Dev.CreateEntry("SwapForceVanillaIK", false, description:
                "Re-enable the IK the game deliberately turned off on your own body. It doesn't " +
                "produce usable arms, so there's no reason to interfere.");
            SwapSolvePeerArms = Dev.CreateEntry("SwapSolvePeerArms", true, description:
                "Solve a peer's avatar arms to their own hand targets instead of copying their " +
                "rig's arm rotations. The game blends a peer's arm IK weight up and down " +
                "continuously (0.1 to 1.0 in one log), so a copied arm flicks between the walking " +
                "animation and the solved pose — the elbow snapping between two places. Their " +
                "hand targets are networked and steady, so solving to those is the fix.");
            RetargetSettleSeconds = Dev.CreateEntry("RetargetSettleSeconds", 0.5f, description:
                "How long to let a rig settle before taking the reference pose off it. A swap or " +
                "a respawn catches the body mid-transition, and a reference taken then is a tilt " +
                "that lasts the whole session.");
            RetargetHipsFollow = Dev.CreateEntry("RetargetHipsFollow", 1.0f);
            RetargetHipsGuard = Dev.CreateEntry("RetargetHipsGuard", true, description:
                "Learn where the game rig's hips sit on a standing body and measure the hips " +
                "follow from there, instead of from wherever they were when the reference was " +
                "taken. A reference taken on a respawning peer — a ragdoll, or a body still " +
                "being stood up — put their avatar a hip's height in the air with its legs " +
                "stretched to the floor until they re-wore it. Off, the old behaviour.");
            PeerSpineToHead = Dev.CreateEntry("PeerSpineToHead", true, description:
                "Turn a peer's avatar torso so its head lands on their real head target instead " +
                "of copying the game body's lean. The game body is 1.5 m tall for everyone: it puts " +
                "its head directly under the player's real head but pinned at 1.48 m, so a taller " +
                "player's forward lean is reproduced with a shorter spine and comes out steeper — " +
                "half again as steep for a 1.77 m player. The head target is networked, steady, and " +
                "where their head actually is. Off, the old behaviour: copy the game body's lean.");
            PeerSpineToHeadMaxDegrees = Dev.CreateEntry("PeerSpineToHeadMaxDegrees", 45f, description:
                "The most the torso correction may turn, in degrees. A head target far from the " +
                "body — a ragdoll, a body still spawning — is not a lean and is not followed.");
            DiagPeerPoseSeconds = Dev.CreateEntry("DiagPeerPoseSeconds", 1.0f, description:
                "Seconds between the one-line report on why a peer's avatar is or isn't moving. " +
                "0 turns it off.");
            // Without it, an avatar whose rest pose isn't a T-pose ends up in one.
            RetargetAlignAtCapture = Dev.CreateEntry("RetargetAlignAtCapture", true);
            // Without it, VRCFaceTracking only sends what a VRChat avatar asked for, and there is
            // no VRChat avatar here.
            FaceForceRelevant = Dev.CreateEntry("FaceForceRelevant", true, description:
                "Ask VRCFaceTracking to send every parameter");
        }
    }
}
