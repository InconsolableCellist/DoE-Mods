using System;
using System.Collections.Generic;
using CustomAvatars.Gate;
using CustomAvatars.Net;
using UnityEngine;

namespace CustomAvatars.Fbt
{
    /// <summary>
    /// Streams your hip and foot targets to modded peers, so their copy of your body crosses
    /// its legs when you do. The vanilla protocol only carries head, hands and root — legs on
    /// a remote client are procedural guesses — so this is the only way a peer can see FBT.
    ///
    /// Same shape as <see cref="Avatars.HandSync"/>: unreliable, change-gated, keyframed every
    /// couple of seconds for late joiners. What goes on the wire is the finished bone-space
    /// TARGETS (calibration offsets already applied), relative to the sender's play-space
    /// root — the receiver needs no knowledge of the sender's trackers or mounting, and the
    /// root is a transform vanilla already syncs, which keeps the stream's positions small and
    /// immune to the two clients smoothing world positions differently.
    ///
    /// 32 bytes a message: [seq][flags], then hip/left/right × (position 6 B + rotation 4 B).
    /// </summary>
    public class TrackerSync
    {
        public struct PoseSet
        {
            public Vector3 HipPos, LeftPos, RightPos;
            public Quaternion HipRot, LeftRot, RightRot;
        }

        public class RemoteEntry
        {
            public PoseSet Poses;
            public float ArrivedAt;
        }

        private const int PacketBytes = 2 + 3 * (6 + 4);
        private const byte FlagKeyframe = 1;

        private readonly ModRoster _roster;
        private readonly byte[] _packet = new byte[PacketBytes];
        private readonly Dictionary<int, RemoteEntry> _remote = new Dictionary<int, RemoteEntry>();

        private PoseSet _lastSent;
        private bool _everSent;
        private byte _sequence;
        private float _nextSendAt;
        private float _nextKeyframeAt;

        /// <summary>Latest poses per sender. The manager decides freshness and application.</summary>
        public IReadOnlyDictionary<int, RemoteEntry> Remote => _remote;

        public TrackerSync(ModRoster roster)
        {
            _roster = roster;
            ModNet.RegisterHandler(ModNet.CodeTrackerPose, OnTrackerPose);
        }

        public void Forget(int actorNumber) => _remote.Remove(actorNumber);

        /// <summary>Call every LateUpdate; <paramref name="local"/> is null while FBT is off.</summary>
        public void Tick(float unscaledTime, PoseSet? local)
        {
            if (local == null || !ModGate.Active || !ModConfig.TrackerSyncEnabled.Value) return;
            if (unscaledTime < _nextSendAt) return;

            var rate = Mathf.Clamp(ModConfig.TrackerSyncHz.Value, 1f, 20f);
            _nextSendAt = unscaledTime + 1f / rate;

            var poses = local.Value;
            var keyframe = unscaledTime >= _nextKeyframeAt || !_everSent;
            if (!keyframe && !Moved(poses)) return;

            var targets = _roster.ModdedPeerActors(ModCaps.Fbt);
            if (targets.Length == 0) return;

            var offset = 0;
            _packet[offset++] = _sequence++;
            _packet[offset++] = keyframe ? FlagKeyframe : (byte)0;
            Packing.WritePosMm(_packet, ref offset, poses.HipPos);
            Packing.WriteQuat(_packet, ref offset, poses.HipRot);
            Packing.WritePosMm(_packet, ref offset, poses.LeftPos);
            Packing.WriteQuat(_packet, ref offset, poses.LeftRot);
            Packing.WritePosMm(_packet, ref offset, poses.RightPos);
            Packing.WriteQuat(_packet, ref offset, poses.RightRot);

            if (!ModNet.SendBytes(ModNet.CodeTrackerPose, _packet, reliable: false, targetActors: targets)) return;

            _lastSent = poses;
            _everSent = true;
            if (keyframe) _nextKeyframeAt = unscaledTime + 2f;
        }

        private bool Moved(PoseSet now)
        {
            var posEpsilon = Mathf.Max(0.001f, ModConfig.TrackerPosEpsilonMm.Value / 1000f);
            var rotEpsilon = Mathf.Max(0.1f, ModConfig.TrackerRotEpsilonDegrees.Value);
            return Vector3.Distance(now.HipPos, _lastSent.HipPos) > posEpsilon
                || Vector3.Distance(now.LeftPos, _lastSent.LeftPos) > posEpsilon
                || Vector3.Distance(now.RightPos, _lastSent.RightPos) > posEpsilon
                || Quaternion.Angle(now.HipRot, _lastSent.HipRot) > rotEpsilon
                || Quaternion.Angle(now.LeftRot, _lastSent.LeftRot) > rotEpsilon
                || Quaternion.Angle(now.RightRot, _lastSent.RightRot) > rotEpsilon;
        }

        private void OnTrackerPose(int senderActor, Il2CppSystem.Object content)
        {
            var bytes = ModNet.AsBytes(content);
            if (bytes == null || bytes.Length < PacketBytes)
            {
                Core.Log.Warning($"Tracker poses from actor {senderActor} were not {PacketBytes} bytes.");
                return;
            }

            var offset = 2;   // sequence and flags are diagnostics; latest-wins needs neither
            var poses = new PoseSet
            {
                HipPos = Packing.ReadPosMm(bytes, ref offset),
                HipRot = Packing.ReadQuat(bytes, ref offset),
                LeftPos = Packing.ReadPosMm(bytes, ref offset),
                LeftRot = Packing.ReadQuat(bytes, ref offset),
                RightPos = Packing.ReadPosMm(bytes, ref offset),
                RightRot = Packing.ReadQuat(bytes, ref offset),
            };

            if (!_remote.TryGetValue(senderActor, out var entry))
                _remote[senderActor] = entry = new RemoteEntry();
            entry.Poses = poses;
            entry.ArrivedAt = Time.unscaledTime;
        }
    }
}
