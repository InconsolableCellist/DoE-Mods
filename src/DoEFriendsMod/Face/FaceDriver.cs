using System;
using System.Collections.Generic;
using DoEFriendsMod.Avatars;
using DoEFriendsMod.Recon;
using UnityEngine;
using Interop = DoEFriendsMod.Recon.Interop;

namespace DoEFriendsMod.Face
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
            public float Current;
        }

        private readonly List<Target> _targets = new List<Target>();
        private Transform _leftEye, _rightEye;
        private Quaternion _leftEyeRest, _rightEyeRest;

        public int TargetCount => _targets.Count;
        public bool HasEyeBones => Interop.Alive(_leftEye) && Interop.Alive(_rightEye);

        public string Build(GameObject model, AvatarManifest manifest)
        {
            _targets.Clear();
            _leftEye = _rightEye = null;

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
            }

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
                   (HasEyeBones ? ", eye bones" : ", no eye bones");
        }

        private static Transform FindBone(GameObject model, string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            try { return model.transform.Find(path); } catch { return null; }
        }

        /// <summary>Apply the latest values. Call from LateUpdate, after the body is posed.</summary>
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
                    var value = Read(state, target.Sources[s]);
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
            if (state.TryGet("/avatar/parameters/v2/" + ueName, out var v)) return v;
            if (state.TryGet("/avatar/parameters/FT/v2/" + ueName, out v)) return v;
            if (state.TryGet("/avatar/parameters/" + ueName, out v)) return v;
            return 0f;
        }

        private void ApplyEyes(FaceState state, float smoothing)
        {
            if (!HasEyeBones) return;

            // UE gives four unsigned directions per eye; a signed angle is their difference.
            var leftYaw = Read(state, "EyeLookOutLeft") - Read(state, "EyeLookInLeft");
            var leftPitch = Read(state, "EyeLookUpLeft") - Read(state, "EyeLookDownLeft");
            var rightYaw = Read(state, "EyeLookInRight") - Read(state, "EyeLookOutRight");
            var rightPitch = Read(state, "EyeLookUpRight") - Read(state, "EyeLookDownRight");

            // `/tracking/eye/LeftRightPitchYaw` is sent unconditionally, with no negotiation at
            // all, so it is the one thing that still works if everything else fails.
            if (Mathf.Abs(leftYaw) + Mathf.Abs(leftPitch) + Mathf.Abs(rightYaw) + Mathf.Abs(rightPitch) < 0.001f)
            {
                if (state.TryGet("/tracking/eye/LeftRightPitchYaw", out var packed))
                {
                    leftPitch = rightPitch = packed;   // single-value fallback; better than frozen eyes
                }
            }

            var maxX = ModConfig.FaceEyePitchDegrees.Value;
            var maxY = ModConfig.FaceEyeYawDegrees.Value;

            Rotate(_leftEye, _leftEyeRest, -leftPitch * maxX, leftYaw * maxY, smoothing);
            Rotate(_rightEye, _rightEyeRest, -rightPitch * maxX, rightYaw * maxY, smoothing);
        }

        private static void Rotate(Transform eye, Quaternion rest, float pitch, float yaw, float smoothing)
        {
            if (!Interop.Alive(eye)) return;
            try
            {
                var wanted = rest * Quaternion.Euler(pitch, yaw, 0f);
                eye.localRotation = Quaternion.Slerp(eye.localRotation, wanted, smoothing);
            }
            catch { }
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
