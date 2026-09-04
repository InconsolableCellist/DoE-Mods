using System;
using CustomAvatars.Recon;
using UnityEngine;
using Interop = CustomAvatars.Recon.Interop;

namespace CustomAvatars.Avatars
{
    /// <summary>
    /// A two-bone analytic IK for the legs, aimed at the game's own feet.
    ///
    /// The retarget copies the vanilla body's leg rotations, and the head anchor then slides
    /// the whole model so its head is at yours. Nothing in that chain ever looks at where the
    /// vanilla feet are, so ours land wherever a differently-proportioned skeleton's hip and
    /// knee angles happen to put them — above the floor when your head is higher than the
    /// one-shot fit was measured at, through it when you crouch.
    ///
    /// The vanilla feet are the truth about feet: the game plants them on the floor, steps
    /// them when you walk, and moves them to the trackers under full-body tracking. So each
    /// leg is solved to its vanilla foot bone, and the foot keeps the orientation the retarget
    /// gave it, which is the vanilla foot's.
    ///
    /// Same solver shape as <see cref="ArmIK"/>: closed form, nothing to converge, the upper
    /// bone alone scaled for stretch so the length is exactly s, and a final hard lock.
    /// </summary>
    public class LegIK
    {
        private class Leg
        {
            public string Side;
            public Transform Upper, Lower, Foot;
            public Transform SourceUpper, SourceLower, SourceFoot;
            public Vector3 UpperRestScale = Vector3.one;
            public Vector3 LowerRestScale = Vector3.one;
            public Vector3 FootRestScale = Vector3.one;
            public Vector3 FootRestLocalPos;
            public float RestNatural;          // at build, in world metres
            public float RestModelScale = 1f;  // the model's scale when RestNatural was measured
            public Transform ModelRoot;
            public float CurrentScale = 1f;
            public bool Stretched;
            public float LastNatural, LastNeeded, LastScale, LastMiss, LastExpectedMiss;
        }

        private Leg _left, _right;

        public bool HasLegs => _left != null || _right != null;

        /// <summary>True when either solver miss is over a centimetre, or a leg's measured length has wandered.</summary>
        public bool WorthLogging => (_left?.LastMiss ?? 0f) > 0.01f || (_right?.LastMiss ?? 0f) > 0.01f ||
                                    ReachWandered(_left) || ReachWandered(_right);

        // Same invariant as the arm: the leg's length is measured from the bones every frame
        // and must come out the same every time, once the model's own scale is taken out —
        // the one-shot height fit a second after the swap changes it legitimately. A reach
        // that grows beyond that is a bone left off the end of its parent, which is what the
        // foot lock did before the rest position was put back.
        private static bool ReachWandered(Leg leg) =>
            leg != null && leg.RestNatural > 0f && Mathf.Abs(leg.LastNatural - RestNaturalNow(leg)) > 0.05f;

        private static float RestNaturalNow(Leg leg)
        {
            var scale = 1f;
            try { if (Interop.Alive(leg.ModelRoot) && leg.RestModelScale > 1e-4f) scale = leg.ModelRoot.lossyScale.y / leg.RestModelScale; }
            catch { }
            return leg.RestNatural * scale;
        }

        public string Build(GameObject model, AvatarManifest manifest, Func<HumanBodyBones, Transform> sourceBone)
        {
            _left = BuildLeg(model, manifest, "Left", sourceBone);
            _right = BuildLeg(model, manifest, "Right", sourceBone);

            var count = (_left != null ? 1 : 0) + (_right != null ? 1 : 0);
            return count == 0 ? "no leg bones could be paired with the game's feet"
                              : $"{count} leg(s) solved to the game's feet";
        }

