using System;
using System.Collections.Generic;
using CustomAvatars.Recon;
using UnityEngine;
using Il2Cpp;
using Interop = CustomAvatars.Recon.Interop;

namespace CustomAvatars.Avatars
{
    /// <summary>
    /// Copies the game's own character pose onto the custom avatar, instead of asking VRIK to
    /// re-derive it.
    ///
    /// The game already solves a correct, fully animated pose for every player — head, arms,
    /// spine and legs — because that is what a vanilla character looks like. `Model_&lt;nick&gt;`
    /// carries a humanoid Animator (`RemoteAnimator`, `isHuman = true`) for local and remote
    /// players alike. Reading that is strictly better than reconstructing it: it can't run away,
    /// it needs no targets, and remote players get walking legs for free.
    ///
    /// Retargeting is done by DELTA, not by absolute rotation: each frame we apply the source
    /// bone's rotation *change since capture* to the destination bone. That makes it immune to
    /// the two skeletons having different rest poses and different bone axes — a UE4-style rig
    /// and a VRChat rig will never agree on absolute orientation, but they agree perfectly on
    /// "this elbow bent 40 degrees".
    ///
    /// Fingers are deliberately excluded: <see cref="HandPoser"/> owns those.
    /// </summary>
    public class PoseRetargeter
    {
        private class Link
        {
            public Transform Source;
            public Transform Target;
            public Quaternion SourceRestInverse;
            public Quaternion SourceRestLocal;
            public Quaternion TargetRest;
            public Quaternion TargetRestLocal;
            public int Depth;
            public string Name;
            public HumanBodyBones Bone;
        }

        /// <summary>
        /// Which bone each one points at, for working out a limb's direction. Only used to
        /// align the two skeletons at capture; a missing entry just means no alignment.
        /// </summary>
        private static readonly (HumanBodyBones parent, HumanBodyBones[] child)[] ChildOf =
        {
            (HumanBodyBones.Hips, new[]{ HumanBodyBones.Spine }),
            (HumanBodyBones.Spine, new[]{ HumanBodyBones.Chest, HumanBodyBones.UpperChest, HumanBodyBones.Neck }),
            (HumanBodyBones.Chest, new[]{ HumanBodyBones.UpperChest, HumanBodyBones.Neck, HumanBodyBones.Head }),
            (HumanBodyBones.UpperChest, new[]{ HumanBodyBones.Neck, HumanBodyBones.Head }),
            (HumanBodyBones.Neck, new[]{ HumanBodyBones.Head }),
            (HumanBodyBones.LeftShoulder, new[]{ HumanBodyBones.LeftUpperArm }),
            (HumanBodyBones.LeftUpperArm, new[]{ HumanBodyBones.LeftLowerArm }),
            (HumanBodyBones.LeftLowerArm, new[]{ HumanBodyBones.LeftHand }),
            (HumanBodyBones.RightShoulder, new[]{ HumanBodyBones.RightUpperArm }),
            (HumanBodyBones.RightUpperArm, new[]{ HumanBodyBones.RightLowerArm }),
            (HumanBodyBones.RightLowerArm, new[]{ HumanBodyBones.RightHand }),
            (HumanBodyBones.LeftUpperLeg, new[]{ HumanBodyBones.LeftLowerLeg }),
            (HumanBodyBones.LeftLowerLeg, new[]{ HumanBodyBones.LeftFoot }),
            (HumanBodyBones.LeftFoot, new[]{ HumanBodyBones.LeftToes }),
            (HumanBodyBones.RightUpperLeg, new[]{ HumanBodyBones.RightLowerLeg }),
            (HumanBodyBones.RightLowerLeg, new[]{ HumanBodyBones.RightFoot }),
            (HumanBodyBones.RightFoot, new[]{ HumanBodyBones.RightToes }),
        };

        /// <summary>
        /// What a hand points at, for the same alignment.
        ///
        /// Hands are the one bone in the chain with nothing below them that we retarget —
        /// fingers belong to <see cref="HandPoser"/> — so they were the one bone that never got
        /// aligned, and kept whatever ninety-degree difference the two rigs happened to have.
        /// On a player you never saw it, because the arm solver overwrites the wrist with the
        /// controller's rotation. On a mannequin, which has no controller, it showed up as a
        /// pair of wrists cocked straight out sideways.
        ///
        /// The fingers are still there to point at even though we don't drive them, so the hand
        /// can be aligned against the knuckle it leads to like every other bone.
        /// </summary>
        private static readonly (HumanBodyBones hand, HumanBodyBones[] knuckle)[] HandPointsAt =
        {
            (HumanBodyBones.LeftHand, new[]{ HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftIndexProximal }),
            (HumanBodyBones.RightHand, new[]{ HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightIndexProximal }),
        };

