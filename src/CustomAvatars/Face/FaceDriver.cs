using System;
using System.Collections.Generic;
using CustomAvatars.Avatars;
using CustomAvatars.Recon;
using UnityEngine;
using Interop = CustomAvatars.Recon.Interop;

namespace CustomAvatars.Face
{
    /// <summary>
    /// Drives an avatar's face from Unified Expressions values.
    ///
    /// We apply blendshapes directly rather than running the avatar's own FX animator
    /// controller, having looked hard at doing the latter. That controller's bulk — OSCmooth
    /// smoothing layers, Binary_Gen quantisation, the `BinaryOut/...` parameter family — exists
    /// to fit VRChat's sync budget and slow parameter updates. Neither constraint applies here:
    /// our stream carries raw bytes at whatever rate we pick, and we smooth on our own terms.
    /// Importing it would mean quantising floats into bits purely so the controller could
    /// un-quantise them again on the same machine.
    ///
    /// The exporter already resolves each UE shape to a renderer and blendshape index, so the
    /// mapping work is done. What's lost versus the controller is an author's bespoke
    /// corrective shapes and non-name-matched mappings — worth revisiting per avatar, not worth
    /// paying for by default.
    /// </summary>
    public class FaceDriver
    {
        /// <summary>One blendshape, and every UE parameter that feeds it.</summary>
        private class Target
        {
            public SkinnedMeshRenderer Renderer;
            public int Index;
            public List<string> Sources = new List<string>();
            public List<int> SourceIds = new List<int>();   // wire ids, for peer-driven faces
            public float Current;
        }

        private readonly struct CombinedSource
        {
            public readonly string Parameter;
            /// <summary>+1 takes the positive half, -1 the negative half, 0 the whole unsigned value.</summary>
            public readonly int Sign;
            public CombinedSource(string parameter, int sign) { Parameter = parameter; Sign = sign; }
        }

        /// <summary>
        /// Raw Unified Expressions name → the combined parameter that carries it, and which half.
        /// Taken from VRCFaceTracking's own combined-shape definitions.
        /// </summary>
        private static readonly Dictionary<string, CombinedSource> Combined =
            new Dictionary<string, CombinedSource>(StringComparer.Ordinal)
        {
            { "MouthCornerPullLeft",   new CombinedSource("SmileFrownLeft", 1) },
            { "MouthCornerSlantLeft",  new CombinedSource("SmileFrownLeft", 1) },
            { "MouthFrownLeft",        new CombinedSource("SmileFrownLeft", -1) },
            { "MouthCornerPullRight",  new CombinedSource("SmileFrownRight", 1) },
            { "MouthCornerSlantRight", new CombinedSource("SmileFrownRight", 1) },
            { "MouthFrownRight",       new CombinedSource("SmileFrownRight", -1) },

            { "MouthUpperRight", new CombinedSource("MouthX", 1) },
            { "MouthLowerRight", new CombinedSource("MouthX", 1) },
            { "MouthUpperLeft",  new CombinedSource("MouthX", -1) },
            { "MouthLowerLeft",  new CombinedSource("MouthX", -1) },

            { "JawRight", new CombinedSource("JawX", 1) },
            { "JawLeft",  new CombinedSource("JawX", -1) },

            { "LipFunnelUpperLeft",  new CombinedSource("LipFunnel", 0) },
            { "LipFunnelUpperRight", new CombinedSource("LipFunnel", 0) },
            { "LipFunnelLowerLeft",  new CombinedSource("LipFunnel", 0) },
            { "LipFunnelLowerRight", new CombinedSource("LipFunnel", 0) },

            { "LipPuckerUpperLeft",  new CombinedSource("LipPucker", 0) },
            { "LipPuckerUpperRight", new CombinedSource("LipPucker", 0) },
            { "LipPuckerLowerLeft",  new CombinedSource("LipPucker", 0) },
            { "LipPuckerLowerRight", new CombinedSource("LipPucker", 0) },

            { "MouthUpperUpLeft",    new CombinedSource("MouthUpperUp", 0) },
            { "MouthUpperUpRight",   new CombinedSource("MouthUpperUp", 0) },
            { "MouthLowerDownLeft",  new CombinedSource("MouthLowerDown", 0) },
            { "MouthLowerDownRight", new CombinedSource("MouthLowerDown", 0) },

            { "NoseSneerLeft",  new CombinedSource("NoseSneer", 0) },
            { "NoseSneerRight", new CombinedSource("NoseSneer", 0) },

            { "BrowInnerUpLeft",  new CombinedSource("BrowExpressionLeft", 1) },
            { "BrowOuterUpLeft",  new CombinedSource("BrowExpressionLeft", 1) },
            { "BrowLowererLeft",  new CombinedSource("BrowExpressionLeft", -1) },
            { "BrowPinchLeft",    new CombinedSource("BrowExpressionLeft", -1) },
            { "BrowInnerUpRight", new CombinedSource("BrowExpressionRight", 1) },
            { "BrowOuterUpRight", new CombinedSource("BrowExpressionRight", 1) },
            { "BrowLowererRight", new CombinedSource("BrowExpressionRight", -1) },
            { "BrowPinchRight",   new CombinedSource("BrowExpressionRight", -1) },

            { "CheekPuffLeft",  new CombinedSource("CheekPuffSuckLeft", 1) },
            { "CheekSuckLeft",  new CombinedSource("CheekPuffSuckLeft", -1) },
            { "CheekPuffRight", new CombinedSource("CheekPuffSuckRight", 1) },
            { "CheekSuckRight", new CombinedSource("CheekPuffSuckRight", -1) },

            { "TongueRight", new CombinedSource("TongueX", 1) },
            { "TongueLeft",  new CombinedSource("TongueX", -1) },
            { "TongueUp",    new CombinedSource("TongueY", 1) },
            { "TongueDown",  new CombinedSource("TongueY", -1) },
        };

