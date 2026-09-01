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

            return new Arm { Upper = upper, Fore = fore, Hand = hand, Target = target };
        }

        /// <summary>Solve both arms. Call after the retarget has posed the body.</summary>
        public void Apply()
        {
            Solve(_left);
            Solve(_right);
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

                // Just short of full extension: a perfectly straight arm has no bend plane, and
                // the next frame would have nothing to rotate about.
                var lat = Mathf.Clamp(Vector3.Distance(a, t), 1e-3f, lab + lcb - 1e-3f);

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

                arm.Hand.rotation = arm.Target.rotation;
            }
            catch { /* one bad frame must not stop the other arm */ }
        }
    }
}