        // Parent-first order matters: we assign world rotations, and moving a parent carries its
        // children with it, so the spine has to settle before the arms hanging off it.
        private static readonly HumanBodyBones[] Bones =
        {
            HumanBodyBones.Hips,
            HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest,
            HumanBodyBones.Neck, HumanBodyBones.Head,
            HumanBodyBones.LeftShoulder, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
            HumanBodyBones.RightShoulder, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes,
            HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, HumanBodyBones.RightToes,
        };

        private readonly List<Link> _links = new List<Link>();

        // One bone we watch, so a peer that won't animate can say which half is broken.
        private const HumanBodyBones ProbeBone = HumanBodyBones.LeftUpperArm;
        private Link _probe;
        private Quaternion _probeWrote;
        private bool _probeWroteValid;

        private Transform _sourceHips, _targetHips;
        private Vector3 _sourceHipsRestLocal, _targetHipsRestLocal;
        /// <summary>The model we are posing. Not `Transform.root`: a mannequin is parented.</summary>
        private Transform _targetRoot;

        public int LinkCount => _links.Count;

        /// <summary>
        /// How far something else turned our probe bone between our own two writes, in degrees.
        /// Zero means we are the only thing posing this avatar; a large number means we are
        /// being overwritten every frame. -1 until it has been measured.
        /// </summary>
        public float OverwrittenDegrees { get; private set; } = -1f;

        /// <summary>Where the game rig's hips are right now, for following a travelling ragdoll.</summary>
        public Vector3? SourceHipsPosition =>
            Interop.Alive(_sourceHips) ? _sourceHips.position : (Vector3?)null;

        /// <summary>Our hips' offset from our own root, so the two can be lined up.</summary>
        public Vector3? TargetHipsOffset =>
            Interop.Alive(_targetHips) && Interop.Alive(_targetRoot)
                ? _targetHips.position - _targetRoot.position : (Vector3?)null;

        /// <summary>Where our hips are right now, to compare with the game rig's.</summary>
        public Vector3? TargetHipsPosition =>
            Interop.Alive(_targetHips) ? _targetHips.position : (Vector3?)null;

        /// <summary>The node the game rig's hips are measured under (`root` on the vanilla body).</summary>
        public Transform SourceHipsParent =>
            Interop.Alive(_sourceHips) ? _sourceHips.parent : null;

        /// <summary>The game rig's hips, local to their parent, right now.</summary>
        public Vector3? SourceHipsLocalNow =>
            Interop.Alive(_sourceHips) ? _sourceHips.localPosition : (Vector3?)null;

        /// <summary>
        /// The origin the hips translation is measured from: where the game rig's hips sat,
        /// local to their parent, when the reference was taken. Every frame's hips shift is
        /// the distance from here, so this has to be where the hips sit on a standing, solved
        /// body. Taken from a ragdoll, or from a body the game was still lifting into its
        /// spawn, it is wrong by the whole difference — for as long as the avatar is worn.
        /// </summary>
        public Vector3? SourceHipsRestLocal =>
            Interop.Alive(_sourceHips) ? _sourceHipsRestLocal : (Vector3?)null;

        /// <summary>Replace the hips origin with one known to be a standing body's.</summary>
        public void SetSourceHipsRest(Vector3 local) => _sourceHipsRestLocal = local;

        /// <summary>
        /// Hold our hips at their own rest instead of following the game's. For when the
        /// origin is known to be bad and nothing better is known yet: a body standing at its
        /// bind pose is right to within a crouch, a body shoved a hip's height into the air
        /// is not right at all.
        /// </summary>
        public bool HipsFollowSuspended { get; set; }

        /// <summary>How far, in world metres, the hips were moved off their rest this frame.</summary>
        public float HipsShiftMetres { get; private set; }

        public string Build(AvatarPlayer player, GameObject model, AvatarManifest manifest)
        {
            Animator source;
            try { source = player.RemoteAnimator; }
            catch { return "the game rig has no Animator — cannot retarget"; }
            if (!Interop.Alive(source)) return "AvatarPlayer.RemoteAnimator is null — cannot retarget";
            return Build(source, model, manifest);
        }