        private readonly List<Target> _targets = new List<Target>();
        private Transform _leftEye, _rightEye, _modelRoot;
        private FaceOverrides _overrides;
        private Quaternion _leftEyeRest, _rightEyeRest;

        public int TargetCount => _targets.Count;

        /// <summary>
        /// Peer-supplied shape values, indexed by wire id. When set, the face is driven from
        /// these instead of from local tracking — the same driver, a different source.
        /// </summary>
        public float[] RemoteValues { get; set; }

        public double RemoteAgeSeconds { get; set; } = -1;
        public bool HasEyeBones => Interop.Alive(_leftEye) && Interop.Alive(_rightEye);

        public string Build(GameObject model, AvatarManifest manifest)
        {
            _targets.Clear();
            _leftEye = _rightEye = null;

            _overrides = FaceOverrides.Load(Avatars.AvatarLibrary.AvatarsDir, manifest?.name, out var overrideSummary);

            var ft = manifest?.faceTracking;
            if (ft?.shapes == null || ft.shapes.Count == 0) return "no face tracking shapes in the manifest";

            // Several UE parameters can legitimately land on one blendshape — the exporter's
            // alias table splits one ARKit shape across two UE names. Group them so the value
            // can be combined rather than the last one written winning.
            var byTarget = new Dictionary<string, Target>();

            foreach (var kv in ft.shapes)
            {
                var reference = kv.Value;
                if (reference == null) continue;

                var t = string.IsNullOrEmpty(reference.renderer)
                    ? model.transform
                    : model.transform.Find(reference.renderer);
                if (!Interop.Alive(t)) continue;

                var smr = t.GetComponent<SkinnedMeshRenderer>();
                if (!Interop.Alive(smr) || !Interop.Alive(smr.sharedMesh)) continue;
                if (reference.index < 0 || reference.index >= smr.sharedMesh.blendShapeCount) continue;

                var key = $"{reference.renderer}#{reference.index}";
                if (!byTarget.TryGetValue(key, out var target))
                {
                    target = new Target { Renderer = smr, Index = reference.index };
                    byTarget[key] = target;
                    _targets.Add(target);
                }
                target.Sources.Add(kv.Key);
                target.SourceIds.Add(UEShapes.IdOf(kv.Key));
            }

            _modelRoot = model.transform;

            if (ft.eyeUseBones)
            {
                _leftEye = FindBone(model, ft.eyeBoneLeft);
                _rightEye = FindBone(model, ft.eyeBoneRight);
                if (Interop.Alive(_leftEye)) _leftEyeRest = _leftEye.localRotation;
                if (Interop.Alive(_rightEye)) _rightEyeRest = _rightEye.localRotation;
            }

            var shared = 0;
            foreach (var t in _targets) if (t.Sources.Count > 1) shared++;

            return $"{_targets.Count} blendshape target(s)" +
                   (shared > 0 ? $", {shared} driven by more than one parameter" : "") +
                   (HasEyeBones ? ", eye bones" : ", no eye bones") +
                   $"; {overrideSummary}";
        }

