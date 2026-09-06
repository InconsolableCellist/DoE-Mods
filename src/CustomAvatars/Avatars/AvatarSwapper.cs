using System;
using System.Collections.Generic;
using CustomAvatars.Gate;
using CustomAvatars.Recon;
using UnityEngine;
using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppRootMotion.FinalIK;
using Interop = CustomAvatars.Recon.Interop;

namespace CustomAvatars.Avatars
{
    /// <summary>
    /// Phase 2b: puts a custom avatar on an actual player.
    ///
    /// Strategy is the parallel-rig puppet from PLAN.md — we do not touch the game's merged
    /// mesh pipeline. The vanilla skeleton stays alive and animated; we hide only its renderer
    /// and run our own model beside it, driven by our own VRIK against the game's IK targets.
    /// That keeps holsters, grab poses and hitboxes vanilla — `AvatarHolster.bone` is a
    /// `HumanBodyBones` resolved through the game's Animator, so it never sees our model. The
    /// swap is cosmetic in the strict sense, which is the fairness property the group cares about.
    /// </summary>
    public class AvatarSwapper
    {
        private AvatarPlayer _player;
        private AvatarBundle _bundle;
        private GameObject _model;       // the instantiated avatar, a scene root
        private CharacterPrefab _fullBody;  // the game's Model_<nick>, which we follow
        private GameObject _leftHandTarget;
        private GameObject _rightHandTarget;
        private AvatarManifest _manifest;
        private readonly List<Transform> _shrinkBones = new List<Transform>();
        private readonly List<Transform> _keepBones = new List<Transform>();
        private string _headChopSpec;   // what the lists were built from, so config edits rebuild them
        private VRIK _vrik;
        private SpringBones _springs;
        private HandPoser _hands;
        private PoseRetargeter _retarget;
        private ArmIK _armIk;
        private LegIK _legIk;
        private float _nextLegLogAt;
        private float _tposeSince, _lastRebindAt;
        private Face.FaceDriver _face;
        private SkinnedMeshRenderer _hiddenVanillaMesh;
        private bool _vanillaMeshWasForcedOff;
        private bool _vanillaMeshWasEnabled = true;
        private UnityEngine.Rendering.ShadowCastingMode _vanillaMeshWasCasting =
            UnityEngine.Rendering.ShadowCastingMode.On;
        private readonly List<(Renderer renderer, bool wasEnabled)> _fpsArmRenderers =
            new List<(Renderer, bool)>();

        public bool IsActive => Interop.Alive(_model);

        /// <summary>One line covering every setting that changes how the swap looks.</summary>
        public static string DescribeSettings() =>
            $"UseVrik={ModConfig.SwapUseVrik.Value}, HideVanillaMesh={ModConfig.SwapHideVanillaMesh.Value}, " +
            $"LocomotionWeight={ModConfig.SwapLocomotionWeight.Value}, HideHead={ModConfig.SelfHideHead.Value}, " +
            $"HideFpsArms={ModConfig.SwapHideFpsArms.Value}, FollowVanillaRoot={ModConfig.SwapFollowVanillaRoot.Value}, " +
            $"PoseSource={ModConfig.SwapPoseSource.Value}, ArmSource={ModConfig.SwapArmSource.Value}, " +
            $"hand trim L=({ModConfig.SwapHandTrimLeftX.Value},{ModConfig.SwapHandTrimLeftY.Value},{ModConfig.SwapHandTrimLeftZ.Value}) " +
            $"R=({ModConfig.SwapHandTrimRightX.Value},{ModConfig.SwapHandTrimRightY.Value},{ModConfig.SwapHandTrimRightZ.Value})";

        private float _settledLogAt;   // unscaled time at which to re-log placement; 0 = done
        private float _nextLeashLogAt;
        private float _lastSolverDrift;
        private float _nextArmLogAt;
        private float _nextPeerPoseLogAt;
        private bool _retargetNeedsRecapture;
        private float _retargetSettleUntil;
        private bool _vanillaMeshWasUpdateOffscreen;
        private Bounds _vanillaMeshWasLocalBounds;
        private int _leashTrips;
        private bool _wasAlive = true;
        private bool _forcedVanillaIk;
        private bool _vanillaIkWas;
        private bool _ragdolling;

        // The hips origin guard. See GuardHipsOrigin.
        private Vector3? _hipsGood;          // where the game rig's hips sit on a standing, solved body
        private float _hipsSteadyStart = -1f; // unscaled time the hips have been plausible since; -1 = not now
        private float _nextHipsLogAt;
        private int _hipsRebases;
        // The peer torso correction, for the peer pose line. See PeerSpineToHead.
        private float _leanPuppet = -1f, _leanReal = -1f, _leanApplied = -1f, _leanWanted = -1f;
        private float _nextHipsWarnAt;
        private const float HipsMinHeight = 0.45f;   // a hip lower than this, in body metres, is lying down
        private const float HipsMaxHeight = 1.5f;    // higher than this is being lifted, or is not a person
        private const float HipsMaxLateral = 0.4f;   // further than this from the root is a ragdoll that travelled
        private const float HipsSteadySeconds = 0.75f;
        private const float HipsRebaseMetres = 0.15f; // an origin further than this from the standing one is wrong

        // Head-anchored placement, for ourselves only. See AlignToHead.
        private Transform _headBone;
        private Vector3 _modelBaseScale = Vector3.one;
        private float _heightScale = 1f;
        private float _calibrateAt;
        private Vector3 _fullBodyRestScale = Vector3.one;
        private float _remoteBodySize = 1f;
        private float _sizeAtFit = 1f;

        /// <summary>The player's size (PlayerSize) the current fit was measured at.</summary>
        public float SizeAtFit => _sizeAtFit;
        private float _nextArmDumpAt;
        private int _armDumpsLeft;
        // Hand-target watch (self only): each of the game's hand IK targets in its controller's
        // frame last frame, the offset it rests at, whether it is currently away from that,
        // and the last time a jump was reported, so a jittering target can't flood the log.
        private readonly Vector3[] _lastTargetPos = new Vector3[2];
        private readonly Vector3[] _targetRest = new Vector3[2];
        private readonly bool[] _hasLastTargetPos = new bool[2];
        private readonly bool[] _targetRestSeeded = new bool[2];
        private readonly bool[] _targetAway = new bool[2];
        private readonly bool[] _fpsHandAway = new bool[2];
        private float _nextTargetJumpLogAt;
        // The first-person arm rig's hand bones — the hands you actually see in vanilla, and
        // the thing the game snaps to a weapon's grip or a bow's string. Self only.
        private readonly Transform[] _fpsHands = new Transform[2];
        // The rotation that turns each hand target holder's parent into the standard target
        // frame (Z along the fingers, Y out the back of the hand). Identity for the IK targets
        // and controllers, measured from the knuckles when the parent is a first-person hand bone.
        private readonly Quaternion[] _handFrameLocal = { Quaternion.identity, Quaternion.identity };

        /// <summary>
        /// How much the model had to be resized to put its head at yours. Peers are told this
        /// number rather than left to guess: it is the one measurement that says how big the
        /// person wearing this avatar actually is, and without it a friend who has shrunk
        /// themselves would still stand full-size on everyone else's screen.
        /// </summary>
        public float HeightScale => _heightScale;

        /// <summary>Raised on our own avatar when the fit changes, so peers can be told.</summary>
        public event Action HeightScaleChanged;

        /// <summary>
        /// Raised when a held T-pose asks for the avatar to be taken off and put back on. The
        /// manager does that, not us: a swapper can't safely replace itself from inside its
        /// own LateUpdate.
        /// </summary>
        public event Action<string> RebindRequested;
        public string AvatarName { get; private set; }

        /// <summary>
        /// True for your own avatar. Head chopping and hiding the first-person arms are
        /// first-person comforts — doing either to a peer's avatar would leave them headless
        /// on your screen and take away your own arms for someone else's body.
        /// </summary>
        public bool IsSelf { get; private set; }

        public int ActorNumber { get; private set; } = -1;

        /// <summary>The finger poser, if this avatar has one. Null while nothing is worn.</summary>
        public HandPoser Hands => _hands;
        public Face.FaceDriver Face => _face;

        /// <summary>True if this swapper is still driving the player object it was built for.</summary>
        public bool IsAttachedTo(AvatarPlayer player) =>
            Interop.Alive(_player) && Interop.Alive(player) && _player.Pointer == player.Pointer;

        public AvatarSwapper()
        {
            // A remote player's avatar is only ours to draw while the network gate is open. Our
            // own survives leaving a room — that's the whole point of the menu working.
            ModGate.ActiveChanged += active => { if (!active && !IsSelf) Revert("gate closed"); };
            ModGate.LocalVisualsChanged += allowed => { if (!allowed) Revert("local visuals off"); };
        }

        public void Toggle(AvatarLibrary library)
        {
            if (IsActive) { Revert("toggled off"); return; }

            if (!ModGate.LocalVisuals)
            {
                Core.Log.Warning($"Swap refused: {ModGate.LocalReason}.");
                return;
            }

            AvatarPlayer local;
            try { local = AvatarPlayer.LocalAvatar; }
            catch (Exception e) { Core.Log.Error($"No local avatar: {e.Message}"); return; }
            if (!Interop.Alive(local)) { Core.Log.Warning("Swap refused: no local AvatarPlayer yet."); return; }

            var name = ModConfig.SelectedAvatar.Value;
            var manifest = string.IsNullOrWhiteSpace(name) ? library.First() : library.Get(name);
            if (manifest == null) { Core.Log.Warning($"No avatar available in {AvatarLibrary.AvatarsDir}"); return; }

            Apply(local, manifest);
        }

        public void Apply(AvatarPlayer player, AvatarManifest manifest) => Apply(player, manifest, true);

