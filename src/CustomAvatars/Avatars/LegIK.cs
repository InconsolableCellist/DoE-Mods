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
            public float CurrentScale = 1f;
            public bool Stretched;
            public float LastNatural, LastNeeded, LastScale, LastMiss, LastExpectedMiss;
        }

        private Leg _left, _right;

        public bool HasLegs => _left != null || _right != null;

        /// <summary>True when either solver miss is over a centimetre.</summary>
        public bool WorthLogging => (_left?.LastMiss ?? 0f) > 0.01f || (_right?.LastMiss ?? 0f) > 0.01f;

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

            return new Leg
            {
                Side = side,
                Upper = upper, Lower = lower, Foot = foot,
                SourceUpper = Interop.Alive(srcUpper) ? srcUpper : null,
                SourceLower = Interop.Alive(srcLower) ? srcLower : null,
                SourceFoot = srcFoot,
                UpperRestScale = upper.localScale,
                LowerRestScale = lower.localScale,
                FootRestScale = foot.localScale,
            };
        }

        /// <summary>
        /// Solve both legs. Call after the retarget has posed the body and the head anchor has
        /// placed it. <paramref name="offset"/> is added to every target: for your own avatar
        /// the game's display body lags behind where you actually are, so its feet are re-based
        /// from the lagging body onto ours.
        /// </summary>
        public void Apply(Vector3 offset, Vector3 modelRight)
        {
            Solve(_left, offset, modelRight);
            Solve(_right, offset, modelRight);
        }

        /// <summary>Put the bone scales back, before this solver is thrown away for a new one.</summary>
        public void Release()
        {
            foreach (var leg in new[] { _left, _right })
            {
                if (leg == null) continue;
                try { if (Interop.Alive(leg.Upper)) RestScales(leg); } catch { }
            }
        }

        public string Describe()
        {
            return $"L {Describe(_left)} | R {Describe(_right)}";

            string Describe(Leg leg)
            {
                if (leg == null) return "-";
                var locked = ModConfig.LegLockFeet.Value ? " locked" : "";
                return $"miss {leg.LastMiss * 100f:0.#}cm{locked} (geometry {leg.LastExpectedMiss * 100f:0.#}cm, " +
                       $"reach {leg.LastNatural * 100f:0.#}cm, needed {leg.LastNeeded * 100f:0.#}cm, " +
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

        private static void Solve(Leg leg, Vector3 offset, Vector3 modelRight)
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
                    return;
                }

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

                var s = 1f;
                if (wantScale > 1.0001f || leg.Stretched)
                {
                    leg.CurrentScale = Mathf.Lerp(leg.CurrentScale, wantScale, 0.35f);
                    s = leg.CurrentScale;
                    ApplyStretch(leg, s);
                    leg.Stretched = s > 1.0001f;
                    if (!leg.Stretched) { leg.CurrentScale = 1f; s = 1f; RestScales(leg); }
                }

                var lab = labRest * s;
                var lcb = lcbRest * s;
                leg.LastExpectedMiss = Mathf.Max(0f, needed - (lab + lcb));

                var lat = Mathf.Clamp(needed, 1e-3f, lab + lcb - 1e-3f);

                var current0 = Vector3.Angle(c - a, b - a) * Mathf.Deg2Rad;
                var knee0 = Vector3.Angle(a - b, c - b) * Mathf.Deg2Rad;
                var current1 = Mathf.Acos(Mathf.Clamp((lcb * lcb - lab * lab - lat * lat) / (-2f * lab * lat), -1f, 1f));
                var knee1 = Mathf.Acos(Mathf.Clamp((lat * lat - lab * lab - lcb * lcb) / (-2f * lab * lcb), -1f, 1f));

                // Bend in the plane the copied pose already has the knee in. A standing leg is
                // nearly straight, which makes that plane ill-defined, so fall back to the
                // vanilla leg's own plane, and past that to "knees forward": with the leg
                // pointing down and the knee ahead of it, (foot−hip)×(knee−hip) points to the
                // model's left.
                var axis = Vector3.Cross(c - a, b - a);
                if (axis.sqrMagnitude < 1e-6f && Interop.Alive(leg.SourceUpper) && Interop.Alive(leg.SourceLower))
                    axis = Vector3.Cross(leg.SourceFoot.position - leg.SourceUpper.position,
                                         leg.SourceLower.position - leg.SourceUpper.position);
                if (axis.sqrMagnitude < 1e-6f) axis = -modelRight;
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