        private static Transform FindBone(GameObject model, string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            try { return model.transform.Find(path); } catch { return null; }
        }

        /// <summary>Apply the latest values. Call from LateUpdate, after the body is posed.</summary>
        /// <summary>Apply peer-supplied values. Same maths, different source.</summary>
        public void ApplyRemote(float deltaTime)
        {
            if (RemoteValues == null || _targets.Count == 0) return;

            var smoothing = 1f - Mathf.Exp(-Mathf.Max(0.1f, ModConfig.FaceSmoothing.Value) * deltaTime * 60f);
            var scale = Mathf.Clamp(ModConfig.FaceShapeScale.Value, 0f, 2f) * 100f;

            for (var i = 0; i < _targets.Count; i++)
            {
                var target = _targets[i];
                if (!Interop.Alive(target.Renderer)) continue;

                var wanted = 0f;
                for (var s = 0; s < target.SourceIds.Count; s++)
                {
                    var id = target.SourceIds[s];
                    if (id < 0 || id >= RemoteValues.Length) continue;
                    var value = RemoteValues[id];
                    if (_overrides != null) value = _overrides.Apply(target.Sources[s], value);
                    if (value > wanted) wanted = value;
                }

                target.Current = Mathf.Lerp(target.Current, Mathf.Clamp01(wanted), smoothing);
                try { target.Renderer.SetBlendShapeWeight(target.Index, target.Current * scale); }
                catch { }
            }

            ApplyRemoteEyes(smoothing);
        }

        private void ApplyRemoteEyes(float smoothing)
        {
            if (!HasEyeBones || RemoteValues == null) return;

            float Get(string name)
            {
                var id = UEShapes.IdOf(name);
                return id >= 0 && id < RemoteValues.Length ? RemoteValues[id] : 0f;
            }

            // Reassembled from the four directions, since that is what crosses the wire.
            var leftYaw = Get("EyeLookOutLeft") - Get("EyeLookInLeft");
            var rightYaw = Get("EyeLookInRight") - Get("EyeLookOutRight");
            var pitch = Get("EyeLookUpLeft") - Get("EyeLookDownLeft");

            Rotate(_leftEye, _leftEyeRest, -pitch * ModConfig.FaceEyePitchDegrees.Value,
                   leftYaw * ModConfig.FaceEyeYawDegrees.Value, smoothing);
            Rotate(_rightEye, _rightEyeRest, -pitch * ModConfig.FaceEyePitchDegrees.Value,
                   rightYaw * ModConfig.FaceEyeYawDegrees.Value, smoothing);
        }

        /// <summary>Read the current value of a shape by wire id, for the outgoing stream.</summary>
        public static float Sample(FaceState state, int id)
        {
            if (state == null || id < 0 || id >= UEShapes.Count) return 0f;
            return Mathf.Clamp01(Read(state, UEShapes.Canonical[id]));
        }

        public void Apply(FaceState state, float deltaTime)
        {
            if (state == null || _targets.Count == 0) return;

            var smoothing = 1f - Mathf.Exp(-Mathf.Max(0.1f, ModConfig.FaceSmoothing.Value) * deltaTime * 60f);
            var scale = Mathf.Clamp(ModConfig.FaceShapeScale.Value, 0f, 2f) * 100f;

            for (var i = 0; i < _targets.Count; i++)
            {
                var target = _targets[i];
                if (!Interop.Alive(target.Renderer)) continue;

                // Combine with max(): where two UE parameters share a blendshape they are two
                // halves of one motion, and summing would double it.
                var wanted = 0f;
                for (var s = 0; s < target.Sources.Count; s++)
                {
                    var name = target.Sources[s];
                    var value = Read(state, name);
                    // Remap per source, not per target: two parameters sharing a blendshape can
                    // legitimately want different ranges.
                    if (_overrides != null) value = _overrides.Apply(name, value);
                    if (value > wanted) wanted = value;
                }

                target.Current = Mathf.Lerp(target.Current, Mathf.Clamp01(wanted), smoothing);
                try { target.Renderer.SetBlendShapeWeight(target.Index, target.Current * scale); }
                catch { }
            }

            ApplyEyes(state, smoothing);
        }