        /// <param name="initialFit">
        /// A starting scale for the model, for a re-wear: the previous fit, adjusted for any
        /// size change. Calibrate still measures a second later; this just means the feet
        /// are not off the floor for that second. 0 for a fresh swap.
        /// </param>
        public void Apply(AvatarPlayer player, AvatarManifest manifest, bool isSelf, float initialFit = 0f)
        {
            IsSelf = isSelf;
            try { ActorNumber = player.ActorNumber; } catch { ActorNumber = -1; }
            ReconLog.Section($"Avatar swap ({(isSelf ? "self" : "remote")}) — {manifest.name} onto {SafeName(player)}");

            var fullBody = SafeFullBody(player);
            if (!Interop.Alive(fullBody))
            {
                Core.Log.Error("Swap failed: AvatarPlayer.FullBody is null — the model isn't built yet. " +
                               "Wait until you've spawned into a lobby and try again.");
                return;
            }

            _bundle = AvatarBundle.Acquire(manifest, out var error);
            if (_bundle == null) { Core.Log.Error($"Swap failed to load `{manifest.name}`: {error}"); return; }

            _player = player;
            _fullBody = fullBody;
            _manifest = manifest;
            AvatarName = manifest.name;
            // A peer's body may get sized to match them (ApplyRemoteBodySize); this is what
            // it goes back to. Our own body is PlayerSize's, not ours.
            try { _fullBodyRestScale = fullBody.transform.localScale; } catch { _fullBodyRestScale = Vector3.one; }
            if (_fullBodyRestScale.x <= 1e-4f) _fullBodyRestScale = Vector3.one;
            _remoteBodySize = 1f;

            try
            {
                // The model is instantiated as a SCENE ROOT with no holder above it.
                //
                // Two bugs got us here. Parenting under Model_<nick> let VRIK's procedural
                // locomotion and the game's own root motion both drive one position, which
                // compounded. Moving it under a holder of our own kept the runaway: VRIK owns
                // `references.root`, which is the MODEL transform, and driving a child's world
                // position from a solver produced a near sign-flip in X — the model landed at
                // x = -52.85 from x = 52.17. The F6 preview never had this problem because its
                // model is a plain scene root. So is this one now.
                if (!Interop.Alive(_bundle.Prefab))
                {
                    Core.Log.Error("The avatar prefab is no longer loaded — press F5 to rescan, then try again.");
                    _bundle.Release();
                    _bundle = null;
                    return;
                }

                _model = UnityEngine.Object.Instantiate(_bundle.Prefab);
                _model.name = $"DFM_Avatar_{manifest.name}";
                UnityEngine.Object.DontDestroyOnLoad(_model);
                Core.Log.Msg($"    avatar Animator: {AvatarBundle.QuietAnimators(_model)}");

                // Configure while inactive: VRIK's Awake initiates its solver, and it must not
                // run before `references` is populated.
                _model.SetActive(false);
                _model.transform.position = fullBody.transform.position;
                _model.transform.rotation = fullBody.transform.rotation;
                _modelBaseScale = AvatarBundle.RootScale(_model, "avatar") * manifest.rig.suggestedScale;
                _model.transform.localScale = _modelBaseScale;
                _heightScale = 1f;
                if (isSelf && float.IsFinite(initialFit) && initialFit > 0f)
                {
                    _heightScale = Mathf.Clamp(initialFit, 0.5f, 2f);
                    _model.transform.localScale = _modelBaseScale * _heightScale;
                }
                _sizeAtFit = PlayerSize.Applied;
                // The head bone is what we anchor ourselves by, so resolve it once here rather
                // than searching the hierarchy every frame.
                _headBone = null;
                if (manifest.rig?.humanoidBones != null &&
                    manifest.rig.humanoidBones.TryGetValue("Head", out var headPath) &&
                    !string.IsNullOrEmpty(headPath))
                    _headBone = _model.transform.Find(headPath);

                var refs = BuildReferences(_model, manifest, out var missing);
                if (refs == null)
                {
                    Core.Log.Error($"Swap failed: humanoid bone map incomplete — missing {missing}. " +
                                   "Re-export with the current exporter.");
                    Revert("reference build failed");
                    return;
                }

                var useRetarget = string.Equals(ModConfig.SwapPoseSource.Value, "VanillaRig",
                                                StringComparison.OrdinalIgnoreCase);

                if (useRetarget)
                {
                    // The vanilla body has to be solving properly for there to be a pose worth
                    // copying. Report what it's doing before we touch it.
                    ReportVanillaIk(fullBody);

                    _retarget = new PoseRetargeter();
                    var result = _retarget.Build(player, _model, manifest);

                    // The game disables VRIK on YOUR body, so its arms never track your
                    // controllers and copying them can't work — your own arms have to be solved.
                    // A peer's body is solved normally, so copying gets their arms pointing the
                    // right way, but not their hands in the right place: rotations alone can't
                    // account for an avatar whose arms aren't the length of the body underneath,
                    // which is why a peer's hands sit a little inside their real ones.
                    // SwapSolvePeerArms solves theirs too, against their own hand targets.
                    var wantSolvedArms = string.Equals(ModConfig.SwapArmSource.Value, "IKTargets",
                                                       StringComparison.OrdinalIgnoreCase);
                    var solveArms = wantSolvedArms && (isSelf || ModConfig.SwapSolvePeerArms.Value);
                    if (isSelf && !solveArms && ModConfig.SwapForceVanillaIK.Value) ForceVanillaIk(fullBody);

                    if (solveArms)
                    {
                        // Which transform the arms chase. The IK targets are what the game's own
                        // rig follows, but they are smoothed for networking — so while you move
                        // with the stick they lag behind the controllers, and a held weapon
                        // (parented to the controller, not the target) slides out of the hand
                        // until you stop. The controllers themselves have no such lag.
                        // Controllers are a local thing. A peer has no controller transforms on
                        // this machine, only the smoothed targets their body already follows.
                        // The first-person hands are a third choice, and for your own body
                        // the truest one: they are the hands you see in vanilla, and the game
                        // snaps them to a weapon's grip and holds them on a bow's string at
                        // full draw, which neither the IK targets nor the controllers know
                        // about. Their bones carry their own axis convention, so the target
                        // frame is measured from their knuckles the same way ArmIK measures
                        // the avatar's.
                        var source = ModConfig.SwapArmTargetSource.Value ?? "";
                        var useControllers = isSelf && string.Equals(source, "Controllers", StringComparison.OrdinalIgnoreCase);
                        var useFpsHands = isSelf && string.Equals(source, "FpsHands", StringComparison.OrdinalIgnoreCase);
                        if (isSelf) CacheFpsHands(player);
                        if (useFpsHands && (!Interop.Alive(_fpsHands[0]) || !Interop.Alive(_fpsHands[1])))
                        {
                            Core.Log.Warning("    arm targets: FpsHands asked for, but the first-person hand bones were not found — using the IK targets");
                            useFpsHands = false;
                        }
                        var leftParent = useFpsHands ? _fpsHands[0] : useControllers ? player.LeftHand : player.IKTargetLeftHand;
                        var rightParent = useFpsHands ? _fpsHands[1] : useControllers ? player.RightHand : player.IKTargetRightHand;
                        Core.Log.Msg($"    arm targets: {(useFpsHands ? "first-person hands" : useControllers ? "controllers" : "IK targets")} " +
                                     $"— `{Interop.Name(leftParent)}` / `{Interop.Name(rightParent)}`");

                        _handFrameLocal[0] = _handFrameLocal[1] = Quaternion.identity;
                        if (useFpsHands)
                        {
                            _handFrameLocal[0] = FpsHandFrame(_fpsHands[0], true, out var noteL);
                            _handFrameLocal[1] = FpsHandFrame(_fpsHands[1], false, out var noteR);
                            Core.Log.Msg($"      L frame {noteL}\n      R frame {noteR}");
                            KeepFpsArmsAnimating();
                        }

                        var lt = HandTarget(ref _leftHandTarget, "DFM_HandTarget_L", leftParent);
                        var rt = HandTarget(ref _rightHandTarget, "DFM_HandTarget_R", rightParent);
                        UpdateHandOffsets();
                        _armIk = new ArmIK();
                        Core.Log.Msg($"    arm source: IKTargets — {_armIk.Build(_model, manifest, lt, rt, _retarget.SourceOf)}");
                        if (!_armIk.HasArms) _armIk = null;
                    }
                    Core.Log.Msg($"    pose source: VanillaRig — {result}");
                    if (_retarget.LinkCount > 0)
                    {
                        _legIk = new LegIK();
                        Core.Log.Msg($"    legs: {_legIk.Build(_model, manifest, _retarget.SourceOf)}");
                        if (!_legIk.HasLegs) _legIk = null;
                    }
                    ArmReferenceAgain("swapped in");
                    if (_retarget.LinkCount == 0)
                    {
                        Core.Log.Warning("    retargeting found no usable bones; falling back to VRIK.");
                        _retarget = null;
                        _armIk = null;
                        _legIk = null;
                        _face = null;
                        useRetarget = false;
                    }
                }

                if (!useRetarget && ModConfig.SwapUseVrik.Value)
                {
                    _vrik = AddVrik(_model);
                    if (_vrik == null) { Revert("VRIK could not be added"); return; }
                    _vrik.references = refs;

                    // FinalIK's own routine for working out which way this particular rig's
                    // wrists face — it derives wristToPalmAxis and palmToThumbAxis from the
                    // hand and finger bones. Without it VRIK assumes an orientation convention
                    // the avatar may not use, which is how you get a paw stuck out sideways
                    // from the wrist. Must run after `references` is assigned.
                    try
                    {
                        _vrik.GuessHandOrientations();
                        Core.Log.Msg($"    hand axes guessed: L wristToPalm {_vrik.solver.leftArm.wristToPalmAxis} " +
                                     $"palmToThumb {_vrik.solver.leftArm.palmToThumbAxis}");
                        Core.Log.Msg($"                       R wristToPalm {_vrik.solver.rightArm.wristToPalmAxis} " +
                                     $"palmToThumb {_vrik.solver.rightArm.palmToThumbAxis}");
                    }
                    catch (Exception e) { Core.Log.Warning($"GuessHandOrientations failed: {e.Message}"); }

                    WireSolver(_vrik, player, manifest);
                }
                else if (!useRetarget)
                {
                    // Loud, because this is a diagnostic toggle people leave switched on by
                    // accident and the symptom — a T-posing avatar — looks exactly like a bug
                    // in the swap rather than a setting.
                    Core.Log.Warning("*** SwapUseVrik=false — NO IK. Your avatar WILL T-pose and will not " +
                                     "follow your head or hands. Set SwapUseVrik=true in MelonPreferences.cfg " +
                                     "and press F3.");
                }

                // Stops clothing and accessory meshes blinking out. A SkinnedMeshRenderer
                // culls against bounds derived from its bind pose unless told otherwise, and an
                // avatar posed far from bind — arms up, or simply a rig whose bind pose sits
                // elsewhere — gets culled while plainly on screen.
                FixRendererBounds(_model);

                _model.SetActive(true);

                CacheVanillaMesh(fullBody);
                if (isSelf) CacheFpsArms(player);

                _springs = new SpringBones();
                var springSummary = _springs.Build(_model, manifest);
                _springs.Reset();

                // Both self and peers get a poser; they differ only in where the curl values
                // come from. Ours reads the controllers, theirs is fed from the wire.
                _face = new Face.FaceDriver();
                Core.Log.Msg($"    face: {_face.Build(_model, manifest)}");
                if (_face.TargetCount == 0) _face = null;

                _hands = new HandPoser { RemoteDriven = !isSelf };
                var handResult = _hands.Build(_model, manifest);
                Core.Log.Msg($"    hand poses: {handResult}{(isSelf ? "" : " (driven by that peer)")}");
                if (isSelf) HandPoser.LogInputBackend();

                Core.Log.Msg($"*** Avatar swapped: {manifest.name} on {SafeName(player)} " +
                             $"(scale x{manifest.rig.suggestedScale:0.###})");
                Core.Log.Msg($"    dynamics: {springSummary}");
                LogPlacement(player);
                _settledLogAt = Time.unscaledTime + 1f;
                // Same moment: by then the pose is real and you are standing where you stand.
                _calibrateAt = isSelf ? Time.unscaledTime + 1f : 0f;
                // One full arm geometry dump a little later, once the fit has settled.
                _armDumpsLeft = 1;
                _nextArmDumpAt = Time.unscaledTime + 3f;
                if (isSelf && !Interop.Alive(_headBone))
                    Core.Log.Warning("    no Head bone in the manifest — cannot anchor the avatar to your head, " +
                                     "so it will sit wherever the game's body is. Re-export the avatar.");
                ReconLog.KeyValue("model root", Interop.ScenePath(_model.transform));
                ReconLog.KeyValue("dynamics", springSummary);
            }
            catch (Exception e)
            {
                Core.Log.Error($"Swap threw: {e}");
                Revert("exception during swap");
            }
        }

        /// <summary>
        /// Fills VRIK.References from the exporter's humanoid bone map rather than from
        /// FinalIK's auto-detection. Auto-detect guesses from bone names, and VRChat rigs use
        /// every naming convention there is; the manifest map came from Unity's own humanoid
        /// rig, so it's authoritative.
        /// </summary>
        private static VRIK.References BuildReferences(GameObject model, AvatarManifest manifest, out string missing)
        {
            missing = null;
            var map = manifest.rig?.humanoidBones;
            if (map == null || map.Count == 0) { missing = "no humanoidBones in manifest"; return null; }

            Transform Bone(params string[] names)
            {
                foreach (var n in names)
                {
                    if (!map.TryGetValue(n, out var path) || string.IsNullOrEmpty(path)) continue;
                    var t = model.transform.Find(path);
                    if (Interop.Alive(t)) return t;
                }
                return null;
            }

            var refs = new VRIK.References
            {
                root = model.transform,
                pelvis = Bone("Hips"),
                spine = Bone("Spine"),
                chest = Bone("UpperChest", "Chest"),   // optional, prefer the higher one
                neck = Bone("Neck"),                   // optional
                head = Bone("Head"),
                leftShoulder = Bone("LeftShoulder"),
                leftUpperArm = Bone("LeftUpperArm"),
                leftForearm = Bone("LeftLowerArm"),
                leftHand = Bone("LeftHand"),
                rightShoulder = Bone("RightShoulder"),
                rightUpperArm = Bone("RightUpperArm"),
                rightForearm = Bone("RightLowerArm"),
                rightHand = Bone("RightHand"),
                leftThigh = Bone("LeftUpperLeg"),
                leftCalf = Bone("LeftLowerLeg"),
                leftFoot = Bone("LeftFoot"),
                leftToes = Bone("LeftToes"),
                rightThigh = Bone("RightUpperLeg"),
                rightCalf = Bone("RightLowerLeg"),
                rightFoot = Bone("RightFoot"),
                rightToes = Bone("RightToes"),
            };

            // Only the non-optional ones are fatal; chest/neck/toes/shoulders are documented
            // as optional by VRIK itself.
            var required = new List<string>();
            if (!Interop.Alive(refs.pelvis)) required.Add("Hips");
            if (!Interop.Alive(refs.spine)) required.Add("Spine");
            if (!Interop.Alive(refs.head)) required.Add("Head");
            if (!Interop.Alive(refs.leftUpperArm)) required.Add("LeftUpperArm");
            if (!Interop.Alive(refs.leftForearm)) required.Add("LeftLowerArm");
            if (!Interop.Alive(refs.leftHand)) required.Add("LeftHand");
            if (!Interop.Alive(refs.rightUpperArm)) required.Add("RightUpperArm");
            if (!Interop.Alive(refs.rightForearm)) required.Add("RightLowerArm");
            if (!Interop.Alive(refs.rightHand)) required.Add("RightHand");
            if (!Interop.Alive(refs.leftThigh)) required.Add("LeftUpperLeg");
            if (!Interop.Alive(refs.rightThigh)) required.Add("RightUpperLeg");

            if (required.Count > 0) { missing = string.Join(", ", required); return null; }
            return refs;
        }