        /// <summary>
        /// Retarget from any humanoid Animator, not just a player's. The character mannequin in
        /// the equipment room is an `AvatarHologram : Idler` with its own humanoid rig and its
        /// own idle animation, so the same delta retargeting drives it — and the avatar
        /// inherits the idling and blinking for free.
        /// </summary>
        public string Build(Animator source, GameObject model, AvatarManifest manifest)
        {
            _links.Clear();

            if (!Interop.Alive(source)) return "no Animator — cannot retarget";
            var isHuman = false;
            try { isHuman = source.isHuman; } catch { }
            if (!isHuman) return "the game rig's Animator is not humanoid — cannot retarget";

            var map = manifest?.rig?.humanoidBones;
            if (map == null) return "no humanoidBones in the manifest — re-export the avatar";

            var missing = 0;
            foreach (var bone in Bones)
            {
                Transform src = null;
                try { src = source.GetBoneTransform(bone); } catch { }
                if (!Interop.Alive(src)) { missing++; continue; }

                if (!map.TryGetValue(bone.ToString(), out var path) || string.IsNullOrEmpty(path)) { missing++; continue; }
                var dst = model.transform.Find(path);
                if (!Interop.Alive(dst)) { missing++; continue; }

                _links.Add(new Link
                {
                    Source = src,
                    Target = dst,
                    SourceRestInverse = Quaternion.Inverse(src.rotation),
                    SourceRestLocal = src.localRotation,
                    TargetRest = dst.rotation,
                    TargetRestLocal = dst.localRotation,
                    Depth = Depth(dst),
                    Name = bone.ToString(),
                    Bone = bone,
                });

                if (bone == ProbeBone) { _probe = _links[_links.Count - 1]; _probeWroteValid = false; }

                if (bone == HumanBodyBones.Hips)
                {
                    _sourceHips = src;
                    _targetHips = dst;
                    _sourceHipsRestLocal = src.localPosition;
                    _targetHipsRestLocal = dst.localPosition;
                }
            }
            _targetRoot = model.transform;

            _links.Sort((a, b) => a.Depth.CompareTo(b.Depth));
            TorsoTurnAtCapture = float.NaN;
            HeadAlignNote = null;
            var aligned = AlignAtCapture(source, model, map);
            var turned = "";
            if (!float.IsNaN(TorsoTurnAtCapture) && Mathf.Abs(TorsoTurnAtCapture) > 5f)
                turned = $", stood {Mathf.Abs(TorsoTurnAtCapture):0}° round from the game's rig (turned to match)";

            // A rig exported at a unit scale of 100 (or 70, or 0.01) keeps that scale on a
            // node between the model root and the hips. Nothing about rotations cares; the
            // hips translation does, which is why it goes through world space below.
            var rigScale = "";
            try
            {
                if (Interop.Alive(_targetHips) && Interop.Alive(_targetHips.parent))
                {
                    var inner = _targetHips.parent.lossyScale.y / Mathf.Max(1e-6f, model.transform.lossyScale.y);
                    if (Mathf.Abs(inner - 1f) > 0.01f) rigScale = $", hips sit at x{inner:0.###} inside the rig";
                }
            }
            catch { }

            return _links.Count == 0
                ? "no bones could be paired between the two rigs"
                : $"{_links.Count} bone(s) paired" + (missing > 0 ? $", {missing} unpaired" : "") +
                  (aligned > 0 ? $", {aligned} aligned at capture" : "") + turned + rigScale +
                  (string.IsNullOrEmpty(HeadAlignNote) ? ", head unaligned" : ", " + HeadAlignNote);
        }

