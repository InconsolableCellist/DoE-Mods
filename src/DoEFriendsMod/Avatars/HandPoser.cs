using System;
using System.Collections.Generic;
using DoEFriendsMod.Recon;
using UnityEngine;
using Il2Cpp;
using Interop = DoEFriendsMod.Recon.Interop;

namespace DoEFriendsMod.Avatars
{
    /// <summary>
    /// Curls the custom avatar's fingers from the controllers, so gripping a weapon closes the
    /// hand instead of leaving an open paw wrapped round an axe.
    ///
    /// The game's own hand posing can't be reused directly: `HandPose` stores baked
    /// `Transform[] bones` captured from *the game's* rig, and those rotations mean nothing on a
    /// VRChat skeleton with different bone axes. So we curl procedurally instead — rotate each
    /// finger joint about the axis that actually bends it, derived from the avatar's own bone
    /// geometry at swap time.
    ///
    /// Input, in order of preference:
    ///   1. `XRInput.GetFingerCurls` — real per-finger curl. `OpenVRInput` implements it over
    ///      `SteamVR_Action_Skeleton`, so Index controllers give genuine articulation.
    ///   2. Grip and trigger axes — universal fallback: trigger drives the index finger, grip
    ///      drives the rest, which is roughly what the vanilla game does.
    ///
    /// This is also the groundwork for ASL support: once fingers can be driven from a source,
    /// swapping that source for a gesture table is a much smaller job than starting here.
    /// </summary>
    public class HandPoser
    {
        private const int FingerCount = 5;   // thumb, index, middle, ring, little

        private class Joint
        {
            public Transform Bone;
            public Quaternion Rest;
            public Vector3 BendAxisLocal;
        }

        private class Hand
        {
            public bool IsLeft;
            public readonly List<Joint>[] Fingers = new List<Joint>[FingerCount];
            public readonly float[] Curl = new float[FingerCount];
        }

        private static readonly string[][] BoneNames =
        {
            new[] { "ThumbProximal", "ThumbIntermediate", "ThumbDistal" },
            new[] { "IndexProximal", "IndexIntermediate", "IndexDistal" },
            new[] { "MiddleProximal", "MiddleIntermediate", "MiddleDistal" },
            new[] { "RingProximal", "RingIntermediate", "RingDistal" },
            new[] { "LittleProximal", "LittleIntermediate", "LittleDistal" },
        };

        private Hand _left, _right;

        public int JointCount { get; private set; }

        public string Build(GameObject model, AvatarManifest manifest)
        {
            _left = BuildHand(model, manifest, true);
            _right = BuildHand(model, manifest, false);
            JointCount = Count(_left) + Count(_right);
            return JointCount == 0
                ? "no finger bones found in the manifest's humanoid map — fingers won't move"
                : $"{JointCount} finger joint(s) across both hands";

            int Count(Hand h)
            {
                var n = 0;
                if (h == null) return 0;
                foreach (var f in h.Fingers) if (f != null) n += f.Count;
                return n;
            }
        }

        private static Hand BuildHand(GameObject model, AvatarManifest manifest, bool isLeft)
        {
            var map = manifest?.rig?.humanoidBones;
            if (map == null) return null;

            var hand = new Hand { IsLeft = isLeft };
            var side = isLeft ? "Left" : "Right";

            for (var f = 0; f < FingerCount; f++)
            {
                var joints = new List<Joint>();
                var transforms = new List<Transform>();

                foreach (var suffix in BoneNames[f])
                {
                    if (!map.TryGetValue(side + suffix, out var path) || string.IsNullOrEmpty(path)) continue;
                    var t = model.transform.Find(path);
                    if (Interop.Alive(t)) transforms.Add(t);
                }
                if (transforms.Count == 0) continue;

                // The bend axis is perpendicular to the plane the finger folds in, which the
                // chain's own geometry gives us: cross(proximal→middle, middle→distal). A
                // finger modelled dead straight makes that degenerate, so fall back to the
                // palm's lateral axis.
                var axis = BendAxis(transforms, model.transform, isLeft);

                foreach (var t in transforms)
                    joints.Add(new Joint
                    {
                        Bone = t,
                        Rest = t.localRotation,
                        BendAxisLocal = t.InverseTransformDirection(axis).normalized,
                    });

                hand.Fingers[f] = joints;
            }
            return hand;
        }

