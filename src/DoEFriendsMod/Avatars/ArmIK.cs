using System;
using DoEFriendsMod.Recon;
using UnityEngine;
using Interop = DoEFriendsMod.Recon.Interop;

namespace DoEFriendsMod.Avatars
{
    /// <summary>
    /// A two-bone analytic IK for the arms, aimed at the game's hand targets.
    ///
    /// This exists because the two pose sources are each right about different things. On your
    /// own client the hand IK *targets* are accurate — they're driven straight from your
    /// controllers, and VRIK tracked them 1:1 — while the vanilla third-person *body's* arms
    /// are not fully solved, because normally nobody looks at them. Retargeting copies the body,
    /// so it inherits that. This takes the accurate half of each: everything below the shoulders
    /// from the retarget, the arms from the targets.
    ///
    /// Analytic rather than iterative: a two-bone chain has a closed-form solution, so there is
    /// nothing to converge, nothing to tune, and no way for it to run away.
    /// </summary>
    public class ArmIK
    {
        private class Arm
        {
            public Transform Upper, Fore, Hand;
            public Transform Target;
            public Vector3 UpperRestScale = Vector3.one;
            public Vector3 ForeRestScale = Vector3.one;
            public Vector3 HandRestScale = Vector3.one;
            public float CurrentScale = 1f;
            public bool Stretched;

            // Kept for the diagnostic: how long the arm is, how far it is being asked to reach.
            public float LastNatural, LastNeeded, LastScale;
        }

        private Arm _left, _right;

        public bool HasArms => _left != null || _right != null;

        public string Build(GameObject model, AvatarManifest manifest, Transform leftTarget, Transform rightTarget)
        {
            _left = BuildArm(model, manifest, "Left", leftTarget);
            _right = BuildArm(model, manifest, "Right", rightTarget);

            var count = (_left != null ? 1 : 0) + (_right != null ? 1 : 0);
            return count == 0 ? "no arm bones could be resolved" : $"{count} arm(s) driven from the hand targets";
        }

        private static Arm BuildArm(GameObject model, AvatarManifest manifest, string side, Transform target)
        {
            if (!Interop.Alive(target)) return null;
            var map = manifest?.rig?.humanoidBones;
            if (map == null) return null;

            Transform Bone(string name) =>
                map.TryGetValue(side + name, out var path) && !string.IsNullOrEmpty(path)
                    ? model.transform.Find(path) : null;

            var upper = Bone("UpperArm");
            var fore = Bone("LowerArm");
            var hand = Bone("Hand");
            if (!Interop.Alive(upper) || !Interop.Alive(fore) || !Interop.Alive(hand)) return null;

            return new Arm
            {
                Upper = upper, Fore = fore, Hand = hand, Target = target,
                UpperRestScale = upper.localScale,
                ForeRestScale = fore.localScale,
                HandRestScale = hand.localScale,
            };
        }

        /// <summary>Solve both arms. Call after the retarget has posed the body.</summary>
        public void Apply()
        {
            Solve(_left);
            Solve(_right);
        }

        /// <summary>
        /// How far each hand ends up from where it was asked to be. If this is large, the
        /// problem is not the solver — it is that the target isn't where we think, or the
        /// shoulder is in the wrong place.
        /// </summary>
        public string Describe()
        {
            return $"L {Describe(_left)} | R {Describe(_right)}";

            string Describe(Arm arm)
            {
                if (arm == null || !Interop.Alive(arm.Hand) || !Interop.Alive(arm.Target)) return "-";
                var miss = Vector3.Distance(arm.Hand.position, arm.Target.position);
                return $"miss {miss * 100f:0.#}cm (reach {arm.LastNatural * 100f:0.#}cm, " +
                       $"needed {arm.LastNeeded * 100f:0.#}cm, stretch x{arm.LastScale:0.00})";
            }
        }

        /// <summary>
        /// The part of a rotation that spins about <paramref name="axis"/>, discarding the part
        /// that tips away from it — the swing-twist decomposition.
        /// </summary>
        private static Quaternion TwistAbout(Quaternion q, Vector3 axis)
        {
            var r = new Vector3(q.x, q.y, q.z);
            var projected = Vector3.Project(r, axis);
            var twist = new Quaternion(projected.x, projected.y, projected.z, q.w);

            var lengthSq = twist.x * twist.x + twist.y * twist.y + twist.z * twist.z + twist.w * twist.w;
            if (lengthSq < 1e-8f) return Quaternion.identity;

            var inverseLength = 1f / Mathf.Sqrt(lengthSq);
            return new Quaternion(twist.x * inverseLength, twist.y * inverseLength,
                                  twist.z * inverseLength, twist.w * inverseLength);
        }