        private static VRIK AddVrik(GameObject model)
        {
            try
            {
                // Il2CppType.Of<T>() + TryCast rather than the generic AddComponent<T>():
                // generic native dispatch through interop is the fragile path.
                var component = model.AddComponent(Il2CppType.Of<VRIK>());
                var vrik = component?.TryCast<VRIK>();
                if (vrik == null) Core.Log.Error("AddComponent<VRIK> returned something that isn't a VRIK.");
                return vrik;
            }
            catch (Exception e)
            {
                Core.Log.Error($"Could not add VRIK: {e.GetType().Name}: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Three-point tracking: head and both hands come from the game's own networked IK
        /// targets, which already sync for remote players, so a peer's custom avatar gets
        /// correct tracking for free. Legs are procedural — there are no foot trackers.
        /// </summary>
        private void WireSolver(VRIK vrik, AvatarPlayer player, AvatarManifest manifest)
        {
            var solver = vrik.solver;
            if (solver == null) { Core.Log.Error("VRIK has no solver."); return; }

            solver.spine.headTarget = player.IKTargetHead;
            solver.spine.positionWeight = 1f;   // NB: the head's weights are named plainly on
            solver.spine.rotationWeight = 1f;   // Spine — there is no headPositionWeight.

            // Aim the arms at child transforms of the game's hand targets rather than at the
            // targets themselves. VRIK's GuessHandOrientations above works out how this rig's
            // wrists face; the child carries the configured trim on top, re-applied every frame
            // from config so it can be dialled in with F3 without respawning the avatar.
            solver.leftArm.target = HandTarget(ref _leftHandTarget, "DFM_HandTarget_L", player.IKTargetLeftHand);
            solver.leftArm.positionWeight = 1f;
            solver.leftArm.rotationWeight = 1f;

            solver.rightArm.target = HandTarget(ref _rightHandTarget, "DFM_HandTarget_R", player.IKTargetRightHand);
            solver.rightArm.positionWeight = 1f;
            solver.rightArm.rotationWeight = 1f;

            // Procedural locomotion exists to move a root nobody else is driving. We drive it
            // from the game's own body position every frame, so leaving this on means two
            // things fighting for one transform — which is exactly how the avatar ended up
            // 100 m away. Configurable so it can be tried again once the basics are right.
            solver.locomotion.weight = Mathf.Clamp01(ModConfig.SwapLocomotionWeight.Value);
            solver.plantFeet = true;
            // Deliberately not touching solver.scale: the model's own transform scale already
            // sizes the avatar, and FinalIK's solver scale has separate meaning for step
            // lengths. Setting both is a good way to get one applied twice.

            Core.Log.Msg($"    VRIK targets: head `{Interop.ScenePath(player.IKTargetHead)}`, " +
                         $"hands `{Interop.Name(player.IKTargetLeftHand)}` / `{Interop.Name(player.IKTargetRightHand)}`");
        }

        private static void FixRendererBounds(GameObject model)
        {
            try
            {
                var smrs = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                var changed = 0;
                for (var i = 0; i < smrs.Length; i++)
                {
                    var smr = smrs[i];
                    if (!Interop.Alive(smr) || smr.updateWhenOffscreen) continue;
                    smr.updateWhenOffscreen = true;
                    changed++;
                }
                if (changed > 0) Core.Log.Msg($"    set updateWhenOffscreen on {changed} skinned mesh(es)");
            }
            catch (Exception e) { Core.Log.Warning($"Could not fix renderer bounds: {e.Message}"); }
        }

        /// <summary>
        /// Catch the game moving a hand out from under us.
        ///
        /// Our hand goes exactly where its target is, so if the hand jumps and the arm log has
        /// nothing to say, something upstream moved. Two things are watched. The game's hand
        /// IK targets are not children of the controllers: they sit under Player_/IKTargets
        /// and the game places them every frame, normally at a fixed offset from the
        /// controller (about 13 cm on the rigs seen so far), so each is measured in its
        /// controller's frame, where a target that is merely following the hand holds still.
        /// And the first-person hand bones — the hands you see in vanilla — are measured
        /// against the IK targets, because the game snaps those hands to weapons and strings
        /// and the targets know nothing about it. Each departure and return is logged once,
        /// with the arm's state, so the two can be told apart.
        /// </summary>
        private void WatchHandTargets()
        {
            if (!Interop.Alive(_player)) return;
            for (var side = 0; side < 2; side++)
            {
                try
                {
                    var target = side == 0 ? _player.IKTargetLeftHand : _player.IKTargetRightHand;
                    var controller = side == 0 ? _player.LeftHand : _player.RightHand;
                    if (!Interop.Alive(target) || !Interop.Alive(controller)) continue;
                    var name = side == 0 ? "L" : "R";
                    var pos = target.position;
                    var local = controller.InverseTransformPoint(pos);

                    var jump = _hasLastTargetPos[side] ? Vector3.Distance(local, _lastTargetPos[side]) : 0f;
                    _lastTargetPos[side] = local;
                    _hasLastTargetPos[side] = true;

                    // The resting offset is seeded only once the target is plausibly near the
                    // controller (the first frames after a swap had it 1.5 m away, before the
                    // game had placed anything), follows slowly while the target behaves, and
                    // freezes while it is away so the excursion stays measurable.
                    if (!_targetRestSeeded[side])
                    {
                        if (local.magnitude > 0.5f) continue;
                        _targetRest[side] = local;
                        _targetRestSeeded[side] = true;
                    }
                    var drift = Vector3.Distance(local, _targetRest[side]);
                    if (drift > 1f) { _targetRest[side] = local; drift = 0f; }
                    var away = _targetAway[side] ? drift > 0.05f : drift > 0.10f;   // hysteresis
                    if (!away) _targetRest[side] = Vector3.Lerp(_targetRest[side], local, 0.02f);

                    var fps = _fpsHands[side];
                    var fpsOff = Interop.Alive(fps) ? Vector3.Distance(fps.position, pos) : -1f;
                    var fpsAway = fpsOff >= 0f && (_fpsHandAway[side] ? fpsOff > 0.05f : fpsOff > 0.10f);

                    var hand = _armIk?.HandPosition(side == 0);
                    var where = $"target `{Interop.Name(target)}` @ ({pos.x:0.000}, {pos.y:0.000}, {pos.z:0.000}), " +
                                $"{local.magnitude * 100f:0.#}cm from the controller, {drift * 100f:0.#}cm from where it rests relative to it" +
                                (fpsOff >= 0f ? $", first-person hand {fpsOff * 100f:0.#}cm from it" : "") +
                                (hand.HasValue ? $", our hand bone {Vector3.Distance(hand.Value, pos) * 100f:0.#}cm from it" : "");

                    if (away != _targetAway[side])
                    {
                        _targetAway[side] = away;
                        Core.Log.Msg(away
                            ? $"hand target {name} LEFT its controller: {where}\n    arms: {_armIk.Describe()}"
                            : $"hand target {name} back on its controller: {where}");
                    }
                    if (fpsAway != _fpsHandAway[side])
                    {
                        _fpsHandAway[side] = fpsAway;
                        Core.Log.Msg(fpsAway
                            ? $"first-person hand {name} LEFT the IK target: {where}\n    arms: {_armIk.Describe()}"
                            : $"first-person hand {name} back on the IK target: {where}");
                    }
                    if (jump > 0.10f && Time.unscaledTime >= _nextTargetJumpLogAt)
                    {
                        _nextTargetJumpLogAt = Time.unscaledTime + 1f;
                        Core.Log.Msg($"hand target {name} moved {jump * 100f:0.#}cm relative to the controller in one frame: {where}\n    arms: {_armIk.Describe()}");
                    }
                }
                catch { }
            }
        }

        /// <summary>The first-person arm rig's hand bones, found by name under its skeleton.</summary>
        private void CacheFpsHands(AvatarPlayer player)
        {
            _fpsHands[0] = _fpsHands[1] = null;
            try
            {
                var rigRoot = RootOf(player.Head);
                if (!Interop.Alive(rigRoot)) return;
                var armsModel = rigRoot.Find("FPS-Arms-Model");
                if (!Interop.Alive(armsModel)) return;
                var skeleton = armsModel.Find("root");
                var under = Interop.Alive(skeleton) ? skeleton : armsModel;
                _fpsHands[0] = FindNamed(under, "hand_l");
                _fpsHands[1] = FindNamed(under, "hand_r");
                if (!Interop.Alive(_fpsHands[0]) || !Interop.Alive(_fpsHands[1]))
                    Core.Log.Warning($"    first-person hands: not both found under `{Interop.ScenePath(under)}`");
            }
            catch (Exception e) { Core.Log.Warning($"    first-person hands lookup failed: {e.GetType().Name}: {e.Message}"); }
        }

        private static Transform FindNamed(Transform root, string name)
        {
            if (!Interop.Alive(root)) return null;
            if (root.name == name) return root;
            for (var i = 0; i < root.childCount; i++)
            {
                var found = FindNamed(root.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>
        /// The rotation, in a first-person hand bone's own space, that gives the standard hand
        /// target frame: Z along the fingers, Y out the back of the hand. Measured from the
        /// knuckles, the same construction ArmIK uses on the avatar, so the two agree by
        /// definition. Identity if the knuckles can't be found, and the trims take over.
        /// </summary>
        private static Quaternion FpsHandFrame(Transform hand, bool isLeft, out string note)
        {
            try
            {
                var suffix = isLeft ? "_l" : "_r";
                var middle = FindNamed(hand, "middle_01" + suffix);
                var index = FindNamed(hand, "index_01" + suffix);
                var little = FindNamed(hand, "pinky_01" + suffix);
                if (Interop.Alive(middle) && Interop.Alive(index) && Interop.Alive(little))
                {
                    var fingers = middle.position - hand.position;
                    var across = index.position - little.position;
                    var angle = Vector3.Angle(fingers, across);
                    if (fingers.sqrMagnitude > 1e-10f && across.sqrMagnitude > 1e-10f && angle > 15f && angle < 165f)
                    {
                        var back = isLeft ? Vector3.Cross(fingers, across) : Vector3.Cross(across, fingers);
                        var frame = Quaternion.LookRotation(fingers.normalized, back.normalized);
                        note = $"from the knuckles ({Interop.Name(middle)}, {Interop.Name(little)} to {Interop.Name(index)})";
                        return Quaternion.Inverse(hand.rotation) * frame;
                    }
                }
            }
            catch { }
            note = "could not be measured (no knuckle bones); the bone's own axes are used, set SwapHandTrim* to correct";
            return Quaternion.identity;
        }

        /// <summary>
        /// With their renderers hidden, an Animator on the first-person arms could stop
        /// updating its bones as culled. When those bones are our targets they must keep moving.
        /// </summary>
        private void KeepFpsArmsAnimating()
        {
            try
            {
                var hand = _fpsHands[0];
                if (!Interop.Alive(hand)) return;
                var animators = hand.root.GetComponentsInChildren<Animator>(true);
                var changed = 0;
                foreach (var animator in animators)
                {
                    if (!Interop.Alive(animator)) continue;
                    if (!Interop.ScenePath(animator.transform).Contains("FPS-Arms-Model")) continue;
                    if (animator.cullingMode == AnimatorCullingMode.AlwaysAnimate) continue;
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    changed++;
                }
                if (changed > 0) Core.Log.Msg($"      set {changed} first-person arm Animator(s) to always animate");
            }
            catch (Exception e) { Core.Log.Warning($"      could not adjust the first-person arm Animator: {e.Message}"); }
        }

        private static Transform HandTarget(ref GameObject holder, string name, Transform parent)
        {
            if (!Interop.Alive(parent)) return null;
            holder = new GameObject(name);
            holder.transform.SetParent(parent, false);
            holder.transform.localPosition = Vector3.zero;
            holder.transform.localRotation = Quaternion.identity;
            return holder.transform;
        }

        /// <summary>
        /// Re-apply the configured wrist trims, so F3 retunes a live avatar. The rotation
        /// that turns the target's frame into this rig's hand bone is measured from the bones
        /// in <see cref="ArmIK"/>; this is only whatever the user adds on top.
        /// </summary>
        private void UpdateHandOffsets()
        {
            try
            {
                if (Interop.Alive(_leftHandTarget))
                    _leftHandTarget.transform.localRotation = _handFrameLocal[0] * Quaternion.Euler(
                        ModConfig.SwapHandTrimLeftX.Value,
                        ModConfig.SwapHandTrimLeftY.Value,
                        ModConfig.SwapHandTrimLeftZ.Value);

                if (Interop.Alive(_rightHandTarget))
                    _rightHandTarget.transform.localRotation = _handFrameLocal[1] * Quaternion.Euler(
                        ModConfig.SwapHandTrimRightX.Value,
                        ModConfig.SwapHandTrimRightY.Value,
                        ModConfig.SwapHandTrimRightZ.Value);
            }
            catch { }
        }

        private static void ReportVanillaIk(CharacterPrefab fullBody)
        {
            try
            {
                var enabled = "?";
                try { enabled = fullBody.ikEnabled.ToString(); } catch { }

                var ik = fullBody.ik;
                if (!Interop.Alive(ik)) { Core.Log.Msg($"    vanilla IK: ikEnabled={enabled}, no VRIK on the body"); return; }

                var solver = ik.solver;
                Core.Log.Msg($"    vanilla IK: ikEnabled={enabled}, VRIK.enabled={ik.enabled}, LOD={solver.LOD}, " +
                             $"armWeights L={solver.leftArm.positionWeight:0.##}/{solver.leftArm.rotationWeight:0.##} " +
                             $"R={solver.rightArm.positionWeight:0.##}/{solver.rightArm.rotationWeight:0.##}, " +
                             $"headWeight={solver.spine.positionWeight:0.##}");
            }
            catch (Exception e) { Core.Log.Warning($"    vanilla IK probe failed: {e.Message}"); }
        }

        /// <summary>
        /// Make sure the game's own body is fully solved on this client.
        ///
        /// We copy the vanilla pose, so anything the game skips, we inherit. Your own
        /// third-person body is the obvious candidate for being skipped — normally nobody
        /// looks at it, since you see the first-person arms instead — and FinalIK's `LOD`
        /// reduces or stops solving when raised. A body whose arms aren't being solved leaves
        /// them near the animation's rest pose, which is exactly the A-pose-with-a-little-drift
        /// that showed up in testing.
        /// </summary>
        private void ForceVanillaIk(CharacterPrefab fullBody)
        {
            try
            {
                try { _vanillaIkWas = fullBody.ikEnabled; fullBody.ikEnabled = true; _forcedVanillaIk = true; }
                catch { }

                var ik = fullBody.ik;
                if (!Interop.Alive(ik)) return;
                if (!ik.enabled) ik.enabled = true;

                var solver = ik.solver;
                if (solver == null) return;
                solver.LOD = 0;                     // 0 = solve everything
                solver.leftArm.positionWeight = 1f;
                solver.leftArm.rotationWeight = 1f;
                solver.rightArm.positionWeight = 1f;
                solver.rightArm.rotationWeight = 1f;
                solver.spine.positionWeight = 1f;
                solver.spine.rotationWeight = 1f;
                Core.Log.Msg("    forced the vanilla body to solve fully (LOD 0, arm and head weights 1)");
            }
            catch (Exception e) { Core.Log.Warning($"    could not force vanilla IK: {e.Message}"); }
        }

        private void CacheVanillaMesh(CharacterPrefab fullBody)
        {
            try
            {
                var mesh = fullBody.characterMesh;
                if (!Interop.Alive(mesh)) { Core.Log.Warning("No characterMesh found to hide."); return; }
                _hiddenVanillaMesh = mesh;
                try { _vanillaMeshWasForcedOff = mesh.forceRenderingOff; } catch { _vanillaMeshWasForcedOff = false; }
                try { _vanillaMeshWasEnabled = mesh.enabled; } catch { _vanillaMeshWasEnabled = true; }
                try { _vanillaMeshWasCasting = mesh.shadowCastingMode; }
                catch { _vanillaMeshWasCasting = UnityEngine.Rendering.ShadowCastingMode.On; }
                try { _vanillaMeshWasUpdateOffscreen = mesh.updateWhenOffscreen; } catch { }
                try { _vanillaMeshWasLocalBounds = mesh.localBounds; } catch { }
                Core.Log.Msg($"    vanilla mesh `{Interop.ScenePath(mesh.transform)}` " +
                             $"— SwapHideVanillaMesh = {ModConfig.SwapHideVanillaMesh.Value}" +
                             (ModConfig.SwapHideVanillaMesh.Value ? "" : " (your old body stays visible)"));
            }
            catch (Exception e) { Core.Log.Warning($"Could not read vanilla mesh: {e.Message}"); }
        }

        /// <summary>
        /// Find the first-person arms. Phase 0 established that self view is TWO meshes:
        /// `Model_&lt;nick&gt;/character_mesh` is the third-person body, and
        /// `VR Controller/FPS-Arms-Model` is a second, complete rig that draws the arms you
        /// actually look at in VR. Hiding the body leaves the arms untouched — which is why a
        /// working SwapHideVanillaMesh still left a vanilla gloved hand in view.
        ///
        /// Only the arm meshes are hidden. The weapon-stat and kill-counter panels are parented
        /// into this same rig's forearm bones, so a blanket hide would remove real UI.
        /// </summary>
        private void CacheFpsArms(AvatarPlayer player)
        {
            _fpsArmRenderers.Clear();
            try
            {
                var rigRoot = Avatars.AvatarSwapper.RootOf(player.Head);
                if (!Interop.Alive(rigRoot)) { Core.Log.Warning("    FPS arms: no VR rig root found."); return; }

                var armsModel = rigRoot.Find("FPS-Arms-Model");
                if (!Interop.Alive(armsModel))
                {
                    Core.Log.Warning($"    FPS arms: no `FPS-Arms-Model` under `{Interop.Name(rigRoot)}`.");
                    return;
                }

                // Hide everything under the arms rig EXCEPT a keep-list, rather than hiding a
                // list of known arm meshes. A hide-list only removes what we thought of: a red
                // outline of the fingers appeared around held weapons, because whatever draws
                // it isn't named FPS_Arm and was left behind when the mesh under it went away.
                // Inverting the rule means anything we didn't anticipate is hidden by default,
                // and the keep-list stays short and stable.
                var keep = (ModConfig.SwapFpsArmKeepPrefixes.Value ?? "").Split(',');
                var renderers = armsModel.GetComponentsInChildren<Renderer>(true);
                for (var i = 0; i < renderers.Length; i++)
                {
                    var r = renderers[i];
                    if (!Interop.Alive(r)) continue;
                    var path = Interop.ScenePath(r.transform);
                    var kept = false;
                    foreach (var raw in keep)
                    {
                        var token = raw.Trim();
                        if (token.Length == 0) continue;
                        if (path.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0) { kept = true; break; }
                    }
                    if (!kept) _fpsArmRenderers.Add((r, r.enabled));
                }

                Core.Log.Msg($"    FPS arms: hiding {_fpsArmRenderers.Count} of {renderers.Length} renderer(s) " +
                             $"under `{Interop.ScenePath(armsModel)}`, keeping paths matching " +
                             $"`{ModConfig.SwapFpsArmKeepPrefixes.Value}` — SwapHideFpsArms = {ModConfig.SwapHideFpsArms.Value}");
                foreach (var (r, wasOn) in _fpsArmRenderers)
                    Core.Log.Msg($"      hide {(wasOn ? "(was on) " : "(was off)")} `{Interop.Name(r)}`");
            }
            catch (Exception e) { Core.Log.Warning($"    FPS arms lookup failed: {e.GetType().Name}: {e.Message}"); }
        }

        private void ApplyFpsArmVisibility()
        {
            if (_fpsArmRenderers.Count == 0) return;
            try
            {
                var hide = ModConfig.SwapHideFpsArms.Value;
                foreach (var (r, wasEnabled) in _fpsArmRenderers)
                {
                    if (!Interop.Alive(r)) continue;
                    // Restoring to `wasEnabled` rather than to true matters: several of these
                    // are off already because they belong to cosmetics you don't have equipped.
                    var target = hide ? false : wasEnabled;
                    if (r.enabled != target) r.enabled = target;
                }
            }
            catch { }
        }

        /// <summary>
        /// Locomotion weight, re-applied each frame so F3 can turn it on and off against a live
        /// avatar. It only does anything useful when SwapFollowVanillaRoot is off — procedural
        /// locomotion moves the root to place the feet, and pinning the root every frame leaves
        /// it nothing to move.
        /// </summary>
        private void ApplyLocomotionWeight()
        {
            if (_vrik == null) return;
            try
            {
                var want = Mathf.Clamp01(ModConfig.SwapLocomotionWeight.Value);
                var solver = _vrik.solver;
                if (solver?.locomotion == null) return;
                if (!Mathf.Approximately(solver.locomotion.weight, want)) solver.locomotion.weight = want;
            }
            catch { }
        }

        public static Transform RootOf(Transform t)
        {
            if (!Interop.Alive(t)) return null;
            var guard = 0;
            while (Interop.Alive(t.parent) && guard++ < 64) t = t.parent;
            return t;
        }

        /// <summary>
        /// Stop the vanilla body drawing without telling the game nobody can see it.
        ///
        /// The game asks `Renderer.isVisible` whether a character is on screen.
        /// `CharacterPrefab.IsVisibleToLocalPlayer` returns `characterMesh.isVisible` verbatim
        /// for anyone who isn't the local player, and a false answer costs that peer everything
        /// we want off them: `ik.solver.LOD` goes to 2, which is FinalIK for "don't solve", and
        /// the glancer, the blendshapes and the hands' `BeforeAnimation` are all skipped. The
        /// pose we copy is then pure walking animation — arms at the sides, head fixed forward —
        /// which is exactly what two playtests reported, and full-body tracking dies with it,
        /// since it fills the empty pelvis and leg slots on that same solver.
        ///
        /// Both obvious ways to hide a renderer set `isVisible` false: `enabled = false`, and
        /// `forceRenderingOff = true`, which was tried in the belief that it left the renderer
        /// in culling. It does not — the peer probe measured `isVisible=False` on 162 lines out
        /// of 162, on both machines.
        ///
        /// So: shadows only. The renderer stays enabled and stays in culling, Unity still skins
        /// it, and a shadow caster counts as visible, so the game goes on solving the player
        /// normally. The cost is a leftover human-shaped shadow under the avatar.
        ///
        /// Re-applied every frame rather than set once at swap time. Two reasons: toggling
        /// SwapHideVanillaMesh and pressing F3 mid-swap now actually does something, and if the
        /// game's own visibility handling ever draws the renderer again, this quietly wins.
        /// </summary>
        private void ApplyVanillaMeshVisibility()
        {
            if (!Interop.Alive(_hiddenVanillaMesh)) return;
            try
            {
                var hide = ModConfig.SwapHideVanillaMesh.Value;
                var mode = (ModConfig.SwapHideVanillaMeshMode.Value ?? "ShadowsOnly").Trim();

                var wantEnabled = _vanillaMeshWasEnabled;
                var wantForcedOff = _vanillaMeshWasForcedOff;
                var wantCasting = _vanillaMeshWasCasting;

                if (hide)
                {
                    if (string.Equals(mode, "Disable", StringComparison.OrdinalIgnoreCase)) wantEnabled = false;
                    else if (string.Equals(mode, "ForceOff", StringComparison.OrdinalIgnoreCase)) wantForcedOff = true;
                    else wantCasting = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
                }

                var changed = false;
                if (_hiddenVanillaMesh.enabled != wantEnabled) { _hiddenVanillaMesh.enabled = wantEnabled; changed = true; }
                if (_hiddenVanillaMesh.forceRenderingOff != wantForcedOff) { _hiddenVanillaMesh.forceRenderingOff = wantForcedOff; changed = true; }
                if (_hiddenVanillaMesh.shadowCastingMode != wantCasting) { _hiddenVanillaMesh.shadowCastingMode = wantCasting; changed = true; }
                // Shadows-only got the game solving peers again, but only while their body was
                // on screen — the moment it left the frustum the solve stopped and the avatar
                // froze mid-stride, which reads as "sometimes it animates and sometimes it
                // doesn't". Our avatar is what people actually look at and it is not the same
                // shape or size as the body underneath, so the game's culling of that body is
                // no longer a useful answer to "can anyone see this player". Bounds big enough
                // to survive any frustum keep it solving for as long as the player exists; the
                // cost is one skinned shadow per peer.
                if (hide && ModConfig.SwapKeepVanillaMeshInView.Value)
                {
                    if (_hiddenVanillaMesh.updateWhenOffscreen) { _hiddenVanillaMesh.updateWhenOffscreen = false; changed = true; }
                    if (_hiddenVanillaMesh.localBounds.size.x < 100f)
                    {
                        _hiddenVanillaMesh.localBounds = new Bounds(Vector3.zero, Vector3.one * 1000f);
                        changed = true;
                    }
                }

                if (changed) Core.Log.Msg($"    vanilla mesh {(hide ? $"hidden ({mode})" : "shown")}.");
            }
            catch { }
        }

        public void LateUpdate(float deltaTime)
        {
            if (!IsActive) return;

            // Order matters. MelonLoader's OnLateUpdate runs after every MonoBehaviour
            // LateUpdate, so VRIK has already solved and moved its root by the time we get
            // here. Correcting first means the diagnostics below measure the state that
            // actually renders — reading before the correction produced a "the solver threw
            // the avatar 100 m away" error every single time while the avatar sat, correctly,
            // on the player.
            ApplyAliveState();

            FollowVanillaRoot();
            UpdateHandOffsets();
            ApplyVanillaMeshVisibility();
            if (IsSelf) ApplyFpsArmVisibility();
            ApplyLocomotionWeight();
            if (IsSelf) ApplyHeadChop();
            Leash();

            if (_settledLogAt > 0f && Time.unscaledTime >= _settledLogAt)
            {
                _settledLogAt = 0f;
                LogSettledPlacement();
            }

            GuardHipsOrigin();
            RecaptureWhenSolved();
            WatchForRebind();

            // Pose first: retargeting writes whole-bone rotations, so fingers and spring chains
            // must run after it or they'd be overwritten the moment they moved.
            if (_retarget != null)
            {
                try { _retarget.Apply(); }
                catch (Exception e) { Core.Log.Warning($"Retarget failed, disabling: {e.Message}"); _retarget = null; }
            }

            PeerSpineToHead();
            LogPeerPose();
            WatchPeerHips();

            // Only once the body is posed do we know where its head actually is.
            if (IsSelf) { Calibrate(); AlignToHead(); }

            // Legs after the anchor: the anchor moves the whole model, and the feet have to be
            // solved from wherever the hips ended up to where the game's feet are. Not while
            // ragdolling — the root is following the hips then, and a corpse's legs are the
            // ragdoll's business.
            if (_legIk != null && !_ragdolling && ModConfig.LegIkEnabled.Value)
            {
                try
                {
                    // The lag offset re-bases feet the GAME placed on its lagging display body.
                    // A foot a tracker is driving is solved to a world target and is not on
                    // that body at all: adding the lag to it put the target a stride ahead of
                    // the real foot the moment the stick was pushed (field-tested: legs fine
                    // standing still, 1–2 m ahead of the hips when walking, stretched to the
                    // cap and locked far past it). Scaled by how much the tracker is driving,
                    // so a fading puck hands the offset back smoothly.
                    var lag = FootTargetOffset();
                    var fbt = Fbt.FbtManager.Local;
                    var left = lag * (1f - (fbt?.LocalFootDrive(Fbt.TrackerRole.LeftFoot) ?? 0f));
                    var right = lag * (1f - (fbt?.LocalFootDrive(Fbt.TrackerRole.RightFoot) ?? 0f));
                    _legIk.Apply(left, right, _model.transform.right, _model.transform.forward);
                }
                catch (Exception e) { Core.Log.Warning($"Leg IK failed, disabling: {e.Message}"); _legIk = null; }

                if (IsSelf && Time.unscaledTime >= _nextLegLogAt)
                {
                    _nextLegLogAt = Time.unscaledTime + 1f;
                    if (_legIk != null && _legIk.WorthLogging)
                    {
                        var fbt = Fbt.FbtManager.Local;
                        var drive = fbt == null ? "" :
                            $", tracker drive L {fbt.LocalFootDrive(Fbt.TrackerRole.LeftFoot):0.00} R {fbt.LocalFootDrive(Fbt.TrackerRole.RightFoot):0.00}";
                        Core.Log.Msg($"legs: {_legIk.Describe()} | body lag {FootTargetOffset().magnitude * 100f:0}cm{drive}");
                    }
                }
            }

            // Arms after the body: the retarget writes the whole skeleton, so solving the arms
            // to the hand targets has to come afterwards or it would be overwritten.
            // Not while ragdolling: a corpse whose wrists still strain toward your controllers
            // is the single most alive-looking thing a dead body can do.
            if (_armIk != null && !_ragdolling)
            {
                try { _armIk.Apply(); }
                catch (Exception e) { Core.Log.Warning($"Arm IK failed, disabling: {e.Message}"); _armIk = null; }

                if (IsSelf && _armIk != null) WatchHandTargets();

                // Report the miss once a second while it is large. A hand that lands 30 cm from
                // its target is not a tuning problem, and the numbers say which part is wrong:
                // a big miss with no stretch means the target is unreachable from where the
                // shoulder is, not that the arm is short.
                if (IsSelf && Time.unscaledTime >= _nextArmLogAt)
                {
                    _nextArmLogAt = Time.unscaledTime + 1f;
                    if (_armIk.WorthLogging) Core.Log.Msg($"arms: {_armIk.Describe()}");

                    // The full geometry: once after every swap, so there is always a baseline
                    // in the log, and again whenever a hand misses by more than the arm's
                    // reach explains — which is a solver fault, not a short arm. Rate limited,
                    // because a fault that persists would otherwise print it every second.
                    var anomalous = _armIk.Anomalous;
                    if ((_armDumpsLeft > 0 || anomalous) && Time.unscaledTime >= _nextArmDumpAt)
                    {
                        _nextArmDumpAt = Time.unscaledTime + 10f;
                        if (_armDumpsLeft > 0) _armDumpsLeft--;
                        Core.Log.Msg($"arm geometry{(anomalous ? " — a hand missed by more than its reach explains" : "")}:\n" +
                                     _armIk.DescribeGeometry());
                    }
                }
            }

            // Fingers before springs: neither pose source touches them, but keeping the order fixed
            // means a future pose source can't start fighting the spring chains by accident.
            if (_hands != null)
            {
                try { _hands.Update(deltaTime); }
                catch (Exception e) { Core.Log.Warning($"Hand poser failed, disabling: {e.Message}"); _hands = null; }
            }

            // Face after the body and hands: nothing above it touches blendshapes or eye
            // bones, but a fixed order means a future pose source can't start fighting it.
            if (_face != null)
            {
                var state = IsSelf ? Core.Instance?.FaceState : null;
                var stale = state?.SecondsSinceLastMessage ?? -1;
                var tracking = state != null && stale >= 0 && stale < ModConfig.FaceStaleSeconds.Value;

                // A peer's face comes from their stream. It ages so that when they stop sending
                // — tracking off, or they took the avatar off — their face relaxes rather than
                // freezing in whatever expression arrived last.
                var remote = false;
                if (!IsSelf && _face.RemoteValues != null)
                {
                    _face.RemoteAgeSeconds += deltaTime;
                    remote = _face.RemoteAgeSeconds < ModConfig.FaceStaleSeconds.Value;
                    if (remote) _face.ApplyRemote(deltaTime);
                    else _face.RemoteValues = null;
                }

                // Applying a peer's face used to return from here, which quietly took the
                // spring chains below with it: a peer's tail stopped swinging for exactly as
                // long as they were face-tracking, and started again when they stopped.
                if (!remote)
                {
                    if (tracking) _face.Apply(state, deltaTime);
                    else if (ModConfig.VoiceJawEnabled.Value) _face.ApplyVoiceJaw(ReadVoiceEnergy(), deltaTime);
                    else _face.Relax(deltaTime);
                }
            }

            if (_springs == null || !ModConfig.SpringsEnabled.Value) return;
            try { _springs.Simulate(deltaTime); }
            catch (Exception e) { Core.Log.Warning($"Swap springs failed, disabling: {e.Message}"); _springs = null; }
        }

        /// <summary>
        /// Hand the body back to the game while the player is down, and take it again when they
        /// get up.
        ///
        /// Death is a ragdoll: the game switches physics on over `CharacterPrefab`'s rigidbodies
        /// and colliders, and dissolves the character's renderers. Our model has neither — no
        /// physics bodies and no dissolve-capable materials — so trying to follow a ragdoll
        /// would leave a custom avatar standing rigidly upright while the real corpse falls over
        /// beside it. Showing the vanilla body for those few seconds is both the honest result
        /// and much less work than reproducing a ragdoll.
        /// </summary>
        private void ApplyAliveState()
        {
            if (!Interop.Alive(_player)) return;

            bool alive;
            try { alive = _player.IsAlive; }
            catch { return; }

            if (alive == _wasAlive) return;
            _wasAlive = alive;

            if (!alive)
            {
                // Don't hide it. The game's ragdoll drives the vanilla rig's BONES, and
                // retargeting copies bone rotations — so the custom avatar ragdolls along with
                // it for free. What it can't inherit is the root moving, since the ragdoll
                // travels while `Model_<nick>`'s transform may not, so the root follows the
                // hips while we're down.
                _ragdolling = true;
                Core.Log.Msg($"{(IsSelf ? "You" : $"Actor {ActorNumber}")} died — following the ragdoll.");
            }
            else
            {
                _ragdolling = false;
                // The rig was ragdolled and re-posed while we were away, so the retarget's
                // captured reference is stale — rebuild it against the pose it came back in.
                RebuildPoseSource();
                Core.Log.Msg($"{(IsSelf ? "You" : $"Actor {ActorNumber}")} respawned — pose reference rebuilt.");
            }
        }

        private void SetCustomModelVisible(bool visible)
        {
            if (!Interop.Alive(_model)) return;
            try
            {
                var renderers = _model.GetComponentsInChildren<Renderer>(true);
                for (var i = 0; i < renderers.Length; i++)
                    if (Interop.Alive(renderers[i])) renderers[i].enabled = visible;
            }
            catch (Exception e) { Core.Log.Warning($"Could not toggle custom model visibility: {e.Message}"); }
        }

        /// <summary>
        /// Say that the reference pose we just took is provisional, and take it again shortly.
        ///
        /// A swap and a respawn both catch the body mid-transition — culled and frozen, or
        /// still snapping round to face its spawn direction — and the reference is a delta
        /// origin, so anything wrong with it is wrong for as long as the avatar is worn. One
        /// playtest respawned into a body turned a full 180°, and two F4s to fix it is two more
        /// than it should take.
        /// </summary>
        private void ArmReferenceAgain(string why)
        {
            _retargetNeedsRecapture = true;
            _retargetSettleUntil = Time.unscaledTime + Mathf.Max(0f, ModConfig.RetargetSettleSeconds.Value);
            if (!RigIsSolved())
                Core.Log.Msg($"    pose source: {why} while the game wasn't solving this rig — " +
                             "will take the reference again once it is.");
        }

        /// <summary>Is the game actually solving this body right now? LOD 2 means it is not.</summary>
        private bool RigIsSolved()
        {
            try
            {
                if (!Interop.Alive(_fullBody)) return false;
                var ik = _fullBody.ik;
                if (!Interop.Alive(ik)) return false;
                var solver = ik.solver;
                return solver != null && solver.LOD == 0;
            }
            catch { return false; }
        }

        /// <summary>
        /// If the reference pose was taken against a rig the game had culled, take it again the
        /// first moment the game starts solving. One line in the log either way, because a
        /// silent re-capture is indistinguishable from the tilt it is there to prevent.
        /// </summary>
        private void RecaptureWhenSolved()
        {
            if (!_retargetNeedsRecapture || _retarget == null) return;
            if (Time.unscaledTime < _retargetSettleUntil) return;
            if (!Interop.Alive(_model) || _manifest == null || !RigIsSolved()) return;
            // Solving is not the same as settled. Half a second after a respawn the game
            // reported LOD 0 with the body still turning (one capture stood 66° round from
            // the rig it was copying) and its hips still on their way up from the corpse.
            // The hips say when the body is actually standing.
            if (!HipsSteadyFor(Mathf.Max(0f, ModConfig.RetargetSettleSeconds.Value))) return;

            _retargetNeedsRecapture = false;
            try
            {
                Animator source = null;
                try { source = _player.RemoteAnimator; } catch { }
                if (!Interop.Alive(source)) return;

                var result = _retarget.Recapture(source, _model, _manifest);
                Core.Log.Msg($"    pose source re-captured once the rig had settled — {result}");
                if (_retarget.LinkCount == 0) _retarget = null;
                RebuildLegIk();
            }
            catch (Exception e) { Core.Log.Warning($"Pose re-capture failed: {e.Message}"); }
        }

        /// <summary>
        /// Keep the hips origin honest.
        ///
        /// The retarget carries the game rig's hips translation onto ours as a delta from
        /// where the hips sat when the reference was taken. That origin is the one part of the
        /// reference the alignment at capture cannot fix: rotations are re-aligned whatever
        /// pose the rig was caught in, but a translation measured from the wrong place is
        /// wrong by that much every frame afterwards. A peer's reference is re-taken the frame
        /// they come back to life, and their body is a ragdoll then — hips on the floor, or
        /// ten metres from where the game's root says they are (one leash trip after a respawn
        /// read 9.8 m). The settled re-capture half a second later found the body still being
        /// stood up. Either way the origin was a hip's height too low, so from then on every
        /// frame shoved the avatar that far into the air, with its legs stretched down to the
        /// feet and its arms down to the hands, until they took the avatar off and on again.
        /// From their own side nothing was wrong, because their own reference was taken on
        /// their own machine at a different moment.
        ///
        /// So the origin is learned rather than trusted: the highest the hips sit while the
        /// body is solved, not ragdolled, and standing somewhere a person's hips can be, held
        /// for a moment. Whenever a fresh reference disagrees with that by more than a crouch
        /// would explain, the learned origin replaces it. Until anything has been learned, an
        /// origin that is not a standing body's is not followed at all — a rig at its bind
        /// pose is right to within a crouch, a rig shoved into the air is not right at all.
        /// </summary>
        private void GuardHipsOrigin()
        {
            if (_retarget == null || !ModConfig.RetargetHipsGuard.Value || !Interop.Alive(_fullBody))
            {
                _hipsSteadyStart = -1f;
                return;
            }
            try
            {
                var parent = _retarget.SourceHipsParent;
                var now = _retarget.SourceHipsLocalNow;
                var rest = _retarget.SourceHipsRestLocal;
                if (!Interop.Alive(parent) || !now.HasValue || !rest.HasValue) { _hipsSteadyStart = -1f; return; }

                var root = _fullBody.transform;
                var scale = Mathf.Max(0.05f, root.lossyScale.y);
                var up = root.up;

                // Height above the body's root and distance out from under it, in the body's
                // own metres, so a peer sized up or down is measured the same.
                bool Plausible(Vector3 local, out float height)
                {
                    var fromRoot = parent.TransformPoint(local) - root.position;
                    height = Vector3.Dot(fromRoot, up) / scale;
                    var lateral = Vector3.ProjectOnPlane(fromRoot, up).magnitude / scale;
                    return height >= HipsMinHeight && height <= HipsMaxHeight && lateral <= HipsMaxLateral;
                }

                var ragdolled = false;
                try { ragdolled = _fullBody.isRagdolled; } catch { }
                var nowOk = Plausible(now.Value, out var nowHeight);
                var restOk = Plausible(rest.Value, out _);
                var steady = nowOk && !ragdolled && !_ragdolling && RigIsSolved();
                if (!steady) _hipsSteadyStart = -1f;
                else if (_hipsSteadyStart < 0f) _hipsSteadyStart = Time.unscaledTime;

                // Learn the standing origin: the highest the hips sit while steady. Standing
                // is the highest a body gets — a crouch is lower, a corpse is lower, and the
                // band above rules out the sky — so a crouch caught at capture is corrected
                // the moment they stand up, not baked in.
                if (steady && Time.unscaledTime - _hipsSteadyStart >= HipsSteadySeconds)
                {
                    if (!_hipsGood.HasValue) _hipsGood = now;
                    else
                    {
                        // A tenth of a metre of hysteresis: the hips rise a few centimetres
                        // when a player reaches up or bobs, a crouch or a corpse is short by
                        // thirty or more.
                        Plausible(_hipsGood.Value, out var goodHeight);
                        if (nowHeight > goodHeight + 0.10f) _hipsGood = now;
                    }
                }

                if (_hipsGood.HasValue)
                {
                    _retarget.HipsFollowSuspended = false;
                    var off = parent.TransformVector(rest.Value - _hipsGood.Value).magnitude;
                    if (off > HipsRebaseMetres * scale)
                    {
                        _retarget.SetSourceHipsRest(_hipsGood.Value);
                        _hipsRebases++;
                        if (Time.unscaledTime >= _nextHipsLogAt)
                        {
                            _nextHipsLogAt = Time.unscaledTime + 5f;
                            Plausible(rest.Value, out var restHeight);
                            Core.Log.Msg($"    hips origin re-based on {Who()}: the reference had the hips " +
                                         $"{off:0.00} m from where they stand (at {restHeight:0.00} m above the root" +
                                         $"{(restOk ? "" : " — a ragdoll, or a body still spawning")}); " +
                                         $"the avatar would have sat that far off for as long as it was worn.");
                        }
                    }
                }
                else
                {
                    var suspend = !restOk;
                    if (suspend != _retarget.HipsFollowSuspended && Time.unscaledTime >= _nextHipsLogAt)
                    {
                        _nextHipsLogAt = Time.unscaledTime + 5f;
                        Core.Log.Msg(suspend
                            ? $"    hips origin on {Who()} was taken off a body that isn't standing — holding the hips at rest until it is."
                            : $"    hips origin on {Who()} looks like a standing body again — following it.");
                    }
                    _retarget.HipsFollowSuspended = suspend;
                }
            }
            catch { _hipsSteadyStart = -1f; }
        }

        /// <summary>
        /// Have the game rig's hips been somewhere a standing body's would be, solved and not
        /// ragdolled, for this long? True when the rig has no hips to ask.
        /// </summary>
        private bool HipsSteadyFor(float seconds)
        {
            if (_retarget == null || !ModConfig.RetargetHipsGuard.Value) return true;
            if (!_retarget.SourceHipsLocalNow.HasValue) return true;
            return _hipsSteadyStart >= 0f && Time.unscaledTime - _hipsSteadyStart >= seconds;
        }

        private string Who()
        {
            if (IsSelf) return "you";
            try { if (Interop.Alive(_player)) return $"peer `{_player.PlayerName}`"; } catch { }
            return $"actor {ActorNumber}";
        }

        /// <summary>
        /// The symptom, watched for directly: a peer's avatar whose hips are nowhere near the
        /// game body's hips. Whatever the cause, that is the body in the air with its legs
        /// stretched to the floor, and it deserves a line that says so rather than a
        /// hand-miss figure someone has to interpret. Once every ten seconds while it lasts.
        /// </summary>
        private void WatchPeerHips()
        {
            if (IsSelf || _retarget == null || _ragdolling) return;
            if (Time.unscaledTime < _nextHipsWarnAt) return;
            try
            {
                var ours = _retarget.TargetHipsPosition;
                var theirs = _retarget.SourceHipsPosition;
                if (!ours.HasValue || !theirs.HasValue || !Interop.Alive(_fullBody)) return;
                var scale = Mathf.Max(0.05f, _fullBody.transform.lossyScale.y);
                var apart = Vector3.Distance(ours.Value, theirs.Value);
                if (apart <= 0.5f * scale) return;
                _nextHipsWarnAt = Time.unscaledTime + 10f;
                var up = Vector3.Dot(ours.Value - theirs.Value, _fullBody.transform.up);
                Core.Log.Warning($"{Who()}'s avatar hips are {apart:0.00} m from their game body's " +
                                 $"({up:+0.00;-0.00} m vertically) — hips shift this frame {_retarget.HipsShiftMetres:0.00} m" +
                                 $"{(_retarget.HipsFollowSuspended ? " (follow suspended)" : "")}, " +
                                 $"origin {(_hipsGood.HasValue ? "learned" : "not learned yet")}, re-based {_hipsRebases} time(s), " +
                                 $"ragdolled={SafeRagdolled()} LOD={(RigIsSolved() ? "0" : "not 0")}.");
            }
            catch { }
        }

        private string SafeRagdolled()
        {
            try { return Interop.Alive(_fullBody) ? _fullBody.isRagdolled.ToString() : "?"; } catch { return "?"; }
        }

        /// <summary>
        /// The legs aim at the game's foot bones, and a respawn hands the game a new body with
        /// new bones. Re-pair them whenever the pose source is re-paired, with the old
        /// solver's stretch taken off the bones first so the new one measures a rest leg.
        /// </summary>
        private void RebuildLegIk()
        {
            _legIk?.Release();
            _legIk = null;
            if (_retarget == null || _manifest == null || !Interop.Alive(_model)) return;
            try
            {
                var legs = new LegIK();
                legs.Build(_model, _manifest, _retarget.SourceOf);
                if (legs.HasLegs) _legIk = legs;
            }
            catch (Exception e) { Core.Log.Warning($"Leg IK rebuild failed: {e.Message}"); }
        }

        /// <summary>
        /// Hold a T-pose and the avatar re-binds. Standing with your arms out is the one pose
        /// nobody strikes by accident mid-dungeon, and it is already what full-body tracking
        /// asks for, so it doubles as "start over" without a key.
        /// </summary>
        private void WatchForRebind()
        {
            if (!IsSelf || _ragdolling || !Interop.Alive(_player)) { _tposeSince = 0f; return; }
            var hold = ModConfig.RebindOnTposeSeconds.Value;
            if (hold <= 0f) return;

            bool posed;
            try { posed = Fbt.FbtCalibrator.IsTposed(_player); }
            catch { posed = false; }
            if (!posed) { _tposeSince = 0f; return; }

            var now = Time.unscaledTime;
            if (_tposeSince <= 0f) { _tposeSince = now; return; }
            if (now - _tposeSince < hold) return;
            // Held long enough. Once per hold, and not more than every ten seconds — a
            // re-bind mid-way through the previous one's settle would capture the settling.
            _tposeSince = 0f;
            if (now - _lastRebindAt < 10f) return;
            _lastRebindAt = now;
            try { RebindRequested?.Invoke("T-pose held"); }
            catch (Exception e) { Core.Log.Warning($"Re-bind request failed: {e.Message}"); }
        }

        private void RebuildPoseSource()
        {
            if (_retarget == null || !Interop.Alive(_player) || !Interop.Alive(_model) || _manifest == null) return;
            try
            {
                Animator source = null;
                try { source = _player.RemoteAnimator; } catch { }
                var result = Interop.Alive(source)
                    ? _retarget.Recapture(source, _model, _manifest)
                    : _retarget.Build(_player, _model, _manifest);
                Core.Log.Msg($"    pose source rebuilt after respawn — {result}");
                ArmReferenceAgain("rebuilt after a respawn");
                if (_retarget.LinkCount == 0) _retarget = null;
                RebuildLegIk();
            }
            catch (Exception e) { Core.Log.Warning($"Pose rebuild failed: {e.Message}"); }
        }

        /// <summary>
        /// Keep our model standing exactly where the game already decided the player's body
        /// goes. `Model_&lt;nick&gt;` is the game's own answer to "where are this player's feet,
        /// and which way are they facing" — it is authoritative, it is computed for us every
        /// frame, and copying it means nothing has to converge on anything. VRIK is then left
        /// solving only what it is good at: spine and arms, relative to a root it doesn't own.
        /// </summary>
        /// <summary>
        /// Put the avatar's head where your head actually is.
        ///
        /// The root followed the game's player object, which is the play-space origin, not you.
        /// Physically take a step in your room and your head moves while that object doesn't, so
        /// the avatar stayed put: feet a foot behind where you were standing, shoulders with it,
        /// and the hand targets — which DO follow your controllers — up to a metre from the
        /// shoulders that were supposed to reach them. The measured numbers were unmistakable
        /// once we looked: `needed` up to 138 cm for an arm 62 cm long. That is not an arm that
        /// is too short, it is a body in the wrong place.
        ///
        /// Vanilla never had this problem because nobody can see their own body in this game —
        /// the third-person body is a networking display object, and its IK is switched off
        /// locally. It has never had to be where you are.
        ///
        /// So we anchor the head instead of the feet. Your eyes are at your head, the shoulders
        /// hang off it in the right place, and the arms have a fighting chance of reaching your
        /// hands. <see cref="Calibrate"/> makes the feet land on the floor while it does.
        /// </summary>
        private void AlignToHead()
        {
            // A ragdoll is thrown around by physics and follows its hips; dragging it back under
            // a head that is no longer attached to it would look exactly as bad as it sounds.
            if (_ragdolling || !Interop.Alive(_headBone) || !Interop.Alive(_model) || !Interop.Alive(_player)) return;

            try
            {
                // FBT escape hatch: with a hip tracker driving the game rig's pelvis, anchoring
                // by the head can fight the hip drive (risk #2 in the FBT plan). This re-anchors
                // by the hips instead — the same placement the ragdoll branch already uses — so
                // the fix can be tried from the settings file if the fight ever shows up.
                if (ModConfig.FbtAnchorHips.Value && _retarget != null)
                {
                    var hips = _retarget.SourceHipsPosition;
                    var offset = _retarget.TargetHipsOffset;
                    if (hips.HasValue && offset.HasValue)
                    {
                        _model.transform.position = hips.Value - offset.Value;
                        return;
                    }
                }

                var head = _player.IKTargetHead;
                if (!Interop.Alive(head)) return;
                _model.transform.position += head.position - _headBone.position;
            }
            catch { }
        }

        /// <summary>
        /// Size a PEER'S avatar the way they are sizing it themselves. Their number, not a
        /// second guess of ours: they measured it against their own head, and everyone in the
        /// room needs to agree on how tall they are.
        /// </summary>
        public void ApplyRemoteHeight(float heightScale)
        {
            if (IsSelf) return;
            if (!float.IsFinite(heightScale) || heightScale < 0.05f || heightScale > 10f) return;
            if (Mathf.Abs(heightScale - _heightScale) < 0.005f) return;

            _heightScale = heightScale;
            if (!Interop.Alive(_model)) return;
            try
            {
                _model.transform.localScale = _modelBaseScale * _heightScale;
                Core.Log.Msg($"    peer height: `{AvatarName}` scaled x{_heightScale:0.000} to match its wearer.");
            }
            catch { }
        }

        /// <summary>
        /// Size a PEER'S game body the way they have sized themselves (their PlayerSize). The
        /// game networks their head and hands at their real, scaled positions and their VRIK
        /// on our client reaches for those — with a full-size body it would crouch to a low
        /// head, and our copy of their avatar would copy the crouch. Never for ourselves:
        /// PlayerSize owns our body.
        /// </summary>
        public void ApplyRemoteBodySize(float size)
        {
            if (IsSelf) return;
            if (!float.IsFinite(size) || size < PlayerSize.Minimum * 0.5f || size > PlayerSize.Maximum * 2f) return;
            if (Mathf.Abs(size - _remoteBodySize) < 0.0005f) return;

            _remoteBodySize = size;
            if (!Interop.Alive(_fullBody)) return;
            try
            {
                _fullBody.transform.localScale = _fullBodyRestScale * size;
                Core.Log.Msg($"    peer size: `{SafeName(_player)}`'s body scaled x{size:0.000} to match them.");
            }
            catch { }
        }

        /// <summary>
        /// Scale the avatar so that, with its head at your head, its feet are on the floor.
        ///
        /// This is the same silhouette the game's own body has. That body is 1.5 m to the head
        /// and stands with its feet on the floor whatever your height; VRIK stretches its spine
        /// up to your head target. Ours is anchored by the head instead, so one uniform scale
        /// does the same job: measure how far your head is above the play-space floor, and
        /// divide by how far this avatar's head is above its own root.
        ///
        /// That second number is NOT measured in game. The exporter measured it, in the rest
        /// pose, and chose `suggestedScale` to put it at the game's 1.5 m; the manifest carries
        /// both. Measuring it live caught one avatar mid-settle — head 0.93 m above the root
        /// while the solver was still drifting a metre a frame — and fitted it at x1.785
        /// instead of x1.10, "once", so the wearer spawned enormous until an F4 re-measured
        /// on a settled rig. The live number is still logged, as a check on the manifest.
        ///
        /// Measured ONCE, a second after the swap, and never again while the avatar is worn.
        /// Not on crouching (it would shrink you), not on standing taller, not on anything.
        /// Every attempt at re-fitting ended with people unable to get back to the size they
        /// started at. Put the avatar on standing and it fits; if it was put on sitting down,
        /// F4 twice, or hold a T-pose.
        ///
        /// Your size (PlayerSize) needs no arithmetic here: it is already on the play space
        /// by the time this runs, so the head is measured where it now is, in world metres,
        /// and the avatar comes out at your chosen size. A size change re-wears the avatar,
        /// which brings it back through here.
        /// </summary>
        private void Calibrate()
        {
            if (_calibrateAt <= 0f || Time.unscaledTime < _calibrateAt) return;
            _calibrateAt = 0f;

            if (!Interop.Alive(_headBone) || !Interop.Alive(_model) || !Interop.Alive(_player)) return;

            try
            {
                var head = _player.IKTargetHead;
                if (!Interop.Alive(head)) return;

                var playerHeight = head.position.y - _player.transform.position.y;
                if (playerHeight < 0.2f)
                {
                    Core.Log.Warning($"    height calibration skipped: you {playerHeight:0.00} m to the eyes — " +
                                     "that isn't a person.");
                    return;
                }

                // The model sits at the scene root at `suggestedScale * _heightScale`, so its
                // head is `baseHeight * _heightScale` above its root in world metres once the
                // rig is standing. Your head is in world metres too, sized play space and all,
                // so the ratio comes out at your chosen size without any further arithmetic.
                var baseHeight = ManifestHeadHeight();
                var raw = playerHeight / baseHeight;

                // What the rig is doing right now, for the log only. It disagrees with the
                // manifest when the pose hasn't settled, or when the manifest was hand-edited.
                var measured = (_headBone.position.y - _model.transform.position.y) / Mathf.Max(0.01f, _heightScale);
                var check = Mathf.Abs(measured - baseHeight) > baseHeight * 0.15f
                    ? $" (measured {measured:0.00} m right now — the rig hasn't settled, or the manifest is off; " +
                      $"measuring would have given x{playerHeight / Mathf.Max(0.05f, measured):0.00})"
                    : $" (measured {measured:0.00} m, agrees)";
                var previous = _heightScale;
                var size = PlayerSize.Applied;
                // A nonsense filter, not a size limit.
                _heightScale = Mathf.Clamp(raw, 0.5f, 2f);
                _sizeAtFit = size;
                _model.transform.localScale = _modelBaseScale * _heightScale;

                var note = Mathf.Abs(raw - _heightScale) > 0.001f ? $" (clamped from x{raw:0.00})" : "";
                var sized = Mathf.Abs(size - 1f) > 0.0005f ? $" (you are x{size:0.00}: {playerHeight / size:0.00} m at vanilla size)" : "";
                Core.Log.Msg($"    height: you {playerHeight:0.00} m to the eyes{sized}, avatar {baseHeight:0.00} m " +
                             $"by its manifest{check} — scaling avatar x{_heightScale:0.000}{note}, once");
                PlayerSize.LogChainNow("at the avatar fit");

                if (Mathf.Abs(previous - _heightScale) > 0.002f)
                {
                    try { HeightScaleChanged?.Invoke(); }
                    catch (Exception e) { Core.Log.Warning($"Height change handler threw: {e.Message}"); }
                }
            }
            catch { }
        }

        /// <summary>
        /// How far this avatar's head is above its root at `suggestedScale`, in metres, from
        /// the exporter's rest-pose measurement. That product is the game's 1.5 m for any
        /// manifest the exporter wrote; it stays honest if someone edits `suggestedScale` by
        /// hand, because the raw head height is recorded next to it.
        /// </summary>
        private float ManifestHeadHeight()
        {
            var rig = _manifest?.rig;
            if (rig != null && rig.headHeight > 0.05f && rig.suggestedScale > 0.0001f)
                return rig.headHeight * rig.suggestedScale;
            if (rig != null && rig.gameHeadHeight > 0.05f) return rig.gameHeadHeight;
            return 1.5f;
        }

        /// <summary>
        /// How far the game's display body is behind where we have put ours. For yourself the
        /// root follows the unsmoothed player object while `Model_&lt;nick&gt;` is smoothed for
        /// networking, so while you move with the stick its feet trail where you were; the
        /// feet are re-based by that difference so the legs don't stretch backwards after you.
        /// Horizontal only — the vertical truth is the feet on the floor. A peer's root IS the
        /// display body, so theirs is zero.
        /// </summary>
        private Vector3 FootTargetOffset()
        {
            if (!IsSelf || !Interop.Alive(_player) || !Interop.Alive(_fullBody)) return Vector3.zero;
            try
            {
                var d = _player.transform.position - _fullBody.transform.position;
                d.y = 0f;
                return d;
            }
            catch { return Vector3.zero; }
        }

        private void FollowVanillaRoot()
        {
            if (!Interop.Alive(_fullBody) || !Interop.Alive(_model)) return;
            // SwapFollowVanillaRoot only ever existed to hand the root to VRIK's procedural
            // locomotion for one experiment. When we're retargeting, the root MUST follow the
            // game's body — nothing else positions it — so the setting doesn't get a say.
            if (_retarget == null && !ModConfig.SwapFollowVanillaRoot.Value) return;
            try
            {
                // Follow the PLAYER object for ourselves, not the model.
                //
                // `Model_<nick>` is the display body and is smoothed for networking, so while
                // you move with the stick it lags behind where you actually are. Our root
                // followed it, so the shoulders lagged, the arms couldn't reach, and a held
                // weapon — parented to the unsmoothed controller — slid out of the hand until
                // you stopped. Aiming the arms at the controllers didn't fix that, because the
                // lag was never in the hand target; it was in the body underneath it.
                // For a remote player the smoothed model IS the authoritative position, so they
                // keep following it.
                var source = IsSelf && Interop.Alive(_player) ? _player.transform : _fullBody.transform;
                var target = source.position;
                if (_ragdolling && _retarget != null)
                {
                    // A ragdoll's hips travel; the object we normally follow doesn't.
                    var hips = _retarget.SourceHipsPosition;
                    if (hips.HasValue) target = hips.Value - (_retarget.TargetHipsOffset ?? Vector3.zero);
                }
                // How far VRIK moved the root before we took it back. Locomotion is off, so
                // this should be small; a large steady value means something inside the solver
                // still wants to own the root and is worth knowing about.
                _lastSolverDrift = Vector3.Distance(_model.transform.position, target);
                _model.transform.SetPositionAndRotation(target, source.rotation);
            }
            catch { }
        }

        /// <summary>
        /// First-person head handling, the same trick VRChat's Head Chop uses: shrink the head
        /// bone to nothing so its geometry collapses out of view, instead of trying to hide it.
        /// The head is part of one merged SkinnedMeshRenderer, so there is no renderer or layer
        /// to switch off — per-bone scale is the only lever that reaches it.
        ///
        /// Local and cosmetic by construction: bone scale on OUR model, which no peer ever
        /// sees. When avatars are networked this must stay a local-only step, or everyone will
        /// watch you walk around headless.
        /// </summary>
        private void ApplyHeadChop()
        {
            try
            {
                // Rebuild only when the spec changes, so F3 retunes without doing a transform
                // search every frame.
                var spec = $"{ModConfig.SelfHeadShrinkBones.Value}|{ModConfig.SelfHeadKeepBones.Value}";
                if (spec != _headChopSpec) { _headChopSpec = spec; RebuildHeadChopLists(); }

                // Dead, and watching your own body from outside it. The head is hidden so it
                // doesn't fill your view from the inside; out here that reasoning is gone and
                // all it leaves is a headless corpse.
                if (!ModConfig.SelfHideHead.Value || _ragdolling)
                {
                    foreach (var t in _shrinkBones) if (Interop.Alive(t)) t.localScale = Vector3.one;
                    foreach (var t in _keepBones) if (Interop.Alive(t)) t.localScale = Vector3.one;
                    return;
                }

                var scale = Mathf.Clamp(ModConfig.SelfHeadBoneScale.Value, 0.00001f, 1f);
                foreach (var t in _shrinkBones)
                    if (Interop.Alive(t)) t.localScale = Vector3.one * scale;

                // Cancel the parent's shrink so this bone renders at its normal size — scale
                // compounds down the hierarchy, so the inverse restores it exactly.
                var inverse = 1f / scale;
                foreach (var t in _keepBones)
                    if (Interop.Alive(t)) t.localScale = Vector3.one * inverse;
            }
            catch { /* never throw in LateUpdate */ }
        }

        private void RebuildHeadChopLists()
        {
            _shrinkBones.Clear();
            _keepBones.Clear();
            if (!Interop.Alive(_model)) return;

            Resolve(ModConfig.SelfHeadShrinkBones.Value, _shrinkBones);
            Resolve(ModConfig.SelfHeadKeepBones.Value, _keepBones);

            Core.Log.Msg($"    head chop: shrinking {_shrinkBones.Count} bone(s), keeping {_keepBones.Count}");

            void Resolve(string spec, List<Transform> into)
            {
                if (string.IsNullOrWhiteSpace(spec)) return;
                foreach (var raw in spec.Split(','))
                {
                    var token = raw.Trim();
                    if (token.Length == 0) continue;

                    var t = ResolveBone(token);
                    if (Interop.Alive(t)) into.Add(t);
                    else Core.Log.Warning($"    head chop: no bone matching `{token}` on this avatar.");
                }
            }
        }

        /// <summary>A humanoid bone name from the manifest map, a transform path, or a plain name.</summary>
        private Transform ResolveBone(string token)
        {
            var map = _manifest?.rig?.humanoidBones;
            if (map != null && map.TryGetValue(token, out var path) && !string.IsNullOrEmpty(path))
            {
                var byMap = _model.transform.Find(path);
                if (Interop.Alive(byMap)) return byMap;
            }

            var byPath = _model.transform.Find(token);
            if (Interop.Alive(byPath)) return byPath;

            try
            {
                var all = _model.GetComponentsInChildren<Transform>(true);
                for (var i = 0; i < all.Length; i++)
                    if (Interop.Alive(all[i]) && string.Equals(all[i].name, token, StringComparison.OrdinalIgnoreCase))
                        return all[i];
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Put a peer's head where their head actually is.
        ///
        /// The game's display body is 1.5 m tall for everyone. On a remote client it puts its
        /// head bone directly under the player's real head target in X and Z, but pinned at
        /// 1.48 m — every dump of every peer agrees to the millimetre — so it reproduces a
        /// forward lean of the head with a spine that is shorter than the player's, and the
        /// torso comes out steeper than the real one: about half again as steep for a 1.77 m
        /// player, a third again for 1.62 m. Copying that body's rotations copies the
        /// exaggeration, and the tallest player in the room leans the most.
        ///
        /// The head target is networked, steady, and where their head is. So after the copy,
        /// the torso is turned about the first spine bone until the avatar's head bone lies on
        /// the line from that bone to the target. The head keeps its own world rotation (their
        /// real gaze, which the game does network), the legs are untouched, and the arms are
        /// solved afterwards from wherever the shoulders ended up. Only the direction is
        /// corrected, never the distance: the height fit already puts the head at the right
        /// height on a standing body.
        ///
        /// Clamped, because a target far from the body is a ragdoll or a body still spawning
        /// and not a lean. Not for yourself: your own model is anchored by the head outright.
        /// </summary>
        private void PeerSpineToHead()
        {
            _leanPuppet = _leanReal = _leanApplied = _leanWanted = -1f;
            if (IsSelf || _retarget == null || _ragdolling || !ModConfig.PeerSpineToHead.Value) return;
            if (!Interop.Alive(_headBone) || !Interop.Alive(_player) || !Interop.Alive(_fullBody)) return;
            try
            {
                var target = _player.IKTargetHead;
                var pivot = _retarget.TargetOf(HumanBodyBones.Spine);
                if (!Interop.Alive(target) || !Interop.Alive(pivot)) return;

                var up = _fullBody.transform.up;
                var theirHips = _retarget.SourceHipsPosition;
                var theirHead = _retarget.SourceOf(HumanBodyBones.Head);
                if (theirHips.HasValue && Interop.Alive(theirHead))
                {
                    _leanPuppet = Vector3.Angle(theirHead.position - theirHips.Value, up);
                    _leanReal = Vector3.Angle(target.position - theirHips.Value, up);
                }

                var from = _headBone.position - pivot.position;
                var to = target.position - pivot.position;
                // A head that is not above the spine, or a target that is nowhere near the body,
                // is not something to turn the torso after.
                if (from.magnitude < 0.1f || to.magnitude < 0.1f || to.magnitude > 2.5f * from.magnitude) return;

                var wanted = Vector3.Angle(from, to);
                _leanWanted = wanted;
                if (wanted < 0.05f) { _leanApplied = 0f; return; }
                var applied = Mathf.Min(wanted, Mathf.Max(0f, ModConfig.PeerSpineToHeadMaxDegrees.Value));
                var axis = Vector3.Cross(from, to);
                if (axis.sqrMagnitude < 1e-10f) return;

                var gaze = _headBone.rotation;
                pivot.rotation = Quaternion.AngleAxis(applied, axis.normalized) * pivot.rotation;
                _headBone.rotation = gaze;
                _leanApplied = applied;
            }
            catch { }
        }

        /// <summary>
        /// One line a second saying why a peer's avatar is or isn't moving.
        ///
        /// Two playtests have now ended with "the peer slides around in an A-pose", and the
        /// guesses that fit that description are several: the game may not be solving their rig
        /// at all, we may be reading it before it is solved, or we may be posing our avatar and
        /// having it overwritten. Those look identical from across a room and take a whole
        /// session each to rule out, so the mod measures all three at once instead.
        ///
        /// The peer's own hands are the honest test of whether the game solved their rig: a
        /// solved arm puts the wrist within a few centimetres of the controller target it is
        /// following, and an unsolved one leaves it wherever the walking animation put it.
        /// </summary>
        private void LogPeerPose()
        {
            if (IsSelf || _retarget == null) return;
            var every = ModConfig.DiagPeerPoseSeconds.Value;
            if (every <= 0f || Time.unscaledTime < _nextPeerPoseLogAt) return;
            _nextPeerPoseLogAt = Time.unscaledTime + every;

            try
            {
                var who = "?";
                try { if (Interop.Alive(_player)) who = _player.PlayerName; } catch { }

                var reach = "hands ?";
                var lh = _retarget.SourceOf(HumanBodyBones.LeftHand);
                var rh = _retarget.SourceOf(HumanBodyBones.RightHand);
                if (Interop.Alive(_player) && Interop.Alive(lh) && Interop.Alive(rh) &&
                    Interop.Alive(_player.IKTargetLeftHand) && Interop.Alive(_player.IKTargetRightHand))
                    reach = $"hands off target L {Vector3.Distance(lh.position, _player.IKTargetLeftHand.position):0.00} m" +
                            $" R {Vector3.Distance(rh.position, _player.IKTargetRightHand.position):0.00} m";

                var travel = "travel ?";
                if (_retarget.TryTravel(HumanBodyBones.LeftUpperArm, out var srcDeg))
                    travel = $"upper arm bent {srcDeg:0.#}° from capture on their rig";
                var stolen = _retarget.OverwrittenDegrees;

                // Renderer.isVisible is the flag the game's own LOD reads. If hiding the body
                // still sets it false, the game stops solving and we have nothing to copy.
                var vis = "mesh ?";
                if (Interop.Alive(_hiddenVanillaMesh))
                    vis = $"mesh enabled={_hiddenVanillaMesh.enabled}" +
                          $" forcedOff={_hiddenVanillaMesh.forceRenderingOff}" +
                          $" shadows={_hiddenVanillaMesh.shadowCastingMode}" +
                          $" isVisible={_hiddenVanillaMesh.isVisible}";

                var ik = "ik ?";
                if (Interop.Alive(_fullBody))
                {
                    var k = _fullBody.ik;
                    var solver = Interop.Alive(k) ? k.solver : null;
                    if (solver != null)
                        ik = $"ikEnabled={_fullBody.ikEnabled} VRIK.enabled={k.enabled} LOD={solver.LOD}" +
                             $" armW L={solver.leftArm.positionWeight:0.##} R={solver.rightArm.positionWeight:0.##}" +
                             $" headW={solver.spine.positionWeight:0.##}";
                }

                var hips = $"hips shift {_retarget.HipsShiftMetres * 100f:0} cm" +
                           (_retarget.HipsFollowSuspended ? " (suspended)" : "") +
                           (_hipsGood.HasValue ? "" : ", origin unlearned");
                var ours = _retarget.TargetHipsPosition;
                var theirs = _retarget.SourceHipsPosition;
                if (ours.HasValue && theirs.HasValue)
                    hips += $", ours {Vector3.Distance(ours.Value, theirs.Value) * 100f:0} cm from theirs";

                var lean = "lean ?";
                if (_leanPuppet >= 0f && _leanReal >= 0f)
                    lean = $"lean: their body {_leanPuppet:0}°, really {_leanReal:0}°" +
                           (_leanWanted >= 0f
                               ? $", torso turned {_leanApplied:0.#}° of {_leanWanted:0.#}° toward their head" +
                                 (_leanApplied + 0.05f < _leanWanted ? " (clamped)" : "")
                               : ", torso not turned");
                else if (!ModConfig.PeerSpineToHead.Value) lean = "lean: copied from their body";

                Core.Log.Msg($"peer `{who}`: {reach} | {travel}" +
                             (stolen >= 0f ? $", {stolen:0.#}° taken back off us between frames" : "") +
                             $" | {hips} | {lean} | {vis} | {ik}");
            }
            catch (Exception e) { Core.Log.Warning($"peer pose probe failed: {e.GetType().Name}: {e.Message}"); }
        }

        /// <summary>
        /// Belt and braces against runaway root motion. The parenting fix should make this
        /// dead code, but a solver that walks the avatar off the map is bad enough — and quiet
        /// enough — that it's worth a hard backstop rather than trusting one fix.
        /// </summary>
        private void Leash()
        {
            if (!Interop.Alive(_player)) return;
            try
            {
                var head = _player.IKTargetHead;
                if (!Interop.Alive(head)) return;

                var pos = _model.transform.position;
                if (float.IsNaN(pos.x) || float.IsNaN(pos.y) || float.IsNaN(pos.z))
                {
                    _model.transform.position = head.position;
                    Core.Log.Error("*** Avatar root went NaN — snapped back to the head target.");
                    return;
                }

                var limit = Mathf.Max(2f, ModConfig.SwapLeashMetres.Value);
                var distance = Vector3.Distance(head.position, pos);
                if (distance <= limit) return;

                _model.transform.position = new Vector3(head.position.x, _model.transform.position.y, head.position.z);
                _leashTrips++;

                // Repeated tripping means the solver is throwing the avatar away faster than we
                // can drag it back, and the result on screen is a body strobing between your
                // feet and the far side of the map — which reads as "the avatar didn't appear"
                // rather than as a setting being wrong. Shut the cause off and say so.
                if (_leashTrips >= 5 && ModConfig.SwapLocomotionWeight.Value > 0f)
                {
                    ModConfig.SwapLocomotionWeight.Value = 0f;
                    ModConfig.SwapFollowVanillaRoot.Value = true;
                    _leashTrips = 0;
                    Core.Log.Error("*** Procedural locomotion is throwing the avatar across the map " +
                                   "(5 leash trips). Turned SwapLocomotionWeight back to 0 and " +
                                   "SwapFollowVanillaRoot back to true for this session. Your avatar " +
                                   "should reappear where it belongs; legs will hold their rest pose.");
                    return;
                }

                if (Time.unscaledTime >= _nextLeashLogAt)
                {
                    _nextLeashLogAt = Time.unscaledTime + 2f;
                    Core.Log.Warning($"Avatar drifted {distance:0.#} m from you (limit {limit:0.#}) — snapped back. " +
                                     "If this repeats, the IK solver is fighting something for control of the root.");
                }
            }
            catch { /* never let the leash throw in LateUpdate */ }
        }

        public void Revert(string why)
        {
            // Un-hide first: if anything below throws, the player still has a body.
            try
            {
                if (Interop.Alive(_hiddenVanillaMesh))
                {
                    _hiddenVanillaMesh.enabled = _vanillaMeshWasEnabled;
                    _hiddenVanillaMesh.forceRenderingOff = _vanillaMeshWasForcedOff;
                    _hiddenVanillaMesh.shadowCastingMode = _vanillaMeshWasCasting;
                    _hiddenVanillaMesh.localBounds = _vanillaMeshWasLocalBounds;
                    _hiddenVanillaMesh.updateWhenOffscreen = _vanillaMeshWasUpdateOffscreen;
                }
            }
            catch (Exception e) { Core.Log.Warning($"Could not restore the vanilla mesh: {e.Message}"); }
            if (_forcedVanillaIk && Interop.Alive(_fullBody))
            {
                try { _fullBody.ikEnabled = _vanillaIkWas; } catch { }
            }
            _forcedVanillaIk = false;

            foreach (var (r, wasEnabled) in _fpsArmRenderers)
                if (Interop.Alive(r)) { try { r.enabled = wasEnabled; } catch { } }
            _fpsArmRenderers.Clear();
            _hiddenVanillaMesh = null;

            if (Interop.Alive(_model))
            {
                try { UnityEngine.Object.Destroy(_model); } catch { }
                Core.Log.Msg($"Avatar swap reverted ({why}).");
            }

            foreach (var holder in new[] { _leftHandTarget, _rightHandTarget })
                if (Interop.Alive(holder)) { try { UnityEngine.Object.Destroy(holder); } catch { } }
            _leftHandTarget = null;
            _rightHandTarget = null;

            // A peer's body we sized goes back to what the game had. (Ours is PlayerSize's.)
            if (!IsSelf && Mathf.Abs(_remoteBodySize - 1f) > 0.0005f && Interop.Alive(_fullBody))
            {
                try { _fullBody.transform.localScale = _fullBodyRestScale; } catch { }
            }
            _remoteBodySize = 1f;
            _fullBodyRestScale = Vector3.one;
            _sizeAtFit = 1f;

            _shrinkBones.Clear();
            _keepBones.Clear();
            _headChopSpec = null;
            _headBone = null;
            _modelBaseScale = Vector3.one;
            _heightScale = 1f;
            _calibrateAt = 0f;
            _armDumpsLeft = 0;
            _leashTrips = 0;
            _wasAlive = true;
            _ragdolling = false;
            _hipsGood = null;
            _hipsSteadyStart = -1f;
            _hipsRebases = 0;
            _nextHipsLogAt = 0f;
            _nextHipsWarnAt = 0f;
            _manifest = null;
            _model = null;
            _fullBody = null;
            _vrik = null;
            _springs = null;
            _hands = null;
            _retarget = null;
            _armIk = null;
            _legIk = null;
            _face = null;
            _player = null;
            // _lastRebindAt deliberately survives a revert: a re-bind IS a revert, and the
            // ten-second gap has to hold across it or a T-pose still being held re-fires.
            _tposeSince = 0f;
            _settledLogAt = 0f;
            AvatarName = null;

            _bundle?.Release();
            _bundle = null;
        }

        /// <summary>
        /// Everything needed to work out why a model that loaded correctly isn't on screen:
        /// where it is, how big, what layer, whether the camera's culling mask includes that
        /// layer, and whether FinalIK accepted the rig or quietly disabled itself.
        /// </summary>
        private void LogPlacement(AvatarPlayer player)
        {
            ReconLog.Line();
            ReconLog.Line("### Swap placement diagnostics");

            ReconLog.Try("transforms", () =>
            {
                ReconLog.KeyValue("_model", $"@ {Interop.Vec(_model.transform.position)} " +
                                            $"lossyScale {Interop.Vec(_model.transform.lossyScale)} " +
                                            $"active {_model.activeInHierarchy} layer {_model.layer}");
                var head = player.IKTargetHead;
                if (Interop.Alive(head))
                    ReconLog.KeyValue("IKTargetHead", $"{Interop.Vec(head.position)} — model is " +
                                                      $"{Vector3.Distance(head.position, _model.transform.position):0.##} m away");
            });

            ReconLog.Try("VRIK state", () =>
            {
                if (_vrik == null) { ReconLog.KeyValue("VRIK", "not added (SwapUseVrik=false)"); return; }
                // FinalIK disables itself when a rig fails validation, and says so only in a
                // Unity log line we may not see. Read the flag directly.
                ReconLog.KeyValue("VRIK.enabled", _vrik.enabled);
                ReconLog.KeyValue("references.isFilled", _vrik.references != null && _vrik.references.isFilled);
                ReconLog.KeyValue("solver.initiated", _vrik.solver != null && _vrik.solver.initiated);
            });

            ReconLog.Try("renderers", () =>
            {
                var renderers = _model.GetComponentsInChildren<Renderer>(true);
                var visible = 0;
                for (var i = 0; i < renderers.Length; i++)
                {
                    var r = renderers[i];
                    if (!Interop.Alive(r)) continue;
                    var on = r.enabled && r.gameObject.activeInHierarchy;
                    if (on) visible++;
                    ReconLog.Line($"- {(on ? "ON " : "off")} `{Interop.Name(r)}` layer {r.gameObject.layer} " +
                                  $"bounds centre {Interop.Vec(r.bounds.center)} size {Interop.Vec(r.bounds.size)}");
                }
                ReconLog.KeyValue("renderers", $"{renderers.Length} ({visible} enabled)");
                Core.Log.Msg($"    placement: model @ {Interop.Vec(_model.transform.position)}, " +
                             $"layer {_model.layer}, {visible}/{renderers.Length} renderers on");
            });

            // A layer excluded from the camera's culling mask renders nothing while every
            // other flag still reads healthy. Enumerate via FindObjectsOfType, which this
            // codebase already uses safely — `Camera.allCameras` took the game down natively,
            // and a native crash is not something the try/catch above can save us from.
            ReconLog.Try("cameras", () =>
            {
                var cameras = UnityEngine.Object.FindObjectsOfType<Camera>();
                if (cameras == null) { ReconLog.Line("- _no cameras found_"); return; }
                var modelLayer = _model.layer;
                for (var i = 0; i < cameras.Length; i++)
                {
                    try
                    {
                        var cam = cameras[i];
                        if (!Interop.Alive(cam)) continue;
                        var mask = cam.cullingMask;
                        var sees = (mask & (1 << modelLayer)) != 0;
                        ReconLog.Line($"- camera `{Interop.Name(cam)}` mask 0x{mask:X8} — " +
                                      $"model layer {modelLayer} {(sees ? "IS" : "is NOT")} rendered");
                        if (!sees)
                            Core.Log.Error($"*** Camera `{Interop.Name(cam)}` does NOT render layer {modelLayer} — " +
                                           "that is why you can't see it.");
                    }
                    catch (Exception e) { ReconLog.Line($"- camera {i}: unreadable ({e.GetType().Name})"); }
                }
            });
        }

        /// <summary>
        /// Re-log placement a second after the swap. The first pass runs before VRIK has
        /// solved even once, so it cannot show what the solver does to the model — and "correct
        /// at spawn, gone a frame later" is precisely the shape of the bug we're chasing.
        /// </summary>
        private void LogSettledPlacement()
        {
            if (!IsActive) return;
            ReconLog.Try("settled placement", () =>
            {
                var pos = _model.transform.position;
                var scale = _model.transform.lossyScale;
                Core.Log.Msg($"    solver drift per frame (corrected): {_lastSolverDrift:0.##} m");
                Core.Log.Msg($"    settled (1s after swap): model @ {Interop.Vec(pos)} " +
                             $"lossyScale {Interop.Vec(scale)} active {_model.activeInHierarchy}" +
                             (_vrik != null ? $" VRIK.enabled {_vrik.enabled}" : ""));

                var bad = float.IsNaN(pos.x) || float.IsNaN(pos.y) || float.IsNaN(pos.z);
                if (bad)
                    Core.Log.Error("*** Model position is NaN — the IK solve has blown up. " +
                                   "Set SwapUseVrik=false to confirm.");
                // Against what it was put on at, not a fixed number: an inch rig legitimately
                // stands at a root scale of 0.024.
                if (scale.x < _modelBaseScale.x * 0.05f)
                    Core.Log.Error($"*** Model has collapsed to scale {scale.x:0.####} — it is present but too small to see.");

                var head = Interop.Alive(_player) ? _player.IKTargetHead : null;
                if (Interop.Alive(head))
                {
                    var d = Vector3.Distance(head.position, pos);
                    Core.Log.Msg($"    distance from IKTargetHead: {d:0.##} m");
                    if (d > 20f)
                        Core.Log.Error($"*** Model is {d:0.#} m from your head — the solver has thrown it across the map.");
                }
            });
        }

        private float ReadVoiceEnergy()
        {
            if (!Interop.Alive(_player)) return 0f;
            try { return _player.VoiceEnergy; }
            catch { return 0f; }
        }

        private static string SafeName(AvatarPlayer p)
        {
            try { return p.PlayerName; } catch { return "?"; }
        }

        private static CharacterPrefab SafeFullBody(AvatarPlayer p)
        {
            try { return p.FullBody; } catch { return null; }
        }
    }
}