        /// <summary>
        /// Point each of the avatar's limbs the same way the game's rig points at capture time.
        ///
        /// Delta retargeting preserves whatever difference the two skeletons had when it
        /// started, and an imported avatar is instantiated in its bind pose — usually a T-pose —
        /// while the game's rig is standing naturally. Legs barely notice, because legs are
        /// nearly identical in both. Arms differ by about ninety degrees, which is exactly the
        /// A-pose-with-outstretched-arms that showed up on the mannequin.
        ///
        /// So before capturing, rotate each bone's stored reference by whatever turns the
        /// avatar's limb direction onto the game rig's. Direction alone doesn't pin down roll
        /// about the bone, so this isn't perfect — but a small roll error is a far better
        /// starting point than a limb sticking out sideways.
        /// </summary>
        private int AlignAtCapture(Animator source, GameObject model, Dictionary<string, string> map)
        {
            if (!ModConfig.RetargetAlignAtCapture.Value) return 0;

            var byBone = new Dictionary<HumanBodyBones, Link>();
            foreach (var link in _links) byBone[link.Bone] = link;

            var aligned = 0;
            foreach (var link in _links)
            {
                Vector3 sourceDir, targetDir;

                // The head has nothing below it that we retarget, so the child lookup below
                // never had anything to aim it at and it went unaligned — the same oversight
                // the hands had, and the reason a peer's head sat up and to the right for a
                // whole session. Eyes give it something to point at.
                if (link.Bone == HumanBodyBones.Head)
                {
                    if (TryAlignHead(source, model, map, byBone, link)) aligned++;
                    continue;
                }

                var child = FindChild(byBone, link.Bone);
                if (child != null)
                {
                    sourceDir = child.Source.position - link.Source.position;
                    targetDir = child.Target.position - link.Target.position;
                }
                else if (!TryHandDirections(source, model, map, link, out sourceDir, out targetDir))
                {
                    continue;
                }

                try
                {
                    if (sourceDir.sqrMagnitude < 1e-8f || targetDir.sqrMagnitude < 1e-8f) continue;

                    // One direction says where a bone points, not how it is rolled about its
                    // own length. On the torso and the legs that roll is which way the body
                    // FACES: they all run near-vertical, so pointing them at the bone below
                    // leaves the avatar facing whichever way its prefab happened to, and the
                    // delta keeps that for as long as it is worn. An inch-authored rig whose
                    // rest pose faced the other way from the game's mannequin stood on the
                    // pedestal with its head and arms (both aligned on two axes) toward you and
                    // its chest and legs turned 180 degrees the other way. The line between the
                    // hip joints, or between the shoulder joints, is a real skeletal axis both
                    // rigs agree on, so it pins the roll the way the eyes pin the head's.
                    Quaternion correction;
                    var haveLateral = TryLateralAxis(byBone, link.Bone, out var sourceLateral, out var targetLateral) ||
                                      TryHandLateral(source, model, map, link, out sourceLateral, out targetLateral);
                    if (haveLateral && Usable(sourceDir, sourceLateral) && Usable(targetDir, targetLateral))
                    {
                        var sourceRot = Quaternion.LookRotation(sourceDir.normalized, sourceLateral);
                        var targetRot = Quaternion.LookRotation(targetDir.normalized, targetLateral);
                        correction = sourceRot * Quaternion.Inverse(targetRot);
                        if (link.Bone == HumanBodyBones.Hips)
                        {
                            // For the log: how far round the avatar stood from the game's rig.
                            var s = Vector3.ProjectOnPlane(sourceLateral, sourceDir);
                            var t = Vector3.ProjectOnPlane(targetLateral, sourceDir);
                            if (s.sqrMagnitude > 1e-8f && t.sqrMagnitude > 1e-8f)
                                TorsoTurnAtCapture = Vector3.SignedAngle(t, s, sourceDir);
                        }
                    }
                    else
                    {
                        correction = Quaternion.FromToRotation(targetDir.normalized, sourceDir.normalized);
                    }
                    link.TargetRest = correction * link.TargetRest;
                    aligned++;
                }
                catch { }
            }
            return aligned;
        }

        /// <summary>
        /// How far round the avatar's hips stood from the game rig's at capture, in degrees
        /// about the spine; NaN when it couldn't be measured. Near 0 for a rig whose rest pose
        /// faces the same way as the game's, near 180 for one that faced the other way.
        /// Corrected either way; this is so the log can say which it was.
        /// </summary>
        public float TorsoTurnAtCapture { get; private set; } = float.NaN;

        /// <summary>
        /// Which measurement the head was aligned on at capture, and whether an eye line had to
        /// be turned back. Null when the head went unaligned. For the log: a head that comes out
        /// facing the wrong way is the one alignment nobody can miss, and this says which of the
        /// three measurements produced it.
        /// </summary>
        public string HeadAlignNote { get; private set; }

        /// <summary>
        /// A second axis for the bones whose first one runs up the body. The hip line for the
        /// hips and the legs, the shoulder line for the spine and up (falling back to the hip
        /// line when an arm is unpaired). Arms keep their one axis: the next joint down hides
        /// their roll, and neither line says anything useful about them. Hands are handled by
        /// <see cref="TryHandLateral"/>.
        /// </summary>
        private static bool TryLateralAxis(Dictionary<HumanBodyBones, Link> byBone, HumanBodyBones bone,
                                           out Vector3 sourceLateral, out Vector3 targetLateral)
        {
            sourceLateral = targetLateral = Vector3.zero;
            switch (bone)
            {
                case HumanBodyBones.Spine:
                case HumanBodyBones.Chest:
                case HumanBodyBones.UpperChest:
                case HumanBodyBones.Neck:
                    if (TryLine(byBone, HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm, out sourceLateral, out targetLateral)) return true;
                    return TryLine(byBone, HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg, out sourceLateral, out targetLateral);
                case HumanBodyBones.Hips:
                case HumanBodyBones.LeftUpperLeg:
                case HumanBodyBones.LeftLowerLeg:
                case HumanBodyBones.RightUpperLeg:
                case HumanBodyBones.RightLowerLeg:
                    if (TryLine(byBone, HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg, out sourceLateral, out targetLateral)) return true;
                    return TryLine(byBone, HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm, out sourceLateral, out targetLateral);
                // A foot points at its toes; which way its sole faces is the roll about that
                // line, and up the shin is the one direction both rigs agree on for it. Without
                // it a foot could match its toe direction with the sole facing sideways or
                // back, which on one mannequin read as feet twisted toward the floor.
                case HumanBodyBones.LeftFoot:
                    return TryLine(byBone, HumanBodyBones.LeftFoot, HumanBodyBones.LeftLowerLeg, out sourceLateral, out targetLateral);
                case HumanBodyBones.RightFoot:
                    return TryLine(byBone, HumanBodyBones.RightFoot, HumanBodyBones.RightLowerLeg, out sourceLateral, out targetLateral);
                default:
                    return false;
            }
        }

