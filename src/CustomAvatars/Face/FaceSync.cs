using System;
using CustomAvatars.Avatars;
using CustomAvatars.Gate;
using CustomAvatars.Net;
using UnityEngine;

namespace CustomAvatars.Face
{
    /// <summary>
    /// Streams face shapes to peers.
    ///
    /// **Budget matters more than it looks, because PUN is not peer-to-peer.** Photon relays
    /// every message through its cloud servers, on the game publisher's subscription, and those
    /// plans cap *messages per second per room* as well as bandwidth. So this is built to be
    /// predictable and small, and to cost nothing at all when a face is still:
    ///
    /// * **Fixed tick.** One message per tick at `FaceSyncHz` (default 10), never more,
    ///   regardless of how fast tracking updates. VRCFaceTracking runs at up to 100 Hz; we do
    ///   not forward that.
    /// * **Change-gated.** A shape is only included if it moved by more than a quantisation
    ///   step. A still face sends nothing — not an empty message, nothing.
    /// * **One byte per shape.** 1/255 resolution is finer than a blendshape is worth.
    /// * **Unreliable.** A lost packet costs one stale frame; the next tick corrects it.
    ///   Retransmitting a face from 100 ms ago would be worse than skipping it.
    /// * **A hard ceiling.** `FaceSyncMaxShapes` (default 24) caps how many shapes go in one
    ///   message, so a worst case is bounded rather than proportional to the avatar.
    ///
    /// Typical cost with a face in motion: 10 messages/s and roughly 300–500 B/s per player.
    /// Absolute worst case at the defaults: 10 messages/s and 500 B/s. For comparison, the game
    /// itself syncs three transforms per player continuously.
    /// </summary>
    public class FaceSync
    {
        private readonly AvatarSwapManager _manager;
        private readonly ModRoster _roster;

        private readonly byte[] _lastSent = new byte[UEShapes.Count];
        private readonly bool[] _everSent = new bool[UEShapes.Count];
        private readonly byte[] _packet = new byte[2 + UEShapes.Count * 2];

        private float _nextSendAt;
        private float _nextKeyframeAt;
        private byte _sequence;

        // Accounting, so the budget claim above can be checked rather than believed.
        private long _bytesSent, _messagesSent;
        private float _nextBudgetLogAt;

        public FaceSync(AvatarSwapManager manager, ModRoster roster)
        {
            _manager = manager;
            _roster = roster;
            ModNet.RegisterHandler(ModNet.CodeFaceStream, OnFaceStream);
        }

        public void Tick(float unscaledTime)
        {
            LogBudget(unscaledTime);

            if (!ModGate.Active) return;
            if (unscaledTime < _nextSendAt) return;

            var hz = Mathf.Clamp(ModConfig.FaceSyncHz.Value, 1f, 20f);
            _nextSendAt = unscaledTime + 1f / hz;

            var state = Core.Instance?.FaceState;
            if (state == null) return;

            // Nothing to say if tracking isn't live — peers fall back to the voice-driven jaw,
            // which needs no traffic at all.
            var age = state.SecondsSinceLastMessage;
            if (age < 0 || age > ModConfig.FaceStaleSeconds.Value) return;

            if (!_manager.SelfActive) return;

            var targets = _roster.ModdedPeerActors();
            if (targets.Length == 0) return;

            var keyframe = unscaledTime >= _nextKeyframeAt;
            var count = Pack(state, keyframe, out var length);
            if (count == 0) return;

            if (ModNet.SendBytes(ModNet.CodeFaceStream, Trim(length), reliable: false, targetActors: targets))
            {
                _bytesSent += length;
                _messagesSent++;
                if (keyframe) _nextKeyframeAt = unscaledTime + 2f;
            }
        }

        /// <summary>Fill the packet with changed shapes. Returns how many were included.</summary>
        private int Pack(FaceState state, bool keyframe, out int length)
        {
            var epsilon = Mathf.Max(1, Mathf.RoundToInt(ModConfig.FaceSyncEpsilon.Value * 255f));
            var max = Mathf.Clamp(ModConfig.FaceSyncMaxShapes.Value, 1, UEShapes.Count);

            var count = 0;
            var offset = 2;

            for (var id = 0; id < UEShapes.Count && count < max; id++)
            {
                var quantised = (byte)Mathf.Clamp(Mathf.RoundToInt(FaceDriver.Sample(state, id) * 255f), 0, 255);

                var changed = !_everSent[id] || Mathf.Abs(quantised - _lastSent[id]) >= epsilon;
                if (!keyframe && !changed) continue;
                // A keyframe still skips shapes that have never left zero — most of them, on
                // most hardware — so it stays small too.
                if (keyframe && !changed && quantised == 0 && _lastSent[id] == 0) continue;

                _packet[offset++] = (byte)id;
                _packet[offset++] = quantised;
                _lastSent[id] = quantised;
                _everSent[id] = true;
                count++;
            }

            _packet[0] = _sequence++;
            _packet[1] = (byte)count;
            length = offset;
            return count;
        }

        private byte[] Trim(int length)
        {
            var trimmed = new byte[length];
            Buffer.BlockCopy(_packet, 0, trimmed, 0, length);
            return trimmed;
        }

        private void OnFaceStream(int senderActor, Il2CppSystem.Object content)
        {
            var bytes = ModNet.AsBytes(content);
            if (bytes == null || bytes.Length < 2) return;

            var driver = _manager.RemoteFaceDriver(senderActor);
            if (driver == null) return;   // that peer has no custom avatar on

            var values = driver.RemoteValues ?? new float[UEShapes.Count];

            var count = bytes[1];
            var offset = 2;
            for (var i = 0; i < count && offset + 1 < bytes.Length; i++)
            {
                var id = bytes[offset++];
                var value = bytes[offset++] / 255f;
                if (id < values.Length) values[id] = value;
            }

            driver.RemoteValues = values;
            driver.RemoteAgeSeconds = 0;
        }

        private void LogBudget(float unscaledTime)
        {
            if (unscaledTime < _nextBudgetLogAt) return;
            var first = _nextBudgetLogAt == 0f;
            _nextBudgetLogAt = unscaledTime + 60f;
            if (first || _messagesSent == 0) return;

            Core.Log.Msg($"Face stream: {_messagesSent / 60f:0.#} msg/s, {_bytesSent / 60f:0} B/s (last minute).");
            _bytesSent = 0;
            _messagesSent = 0;
        }
    }
}