        /// <summary>
        /// VRCFaceTracking's default address for a shape is `/avatar/parameters/v2/<Name>`, but
        /// it adopts whatever prefix a receiver declares — so accept the common variants rather
        /// than depending on one.
        /// </summary>
        private static float Read(FaceState state, string ueName)
        {
            if (TryRaw(state, ueName, out var direct)) return direct;

            // Fall back to VRCFaceTracking's COMBINED parameters.
            //
            // Its combined set packs a pair of opposing raw shapes into one signed value —
            // `SmileFrownLeft` is `MouthCornerPullLeft - MouthFrownLeft`, `JawX` is
            // `JawRight - JawLeft`, and so on. Avatars built on the face-tracking templates are
            // authored against those combined names, so an avatar can be fully rigged for a
            // shape while the raw parameter behind it never moves — which is why smiles, sneers
            // and mouth movement did nothing while jaw and tongue worked.
            if (Combined.TryGetValue(ueName, out var source))
            {
                if (TryRaw(state, source.Parameter, out var combined))
                    return source.Sign == 0 ? Mathf.Clamp01(combined)
                         : source.Sign > 0 ? Mathf.Clamp01(combined) : Mathf.Clamp01(-combined);
            }

            // Several shapes our exporter maps are NOT sent by VRCFaceTracking as raw shapes:
            // its UnifiedExpressions enum has no EyeClosed* or EyeLook* members at all. Eye
            // openness and gaze live in a separate eye structure and go out as COMBINED
            // parameters — EyeLidLeft/Right and EyeLeftX/EyeRightX/EyeY. Reading the raw names
            // returns nothing, which is why eyelids and gaze would never have moved.
            switch (ueName)
            {
                // `EyeLid` is not 0..1 open-to-shut. VRCFaceTracking's templates rest it at
                // 0.75 with the eye fully OPEN — that is the documented default in the
                // expression parameters asset — and values above that are a wide-eyed stare.
                // Treating 1.0 as fully open therefore left the eyes a quarter shut at rest and
                // never opened them properly.
                case "EyeClosedLeft":
                    return TryRaw(state, "EyeLidLeft", out var lidL) ? Closure(lidL) : 0f;
                case "EyeClosedRight":
                    return TryRaw(state, "EyeLidRight", out var lidR) ? Closure(lidR) : 0f;
                case "EyeWideLeft":
                    return TryRaw(state, "EyeLidLeft", out var wideL) ? Widen(wideL) : 0f;
                case "EyeWideRight":
                    return TryRaw(state, "EyeLidRight", out var wideR) ? Widen(wideR) : 0f;

                // Gaze arrives signed on one axis per eye; the four UE directions are its
                // positive and negative halves.
                case "EyeLookOutLeft":  return Positive(state, "EyeLeftX");
                case "EyeLookInLeft":   return Negative(state, "EyeLeftX");
                case "EyeLookOutRight": return Negative(state, "EyeRightX");
                case "EyeLookInRight":  return Positive(state, "EyeRightX");
                case "EyeLookUpLeft":
                case "EyeLookUpRight":   return Positive(state, "EyeY");
                case "EyeLookDownLeft":
                case "EyeLookDownRight": return Negative(state, "EyeY");
            }
            return 0f;
        }

        /// <summary>Eyelid value → how shut the eye is, with `EyeLidOpenPoint` counting as open.</summary>
        private static float Closure(float lid)
        {
            var open = Mathf.Clamp(ModConfig.FaceEyeLidOpenPoint.Value, 0.05f, 1f);
            return Mathf.Clamp01((open - lid) / open);
        }

        /// <summary>Above the open point the eye is widening rather than opening further.</summary>
        private static float Widen(float lid)
        {
            var open = Mathf.Clamp(ModConfig.FaceEyeLidOpenPoint.Value, 0.05f, 1f);
            if (lid <= open || open >= 0.999f) return 0f;
            return Mathf.Clamp01((lid - open) / (1f - open));
        }

        private static bool TryRaw(FaceState state, string name, out float value)
        {
            if (state.TryGet("/avatar/parameters/v2/" + name, out value)) return true;
            if (state.TryGet("/avatar/parameters/FT/v2/" + name, out value)) return true;
            if (state.TryGet("/avatar/parameters/" + name, out value)) return true;
            value = 0f;
            return false;
        }

        private static float Positive(FaceState state, string name) =>
            TryRaw(state, name, out var v) ? Mathf.Clamp01(v) : 0f;

        private static float Negative(FaceState state, string name) =>
            TryRaw(state, name, out var v) ? Mathf.Clamp01(-v) : 0f;

