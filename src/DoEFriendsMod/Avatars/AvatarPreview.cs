using System;
using System.Collections.Generic;
using DoEFriendsMod.Gate;
using DoEFriendsMod.Recon;
using UnityEngine;
using Il2Cpp;
// The game has a global-namespace `Interop` type of its own, which lands in Il2Cpp.* and
// collides with our helper once both namespaces are imported. Alias ours explicitly.
using Interop = DoEFriendsMod.Recon.Interop;

namespace DoEFriendsMod.Avatars
{
    /// <summary>
    /// Spawns a custom avatar standing in front of you, and audits it. No IK, no networking,
    /// no swapping — this exists to answer the questions that can only be answered by putting
    /// the thing on a GPU inside this game:
    ///
    ///   1. Does a locked-Poiyomi bundle survive the round-trip, or does it come back
    ///      as <c>Hidden/InternalErrorShader</c> (magenta)?
    ///   2. Does it render in BOTH eyes under Single Pass Instanced?
    ///   3. Do the manifest's (renderer path, blendshape index) pairs actually resolve on the
    ///      instantiated object?
    ///
    /// Local and cosmetic, but still gated: ground rule 1 says every feature is, and a
    /// preview that ignored the gate would be the first crack in that.
    /// </summary>
    public class AvatarPreview
    {
        private readonly AvatarLibrary _library;
        private AvatarBundle _bundle;
        private GameObject _instance;
        private SpringBones _springs;
        private Face.FaceDriver _face;

        public AvatarPreview(AvatarLibrary library)
        {
            _library = library;
            ModGate.ActiveChanged += active => { if (!active) Despawn("gate closed"); };
        }

        public bool IsSpawned => Interop.Alive(_instance);

        public void Toggle()
        {
            if (IsSpawned) { Despawn("toggled off"); return; }

            if (!ModGate.Active)
            {
                Core.Log.Warning($"Preview refused: gate is inert ({ModGate.Reason}). " +
                                 "Join a private lobby with everyone on the same build.");
                return;
            }

            var name = ModConfig.PreviewAvatarName.Value;
            var manifest = string.IsNullOrWhiteSpace(name) ? _library.First() : _library.Get(name);
            if (manifest == null)
            {
                Core.Log.Warning(string.IsNullOrWhiteSpace(name)
                    ? $"No avatars available. Drop a .avatar + .manifest.json pair into {AvatarLibrary.AvatarsDir}"
                    : $"No avatar named `{name}`. Set PreviewAvatarName in MelonPreferences.cfg, or clear it to use the first.");
                return;
            }

            Spawn(manifest);
        }

        private void Spawn(AvatarManifest manifest)
        {
            ReconLog.Section($"Avatar preview — {manifest.name}");
            ReconLog.KeyValue("manifest", manifest.Describe());

            _bundle = AvatarBundle.Acquire(manifest, out var error);
            if (_bundle == null)
            {
                Core.Log.Error($"Failed to load `{manifest.name}`: {error}");
                ReconLog.Line($"- **LOAD FAILED**: {error}");
                return;
            }

            try
            {
                if (!Interop.Alive(_bundle.Prefab))
                {
                    Core.Log.Error("The avatar prefab is no longer loaded — press F5 to rescan, then try again.");
                    _bundle.Release();
                    _bundle = null;
                    return;
                }

                _instance = UnityEngine.Object.Instantiate(_bundle.Prefab);
                _instance.name = $"DFM_Preview_{manifest.name}";
            }
            catch (Exception e)
            {
                Core.Log.Error($"Instantiate failed: {e}");
                _bundle.Dispose();
                _bundle = null;
                return;
            }

            PlaceInFrontOfPlayer(manifest);

            // Build springs after placement: the chains capture rest state from live world
            // positions, so a later reposition would leave them stretched toward the origin.
            _springs = new SpringBones();
            var springSummary = _springs.Build(_instance, manifest);
            _springs.Reset();

            // The preview is the only way to see your own face. Your head is scaled away in
            // first person and you couldn't look at it anyway, so driving the preview's face
            // from the same tracking data turns F6 into a mirror.
            _face = new Face.FaceDriver();
            var faceSummary = _face.Build(_instance, manifest);
            if (_face.TargetCount == 0) _face = null;
            Core.Log.Msg($"Preview face: {faceSummary}");
            Core.Log.Msg($"Dynamics: {springSummary}");
            ReconLog.KeyValue("dynamics", springSummary);

            Core.Log.Msg($"*** Preview spawned: {manifest.name} (scale x{manifest.rig.suggestedScale:0.###})");
            AuditShaders();
            AuditFaceShapes(manifest);
            ReconLog.Line();
            ReconLog.Line("### Preview hierarchy");
            HierarchyDump.Tree(_instance.transform, 4);
            ReconLog.Headline($"Preview audit written to {ReconLog.CurrentFile}");
        }

