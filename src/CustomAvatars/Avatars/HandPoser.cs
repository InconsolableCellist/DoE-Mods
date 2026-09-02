using System;
using System.Collections.Generic;
using CustomAvatars.Recon;
using UnityEngine;
using Il2Cpp;
using Interop = CustomAvatars.Recon.Interop;

namespace CustomAvatars.Avatars
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
            /// <summary>
            /// Whether real per-finger data has EVER been seen. A skeletal action that exists
            /// but always reports zero — which is what non-Index controllers give you through
            /// SteamVR — is indistinguishable from a permanently open hand, and trusting it
            /// means the grip axis is never consulted and nothing ever moves.
            /// </summary>
            public bool SawRealCurls;
            public float LastGrip, LastTrigger;
            public bool HadInput;
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

        /// <summary>
        /// When true, curls are supplied by <see cref="SetRemoteCurls"/> instead of read from
        /// the controllers — a peer's fingers are driven by THEIR hands, not ours.
        /// </summary>
        public bool RemoteDriven { get; set; }

        /// <summary>Current curls, thumb-to-little, left hand then right. Ten values, 0..1.</summary>
        public void GetCurls(float[] into)
        {
            if (into == null || into.Length < FingerCount * 2) return;
            for (var i = 0; i < FingerCount; i++) into[i] = _left?.Curl[i] ?? 0f;
            for (var i = 0; i < FingerCount; i++) into[FingerCount + i] = _right?.Curl[i] ?? 0f;
        }

        public void SetRemoteCurls(float[] curls)
        {
            if (curls == null || curls.Length < FingerCount * 2) return;
            for (var i = 0; i < FingerCount; i++)
            {
                if (_left != null) _left.Curl[i] = curls[i];
                if (_right != null) _right.Curl[i] = curls[FingerCount + i];
            }
        }

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

        /// <summary>
        /// A finger bent less than this at rest counts as straight, and gets its bend axis from
        /// the palm direction instead of its own geometry. An angle, not a raw cross-product
        /// magnitude: the old test scaled with bone length, so a small avatar's fingers all
        /// counted as straight while a big one's never did.
        /// </summary>
        private const float StraightFingerDegrees = 3f;

        private class FingerInfo
        {
            public List<Transform> Chain;
            public float RestBend;      // degrees between the first two segments
            public bool Curled;
            public bool Flipped;        // rest curl opposed the palm; the axis was negated
        }

        private static Hand BuildHand(GameObject model, AvatarManifest manifest, bool isLeft)
        {
            var map = manifest?.rig?.humanoidBones;
            if (map == null) return null;

            var hand = new Hand { IsLeft = isLeft };
            var side = isLeft ? "Left" : "Right";
            var fingers = new FingerInfo[FingerCount];

            for (var f = 0; f < FingerCount; f++)
            {
                var transforms = new List<Transform>();
                foreach (var suffix in BoneNames[f])
                {
                    if (!map.TryGetValue(side + suffix, out var path) || string.IsNullOrEmpty(path)) continue;
                    var t = model.transform.Find(path);
                    if (Interop.Alive(t)) transforms.Add(t);
                }
                if (transforms.Count == 0) continue;

                var info = new FingerInfo { Chain = transforms };
                if (transforms.Count >= 3)
                {
                    try
                    {
                        var v1 = transforms[1].position - transforms[0].position;
                        var v2 = transforms[2].position - transforms[1].position;
                        if (v1.sqrMagnitude > 1e-12f && v2.sqrMagnitude > 1e-12f)
                            info.RestBend = Vector3.Angle(v1, v2);
                    }
                    catch { }
                }
                info.Curled = info.RestBend > StraightFingerDegrees;
                fingers[f] = info;
            }

            // Everything hangs off one absolute reference: which way the palm faces. Each
            // finger's axis is then cross(finger, palm), whose sign is right on both hands by
            // construction. The old code took the sign from each finger's own rest curl, fell
            // back to a lateral axis that was inverted on the right hand, and then made the
            // fingers vote — so one correctly-curled finger on a hand of straight ones lost the
            // vote and got flipped to match the wrong ones.
            var palm = PalmDirection(fingers, model.transform, out var palmSource);

            var notes = new List<string>();
            for (var f = 0; f < FingerCount; f++)
            {
                var info = fingers[f];
                if (info == null) continue;

                var axis = BendAxis(info, palm, model.transform, f == 0);
                if (axis.sqrMagnitude < 1e-8f) continue;

                var joints = new List<Joint>();
                foreach (var t in info.Chain)
                    joints.Add(new Joint
                    {
                        Bone = t,
                        Rest = t.localRotation,
                        BendAxisLocal = t.InverseTransformDirection(axis).normalized,
                    });
                hand.Fingers[f] = joints;

                notes.Add($"{FingerNames[f]} {(info.Curled ? "curled" : "straight")} {info.RestBend:0}°" +
                          (info.Flipped ? " (rest curl opposes the palm — axis flipped)" : ""));
            }

            var flip = isLeft ? ModConfig.HandCurlFlipLeft.Value : ModConfig.HandCurlFlipRight.Value;
            if (flip)
            {
                foreach (var joints in hand.Fingers)
                {
                    if (joints == null) continue;
                    foreach (var j in joints) j.BendAxisLocal = -j.BendAxisLocal;
                }
            }

            Core.Log.Msg($"    hand poses: {side.ToLowerInvariant()} palm from {palmSource}" +
                         (flip ? $", HandCurlFlip{side} negated every axis" : "") +
                         $"; {string.Join(", ", notes)}");
            return hand;
        }

        private static readonly string[] FingerNames = { "thumb", "index", "middle", "ring", "little" };

        /// <summary>
        /// The direction from the back of the hand through the palm, from the avatar's own
        /// bones. In order of trust: the way the fingers already curl at rest; failing that
        /// the side of the hand the thumb leans to; failing that the humanoid T-pose
        /// convention, palms down.
        /// </summary>
        private static Vector3 PalmDirection(FingerInfo[] fingers, Transform modelRoot, out string source)
        {
            // 1. Fingers modelled with a rest curl curl toward the palm — nobody exports a hand
            //    hyperextended. The part of the second segment that isn't along the first is
            //    that curl direction. Weighted by the bend, so a barely-bent finger can't
            //    outvote a clearly-curled one.
            var sum = Vector3.zero;
            var count = 0;
            for (var f = 1; f < FingerCount; f++)   // not the thumb: it curls across, not down
            {
                var info = fingers[f];
                if (info == null || !info.Curled || info.Chain.Count < 3) continue;
                try
                {
                    var v1 = (info.Chain[1].position - info.Chain[0].position).normalized;
                    var v2 = info.Chain[2].position - info.Chain[1].position;
                    var curl = v2 - Vector3.Dot(v2, v1) * v1;
                    if (curl.sqrMagnitude < 1e-12f) continue;
                    sum += curl.normalized * info.RestBend;
                    count++;
                }
                catch { }
            }
            if (count > 0 && sum.sqrMagnitude > 1e-8f)
            {
                source = $"{count} curled finger(s)";
                return sum.normalized;
            }

            // 2. Straight fingers: use the thumb. The plane of the hand is spanned by the
            //    fingers and the line across the knuckles; the thumb sits on the palm side
            //    of it. Only trusted when the thumb clearly leaves the plane.
            try
            {
                var thumb = fingers[0];
                var index = fingers[1];
                var outer = fingers[4] ?? fingers[3];
                if (thumb != null && thumb.Chain.Count >= 2 && index != null && index.Chain.Count >= 2 && outer != null)
                {
                    var fingerDir = (index.Chain[1].position - index.Chain[0].position).normalized;
                    var across = outer.Chain[0].position - index.Chain[0].position;
                    var normal = Vector3.Cross(fingerDir, across);
                    var thumbDir = (thumb.Chain[thumb.Chain.Count - 1].position - thumb.Chain[0].position).normalized;
                    if (normal.sqrMagnitude > 1e-12f)
                    {
                        normal.Normalize();
                        var lean = Vector3.Dot(thumbDir, normal);
                        if (Mathf.Abs(lean) > 0.1f)
                        {
                            source = $"the thumb's lean ({lean * 100f:+0;-0}% out of the hand plane)";
                            return lean > 0f ? normal : -normal;
                        }
                    }
                }
            }
            catch { }

            // 3. T-pose convention: arms out, palms facing the floor.
            source = "the T-pose convention (palms down)";
            return -modelRoot.up;
        }

        /// <summary>
        /// The world axis a positive curl rotates this finger about. For a finger with a rest
        /// curl, the plane it already folds in; otherwise the plane through the palm direction.
        /// Either way the sign is checked against the palm, so a positive curl always closes
        /// the hand.
        /// </summary>
        private static Vector3 BendAxis(FingerInfo info, Vector3 palm, Transform modelRoot, bool isThumb)
        {
            try
            {
                var chain = info.Chain;
                var fingerDir = chain.Count >= 2
                    ? (chain[1].position - chain[0].position).normalized
                    : chain[0].forward;

                // A finger's own rest curl is the best evidence of how it bends — but the
                // thumb aside, it must agree with the palm, or the rig has that finger bent
                // backwards and following it would too.
                if (info.Curled && chain.Count >= 3)
                {
                    var v1 = chain[1].position - chain[0].position;
                    var v2 = chain[2].position - chain[1].position;
                    var axis = Vector3.Cross(v1, v2);
                    if (axis.sqrMagnitude > 1e-12f)
                    {
                        axis.Normalize();
                        if (!isThumb)
                        {
                            var expected = Vector3.Cross(fingerDir, palm);
                            if (expected.sqrMagnitude > 1e-8f && Vector3.Dot(axis, expected) < 0f)
                            {
                                info.Flipped = true;
                                axis = -axis;
                            }
                        }
                        return axis;
                    }
                }

                // Straight: bend toward the palm.
                var lateral = Vector3.Cross(fingerDir, palm);
                if (lateral.sqrMagnitude > 1e-8f) return lateral.normalized;

                // Finger pointing straight at or away from the palm? Something is odd about
                // this rig; take the horizontal axis across the hand and hope.
                lateral = Vector3.Cross(modelRoot.up, fingerDir);
                if (lateral.sqrMagnitude > 1e-8f) return lateral.normalized;
                return Vector3.zero;
            }
            catch { return Vector3.zero; }
        }

        /// <summary>Read the controllers and apply. Call from LateUpdate, after VRIK.</summary>
        private float _nextDebugAt;

        public void Update(float deltaTime)
        {
            if (_left == null && _right == null) return;
            if (!ModConfig.HandPosesEnabled.Value) return;

            if (!RemoteDriven)
            {
                ReadCurls(_left);
                ReadCurls(_right);
            }

            if (!RemoteDriven && ModConfig.HandPoseDebug.Value && Time.unscaledTime >= _nextDebugAt)
            {
                _nextDebugAt = Time.unscaledTime + 0.5f;
                Core.Log.Msg($"hands: L grip {_left?.LastGrip:0.00} trig {_left?.LastTrigger:0.00} " +
                             $"curls [{Join(_left)}] perFinger={_left?.SawRealCurls} input={_left?.HadInput} | " +
                             $"R grip {_right?.LastGrip:0.00} trig {_right?.LastTrigger:0.00} " +
                             $"curls [{Join(_right)}] perFinger={_right?.SawRealCurls}");
            }

            var smoothing = 1f - Mathf.Exp(-Mathf.Max(0.01f, ModConfig.HandCurlSmoothing.Value) * deltaTime * 60f);
            Apply(_left, smoothing);
            Apply(_right, smoothing);
        }

        private static void ReadCurls(Hand hand)
        {
            if (hand == null) return;
            var target = new float[FingerCount];

            try
            {
                var input = XRInput.Instance;
                if (!Interop.Alive(input)) { hand.HadInput = false; return; }
                hand.HadInput = true;

                var handedness = hand.IsLeft ? Handedness.Left : Handedness.Right;
                var grip = Mathf.Clamp01(hand.IsLeft ? input.leftHandTrigger : input.rightHandTrigger);
                var trigger = Mathf.Clamp01(hand.IsLeft ? input.leftIndexTrigger : input.rightIndexTrigger);
                hand.LastGrip = grip;
                hand.LastTrigger = trigger;

                float[] curls = null;
                try
                {
                    var raw = input.GetFingerCurls(handedness, true);
                    if (raw != null && raw.Length >= FingerCount)
                    {
                        curls = new float[FingerCount];
                        for (var i = 0; i < FingerCount; i++) curls[i] = Mathf.Clamp01(raw[i]);
                        for (var i = 0; i < FingerCount; i++) if (curls[i] > 0.05f) { hand.SawRealCurls = true; break; }
                    }
                }
                catch { /* backend without skeletal input */ }

                if (hand.SawRealCurls && curls != null)
                {
                    for (var i = 0; i < FingerCount; i++) target[i] = curls[i];
                }
                else
                {
                    // Trigger bends the index, grip bends the rest — the same split the vanilla
                    // game uses to choose a HandPose.Type.
                    target[0] = grip;
                    target[1] = trigger;
                    target[2] = grip;
                    target[3] = grip;
                    target[4] = grip;
                }
            }
            catch { hand.HadInput = false; return; }

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

        private static string Join(Hand h)
        {
            if (h == null) return "-";
            var parts = new string[FingerCount];
            for (var i = 0; i < FingerCount; i++) parts[i] = h.Curl[i].ToString("0.00");
            return string.Join(" ", parts);
        }

        /// <summary>Report what input backend we're actually talking to.</summary>
        public static void LogInputBackend()
        {
            try
            {
                var input = XRInput.Instance;
                if (!Interop.Alive(input)) { Core.Log.Warning("    hand input: XRInput.Instance is null — fingers cannot move."); return; }
                var typeName = HierarchyDump.TypeName(input);
                var probe = input.GetFingerCurls(Handedness.Right, true);
                Core.Log.Msg($"    hand input: {typeName}, GetFingerCurls returned " +
                             (probe == null ? "null" : $"{probe.Length} value(s)") +
                             $"; grip axis reads {input.rightHandTrigger:0.00}");
            }
            catch (Exception e) { Core.Log.Warning($"    hand input probe failed: {e.GetType().Name}: {e.Message}"); }
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