        private static bool TryLine(Dictionary<HumanBodyBones, Link> byBone, HumanBodyBones left, HumanBodyBones right,
                                    out Vector3 sourceLine, out Vector3 targetLine)
        {
            sourceLine = targetLine = Vector3.zero;
            if (!byBone.TryGetValue(left, out var l) || !byBone.TryGetValue(right, out var r)) return false;
            if (!Interop.Alive(l.Source) || !Interop.Alive(r.Source) || !Interop.Alive(l.Target) || !Interop.Alive(r.Target)) return false;
            sourceLine = r.Source.position - l.Source.position;
            targetLine = r.Target.position - l.Target.position;
            return sourceLine.sqrMagnitude > 1e-8f && targetLine.sqrMagnitude > 1e-8f;
        }

        /// <summary>Two axes only pin a frame when they aren't close to parallel.</summary>
        private static bool Usable(Vector3 forward, Vector3 hint)
        {
            var a = Vector3.Angle(forward, hint);
            return a > 15f && a < 165f;
        }

        /// <summary>
        /// Line the head up on two axes rather than one: up from the neck, and which way it
        /// faces from the eyes.
        ///
        /// Every other bone is aligned by pointing it at the bone below, which fixes where a
        /// limb points but says nothing about how it is rolled about its own length. On an arm
        /// that error is small and hidden by the next joint down. On a head there is no next
        /// joint, and the error is the whole thing you look at — a peer whose face is turned
        /// up and to the right for as long as they wear the avatar. Two axes pin all three
        /// degrees of freedom, so the head starts out facing exactly where the game's head
        /// faces and the delta carries it correctly from there.
        /// </summary>
        private bool TryAlignHead(Animator source, GameObject model, Dictionary<string, string> map,
                                  Dictionary<HumanBodyBones, Link> byBone, Link head)
        {
            try
            {
                if (!Interop.Alive(head.Source) || !Interop.Alive(head.Target)) return false;

                // The neck if the avatar has one, the chest if it doesn't; either gives the
                // direction the head sits along.
                if (!byBone.TryGetValue(HumanBodyBones.Neck, out var below) &&
                    !byBone.TryGetValue(HumanBodyBones.UpperChest, out below) &&
                    !byBone.TryGetValue(HumanBodyBones.Chest, out below)) return false;
                if (!Interop.Alive(below.Source) || !Interop.Alive(below.Target)) return false;

                var srcUp = head.Source.position - below.Source.position;
                var dstUp = head.Target.position - below.Target.position;
                if (srcUp.sqrMagnitude < 1e-8f || dstUp.sqrMagnitude < 1e-8f) return false;

                // The shoulder line, or the hip line if an arm is unpaired. This is the same
                // axis the torso is aligned on, and it is the only one of the three below whose
                // direction is certain: the humanoid map says which upper arm is the left one,
                // and a rig whose arms were swapped would be visibly inside out long before it
                // got here.
                var haveBody = TryLine(byBone, HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm, out var srcBody, out var dstBody) ||
                               TryLine(byBone, HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg, out srcBody, out dstBody);

                var haveEyes = TryEyeMidpoint(source, model, map, out var srcEye, out var dstEye, out var srcEyeLine, out var dstEyeLine);
                var haveEyeLine = haveEyes && srcEyeLine.sqrMagnitude > 1e-8f && dstEyeLine.sqrMagnitude > 1e-8f;

                // Left eye to right eye, crossed with the neck axis, is which way the face
                // points — and it reads the head's own turn, which the shoulders don't. What it
                // cannot do on its own is tell a rig that labelled its eye bones the other way
                // round from one that didn't: swap the two and the same measurement points out
                // the back of the head. Nothing else on the avatar depends on which eye is
                // which, so a rigger can label them backwards and never see it; one tester's
                // avatar (`eye_l.003`/`eye_r.003` on an otherwise `_L`/`_R` rig, a face grafted
                // in from elsewhere) wore its head turned exactly 180° round.
                //
                // So the sign comes from the body, not the label: an eye line that runs against
                // the rig's own shoulder line is flipped before use, on each rig separately.
                // A head can only turn about eighty degrees on a neck, so a live rig caught
                // mid-glance is never near the ninety that would flip it by mistake.
                var srcFlipped = false;
                var dstFlipped = false;
                if (haveEyeLine && haveBody)
                {
                    if (Vector3.Dot(srcEyeLine, srcBody) < 0f) { srcEyeLine = -srcEyeLine; srcFlipped = true; }
                    if (Vector3.Dot(dstEyeLine, dstBody) < 0f) { dstEyeLine = -dstEyeLine; dstFlipped = true; }
                }

                Vector3 srcForward, dstForward;
                var srcAcross = haveEyeLine ? Vector3.Cross(srcEyeLine, srcUp) : Vector3.zero;
                var dstAcross = haveEyeLine ? Vector3.Cross(dstEyeLine, dstUp) : Vector3.zero;
                if (haveEyeLine && srcAcross.sqrMagnitude > 1e-8f && dstAcross.sqrMagnitude > 1e-8f)
                {
                    srcForward = srcAcross;
                    dstForward = dstAcross;
                    var mislabelled = srcFlipped && dstFlipped ? "both rigs'"
                                    : dstFlipped ? "the avatar's"
                                    : srcFlipped ? "the game rig's" : null;
                    HeadAlignNote = mislabelled == null
                        ? "head on the eye line"
                        : $"head on the eye line ({mislabelled} eye bones are labelled left for right — turned back)";
                }
                else if (haveBody)
                {
                    // No eye line on one of the rigs. The shoulders give the same forward,
                    // minus whatever the head was turned by at capture — a few degrees of
                    // error against a rig that has no eyes to ask.
                    srcForward = Vector3.Cross(srcBody, srcUp);
                    dstForward = Vector3.Cross(dstBody, dstUp);
                    HeadAlignNote = "head on the shoulder line (one of the rigs has no pair of eye bones)";
                }
                else if (haveEyes)
                {
                    // Last resort: where the eyes sit relative to the head bone, flattened
                    // against the neck. This is a modelling choice rather than a skeletal
                    // direction — one avatar's eyes sat 2.7 units above its head bone and 0.16
                    // in front of it while its neck leaned further forward than that, so
                    // flattened they came out BEHIND the head and the mannequin faced its own
                    // back. Only reached now when a rig has neither a second eye nor a pair of
                    // arms or legs to measure across.
                    srcForward = Vector3.ProjectOnPlane(srcEye - head.Source.position, srcUp);
                    dstForward = Vector3.ProjectOnPlane(dstEye - head.Target.position, dstUp);
                    HeadAlignNote = "head on the eye midpoint (no second eye, no shoulder or hip line)";
                }
                else return false;

                if (srcForward.sqrMagnitude < 1e-8f || dstForward.sqrMagnitude < 1e-8f) return false;

                var srcRot = Quaternion.LookRotation(srcForward.normalized, srcUp.normalized);
                var dstRot = Quaternion.LookRotation(dstForward.normalized, dstUp.normalized);
                head.TargetRest = srcRot * Quaternion.Inverse(dstRot) * head.TargetRest;
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// Where the eyes are on each rig, averaged when both are mapped so the direction comes
        /// out of the middle of the face rather than out of one eye.
        /// </summary>
        private static bool TryEyeMidpoint(Animator source, GameObject model, Dictionary<string, string> map,
                                           out Vector3 sourceMid, out Vector3 targetMid,
                                           out Vector3 sourceLine, out Vector3 targetLine)
        {
            sourceMid = targetMid = Vector3.zero;
            // Left eye to right eye, on each rig; zero unless both eyes were found.
            sourceLine = targetLine = Vector3.zero;
            var found = 0;
            Vector3 srcLeft = Vector3.zero, dstLeft = Vector3.zero, srcRight = Vector3.zero, dstRight = Vector3.zero;
            bool haveLeft = false, haveRight = false;
            foreach (var eye in new[] { HumanBodyBones.LeftEye, HumanBodyBones.RightEye })
            {
                try
                {
                    var src = source.GetBoneTransform(eye);
                    if (!Interop.Alive(src)) continue;
                    if (!map.TryGetValue(eye.ToString(), out var path) || string.IsNullOrEmpty(path)) continue;
                    var dst = model.transform.Find(path);
                    if (!Interop.Alive(dst)) continue;
                    sourceMid += src.position;
                    targetMid += dst.position;
                    found++;
                    if (eye == HumanBodyBones.LeftEye) { srcLeft = src.position; dstLeft = dst.position; haveLeft = true; }
                    else { srcRight = src.position; dstRight = dst.position; haveRight = true; }
                }
                catch { }
            }
            if (found == 0) return false;
            sourceMid /= found;
            targetMid /= found;
            if (haveLeft && haveRight)
            {
                sourceLine = srcRight - srcLeft;
                targetLine = dstRight - dstLeft;
            }
            return true;
        }

        /// <summary>
        /// Aim a hand at its knuckles. Both rigs have finger bones even though neither of us
        /// retargets them, so they are available to point at; if either rig is missing them the
        /// hand simply goes unaligned, as it did before.
        /// </summary>
        /// <summary>
        /// The line across the knuckles, little finger to index, on both rigs. A hand aligned on
        /// its finger direction alone can match it with the palm facing any way round; this
        /// pins the roll to the palm's actual plane, so a rig whose rest pose holds its palms
        /// differently from the game's (or labels its hand bone's axes differently) still copies
        /// the game's hand the right way up.
        /// </summary>
        private static bool TryHandLateral(Animator source, GameObject model, Dictionary<string, string> map,
                                           Link link, out Vector3 sourceLateral, out Vector3 targetLateral)
        {
            sourceLateral = targetLateral = Vector3.zero;
            HumanBodyBones index, little, ring;
            if (link.Bone == HumanBodyBones.LeftHand)
            {
                index = HumanBodyBones.LeftIndexProximal; little = HumanBodyBones.LeftLittleProximal; ring = HumanBodyBones.LeftRingProximal;
            }
            else if (link.Bone == HumanBodyBones.RightHand)
            {
                index = HumanBodyBones.RightIndexProximal; little = HumanBodyBones.RightLittleProximal; ring = HumanBodyBones.RightRingProximal;
            }
            else return false;

            foreach (var outer in new[] { little, ring })
            {
                try
                {
                    var srcIndex = source.GetBoneTransform(index);
                    var srcOuter = source.GetBoneTransform(outer);
                    if (!Interop.Alive(srcIndex) || !Interop.Alive(srcOuter)) continue;
                    if (!map.TryGetValue(index.ToString(), out var indexPath) || string.IsNullOrEmpty(indexPath)) return false;
                    if (!map.TryGetValue(outer.ToString(), out var outerPath) || string.IsNullOrEmpty(outerPath)) continue;
                    var dstIndex = model.transform.Find(indexPath);
                    var dstOuter = model.transform.Find(outerPath);
                    if (!Interop.Alive(dstIndex) || !Interop.Alive(dstOuter)) continue;

                    sourceLateral = srcIndex.position - srcOuter.position;
                    targetLateral = dstIndex.position - dstOuter.position;
                    return sourceLateral.sqrMagnitude > 1e-8f && targetLateral.sqrMagnitude > 1e-8f;
                }
                catch { }
            }
            return false;
        }

        private static bool TryHandDirections(Animator source, GameObject model, Dictionary<string, string> map,
                                              Link link, out Vector3 sourceDir, out Vector3 targetDir)
        {
            sourceDir = targetDir = Vector3.zero;

            foreach (var (hand, knuckles) in HandPointsAt)
            {
                if (hand != link.Bone) continue;

                foreach (var knuckle in knuckles)
                {
                    try
                    {
                        var src = source.GetBoneTransform(knuckle);
                        if (!Interop.Alive(src)) continue;
                        if (!map.TryGetValue(knuckle.ToString(), out var path) || string.IsNullOrEmpty(path)) continue;
                        var dst = model.transform.Find(path);
                        if (!Interop.Alive(dst)) continue;

                        sourceDir = src.position - link.Source.position;
                        targetDir = dst.position - link.Target.position;
                        return true;
                    }
                    catch { }
                }
                return false;
            }
            return false;
        }

        private static Link FindChild(Dictionary<HumanBodyBones, Link> byBone, HumanBodyBones parent)
        {
            foreach (var (p, children) in ChildOf)
            {
                if (p != parent) continue;
                foreach (var c in children)
                    if (byBone.TryGetValue(c, out var link)) return link;
                return null;
            }
            return null;
        }

        private static int Depth(Transform t)
        {
            var d = 0;
            for (var cur = t; Interop.Alive(cur); cur = cur.parent) d++;
            return d;
        }

        /// <summary>
        /// Take the reference pose again, after first putting the avatar back the way it was
        /// imported.
        ///
        /// Retargeting is a delta from a reference pose, so the reference is only worth having
        /// if it was captured against a rig the game was actually posing. Capture it while a
        /// peer is culled — LOD 2, frozen in whatever frame the walking animation stopped on —
        /// and every frame afterwards is measured from a pose that never happened. It doesn't
        /// look like a frozen avatar, it looks like a permanent tilt: a head that stares up and
        /// to the right for the rest of the session, which is what a playtest reported.
        ///
        /// Putting the bones back first matters. A plain re-Build would capture the avatar in
        /// whatever pose the bad reference had already twisted it into and bake that in too.
        /// </summary>
        public string Recapture(Animator source, GameObject model, AvatarManifest manifest)
        {
            // Parent first: _links is already sorted by depth, and local rotations are only
            // meaningful once the parent above them is back where it started.
            foreach (var link in _links)
            {
                if (!Interop.Alive(link.Target)) continue;
                try { link.Target.localRotation = link.TargetRestLocal; } catch { }
            }
            if (Interop.Alive(_targetHips))
            {
                try { _targetHips.localPosition = _targetHipsRestLocal; } catch { }
            }

            _probe = null;
            _probeWroteValid = false;
            OverwrittenDegrees = -1f;
            return Build(source, model, manifest);
        }

        /// <summary>The game bone we read for this humanoid bone, or null if it wasn't paired.</summary>
        public Transform SourceOf(HumanBodyBones bone)
        {
            foreach (var link in _links)
                if (link.Bone == bone) return Interop.Alive(link.Source) ? link.Source : null;
            return null;
        }

        /// <summary>
        /// How far a bone has bent since capture, in degrees.
        ///
        /// Local rotation, deliberately. The first version of this measured world rotation and
        /// was useless: a player who turns on the spot swings every bone's world rotation
        /// through 180 degrees without bending a single joint, so an arm hanging dead still
        /// reported 170 degrees of movement. Local rotation is the joint angle and nothing
        /// else, so a rig the game has stopped solving reads near zero and says so.
        /// </summary>
        public bool TryTravel(HumanBodyBones bone, out float sourceDegrees)
        {
            sourceDegrees = -1f;
            foreach (var link in _links)
            {
                if (link.Bone != bone) continue;
                if (!Interop.Alive(link.Source)) return false;
                try
                {
                    sourceDegrees = Quaternion.Angle(link.Source.localRotation, link.SourceRestLocal);
                    return true;
                }
                catch { return false; }
            }
            return false;
        }

        /// <summary>Apply this frame's pose. Call from LateUpdate, after the game has animated.</summary>
        public void Apply()
        {
            // Before we touch anything: is the bone we left behind last frame still where we
            // left it? If not, something between our two writes is posing this avatar too.
            if (_probeWroteValid && _probe != null && Interop.Alive(_probe.Target))
            {
                try { OverwrittenDegrees = Quaternion.Angle(_probe.Target.rotation, _probeWrote); } catch { }
            }

            for (var i = 0; i < _links.Count; i++)
            {
                var link = _links[i];
                if (!Interop.Alive(link.Source) || !Interop.Alive(link.Target)) continue;
                try
                {
                    // Rotation the source has gained since capture, applied to the target's own
                    // captured orientation. Absolute copying would need the two rigs to share
                    // bone axes, which they never do.
                    link.Target.rotation = link.Source.rotation * link.SourceRestInverse * link.TargetRest;
                }
                catch { }
            }

            if (_probe != null && Interop.Alive(_probe.Target))
            {
                try { _probeWrote = _probe.Target.rotation; _probeWroteValid = true; } catch { }
            }

            // Hips translation carries crouching and bobbing, which rotation alone can't.
            //
            // Through world space, not local to local. The two hips live under different
            // parents, and a local delta only means the same thing in both if those parents
            // share axes AND scale. One avatar's rig kept a x70 unit scale on the node above
            // its hips, so every centimetre the game rig bobbed shoved its hips 70 cm: the
            // mannequin slid off its pedestal whenever the hologram moved, and on the wearer
            // the head anchor hid it as legs missing by a metre and a bad height reading.
            // Scaled back up by the model's own scale so the effect stays proportional to the
            // avatar's size, which is what a unit rig always got.
            if (Interop.Alive(_sourceHips) && Interop.Alive(_targetHips))
            {
                try
                {
                    var follow = ModConfig.RetargetHipsFollow.Value;
                    var delta = _sourceHips.localPosition - _sourceHipsRestLocal;
                    var sourceParent = _sourceHips.parent;
                    var targetParent = _targetHips.parent;
                    var haveParents = Interop.Alive(sourceParent) && Interop.Alive(targetParent) && Interop.Alive(_targetRoot);
                    if (haveParents)
                    {
                        var world = sourceParent.TransformVector(delta);
                        delta = Vector3.Scale(targetParent.InverseTransformVector(world), _targetRoot.lossyScale);
                    }
                    if (HipsFollowSuspended) delta = Vector3.zero;
                    delta *= follow;
                    _targetHips.localPosition = _targetHipsRestLocal + delta;
                    HipsShiftMetres = haveParents ? targetParent.TransformVector(delta).magnitude : delta.magnitude;
                }
                catch { }
            }
        }

        public string Describe()
        {
            var names = new List<string>();
            foreach (var l in _links) names.Add(l.Name);
            return string.Join(", ", names);
        }
    }
}