        private void PlaceInFrontOfPlayer(AvatarManifest manifest)
        {
            try
            {
                var local = AvatarPlayer.LocalAvatar;
                var t = _instance.transform;
                t.localScale = Vector3.one * manifest.rig.suggestedScale;

                if (!Interop.Alive(local)) { Core.Log.Warning("No local avatar — preview left at origin."); return; }

                var head = local.Head;
                var floorY = local.transform.position.y;
                if (!Interop.Alive(head)) { t.position = local.transform.position; return; }

                var forward = head.forward;
                forward.y = 0f;
                if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
                forward.Normalize();

                var pos = head.position + forward * 2f;
                pos.y = floorY;
                t.position = pos;
                t.rotation = Quaternion.LookRotation(-forward, Vector3.up); // face the player
            }
            catch (Exception e)
            {
                Core.Log.Warning($"Preview placement failed ({e.GetType().Name}: {e.Message}) — left at origin.");
            }
        }

        /// <summary>
        /// The one test that matters. A shader that failed to come across in the bundle shows
        /// up here as Hidden/InternalErrorShader, and the avatar renders magenta in-game.
        /// </summary>
        private void AuditShaders()
        {
            ReconLog.Line();
            ReconLog.Line("### Runtime shader audit");
            var broken = 0;
            var seen = new HashSet<string>();

            try
            {
                var renderers = _instance.GetComponentsInChildren<Renderer>(true);
                for (var i = 0; i < renderers.Length; i++)
                {
                    var r = renderers[i];
                    if (!Interop.Alive(r)) continue;
                    var mats = r.sharedMaterials;
                    if (mats == null) continue;
                    for (var m = 0; m < mats.Length; m++)
                    {
                        var mat = mats[m];
                        if (!Interop.Alive(mat))
                        {
                            broken++;
                            ReconLog.Line($"- **NULL MATERIAL** on `{Interop.ScenePath(r.transform)}` slot {m}");
                            continue;
                        }
                        var shaderName = Interop.Alive(mat.shader) ? mat.shader.name : "<null>";
                        if (shaderName == "Hidden/InternalErrorShader" || shaderName == "<null>")
                        {
                            broken++;
                            ReconLog.Line($"- **BROKEN** `{Interop.ScenePath(r.transform)}` slot {m} " +
                                          $"material `{Interop.Name(mat)}` → {shaderName}");
                        }
                        else if (seen.Add(shaderName))
                        {
                            ReconLog.Line($"- ok `{shaderName}`");
                        }
                    }
                }

                ReconLog.KeyValue("renderers", renderers.Length);
                ReconLog.KeyValue("distinct shaders resolved", seen.Count);

                if (broken > 0)
                    Core.Log.Error($"*** {broken} material(s) resolved to a broken shader — the avatar will be MAGENTA. " +
                                   "The shader did not survive the bundle; check that it's locked and re-export.");
                else
                    Core.Log.Msg($"*** Shader audit clean: {seen.Count} shader(s) resolved, none broken.");
            }
            catch (Exception e) { ReconLog.Error("shader audit", e); }
        }