        private static Leg BuildLeg(GameObject model, AvatarManifest manifest, string side,
                                    Func<HumanBodyBones, Transform> sourceBone)
        {
            var map = manifest?.rig?.humanoidBones;
            if (map == null || sourceBone == null) return null;

            Transform Bone(string name) =>
                map.TryGetValue(side + name, out var path) && !string.IsNullOrEmpty(path)
                    ? model.transform.Find(path) : null;

            var upper = Bone("UpperLeg");
            var lower = Bone("LowerLeg");
            var foot = Bone("Foot");
            if (!Interop.Alive(upper) || !Interop.Alive(lower) || !Interop.Alive(foot)) return null;

            var isLeft = side == "Left";
            Transform srcUpper = null, srcLower = null, srcFoot = null;
            try
            {
                srcUpper = sourceBone(isLeft ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg);
                srcLower = sourceBone(isLeft ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg);
                srcFoot = sourceBone(isLeft ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
            }
            catch { }
            // No vanilla foot, no target: leave that leg to the retarget.
            if (!Interop.Alive(srcFoot)) return null;

            var leg = new Leg
            {
                Side = side,
                Upper = upper, Lower = lower, Foot = foot,
                SourceUpper = Interop.Alive(srcUpper) ? srcUpper : null,
                SourceLower = Interop.Alive(srcLower) ? srcLower : null,
                SourceFoot = srcFoot,
                UpperRestScale = upper.localScale,
                LowerRestScale = lower.localScale,
                FootRestScale = foot.localScale,
                FootRestLocalPos = foot.localPosition,
                ModelRoot = model.transform,
            };
            try
            {
                leg.RestNatural = Vector3.Distance(upper.position, lower.position) + Vector3.Distance(lower.position, foot.position);
                leg.RestModelScale = Mathf.Max(1e-4f, model.transform.lossyScale.y);
            }
            catch { }
            return leg;
        }

        /// <summary>
        /// Solve both legs. Call after the retarget has posed the body and the head anchor has
        /// placed it. Each offset is added to that leg's target: for your own avatar the game's
        /// display body lags behind where you actually are, so a foot the game placed on that
        /// body is re-based onto ours. A foot a tracker is driving is already solved to a world
        /// target and gets no offset — the caller passes zero for it.
        /// </summary>
        public void Apply(Vector3 offsetLeft, Vector3 offsetRight, Vector3 modelRight, Vector3 modelForward)
        {
            Solve(_left, offsetLeft, modelRight, modelForward);
            Solve(_right, offsetRight, modelRight, modelForward);
        }

        /// <summary>Put the bone scales back, before this solver is thrown away for a new one.</summary>
        public void Release()
        {
            foreach (var leg in new[] { _left, _right })
            {
                if (leg == null) continue;
                try { if (Interop.Alive(leg.Upper)) RestScales(leg); } catch { }
                try { if (Interop.Alive(leg.Foot)) leg.Foot.localPosition = leg.FootRestLocalPos; } catch { }
            }
        }

        public string Describe()
        {
            return $"L {Describe(_left)} | R {Describe(_right)}";

            string Describe(Leg leg)
            {
                if (leg == null) return "-";
                var locked = ModConfig.LegLockFeet.Value ? " locked" : "";
                var rest = ReachWandered(leg) ? $" (was {RestNaturalNow(leg) * 100f:0.#}cm at build, at this scale)" : "";
                return $"miss {leg.LastMiss * 100f:0.#}cm{locked} (geometry {leg.LastExpectedMiss * 100f:0.#}cm, " +
                       $"reach {leg.LastNatural * 100f:0.#}cm{rest}, needed {leg.LastNeeded * 100f:0.#}cm, " +
                       $"stretch x{leg.LastScale:0.00})";
            }
        }

        private static bool Finite(Vector3 v) =>
            !float.IsNaN(v.x + v.y + v.z) && !float.IsInfinity(v.x + v.y + v.z);

        private static void RestScales(Leg leg)
        {
            leg.Upper.localScale = leg.UpperRestScale;
            leg.Lower.localScale = leg.LowerRestScale;
            leg.Foot.localScale = leg.FootRestScale;
        }

        /// <summary>Scale the thigh alone: that lengthens both bones by exactly s. See ArmIK.ApplyStretch.</summary>
        private static void ApplyStretch(Leg leg, float s)
        {
            leg.Upper.localScale = leg.UpperRestScale * s;
            leg.Lower.localScale = leg.LowerRestScale;
            leg.Foot.localScale = leg.FootRestScale / s;
        }

        private static void Solve(Leg leg, Vector3 offset, Vector3 modelRight, Vector3 modelForward)
        {
            if (leg == null) return;
            if (!Interop.Alive(leg.Upper) || !Interop.Alive(leg.Lower) ||
                !Interop.Alive(leg.Foot) || !Interop.Alive(leg.SourceFoot)) return;

            try
            {
                var t = leg.SourceFoot.position + offset;
                if (!Finite(leg.Upper.position) || !Finite(leg.Lower.position) ||
                    !Finite(leg.Foot.position) || !Finite(t) ||
                    !float.IsFinite(leg.CurrentScale) || leg.CurrentScale < 0.5f)
                {
                    leg.CurrentScale = 1f;
                    leg.Stretched = false;
                    RestScales(leg);
                    leg.Foot.localPosition = leg.FootRestLocalPos;
                    return;
                }

                // Put the foot back on the end of the shin before measuring anything. The lock
                // at the bottom moves the foot bone to the target whenever the solve misses,
                // the retarget only writes rotations, so the offset stayed in the shin's frame
                // and was measured as shin length from then on — and a longer measured shin
                // makes the next solve miss by more. Every session began with a 164 cm miss on
                // the first frame (the game's display body far from ours) that left the leg
                // reading 195 cm instead of 85; under full-body tracking, where the game's
                // hip-to-foot distance exceeds this avatar's leg and the solve misses every
                // frame, the reach ran away to 390 cm in seconds — legs stretched off into
                // the distance. The arm solver had this exact fix in 0.42.0; the leg didn't.
                leg.Foot.localPosition = leg.FootRestLocalPos;

                // The retarget gave the foot the vanilla foot's orientation. The solve below
                // rotates the leg above it and carries it along; it is put back at the end.
                var footRotation = leg.Foot.rotation;

                var a = leg.Upper.position;
                var b = leg.Lower.position;
                var c = leg.Foot.position;

                var previous = leg.CurrentScale;
                var labRest = Vector3.Distance(a, b) / previous;
                var lcbRest = Vector3.Distance(b, c) / previous;
                if (labRest < 1e-5f || lcbRest < 1e-5f) return;

                var natural = labRest + lcbRest;
                var needed = Vector3.Distance(a, t);
                var maxStretch = 1f + Mathf.Clamp(ModConfig.LegStretch.Value, 0f, 1f);
                var wantScale = Mathf.Clamp(needed / natural, 1f, maxStretch);

                leg.LastNatural = natural;
                leg.LastNeeded = needed;
                leg.LastScale = wantScale;

                // Exact, not smoothed: the foot is pinned either way, and a longer shin is
                // less wrong than a foot off the end of it. See ArmIK for the reasoning.
                var s = wantScale;
                if (s > 1.0001f) { ApplyStretch(leg, s); leg.Stretched = true; }
                else if (leg.Stretched) { s = 1f; leg.Stretched = false; RestScales(leg); }
                else s = 1f;
                leg.CurrentScale = s;

                var lab = labRest * s;
                var lcb = lcbRest * s;
                leg.LastExpectedMiss = Mathf.Max(0f, needed - (lab + lcb));

                var lat = Mathf.Clamp(needed, 1e-3f, lab + lcb - 1e-3f);

                // Which way the knee points is never left to our own pose. A digitigrade rig
                // starts with a deeply bent knee and an ankle bent the other way, and the copied
                // rotations can put its knee behind the hip–foot line as easily as in front;
                // taking the bend plane from that pose then solved the knee backwards, and it
                // alternated as the knee crossed the line. Knees point forward. The vanilla
                // leg says which way forward is — its knee is always ahead of its hip–foot
                // line — and if it is standing dead straight, the model's own forward does.
                var hint = Vector3.zero;
                if (Interop.Alive(leg.SourceUpper) && Interop.Alive(leg.SourceLower))
                {
                    var srcLeg = leg.SourceFoot.position - leg.SourceUpper.position;
                    hint = Vector3.ProjectOnPlane(leg.SourceLower.position - leg.SourceUpper.position, srcLeg);
                }
                if (hint.sqrMagnitude < 0.03f * 0.03f) hint = modelForward;
                hint = Vector3.ProjectOnPlane(hint, c - a);
                if (hint.sqrMagnitude < 1e-8f) hint = modelForward;

                // If our knee is currently on the wrong side, mirror it across the hip–foot
                // line first: a half turn about that line keeps hip and foot where they are
                // and swings the knee, and the kneecap, round to the front.
                var kneeSide = Vector3.Dot(Vector3.ProjectOnPlane(b - a, c - a), hint);
                if (kneeSide < 0f)
                {
                    leg.Upper.rotation = Quaternion.AngleAxis(180f, (c - a).normalized) * leg.Upper.rotation;
                    b = leg.Lower.position;
                    c = leg.Foot.position;
                }

                var current0 = Vector3.Angle(c - a, b - a) * Mathf.Deg2Rad;
                var knee0 = Vector3.Angle(a - b, c - b) * Mathf.Deg2Rad;
                var current1 = Mathf.Acos(Mathf.Clamp((lcb * lcb - lab * lab - lat * lat) / (-2f * lab * lat), -1f, 1f));
                var knee1 = Mathf.Acos(Mathf.Clamp((lat * lat - lab * lab - lcb * lcb) / (-2f * lab * lcb), -1f, 1f));

                // Bend about the axis that puts the knee on the hint's side: with the leg
                // pointing down and the knee ahead, (foot−hip)×forward points to the model's
                // left, and a positive rotation about it straightens the knee.
                var axis = Vector3.Cross(c - a, hint);
                if (axis.sqrMagnitude < 1e-8f) axis = -modelRight;
                if (axis.sqrMagnitude < 1e-8f) return;
                axis.Normalize();

                leg.Upper.rotation = Quaternion.AngleAxis((current1 - current0) * Mathf.Rad2Deg, axis) * leg.Upper.rotation;
                leg.Lower.rotation = Quaternion.AngleAxis((knee1 - knee0) * Mathf.Rad2Deg, axis) * leg.Lower.rotation;

                var footNow = leg.Foot.position;
                leg.Upper.rotation = Quaternion.FromToRotation(footNow - a, t - a) * leg.Upper.rotation;

                leg.LastMiss = Vector3.Distance(leg.Foot.position, t);

                leg.Foot.rotation = footRotation;
                if (ModConfig.LegLockFeet.Value) leg.Foot.position = t;
            }
            catch { /* one bad frame must not stop the other leg */ }
        }
    }
}
