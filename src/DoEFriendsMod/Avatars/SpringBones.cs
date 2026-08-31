using System;
using System.Collections.Generic;
using DoEFriendsMod.Recon;
using UnityEngine;

namespace DoEFriendsMod.Avatars
{
    /// <summary>
    /// Secondary motion for custom avatars — tails, ears, hair — rebuilt from the PhysBone
    /// configuration the exporter captured before stripping the VRC SDK.
    ///
    /// **Why not the game's own `Multiflex`?** It's there (dump.cs:2555), it's a proper
    /// Burst/Jobs spring solver with four collider types, and `CharacterPrefab` already drives
    /// one for the vanilla capes. But it exposes no `Update` — something else calls its
    /// `Read`/`Schedule`/`Complete`/`Write` in an order we'd be guessing at — and its state
    /// lives in `NativeArray&lt;float3&gt;` fields whose lifetime we don't control from managed
    /// code through interop. That's a debugging session with someone else's job scheduler.
    /// This is a standard VRM-style spring chain: ~150 lines, entirely predictable, and good
    /// enough for a tail. Multiflex stays on the table as the quality upgrade.
    ///
    /// Not a MonoBehaviour: registering a managed type into the Il2Cpp domain is avoidable
    /// friction, and MelonMod gives us `OnLateUpdate` for free.
    /// </summary>
    public class SpringBones
    {
        private class Node
        {
            public Transform Bone;
            public Quaternion RestLocalRotation;
            public Vector3 BoneAxis;      // rest direction to the child, in bone-local space
            public float Length;          // rest distance to the child
            public Vector3 CurrentTip;    // world-space simulated tip
            public Vector3 PreviousTip;
        }

        private class Chain
        {
            public string Name;
            public List<Node> Nodes = new List<Node>();
            // Raw 0..1 values straight from the PhysBone. The effective forces are derived
            // every frame from these plus the tuning constants, so editing the constants and
            // reloading preferences (F3) retunes a live avatar with no respawn.
            public float Stiffness01;
            public float Spring01;
            public float Gravity01;
            public float Immobile01;
            /// <summary>PhysBone's cone limit in degrees from the rest direction. 0 = none.</summary>
            public float MaxAngle;
        }

        private class Collider
        {
            public Transform Transform;
            public Vector3 Offset;
            public float Radius;
        }

        private readonly List<Chain> _chains = new List<Chain>();
        private readonly List<Collider> _colliders = new List<Collider>();

        public int ChainCount => _chains.Count;
        public int BoneCount { get; private set; }
        public int ColliderCount => _colliders.Count;

