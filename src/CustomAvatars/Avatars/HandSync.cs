using System;
using CustomAvatars.Gate;
using CustomAvatars.Net;
using UnityEngine;

namespace CustomAvatars.Avatars
{
    /// <summary>
    /// Streams finger curls to peers so their copy of your avatar closes its hands when you do.
    ///
    /// Ten bytes, one per finger, unreliable, at a modest tick rate. Unreliable is right here:
    /// a dropped packet costs one stale pose for a fraction of a second, and the next tick
    /// corrects it — retransmitting stale hand positions would be worse than skipping them.
    /// Sent only when something actually moved, so a still hand costs nothing at all.
    ///
    /// This is deliberately the same shape the face stream will take, so that one works out
    /// the awkward parts (rate limiting, change gating, per-sender state) while it's carrying
    /// ten bytes rather than ninety-eight.
    /// </summary>
    public class HandSync
    {
        private const int Values = 10;   // five fingers per hand
        /// <summary>A finger has to move this much before it's worth a packet.</summary>
        private const float Epsilon = 2f / 255f;

        private readonly AvatarSwapManager _manager;
        private readonly ModRoster _roster;

        private readonly float[] _current = new float[Values];
        private readonly byte[] _lastSent = new byte[Values];
        private readonly byte[] _packet = new byte[Values];
        private float _nextSendAt;
        private float _nextKeyframeAt;
        private bool _everSent;

        public HandSync(AvatarSwapManager manager, ModRoster roster)
        {
            _manager = manager;
            _roster = roster;
            ModNet.RegisterHandler(ModNet.CodeHandPose, OnHandPose);
        }

        public void Tick(float unscaledTime)
        {
            if (!ModGate.Active || !ModConfig.HandPosesEnabled.Value) return;
            if (unscaledTime < _nextSendAt) return;

            var rate = Mathf.Clamp(ModConfig.HandSyncHz.Value, 1f, 30f);
            _nextSendAt = unscaledTime + 1f / rate;

            var poser = _manager.SelfHandPoser;
            if (poser == null) return;

            poser.GetCurls(_current);

            // A keyframe every couple of seconds covers late joiners and anyone who missed the
            // last change, without needing acknowledgements.
            var keyframe = unscaledTime >= _nextKeyframeAt || !_everSent;
            var changed = keyframe;

            for (var i = 0; i < Values; i++)
            {
                _packet[i] = (byte)Mathf.Clamp(Mathf.RoundToInt(_current[i] * 255f), 0, 255);
                if (!changed && Mathf.Abs(_packet[i] - _lastSent[i]) / 255f > Epsilon) changed = true;
            }
            if (!changed) return;

            var targets = _roster.ModdedPeerActors();
            if (targets.Length == 0) return;

            if (!ModNet.SendBytes(ModNet.CodeHandPose, _packet, reliable: false, targetActors: targets)) return;

            Buffer.BlockCopy(_packet, 0, _lastSent, 0, Values);
            _everSent = true;
            if (keyframe) _nextKeyframeAt = unscaledTime + 2f;
        }

        private void OnHandPose(int senderActor, Il2CppSystem.Object content)
        {
            var bytes = ModNet.AsBytes(content);
            if (bytes == null || bytes.Length < Values)
            {
                Core.Log.Warning($"Hand pose from actor {senderActor} was not {Values} bytes.");
                return;
            }

            var poser = _manager.RemoteHandPoser(senderActor);
            if (poser == null) return;   // that peer has no custom avatar on right now

            var curls = new float[Values];
            for (var i = 0; i < Values; i++) curls[i] = bytes[i] / 255f;
            poser.SetRemoteCurls(curls);
        }
    }
}