        private static Vector3 BendAxis(List<Transform> chain, Transform modelRoot, bool isLeft)
        {
            try
            {
                if (chain.Count >= 3)
                {
                    var v1 = chain[1].position - chain[0].position;
                    var v2 = chain[2].position - chain[1].position;
                    var cross = Vector3.Cross(v1, v2);
                    if (cross.sqrMagnitude > 1e-8f) return cross.normalized;
                }

                // Straight finger: bend about the axis across the palm. The finger direction
                // crossed with the model's up gives that, and the sign flips per hand.
                var dir = chain.Count >= 2
                    ? (chain[1].position - chain[0].position).normalized
                    : chain[0].forward;
                var lateral = Vector3.Cross(dir, modelRoot.up);
                if (lateral.sqrMagnitude < 1e-8f) lateral = modelRoot.right;
                return (isLeft ? -lateral : lateral).normalized;
            }
            catch { return isLeft ? -modelRoot.right : modelRoot.right; }
        }

        /// <summary>Read the controllers and apply. Call from LateUpdate, after VRIK.</summary>
        public void Update(float deltaTime)
        {
            if (_left == null && _right == null) return;
            if (!ModConfig.HandPosesEnabled.Value) return;

            ReadCurls(_left);
            ReadCurls(_right);

            var smoothing = 1f - Mathf.Exp(-Mathf.Max(0.01f, ModConfig.HandCurlSmoothing.Value) * deltaTime * 60f);
            Apply(_left, smoothing);
            Apply(_right, smoothing);
        }

        private static void ReadCurls(Hand hand)
        {
            if (hand == null) return;
            var target = new float[FingerCount];

            var gotPerFinger = false;
            try
            {
                var input = XRInput.Instance;
                if (Interop.Alive(input))
                {
                    var handedness = hand.IsLeft ? Handedness.Left : Handedness.Right;
                    var curls = input.GetFingerCurls(handedness, true);
                    if (curls != null && curls.Length >= FingerCount)
                    {
                        for (var i = 0; i < FingerCount; i++) target[i] = Mathf.Clamp01(curls[i]);
                        gotPerFinger = true;
                    }

                    if (!gotPerFinger)
                    {
                        // Trigger bends the index, grip bends the rest — the same split the
                        // vanilla game uses to pick a HandPose.Type.
                        var grip = Mathf.Clamp01(hand.IsLeft ? input.leftHandTrigger : input.rightHandTrigger);
                        var trigger = Mathf.Clamp01(hand.IsLeft ? input.leftIndexTrigger : input.rightIndexTrigger);
                        target[0] = grip;              // thumb
                        target[1] = trigger;           // index
                        target[2] = grip;
                        target[3] = grip;
                        target[4] = grip;
                    }
                }
            }
            catch { /* no input this frame; keep the last pose rather than snapping open */ }

            for (var i = 0; i < FingerCount; i++) hand.Curl[i] = target[i];
        }

        private static void Apply(Hand hand, float smoothing)
        {
            if (hand == null) return;

            var thumbDegrees = ModConfig.ThumbCurlDegrees.Value;
            var fingerDegrees = ModConfig.HandCurlDegrees.Value;

            for (var f = 0; f < FingerCount; f++)
            {
                var joints = hand.Fingers[f];
                if (joints == null) continue;

                var degrees = (f == 0 ? thumbDegrees : fingerDegrees) * hand.Curl[f];
                for (var j = 0; j < joints.Count; j++)
                {
                    var joint = joints[j];
                    if (!Interop.Alive(joint.Bone)) continue;
                    try
                    {
                        var wanted = joint.Rest * Quaternion.AngleAxis(degrees, joint.BendAxisLocal);
                        joint.Bone.localRotation = Quaternion.Slerp(joint.Bone.localRotation, wanted, smoothing);
                    }
                    catch { }
                }
            }
        }

        /// <summary>Put every finger back to the pose it was exported in.</summary>
        public void Reset()
        {
            foreach (var hand in new[] { _left, _right })
            {
                if (hand == null) continue;
                foreach (var joints in hand.Fingers)
                {
                    if (joints == null) continue;
                    foreach (var joint in joints)
                        if (Interop.Alive(joint.Bone)) { try { joint.Bone.localRotation = joint.Rest; } catch { } }
                }
            }
        }
    }
}