        /// <summary>Builds the runtime chains against an instantiated avatar. Returns a summary.</summary>
        public string Build(GameObject root, AvatarManifest manifest)
        {
            _chains.Clear();
            _colliders.Clear();
            BoneCount = 0;

            var dyn = manifest.dynamics;
            if (dyn?.chains == null || dyn.chains.Count == 0)
                return "no dynamics in manifest — nothing will swing (re-export with the current exporter)";

            var missingPaths = 0;

            if (dyn.colliders != null)
                foreach (var c in dyn.colliders)
                {
                    // Spheres only for now: capsules and planes are a refinement, and a tail
                    // that stops at the hips is already most of the value.
                    if (!string.IsNullOrEmpty(c.shape) &&
                        !c.shape.Equals("Sphere", StringComparison.OrdinalIgnoreCase)) continue;

                    var t = Find(root, c.path);
                    if (!Interop.Alive(t)) { missingPaths++; continue; }
                    _colliders.Add(new Collider
                    {
                        Transform = t,
                        Offset = ToVec(c.position),
                        Radius = Mathf.Max(0.001f, c.radius),
                    });
                }

            foreach (var info in dyn.chains)
            {
                if (info?.bones == null || info.bones.Count < 2) continue;

                var chain = new Chain
                {
                    Name = info.name ?? "chain",
                    // `stiffness` and `pull` both resist leaving the animated pose; take the
                    // stronger of the two rather than pretending to reproduce PhysBone exactly.
                    Stiffness01 = Mathf.Clamp01(Mathf.Max(info.stiffness, info.pull)),
                    Spring01 = Mathf.Clamp01(info.spring),
                    Gravity01 = Mathf.Clamp01(info.gravity),
                    Immobile01 = Mathf.Clamp01(info.immobile),
                    // Honour the avatar's own limit when it set one. A chain with no limit can
                    // fold back through the body, which is the "bone limits not respected" look.
                    MaxAngle = info.maxAngleX > 0.01f ? info.maxAngleX : ModConfig.SpringMaxAngleFallback.Value,
                };

                var bones = new List<Transform>();
                foreach (var path in info.bones)
                {
                    var t = Find(root, path);
                    if (!Interop.Alive(t)) { missingPaths++; bones.Clear(); break; }
                    bones.Add(t);
                }
                if (bones.Count < 2) continue;

                // Node i drives bone i and tracks the world position of bone i+1.
                for (var i = 0; i < bones.Count - 1; i++)
                {
                    var bone = bones[i];
                    var child = bones[i + 1];
                    var length = Vector3.Distance(bone.position, child.position);
                    if (length < 0.0001f) continue;

                    chain.Nodes.Add(new Node
                    {
                        Bone = bone,
                        RestLocalRotation = bone.localRotation,
                        BoneAxis = bone.InverseTransformDirection(child.position - bone.position).normalized,
                        Length = length,
                        CurrentTip = child.position,
                        PreviousTip = child.position,
                    });
                }

                // VRCPhysBone appends a virtual endpoint beyond the last real bone. Without
                // it the final bone never rotates — on a six-segment tail that's a visibly
                // stiff tip. endpointPosition is in the last bone's local space.
                var endpoint = ToVec(info.endpointPosition);
                if (endpoint.sqrMagnitude > 1e-8f)
                {
                    var last = bones[bones.Count - 1];
                    var tipWorld = last.TransformPoint(endpoint);
                    var length = Vector3.Distance(last.position, tipWorld);
                    if (length > 0.0001f)
                        chain.Nodes.Add(new Node
                        {
                            Bone = last,
                            RestLocalRotation = last.localRotation,
                            BoneAxis = endpoint.normalized,
                            Length = length,
                            CurrentTip = tipWorld,
                            PreviousTip = tipWorld,
                        });
                }

                if (chain.Nodes.Count == 0) continue;
                BoneCount += chain.Nodes.Count;
                _chains.Add(chain);
            }

            var sources = new HashSet<string>();
            foreach (var info in dyn.chains) if (!string.IsNullOrEmpty(info?.source)) sources.Add(info.source);
            var from = sources.Count > 0 ? $" from {string.Join("/", sources)}" : "";

            foreach (var chain in _chains)
                Core.Log.Msg($"  chain `{chain.Name}`: {chain.Nodes.Count} bone(s), " +
                             $"stiffness {chain.Stiffness01:0.##}, spring {chain.Spring01:0.##}, " +
                             $"gravity {chain.Gravity01:0.##}, immobile {chain.Immobile01:0.##}, " +
                             $"maxAngle {(chain.MaxAngle > 0.01f ? $"{chain.MaxAngle:0}°" : "none")}");
            foreach (var col in _colliders)
                Core.Log.Msg($"  collider r={col.Radius:0.###} on `{Interop.ScenePath(col.Transform)}` " +
                             $"offset {col.Offset}");

            var summary = $"{_chains.Count} chain(s){from}, {BoneCount} bone(s), {_colliders.Count} sphere collider(s)";
            if (missingPaths > 0) summary += $" — {missingPaths} path(s) did not resolve on this mesh";
            return summary;
        }

        /// <summary>
        /// One Verlet step per chain. Call from LateUpdate, after animation and IK have posed
        /// the skeleton — otherwise the animator overwrites everything we just did.
        /// </summary>
        public void Simulate(float deltaTime)
        {
            if (_chains.Count == 0) return;

            // A frame hitch with an unclamped dt sends a Verlet chain to the moon.
            var dt = Mathf.Clamp(deltaTime, 0.001f, 0.05f);

            for (var c = 0; c < _chains.Count; c++)
            {
                var chain = _chains[c];
                for (var n = 0; n < chain.Nodes.Count; n++)
                {
                    var node = chain.Nodes[n];
                    if (!Interop.Alive(node.Bone)) continue;

                    try { StepNode(chain, node, dt); }
                    catch { /* one bad bone must not stop the rest of the avatar moving */ }
                }
            }
        }

