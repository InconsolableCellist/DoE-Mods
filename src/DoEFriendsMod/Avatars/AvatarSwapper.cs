using System;
using System.Collections.Generic;
using DoEFriendsMod.Gate;
using DoEFriendsMod.Recon;
using UnityEngine;
using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppRootMotion.FinalIK;
using Interop = DoEFriendsMod.Recon.Interop;

namespace DoEFriendsMod.Avatars
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
        private GameObject _root;        // our neutral child under Model_<nick>
        private GameObject _model;       // the instantiated avatar
        private VRIK _vrik;
        private SpringBones _springs;
        private SkinnedMeshRenderer _hiddenVanillaMesh;

        public bool IsActive => Interop.Alive(_model);

        private float _settledLogAt;   // unscaled time at which to re-log placement; 0 = done
        public string AvatarName { get; private set; }

        public AvatarSwapper()
        {
            ModGate.ActiveChanged += active => { if (!active) Revert("gate closed"); };
        }

        public void Toggle(AvatarLibrary library)
        {
            if (IsActive) { Revert("toggled off"); return; }

            if (!ModGate.Active)
            {
                Core.Log.Warning($"Swap refused: gate is inert ({ModGate.Reason}).");
                return;
            }

            AvatarPlayer local;
            try { local = AvatarPlayer.LocalAvatar; }
            catch (Exception e) { Core.Log.Error($"No local avatar: {e.Message}"); return; }
            if (!Interop.Alive(local)) { Core.Log.Warning("Swap refused: no local AvatarPlayer yet."); return; }

            var name = ModConfig.PreviewAvatarName.Value;
            var manifest = string.IsNullOrWhiteSpace(name) ? library.First() : library.Get(name);
            if (manifest == null) { Core.Log.Warning($"No avatar available in {AvatarLibrary.AvatarsDir}"); return; }

            Apply(local, manifest);
        }

        public void Apply(AvatarPlayer player, AvatarManifest manifest)
        {
            ReconLog.Section($"Avatar swap — {manifest.name} onto {SafeName(player)}");

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
            AvatarName = manifest.name;

            try
            {
                // A neutral child of Model_<nick>: our model inherits the game's root motion
                // (that object is at floor level and follows the player) without us having to
                // reproduce it, and reverting is a single Destroy.
                _root = new GameObject($"DFM_Avatar_{manifest.name}");
                _root.transform.SetParent(fullBody.transform, false);
                _root.transform.localPosition = Vector3.zero;
                _root.transform.localRotation = Quaternion.identity;
                _root.transform.localScale = Vector3.one;

                _model = UnityEngine.Object.Instantiate(_bundle.Prefab);
                // Configure while inactive: VRIK's Awake initiates its solver, and it must not
                // run before `references` is populated.
                _model.SetActive(false);
                _model.transform.SetParent(_root.transform, false);
                _model.transform.localPosition = Vector3.zero;
                _model.transform.localRotation = Quaternion.identity;
                _model.transform.localScale = Vector3.one * manifest.rig.suggestedScale;

                var refs = BuildReferences(_model, manifest, out var missing);
                if (refs == null)
                {
                    Core.Log.Error($"Swap failed: humanoid bone map incomplete — missing {missing}. " +
                                   "Re-export with the current exporter.");
                    Revert("reference build failed");
                    return;
                }

                if (ModConfig.SwapUseVrik.Value)
                {
                    _vrik = AddVrik(_model);
                    if (_vrik == null) { Revert("VRIK could not be added"); return; }
                    _vrik.references = refs;
                    WireSolver(_vrik, player, manifest);
                }
                else
                {
                    Core.Log.Msg("    SwapUseVrik=false — model attached with NO IK (diagnostic mode).");
                }

                _model.SetActive(true);

                if (ModConfig.SwapHideVanillaMesh.Value) HideVanillaMesh(fullBody);
                else Core.Log.Msg("    SwapHideVanillaMesh=false — vanilla mesh left visible as a reference.");

                _springs = new SpringBones();
                var springSummary = _springs.Build(_model, manifest);
                _springs.Reset();

                Core.Log.Msg($"*** Avatar swapped: {manifest.name} on {SafeName(player)} " +
                             $"(scale x{manifest.rig.suggestedScale:0.###})");
                Core.Log.Msg($"    dynamics: {springSummary}");
                LogPlacement(player);
                _settledLogAt = Time.unscaledTime + 1f;
                ReconLog.KeyValue("model root", Interop.ScenePath(_root.transform));
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
        private static void WireSolver(VRIK vrik, AvatarPlayer player, AvatarManifest manifest)
        {
            var solver = vrik.solver;
            if (solver == null) { Core.Log.Error("VRIK has no solver."); return; }

            solver.spine.headTarget = player.IKTargetHead;
            solver.spine.positionWeight = 1f;   // NB: the head's weights are named plainly on
            solver.spine.rotationWeight = 1f;   // Spine — there is no headPositionWeight.

            solver.leftArm.target = player.IKTargetLeftHand;
            solver.leftArm.positionWeight = 1f;
            solver.leftArm.rotationWeight = 1f;

            solver.rightArm.target = player.IKTargetRightHand;
            solver.rightArm.positionWeight = 1f;
            solver.rightArm.rotationWeight = 1f;

            solver.locomotion.weight = 1f;
            solver.plantFeet = true;
            solver.scale = Mathf.Max(0.01f, manifest.rig.suggestedScale);

            Core.Log.Msg($"    VRIK targets: head `{Interop.ScenePath(player.IKTargetHead)}`, " +
                         $"hands `{Interop.Name(player.IKTargetLeftHand)}` / `{Interop.Name(player.IKTargetRightHand)}`");
        }

        private void HideVanillaMesh(CharacterPrefab fullBody)
        {
            try
            {
                var mesh = fullBody.characterMesh;
                if (!Interop.Alive(mesh)) { Core.Log.Warning("No characterMesh to hide."); return; }
                _hiddenVanillaMesh = mesh;
                mesh.enabled = false;
                Core.Log.Msg($"    hid vanilla mesh `{Interop.ScenePath(mesh.transform)}`");
            }
            catch (Exception e) { Core.Log.Warning($"Could not hide vanilla mesh: {e.Message}"); }
        }

        public void LateUpdate(float deltaTime)
        {
            if (!IsActive) return;

            if (_settledLogAt > 0f && Time.unscaledTime >= _settledLogAt)
            {
                _settledLogAt = 0f;
                LogSettledPlacement();
            }

            if (_springs == null || !ModConfig.SpringsEnabled.Value) return;
            try { _springs.Simulate(deltaTime); }
            catch (Exception e) { Core.Log.Warning($"Swap springs failed, disabling: {e.Message}"); _springs = null; }
        }

        public void Revert(string why)
        {
            // Un-hide first: if anything below throws, the player still has a body.
            if (Interop.Alive(_hiddenVanillaMesh))
            {
                try { _hiddenVanillaMesh.enabled = true; } catch { }
            }
            _hiddenVanillaMesh = null;

            if (Interop.Alive(_root))
            {
                try { UnityEngine.Object.Destroy(_root); } catch { }
                Core.Log.Msg($"Avatar swap reverted ({why}).");
            }

            _root = null;
            _model = null;
            _vrik = null;
            _springs = null;
            _player = null;
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
                ReconLog.KeyValue("_root", $"{Interop.ScenePath(_root.transform)} @ {Interop.Vec(_root.transform.position)} " +
                                          $"lossyScale {Interop.Vec(_root.transform.lossyScale)} " +
                                          $"active {_root.activeInHierarchy} layer {_root.layer}");
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
                Core.Log.Msg($"    settled (1s after swap): model @ {Interop.Vec(pos)} " +
                             $"lossyScale {Interop.Vec(scale)} active {_model.activeInHierarchy}" +
                             (_vrik != null ? $" VRIK.enabled {_vrik.enabled}" : ""));

                var bad = float.IsNaN(pos.x) || float.IsNaN(pos.y) || float.IsNaN(pos.z);
                if (bad)
                    Core.Log.Error("*** Model position is NaN — the IK solve has blown up. " +
                                   "Set SwapUseVrik=false to confirm.");
                if (scale.x < 0.01f)
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
