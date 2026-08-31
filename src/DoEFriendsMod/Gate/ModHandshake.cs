using System;
using DoEFriendsMod.Net;

namespace DoEFriendsMod.Gate
{
    /// <summary>
    /// A deliberately trivial user of <see cref="ModNet"/>: when the gate opens, say hello on
    /// code 140 and log what comes back.
    ///
    /// Discovery itself is done by player custom properties, so this carries no load — its job
    /// is to prove RaiseEvent round-trips between two modded clients *before* Phase 2 builds
    /// avatar streaming on top of it. Debugging a silent transport underneath a broken avatar
    /// swap is not a position worth being in.
    /// </summary>
    public class ModHandshake
    {
        private readonly ModRoster _roster;

        public ModHandshake(ModRoster roster)
        {
            _roster = roster;
            ModNet.RegisterHandler(ModNet.CodeHandshake, OnHello);
            ModGate.ActiveChanged += OnGateChanged;
            _roster.PeerJoined += OnPeerJoined;
        }

        private void OnGateChanged(bool active)
        {
            if (active) SayHello("gate opened");
        }

        private void OnPeerJoined(ModPeer peer)
        {
            // A late joiner needs to hear from us; re-greeting the room is one packet.
            if (ModGate.Active && !peer.IsLocal) SayHello($"peer {peer.NickName} joined");
        }

        private void SayHello(string why)
        {
            var targets = _roster.ModdedPeerActors();
            if (targets.Length == 0) return;

            var payload = $"hello|{Core.Version}|{Recon.SelfCheck.ShortHash}";
            if (ModNet.Send(ModNet.CodeHandshake, payload, reliable: true, targetActors: targets))
                Core.Log.Msg($"Handshake sent to {targets.Length} peer(s) ({why}).");
        }

        private void OnHello(int senderActor, Il2CppSystem.Object content)
        {
            var text = ReferenceEquals(content, null) ? "<null>" : content.ToString();
            Core.Log.Msg($"*** Handshake received from actor {senderActor}: {text}");
        }
    }
}
