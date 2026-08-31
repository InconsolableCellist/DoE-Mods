using System;
using System.Collections.Generic;
using DoEFriendsMod.Recon;
using UnityEngine;
using Il2Cpp;
using Interop = DoEFriendsMod.Recon.Interop;

namespace DoEFriendsMod.Avatars
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
            public Quaternion TargetRest;
            public int Depth;
            public string Name;
        }

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
        private Transform _sourceHips, _targetHips;
        private Vector3 _sourceHipsRestLocal, _targetHipsRestLocal;

        public int LinkCount => _links.Count;

        public string Build(AvatarPlayer player, GameObject model, AvatarManifest manifest)
        {
            _links.Clear();

            Animator source;
            try { source = player.RemoteAnimator; }
            catch { return "the game rig has no Animator — cannot retarget"; }

            if (!Interop.Alive(source)) return "AvatarPlayer.RemoteAnimator is null — cannot retarget";
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
                    TargetRest = dst.rotation,
                    Depth = Depth(dst),
                    Name = bone.ToString(),
                });

                if (bone == HumanBodyBones.Hips)
                {
                    _sourceHips = src;
                    _targetHips = dst;
                    _sourceHipsRestLocal = src.localPosition;
                    _targetHipsRestLocal = dst.localPosition;
                }
            }

            _links.Sort((a, b) => a.Depth.CompareTo(b.Depth));
            return _links.Count == 0
                ? "no bones could be paired between the two rigs"
                : $"{_links.Count} bone(s) paired" + (missing > 0 ? $", {missing} unpaired" : "");
        }

        private static int Depth(Transform t)
        {
            var d = 0;
            for (var cur = t; Interop.Alive(cur); cur = cur.parent) d++;
            return d;
        }

        /// <summary>Apply this frame's pose. Call from LateUpdate, after the game has animated.</summary>
        public void Apply()
        {
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