        private void StepNode(Chain chain, Node node, float dt)
        {
            var bone = node.Bone;
            var parentRotation = Interop.Alive(bone.parent) ? bone.parent.rotation : Quaternion.identity;

            // Unit vector along the bone's ANIMATED rest direction, in world space.
            var restDir = parentRotation * node.RestLocalRotation * node.BoneAxis;

            // Forces, all in metres-per-second and therefore all scaled by dt. The previous
            // version mixed units — a per-frame fraction for the restoring force against a
            // dt-squared gravity — so restore outweighed gravity by roughly 150:1 and the
            // chain snapped back to the animated pose no matter what gravity was set to.
            // That is exactly "sticks straight out and won't droop".
            var stiffnessForce = restDir * (chain.Stiffness01 * ModConfig.SpringStiffnessScale.Value * dt);
            var gravityForce = Vector3.down * (chain.Gravity01 * ModConfig.SpringGravityScale.Value * dt);

            // Higher VRC `spring` means more bounce, so less damping. `immobile` (ignore the
            // wearer's own motion) also reads as damping here.
            var drag = Mathf.Clamp(
                ModConfig.SpringDragBase.Value
                - chain.Spring01 * ModConfig.SpringDragFromSpring.Value
                + chain.Immobile01 * 0.3f,
                0.05f, 0.95f);

            var inertia = (node.CurrentTip - node.PreviousTip) * (1f - drag);

            var nextTip = node.CurrentTip + inertia + stiffnessForce + gravityForce;

            // Keep the bone rigid: the tip stays exactly one bone-length from its origin.
            nextTip = bone.position + (nextTip - bone.position).normalized * node.Length;

            // Cone limit: never let the joint swing further from its animated direction than
            // the PhysBone allowed.
            if (chain.MaxAngle > 0.01f)
            {
                var current = nextTip - bone.position;
                var angle = Vector3.Angle(restDir, current);
                if (angle > chain.MaxAngle)
                {
                    var clamped = Vector3.RotateTowards(restDir, current,
                        chain.MaxAngle * Mathf.Deg2Rad, 0f).normalized;
                    nextTip = bone.position + clamped * node.Length;
                }
            }

            if (ModConfig.SpringCollidersEnabled.Value)
                nextTip = PushOutOfColliders(nextTip, bone.position, node.Length);

            node.PreviousTip = node.CurrentTip;
            node.CurrentTip = nextTip;

            var to = nextTip - bone.position;
            if (to.sqrMagnitude < 1e-8f) return;
            bone.rotation = Quaternion.FromToRotation(restDir, to) * parentRotation * node.RestLocalRotation;
        }

        private Vector3 PushOutOfColliders(Vector3 tip, Vector3 origin, float length)
        {
            for (var i = 0; i < _colliders.Count; i++)
            {
                var col = _colliders[i];
                if (!Interop.Alive(col.Transform)) continue;

                var center = col.Transform.TransformPoint(col.Offset);
                var delta = tip - center;
                var distance = delta.magnitude;
                if (distance >= col.Radius || distance < 1e-6f) continue;

                tip = center + delta / distance * col.Radius;
                // Re-apply the bone-length constraint: pushing out of a collider must not
                // stretch the bone, or the chain visibly grows while it's touching something.
                tip = origin + (tip - origin).normalized * length;
            }
            return tip;
        }

        /// <summary>Snap every chain back to its animated pose — on spawn, or after a teleport.</summary>
        public void Reset()
        {
            foreach (var chain in _chains)
                foreach (var node in chain.Nodes)
                {
                    if (!Interop.Alive(node.Bone)) continue;
                    try
                    {
                        var parentRotation = Interop.Alive(node.Bone.parent) ? node.Bone.parent.rotation : Quaternion.identity;
                        var tip = node.Bone.position + parentRotation * node.RestLocalRotation * node.BoneAxis * node.Length;
                        node.CurrentTip = node.PreviousTip = tip;
                    }
                    catch { }
                }
        }

        private static Transform Find(GameObject root, string path)
        {
            if (string.IsNullOrEmpty(path)) return root.transform;
            try { return root.transform.Find(path); } catch { return null; }
        }

        private static Vector3 ToVec(List<float> v) =>
            v != null && v.Count >= 3 ? new Vector3(v[0], v[1], v[2]) : Vector3.zero;
    }
}