        /// <summary>
        /// Verifies the manifest actually describes this object: every (renderer path, index)
        /// must resolve, and the blendshape at that index must have the name we expect.
        /// Catches a manifest paired with the wrong bundle before Phase 3 depends on it.
        /// </summary>
        private void AuditFaceShapes(AvatarManifest manifest)
        {
            var ft = manifest.faceTracking;
            if (ft?.shapes == null || ft.shapes.Count == 0) { ReconLog.Line("_no face tracking shapes in manifest_"); return; }

            ReconLog.Line();
            ReconLog.Line("### Manifest ↔ mesh cross-check");

            int ok = 0, badPath = 0, badIndex = 0;
            var missingRenderers = new HashSet<string>();

            foreach (var kv in ft.shapes)
            {
                var reference = kv.Value;
                if (reference == null) { badPath++; continue; }

                var t = string.IsNullOrEmpty(reference.renderer)
                    ? _instance.transform
                    : _instance.transform.Find(reference.renderer);

                if (!Interop.Alive(t))
                {
                    badPath++;
                    missingRenderers.Add(reference.renderer ?? "<root>");
                    continue;
                }

                var smr = t.GetComponent<SkinnedMeshRenderer>();
                if (!Interop.Alive(smr) || !Interop.Alive(smr.sharedMesh) ||
                    reference.index < 0 || reference.index >= smr.sharedMesh.blendShapeCount)
                {
                    badIndex++;
                    continue;
                }
                ok++;
            }

            ReconLog.KeyValue("shapes resolved", $"{ok} of {ft.shapes.Count}");
            if (badPath > 0) ReconLog.KeyValue("unresolved renderer paths", $"{badPath} ({string.Join(", ", missingRenderers)})");
            if (badIndex > 0) ReconLog.KeyValue("out-of-range indices", badIndex);
            ReconLog.KeyValue("visemes in manifest", ft.visemes?.Count ?? 0);
            ReconLog.KeyValue("eye bones", ft.eyeUseBones ? $"{ft.eyeBoneLeft} / {ft.eyeBoneRight}" : "none (blendshape gaze)");

            if (badPath + badIndex > 0)
                Core.Log.Error($"*** Manifest/mesh mismatch: {badPath} bad path(s), {badIndex} bad index(es). " +
                               "The manifest may be paired with a different bundle build.");
            else
                Core.Log.Msg($"*** Manifest cross-check clean: all {ok} shapes resolve on the loaded mesh.");
        }

        /// <summary>Driven from Core.OnLateUpdate — after animation, before the frame renders.</summary>
        public void LateUpdate(float deltaTime)
        {
            if (!IsSpawned) return;

            if (_face != null)
            {
                var state = Core.Instance?.FaceState;
                if (state != null)
                {
                    var stale = state.SecondsSinceLastMessage;
                    if (stale >= 0 && stale < ModConfig.FaceStaleSeconds.Value) _face.Apply(state, deltaTime);
                    else _face.Relax(deltaTime);
                }
            }

            if (_springs == null || !ModConfig.SpringsEnabled.Value) return;
            try { _springs.Simulate(deltaTime); }
            catch (Exception e) { Core.Log.Warning($"Spring simulation failed, disabling: {e.Message}"); _springs = null; }
        }

        public void Despawn(string why)
        {
            if (Interop.Alive(_instance))
            {
                try { UnityEngine.Object.Destroy(_instance); } catch { }
                Core.Log.Msg($"Preview despawned ({why}).");
            }
            _instance = null;
            _springs = null;
            _face = null;

            // Unload the bundle but NOT its loaded objects — the instance is being destroyed
            // separately, and unloading assets out from under a live GameObject is how you get
            // an avatar that renders as nothing at all.
            _bundle?.Release();
            _bundle = null;
        }
    }
}
