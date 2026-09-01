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
            Interop.Alive(_targetHips) && Interop.Alive(_targetHips.root)
                ? _targetHips.position - _targetHips.root.position : (Vector3?)null;

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

            _links.Sort((a, b) => a.Depth.CompareTo(b.Depth));
            var aligned = AlignAtCapture(source, model, map);

            return _links.Count == 0
                ? "no bones could be paired between the two rigs"
                : $"{_links.Count} bone(s) paired" + (missing > 0 ? $", {missing} unpaired" : "") +
                  (aligned > 0 ? $", {aligned} aligned at capture" : "");
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

                    var correction = Quaternion.FromToRotation(targetDir.normalized, sourceDir.normalized);
                    link.TargetRest = correction * link.TargetRest;
                    aligned++;
                }
                catch { }
            }
            return aligned;
        }

        /// <summary>
        /// Line the head up on two axes rather than one: forward from the eyes, up from the
        /// neck.
        ///
        /// Every other bone is aligned by pointing it at the bone below, which fixes where a
        /// limb points but says nothing about how it is rolled about its own length. On an arm
        /// that error is small and hidden by the next joint down. On a head there is no next
        /// joint, and the error is the whole thing you look at — a peer whose face is turned
        /// up and to the right for as long as they wear the avatar. Two axes pin all three
        /// degrees of freedom, so the head starts out facing exactly where the game's head
        /// faces and the delta carries it correctly from there.
        /// </summary>
        private static bool TryAlignHead(Animator source, GameObject model, Dictionary<string, string> map,
                                         Dictionary<HumanBodyBones, Link> byBone, Link head)
        {
            try
            {
                if (!Interop.Alive(head.Source) || !Interop.Alive(head.Target)) return false;
                if (!TryEyeMidpoint(source, model, map, out var srcEye, out var dstEye)) return false;

                // The neck if the avatar has one, the chest if it doesn't; either gives the
                // direction the head sits along.
                if (!byBone.TryGetValue(HumanBodyBones.Neck, out var below) &&
                    !byBone.TryGetValue(HumanBodyBones.UpperChest, out below) &&
                    !byBone.TryGetValue(HumanBodyBones.Chest, out below)) return false;
                if (!Interop.Alive(below.Source) || !Interop.Alive(below.Target)) return false;

                var srcForward = srcEye - head.Source.position;
                var dstForward = dstEye - head.Target.position;
                var srcUp = head.Source.position - below.Source.position;
                var dstUp = head.Target.position - below.Target.position;

                if (srcForward.sqrMagnitude < 1e-8f || dstForward.sqrMagnitude < 1e-8f ||
                    srcUp.sqrMagnitude < 1e-8f || dstUp.sqrMagnitude < 1e-8f) return false;

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
                                           out Vector3 sourceMid, out Vector3 targetMid)
        {
            sourceMid = targetMid = Vector3.zero;
            var found = 0;
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
                }
                catch { }
            }
            if (found == 0) return false;
            sourceMid /= found;
            targetMid /= found;
            return true;
        }

        /// <summary>
        /// Aim a hand at its knuckles. Both rigs have finger bones even though neither of us
        /// retargets them, so they are available to point at; if either rig is missing them the
        /// hand simply goes unaligned, as it did before.
        /// </summary>
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
            if (Interop.Alive(_sourceHips) && Interop.Alive(_targetHips))
            {
                try
                {
                    var delta = _sourceHips.localPosition - _sourceHipsRestLocal;
                    _targetHips.localPosition = _targetHipsRestLocal + delta * ModConfig.RetargetHipsFollow.Value;
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
