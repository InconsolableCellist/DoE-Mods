using System;
using CustomAvatars.Recon;
using UnityEngine;
using Interop = CustomAvatars.Recon.Interop;

namespace CustomAvatars.Avatars
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
            public string Side;
            public Transform Shoulder;
            public Transform Upper, Fore, Hand;
            public Transform Target;
            // The game's own bones for the same joints, when we have them: the honest test of
            // whether our shoulder is where the shoulder should be.
            public Transform SourceUpper, SourceHand;
            public Vector3 UpperRestScale = Vector3.one;
            public Vector3 ForeRestScale = Vector3.one;
            public Vector3 HandRestScale = Vector3.one;
            // The wrist as the modeller left it: the one orientation, relative to the forearm,
            // that is known to skin properly. Everything the twist code does is measured from it.
            public Quaternion HandRestLocal = Quaternion.identity;
            // From the hand target's frame to this rig's hand bone, measured from the bones at
            // build. What the bone is given every frame is the target's rotation times this.
            // See HandFromTargetFrame.
            public Quaternion HandFromTarget = Quaternion.identity;
            public string HandFrameNote = "";
            // Elbow-to-wrist, in the forearm's own space. The axis a forearm pronates about.
            public Vector3 ForeAxisLocal = Vector3.forward;
            public float CurrentScale = 1f;
            // How much each bone is actually lengthened; CurrentScale is the whole-arm figure.
            public float CurrentUpperScale = 1f, CurrentForeScale = 1f;
            public bool Stretched;

            // Kept for the diagnostic: how long the arm is, how far it is being asked to reach,
            // how far the solver actually left the hand from the target before the lock, and
            // how far the geometry says it had to.
            public float LastNatural, LastNeeded, LastScale, LastMiss, LastExpectedMiss;
            public float LastWristTwist, LastForearmTake;

            // The collarbone rotation we are currently contributing, and the two rotations that
            // let us tell "the retarget re-posed this bone" from "nobody touched it since we
            // wrote to it last frame".
            public float CurrentYield;
            public Quaternion PreYieldLocal, YieldedLocal;
            public bool HasYield;
        }

        private Arm _left, _right;

        public bool HasArms => _left != null || _right != null;

        /// <summary>
        /// True when either hand ended up further from its target than the arm's geometry can
        /// account for. A solver working correctly leaves exactly max(0, needed − reach), and
        /// anything well beyond that is a bug worth a full geometry dump.
        /// </summary>
        public bool Anomalous => IsAnomalous(_left) || IsAnomalous(_right);

        /// <summary>True when either solver miss is over a centimetre — worth a line in the log.</summary>
        public bool WorthLogging => (_left?.LastMiss ?? 0f) > 0.01f || (_right?.LastMiss ?? 0f) > 0.01f;

        private static bool IsAnomalous(Arm arm) =>
            arm != null && arm.LastMiss > arm.LastExpectedMiss + 0.05f;

        public string Build(GameObject model, AvatarManifest manifest, Transform leftTarget, Transform rightTarget,
                            Func<HumanBodyBones, Transform> sourceBone = null)
        {
            _left = BuildArm(model, manifest, "Left", leftTarget, sourceBone);
            _right = BuildArm(model, manifest, "Right", rightTarget, sourceBone);

            var count = (_left != null ? 1 : 0) + (_right != null ? 1 : 0);
            if (count == 0) return "no arm bones could be resolved";
            var notes = "";
            if (_left != null) notes += $"\n      L hand: {_left.HandFrameNote}";
            if (_right != null) notes += $"\n      R hand: {_right.HandFrameNote}";
            return $"{count} arm(s) driven from the hand targets{notes}";
        }

        private static Arm BuildArm(GameObject model, AvatarManifest manifest, string side, Transform target,
                                    Func<HumanBodyBones, Transform> sourceBone)
        {
            if (!Interop.Alive(target)) return null;
            var map = manifest?.rig?.humanoidBones;
            if (map == null) return null;

            Transform Bone(string name) =>
                map.TryGetValue(side + name, out var path) && !string.IsNullOrEmpty(path)
                    ? model.transform.Find(path) : null;

            // Optional: plenty of rigs have one, some don't, and the solver works either way.
            var shoulder = Bone("Shoulder");
            var upper = Bone("UpperArm");
            var fore = Bone("LowerArm");
            var hand = Bone("Hand");
            if (!Interop.Alive(upper) || !Interop.Alive(fore) || !Interop.Alive(hand)) return null;

            var arm = new Arm
            {
                Side = side,
                Shoulder = Interop.Alive(shoulder) ? shoulder : null,
                Upper = upper, Fore = fore, Hand = hand, Target = target,
                UpperRestScale = upper.localScale,
                ForeRestScale = fore.localScale,
                HandRestScale = hand.localScale,
                HandRestLocal = hand.localRotation,
            };

            // The bone points at its child, so the child's local position is the bone's axis.
            var axis = hand.localPosition;
            if (axis.sqrMagnitude < 1e-10f)
            {
                try { axis = fore.InverseTransformDirection(hand.position - fore.position); } catch { }
            }
            arm.ForeAxisLocal = axis.sqrMagnitude > 1e-10f ? axis.normalized : Vector3.forward;

            arm.HandFromTarget = HandFromTargetFrame(model, side, hand, fore, Bone, out arm.HandFrameNote);

            if (sourceBone != null)
            {
                var isLeft = side == "Left";
                try
                {
                    arm.SourceUpper = sourceBone(isLeft ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
                    arm.SourceHand = sourceBone(isLeft ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                }
                catch { }
            }
            return arm;
        }

        /// <summary>Solve both arms. Call after the retarget has posed the body.</summary>
        public void Apply()
        {
            Solve(_left);
            Solve(_right);
        }

        /// <summary>Put the bone scales back, before this solver is thrown away for a new one.</summary>
        public void Release()
        {
            foreach (var arm in new[] { _left, _right })
            {
                if (arm == null) continue;
                try { if (Interop.Alive(arm.Upper)) RestScales(arm); } catch { }
            }
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
                var yield = arm.CurrentYield >= 0.5f ? $", shoulder {arm.CurrentYield:0}°" : "";
                var off = "";
                if (Interop.Alive(arm.SourceUpper))
                {
                    try { off = $", shoulder off {Vector3.Distance(arm.Upper.position, arm.SourceUpper.position) * 100f:0.#}cm"; }
                    catch { }
                }
                var locked = ModConfig.ArmLockHands.Value ? " locked" : "";
                return $"miss {arm.LastMiss * 100f:0.#}cm{locked} (geometry {arm.LastExpectedMiss * 100f:0.#}cm, " +
                       $"reach {arm.LastNatural * 100f:0.#}cm, needed {arm.LastNeeded * 100f:0.#}cm, " +
                       $"stretch x{arm.LastScale:0.00}{yield}{off}, " +
                       $"wrist {arm.LastWristTwist:0}° fore {arm.LastForearmTake:0}°)";
            }
        }

        /// <summary>
        /// Everything about where the bones actually are, for the frame just solved. The
        /// one-line summary says *that* a hand missed; this says *why*, or at least narrows it
        /// to a bone: a joint that isn't where its game counterpart is, a scale that isn't what
        /// we set, a target that isn't where the controller is.
        /// </summary>
        public string DescribeGeometry()
        {
            return DescribeGeometry(_left) + "\n" + DescribeGeometry(_right);

            static string V(Vector3 v) => $"({v.x:0.000}, {v.y:0.000}, {v.z:0.000})";
            static string S(Vector3 v) => $"({v.x:0.###}, {v.y:0.###}, {v.z:0.###})";

            string DescribeGeometry(Arm arm)
            {
                if (arm == null) return "    (no arm)";
                try
                {
                    var a = arm.Upper.position;
                    var b = arm.Fore.position;
                    var c = arm.Hand.position;
                    var t = arm.Target.position;
                    var elbow = Vector3.Angle(a - b, c - b);
                    var lines =
                        $"    {arm.Side}: shoulder {(Interop.Alive(arm.Shoulder) ? V(arm.Shoulder.position) : "-")} " +
                        $"upper {V(a)} fore {V(b)} hand {V(c)} target {V(t)}\n" +
                        $"      |upper-fore| {Vector3.Distance(a, b) * 100f:0.#}cm |fore-hand| {Vector3.Distance(b, c) * 100f:0.#}cm " +
                        $"|upper-hand| {Vector3.Distance(a, c) * 100f:0.#}cm |upper-target| {Vector3.Distance(a, t) * 100f:0.#}cm " +
                        $"elbow {elbow:0}° stretch x{arm.CurrentScale:0.000}\n" +
                        $"      scale local upper {S(arm.Upper.localScale)} fore {S(arm.Fore.localScale)} hand {S(arm.Hand.localScale)} " +
                        $"| lossy upper {S(arm.Upper.lossyScale)} fore {S(arm.Fore.lossyScale)} hand {S(arm.Hand.lossyScale)}";
                    if (Interop.Alive(arm.SourceUpper) && Interop.Alive(arm.SourceHand))
                    {
                        var su = arm.SourceUpper.position;
                        var sh = arm.SourceHand.position;
                        lines += $"\n      game rig: upper {V(su)} hand {V(sh)} — our shoulder joint is " +
                                 $"{Vector3.Distance(a, su) * 100f:0.#}cm from theirs, their hand is " +
                                 $"{Vector3.Distance(sh, t) * 100f:0.#}cm from the target";
                    }
                    return lines;
                }
                catch (Exception e) { return $"    {arm.Side}: geometry unreadable ({e.GetType().Name})"; }
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

        /// <summary>
        /// The angle of a twist quaternion about its axis, signed, in (-180, 180]. Built so that
        /// <c>Quaternion.AngleAxis(result, axis)</c> gives the twist back.
        /// </summary>
        private static float SignedTwistDegrees(Quaternion twist, Vector3 axis)
        {
            var along = twist.x * axis.x + twist.y * axis.y + twist.z * axis.z;
            var degrees = 2f * Mathf.Atan2(along, twist.w) * Mathf.Rad2Deg;
            while (degrees > 180f) degrees -= 360f;
            while (degrees <= -180f) degrees += 360f;
            return degrees;
        }

        /// <summary>
        /// Rotate the collarbone toward a hand target that is out of the arm's reach, by no more
        /// than <c>ArmShoulderYieldDegrees</c>.
        ///
        /// The amount is derived from the avatar's own bones — how far short the arm falls, and
        /// how much reach this particular rig's collarbone can buy — so it is right for a rig
        /// with long arms and a rig with short ones without anybody tuning a number. Whatever is
        /// still out of reach afterwards falls through to stretching, as before.
        /// </summary>
        private static void YieldShoulder(Arm arm)
        {
            if (!Interop.Alive(arm.Shoulder)) return;

            // If the bone still holds exactly what we wrote last frame, nothing re-posed it, so
            // put it back before adding to it. Otherwise the rotation accumulates every frame on
            // any rig whose collarbone the retarget doesn't drive, and the shoulder slowly winds
            // itself round.
            if (arm.HasYield && arm.Shoulder.localRotation == arm.YieldedLocal)
                arm.Shoulder.localRotation = arm.PreYieldLocal;
            arm.HasYield = false;

            var maxDegrees = Mathf.Max(0f, ModConfig.ArmShoulderYieldDegrees.Value);

            var pivot = arm.Shoulder.position;
            var from = arm.Upper.position - pivot;
            var to = arm.Target.position - pivot;
            if (from.sqrMagnitude < 1e-8f || to.sqrMagnitude < 1e-8f) return;

            var wanted = 0f;
            var full = Quaternion.FromToRotation(from, to);
            full.ToAngleAxis(out var fullAngle, out var axis);
            if (axis.sqrMagnitude < 1e-8f || float.IsNaN(fullAngle)) return;

            if (maxDegrees > 0.01f)
            {
                var reach = Vector3.Distance(arm.Upper.position, arm.Fore.position) +
                            Vector3.Distance(arm.Fore.position, arm.Hand.position);
                var distance = Vector3.Distance(arm.Upper.position, arm.Target.position);
                var deficit = distance - reach;
                if (deficit > 0f)
                {
                    // Rotating all the way puts the shoulder joint on the line to the hand, which
                    // is the most reach this collarbone can possibly buy. Take the fraction of
                    // that which covers the shortfall, and no more — a shoulder that shrugs when
                    // it doesn't need to looks worse than an arm that's slightly short.
                    var gain = distance - Vector3.Distance(pivot + full * from, arm.Target.position);
                    if (gain > 1e-4f)
                        wanted = Mathf.Min(fullAngle * Mathf.Clamp01(deficit / gain), maxDegrees);
                }
            }

            // Ease in and out, so an arm hovering at the edge of its reach doesn't shrug on and
            // off every other frame.
            arm.CurrentYield = Mathf.Lerp(arm.CurrentYield, wanted, 0.35f);
            if (arm.CurrentYield < 0.01f) { arm.CurrentYield = 0f; return; }

            arm.PreYieldLocal = arm.Shoulder.localRotation;
            arm.Shoulder.rotation = Quaternion.AngleAxis(arm.CurrentYield, axis) * arm.Shoulder.rotation;
            arm.YieldedLocal = arm.Shoulder.localRotation;
            arm.HasYield = true;
        }

        private static bool Finite(Vector3 v) =>
            !float.IsNaN(v.x + v.y + v.z) && !float.IsInfinity(v.x + v.y + v.z);

        private static void RestScales(Arm arm)
        {
            arm.Upper.localScale = arm.UpperRestScale;
            arm.Fore.localScale = arm.ForeRestScale;
            arm.Hand.localScale = arm.HandRestScale;
        }

        /// <summary>
        /// Lengthen the upper arm by <paramref name="su"/> and the forearm by <paramref name="sf"/>,
        /// and nothing else.
        ///
        /// Scale compounds down a hierarchy. The first version of this scaled the upper arm AND
        /// the forearm by s, which made the forearm s² long in the world — and then solved the
        /// angles for lengths inflated once more on top, so at an 8% stretch the solver was
        /// planning for an arm 21% longer than the one it had. The forearm's offset from the
        /// elbow is measured in the upper arm's units, so the upper arm's scale sets the upper
        /// bone's length; the forearm's LOCAL scale is then sf over su, which leaves it at sf in
        /// the world, and the hand undoes that so the paw stays its own size. With sf = 1 the
        /// forearm mesh is not distorted at all — only the upper arm, which in first person you
        /// almost never see.
        /// </summary>
        private static void ApplyStretch(Arm arm, float su, float sf)
        {
            arm.Upper.localScale = arm.UpperRestScale * su;
            arm.Fore.localScale = arm.ForeRestScale * (sf / su);
            arm.Hand.localScale = arm.HandRestScale / sf;
        }

        /// <summary>
        /// Split a whole-arm stretch between the two bones. The extra length is shared by
        /// <c>ArmStretchUpperShare</c> (1 = all on the upper arm), and each bone's own factor
        /// is that share of the extra over its own rest length, so the total still comes to
        /// exactly what the solve asked for whatever the rig's proportions are.
        /// </summary>
        private static void SplitStretch(float s, float labRest, float lcbRest, out float su, out float sf)
        {
            var share = Mathf.Clamp01(ModConfig.ArmStretchUpperShare.Value);
            var extra = (s - 1f) * (labRest + lcbRest);
            su = 1f + share * extra / labRest;
            sf = 1f + (1f - share) * extra / lcbRest;
            if (!float.IsFinite(su) || su < 1f) su = 1f;
            if (!float.IsFinite(sf) || sf < 1f) sf = 1f;
        }

        private static void Solve(Arm arm)
        {
            if (arm == null) return;
            if (!Interop.Alive(arm.Upper) || !Interop.Alive(arm.Fore) ||
                !Interop.Alive(arm.Hand) || !Interop.Alive(arm.Target)) return;

            try
            {
                // NaN firewall. A single non-finite frame — a bad IK target upstream was
                // enough in testing — must not be allowed to stick: CurrentScale lerps toward
                // itself, so once it goes NaN the bone scales stay NaN and the mesh stays
                // broken long after the cause is gone. Skip the frame, put the bones back to
                // rest, and the next clean frame carries on as if nothing happened.
                if (!Finite(arm.Upper.position) || !Finite(arm.Fore.position) ||
                    !Finite(arm.Hand.position) || !Finite(arm.Target.position) ||
                    !float.IsFinite(arm.CurrentScale) || arm.CurrentScale < 0.5f)
                {
                    arm.CurrentScale = 1f;
                    arm.CurrentUpperScale = 1f;
                    arm.CurrentForeScale = 1f;
                    arm.Stretched = false;
                    arm.CurrentYield = 0f;
                    arm.HasYield = false;
                    RestScales(arm);
                    return;
                }

                // Buy reach from the collarbone before resorting to stretching bones. Measured
                // in a session: hands were missing their targets by up to 10 cm at full
                // extension, and the log said why — `needed` was simply larger than `reach`,
                // every time. Not a solver bug. A custom avatar is rarely the same proportions
                // as the game character, but your controllers are where your real hands are, so
                // the avatar has to cover the difference somehow. Your own shoulder does this:
                // reach for something far away and your collarbone comes with you.
                YieldShoulder(arm);

                var a = arm.Upper.position;
                var b = arm.Fore.position;
                var c = arm.Hand.position;
                var t = arm.Target.position;

                // These positions were produced by whatever stretch we wrote last frame, so
                // divide it back out to get the arm's own length. Measuring the bones rather
                // than caching a length at build keeps this right when the whole model is
                // rescaled to fit the player.
                var labRest = Vector3.Distance(a, b) / arm.CurrentUpperScale;
                var lcbRest = Vector3.Distance(b, c) / arm.CurrentForeScale;
                if (labRest < 1e-5f || lcbRest < 1e-5f) return;

                var natural = labRest + lcbRest;
                var needed = Vector3.Distance(a, t);
                var maxStretch = 1f + Mathf.Clamp(ModConfig.ArmStretch.Value, 0f, 1f);
                var wantScale = Mathf.Clamp(needed / natural, 1f, maxStretch);

                arm.LastNatural = natural;
                arm.LastNeeded = needed;
                arm.LastScale = wantScale;

                // Exactly what is needed, this frame, no smoothing. The old lerp existed so a
                // hand at the edge of reach wouldn't pop between lengths; now the hand is on
                // the target either way and the only question is whether the arm or the wrist
                // skin covers the distance. In first person a longer forearm is far less
                // visible than a wrist pulled off the end of it, so the arm takes as much as
                // ArmStretch allows and the wrist lock only gets the remainder.
                var s = wantScale;
                float su = 1f, sf = 1f;
                if (s > 1.0001f)
                {
                    SplitStretch(s, labRest, lcbRest, out su, out sf);
                    ApplyStretch(arm, su, sf);
                    arm.Stretched = true;
                }
                else if (arm.Stretched) { s = 1f; arm.Stretched = false; RestScales(arm); }
                else s = 1f;
                arm.CurrentScale = s;
                arm.CurrentUpperScale = su;
                arm.CurrentForeScale = sf;

                // The lengths the bones have NOW, after this frame's stretch. Each bone is
                // scaled along itself about its own joint, so a, b, c still describe the bend
                // even though b and c have moved along their bones.
                var lab = labRest * su;
                var lcb = lcbRest * sf;
                arm.LastExpectedMiss = Mathf.Max(0f, needed - (lab + lcb));

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

                // What the solve alone achieved, before anything below papers over it. This is
                // the number that says whether the solver is working: it should never be more
                // than the geometric shortfall.
                arm.LastMiss = Vector3.Distance(arm.Hand.position, t);

                // The target's orientation, said in this rig's own hand-bone convention.
                var handRotation = arm.Target.rotation * arm.HandFromTarget;
                Pronate(arm, handRotation);
                arm.Hand.rotation = handRotation;

                // Dead on, whatever happened above. Moving the hand bone by itself pulls the
                // skin at the wrist rather than lengthening the arm, and the skin is
                // weight-blended across that joint, so a few centimetres are invisible and a
                // real miss shows as a stretched wrist instead of a hand floating off the
                // weapon it is supposed to be holding. The solver miss above is still logged,
                // so this can't hide a bug — it just stops you seeing it in the headset.
                if (ModConfig.ArmLockHands.Value) arm.Hand.position = t;
            }
            catch { /* one bad frame must not stop the other arm */ }
        }

        /// <summary>
        /// The rotation from the hand target's frame to this rig's hand bone, so the bone can be
        /// handed the target's orientation without anyone caring how the rig labels its axes.
        ///
        /// The game's hand targets are laid out the way you would expect a controller to be:
        /// Z along the fingers, Y out the back of the hand. What a hand BONE calls its axes is
        /// up to whoever rigged it. One Blender export runs Y down the fingers with Z out the
        /// back of the hand; another runs Y down the fingers with X out the back. Writing the
        /// target's rotation straight onto both leaves the second one's palm rolled ninety
        /// degrees about the forearm, which is exactly how one tester's avatar arrived. The
        /// (-90, 0, 180) that used to be tuned into the config was nothing but the first rig's
        /// answer to this question, and it was wrong for everyone else.
        ///
        /// So the target's frame is rebuilt here from geometry every rig agrees on — wrist to
        /// the middle knuckle for the fingers, little knuckle to index knuckle across the palm,
        /// and their cross product for the back of the hand (which side that is depends on
        /// which hand) — and the bone is measured against it in the rest pose. A rig with no
        /// finger bones gets the forearm's own direction for the fingers and the T-pose
        /// convention (palms facing the floor) for the back, the same last resort the finger
        /// poser uses. Measured on the rig that (-90, 0, 180) was tuned against, this comes
        /// out at exactly (-90, 0, 180).
        /// </summary>
        private static Quaternion HandFromTargetFrame(GameObject model, string side, Transform hand, Transform fore,
                                                      Func<string, Transform> bone, out string note)
        {
            var isLeft = side == "Left";
            Vector3 fingers = Vector3.zero, back = Vector3.zero;
            var source = "";

            Transform First(params string[] names)
            {
                foreach (var name in names)
                {
                    try { var t = bone(name); if (Interop.Alive(t)) return t; } catch { }
                }
                return null;
            }

            try
            {
                var middle = First("MiddleProximal", "IndexProximal", "RingProximal");
                var index = First("IndexProximal", "MiddleProximal");
                var little = First("LittleProximal", "RingProximal", "MiddleProximal");
                if (middle != null && index != null && little != null && index != little)
                {
                    var f = middle.position - hand.position;
                    var across = index.position - little.position;
                    var angle = Vector3.Angle(f, across);
                    if (f.sqrMagnitude > 1e-10f && across.sqrMagnitude > 1e-10f && angle > 15f && angle < 165f)
                    {
                        fingers = f;
                        // Fingers x across-the-palm is out of the back of a left hand and out of
                        // the palm of a right one: the two hands are mirror images.
                        back = isLeft ? Vector3.Cross(f, across) : Vector3.Cross(across, f);
                        source = $"the knuckles ({Interop.Name(middle)}, {Interop.Name(little)} to {Interop.Name(index)})";
                    }
                }
            }
            catch { }

            if (fingers.sqrMagnitude < 1e-10f)
            {
                try
                {
                    fingers = hand.position - fore.position;
                    back = model.transform.up;
                    source = "the forearm and the T-pose convention (no finger bones to measure)";
                }
                catch { }
            }

            if (fingers.sqrMagnitude < 1e-10f || Vector3.Cross(fingers, back).sqrMagnitude < 1e-12f)
            {
                note = "axes could not be measured; the target's frame is used as-is";
                return Quaternion.identity;
            }

            var frame = Quaternion.LookRotation(fingers.normalized, back.normalized);
            var boneInFrame = Quaternion.Inverse(frame) * hand.rotation;
            note = $"fingers along the bone's {Closest(boneInFrame, Vector3.forward)}, " +
                   $"back of the hand along its {Closest(boneInFrame, Vector3.up)}, from {source}";
            return boneInFrame;

            // Which of the bone's own axes (as the frame sees them) lies nearest a frame direction.
            static string Closest(Quaternion boneInFrame, Vector3 direction)
            {
                var best = ""; var bestDot = 0f;
                foreach (var (axis, name) in new[] { (Vector3.right, "X"), (Vector3.up, "Y"), (Vector3.forward, "Z") })
                {
                    var d = Vector3.Dot(boneInFrame * axis, direction);
                    if (Mathf.Abs(d) <= Mathf.Abs(bestDot)) continue;
                    bestDot = d;
                    best = (d < 0f ? "-" : "+") + name;
                }
                return best;
            }
        }

        /// <summary>
        /// Split the roll the controller asks for between the forearm and the wrist, and never
        /// let the wrist carry more than it can skin.
        ///
        /// A real forearm pronates along most of its length; putting the whole turn on the
        /// wrist joint pinches the mesh into a straw when you rotate your palm up. The first
        /// version shared the roll, but measured it against the hand orientation the retarget
        /// had left — which for your own body is the idle animation's wrist, whose roll bears
        /// no relation to your controller's. The wrist was left holding half of (controller
        /// minus animation), which on the right arm was routinely past ninety degrees, and past
        /// ninety degrees is the sliver.
        ///
        /// This measures the twist the wrist would have to hold relative to the bind pose — the
        /// one orientation known to skin properly — hands <c>ArmTwistShare</c> of it to the
        /// forearm, and then turns the forearm further if what remains is over
        /// <c>ArmWristTwistLimitDegrees</c>. Rotating the forearm about its own axis leaves the
        /// hand exactly where the solver put it, so none of this costs any accuracy.
        /// </summary>
        private static void Pronate(Arm arm, Quaternion handRotation)
        {
            var share = Mathf.Clamp01(ModConfig.ArmTwistShare.Value);
            var limit = Mathf.Clamp(ModConfig.ArmWristTwistLimitDegrees.Value, 0f, 180f);

            // The wrist's local rotation once it takes the controller's orientation, and how
            // that differs from the bind pose, both in the forearm's frame.
            var handLocal = Quaternion.Inverse(arm.Fore.rotation) * handRotation;
            var deviation = handLocal * Quaternion.Inverse(arm.HandRestLocal);
            var twist = TwistAbout(deviation, arm.ForeAxisLocal);
            var angle = SignedTwistDegrees(twist, arm.ForeAxisLocal);

            var take = angle * share;
            var left = angle - take;
            if (Mathf.Abs(left) > limit) take += left - Mathf.Sign(left) * limit;

            arm.LastWristTwist = angle - take;
            arm.LastForearmTake = take;
            if (Mathf.Abs(take) < 0.01f) return;

            // About the forearm's own axis: the hand sits on that axis, so it doesn't move.
            arm.Fore.rotation = arm.Fore.rotation * Quaternion.AngleAxis(take, arm.ForeAxisLocal);
        }
    }
}