        private static void Solve(Arm arm)
        {
            if (arm == null) return;
            if (!Interop.Alive(arm.Upper) || !Interop.Alive(arm.Fore) ||
                !Interop.Alive(arm.Hand) || !Interop.Alive(arm.Target)) return;

            try
            {
                var a = arm.Upper.position;
                var b = arm.Fore.position;
                var c = arm.Hand.position;
                var t = arm.Target.position;

                var lab = Vector3.Distance(a, b);
                var lcb = Vector3.Distance(b, c);
                if (lab < 1e-5f || lcb < 1e-5f) return;

                // Actually lengthen the arm when the target is out of reach, by scaling the
                // bones — the previous version only pretended to, inflating the lengths used in
                // the angle solve while leaving the bones their real size, so the hand pointed
                // correctly and still stopped short. That is why raising the limit changed
                // nothing.
                var natural = lab + lcb;
                var needed = Vector3.Distance(a, t);
                var maxStretch = 1f + Mathf.Clamp(ModConfig.ArmStretch.Value, 0f, 1f);
                var wantScale = natural > 1e-4f ? Mathf.Clamp(needed / natural, 1f, maxStretch) : 1f;

                arm.LastNatural = natural;
                arm.LastNeeded = needed;
                arm.LastScale = wantScale;

                if (wantScale > 1.0001f || arm.Stretched)
                {
                    // Smooth, so an arm at the edge of reach doesn't pop between lengths.
                    arm.CurrentScale = Mathf.Lerp(arm.CurrentScale, wantScale, 0.35f);
                    var s = arm.CurrentScale;
                    arm.Upper.localScale = arm.UpperRestScale * s;
                    arm.Fore.localScale = arm.ForeRestScale * s;
                    // Keep the hand its normal size; only the limb stretches.
                    arm.Hand.localScale = arm.HandRestScale / s;
                    arm.Stretched = s > 1.0001f;

                    lab *= s;
                    lcb *= s;
                }

                var lat = Mathf.Clamp(needed, 1e-3f, lab + lcb - 1e-3f);

                var current0 = Vector3.Angle(c - a, b - a) * Mathf.Deg2Rad;
                var elbow0 = Vector3.Angle(a - b, c - b) * Mathf.Deg2Rad;

                var current1 = Mathf.Acos(Mathf.Clamp((lcb * lcb - lab * lab - lat * lat) / (-2f * lab * lat), -1f, 1f));
                var elbow1 = Mathf.Acos(Mathf.Clamp((lat * lat - lab * lab - lcb * lcb) / (-2f * lab * lcb), -1f, 1f));

                // Bend about the plane the arm is already in, so the elbow keeps pointing the
                // way the retargeted pose put it rather than snapping to an arbitrary side.
                var axis = Vector3.Cross(c - a, b - a);
                if (axis.sqrMagnitude < 1e-8f) axis = Vector3.Cross(c - a, arm.Upper.up);
                if (axis.sqrMagnitude < 1e-8f) return;
                axis.Normalize();

                arm.Upper.rotation = Quaternion.AngleAxis((current1 - current0) * Mathf.Rad2Deg, axis) * arm.Upper.rotation;
                arm.Fore.rotation = Quaternion.AngleAxis((elbow1 - elbow0) * Mathf.Rad2Deg, axis) * arm.Fore.rotation;

                // Now swing the whole arm so the hand lands on the target.
                var handNow = arm.Hand.position;
                arm.Upper.rotation = Quaternion.FromToRotation(handNow - a, t - a) * arm.Upper.rotation;

                // Share the roll with the forearm. A real forearm pronates along most of its
                // length; putting the whole turn on the wrist joint pinches the mesh into a
                // straw when you rotate your palm up. Only the twist component is passed back —
                // bending the elbow here would move the hand off the target we just hit.
                var share = Mathf.Clamp01(ModConfig.ArmTwistShare.Value);
                if (share > 0.001f)
                {
                    var forearmAxis = arm.Hand.position - arm.Fore.position;
                    if (forearmAxis.sqrMagnitude > 1e-8f)
                    {
                        forearmAxis.Normalize();
                        var rollNeeded = arm.Target.rotation * Quaternion.Inverse(arm.Hand.rotation);
                        var twist = TwistAbout(rollNeeded, forearmAxis);

                        // Spread it across both bones. A real arm pronates along its whole
                        // length, and putting the entire turn below the elbow still pinches the
                        // mesh at the extremes — palm fully down was still collapsing to a
                        // straw even after the forearm started helping.
                        var upperShare = share * 0.4f;
                        if (Interop.Alive(arm.Upper))
                            arm.Upper.rotation = Quaternion.Slerp(Quaternion.identity, twist, upperShare) * arm.Upper.rotation;
                        arm.Fore.rotation = Quaternion.Slerp(Quaternion.identity, twist, share) * arm.Fore.rotation;
                    }
                }

                arm.Hand.rotation = arm.Target.rotation;
            }
            catch { /* one bad frame must not stop the other arm */ }
        }
    }
}