        private void ApplyEyes(FaceState state, float smoothing)
        {
            if (!HasEyeBones) return;

            // UE gives four unsigned directions per eye; a signed angle is their difference.
            // Straight from the combined parameters, which is how VRCFaceTracking actually
            // sends gaze — already signed, so no reassembly from four directions needed.
            TryRaw(state, "EyeLeftX", out var leftYaw);
            TryRaw(state, "EyeRightX", out var rightYaw);
            TryRaw(state, "EyeY", out var pitch);
            var leftPitch = pitch;
            var rightPitch = pitch;

            var maxX = ModConfig.FaceEyePitchDegrees.Value;
            var maxY = ModConfig.FaceEyeYawDegrees.Value;

            Rotate(_leftEye, _leftEyeRest, -leftPitch * maxX, leftYaw * maxY, smoothing);
            Rotate(_rightEye, _rightEyeRest, -rightPitch * maxX, rightYaw * maxY, smoothing);
        }

        /// <summary>
        /// Rotate an eye about the AVATAR's own up and right axes, not the bone's local ones.
        ///
        /// Bone-local axes are the wrong frame to work in: which local axis turns an eye left
        /// depends on how the rig was built, and mirrored left/right eye bones disagree with
        /// each other — turning both about local Y sent one eye up and the other down while
        /// pitch happened to work. The avatar's own up and right are the same for both eyes and
        /// mean the same thing on every rig, so there is nothing to configure and nothing to
        /// get backwards.
        /// </summary>
        private void Rotate(Transform eye, Quaternion rest, float pitch, float yaw, float smoothing)
        {
            if (!Interop.Alive(eye) || !Interop.Alive(_modelRoot)) return;
            try
            {
                var parentRotation = Interop.Alive(eye.parent) ? eye.parent.rotation : Quaternion.identity;
                var restWorld = parentRotation * rest;

                var wanted = Quaternion.AngleAxis(yaw, _modelRoot.up)
                           * Quaternion.AngleAxis(pitch, _modelRoot.right)
                           * restWorld;

                eye.rotation = Quaternion.Slerp(eye.rotation, wanted, smoothing);
            }
            catch { }
        }

        /// <summary>
        /// Open the mouth from voice loudness, for anyone without face tracking.
        ///
        /// The game has no viseme system to borrow: it drives the vanilla jaw straight from
        /// Vivox voice amplitude (`CharacterPrefab.closedJawAngle`/`openedJawAngle`,
        /// `voiceEnergyOverride`), not from phonemes. `AvatarPlayer.VoiceEnergy` is computed on
        /// every client for every player, so this needs no network traffic at all and works for
        /// peers immediately — which matters, because a silent, motionless mouth reads as
        /// broken far more than a crude one does.
        ///
        /// Drives `JawOpen` and the `aa` viseme if the avatar has them, which between them cover
        /// most rigs.
        /// </summary>
        public void ApplyVoiceJaw(float energy, float deltaTime)
        {
            if (_targets.Count == 0) return;

            var smoothing = 1f - Mathf.Exp(-8f * deltaTime);
            var wanted = Mathf.Clamp01(energy * Mathf.Max(0f, ModConfig.VoiceJawScale.Value));
            var scale = Mathf.Clamp(ModConfig.FaceShapeScale.Value, 0f, 2f) * 100f;

            for (var i = 0; i < _targets.Count; i++)
            {
                var target = _targets[i];
                if (!Interop.Alive(target.Renderer)) continue;

                var drives = false;
                for (var s = 0; s < target.Sources.Count; s++)
                    if (target.Sources[s] == "JawOpen" || target.Sources[s] == "aa") { drives = true; break; }

                // Everything else eases back to rest rather than freezing wherever it was.
                var goal = drives ? wanted : 0f;
                target.Current = Mathf.Lerp(target.Current, goal, smoothing);
                try { target.Renderer.SetBlendShapeWeight(target.Index, target.Current * scale); }
                catch { }
            }
        }

        /// <summary>Relax the face — used when tracking goes stale so it doesn't freeze mid-expression.</summary>
        public void Relax(float deltaTime)
        {
            var smoothing = 1f - Mathf.Exp(-2f * deltaTime);
            for (var i = 0; i < _targets.Count; i++)
            {
                var target = _targets[i];
                if (!Interop.Alive(target.Renderer)) continue;
                target.Current = Mathf.Lerp(target.Current, 0f, smoothing);
                try { target.Renderer.SetBlendShapeWeight(target.Index, target.Current * 100f); }
                catch { }
            }
        }
    }
}
