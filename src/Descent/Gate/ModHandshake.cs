using Descent.Net;

namespace Descent.Gate
{
    /// <summary>
    /// When the gate opens, say hello on the handshake code and log what comes back. Proves
    /// RaiseEvent round-trips between two Descent clients before anything is built on it.
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
            if (ModGate.Active && !peer.IsLocal) SayHello($"peer {peer.NickName} joined");
        }

        private void SayHello(string why)
        {
            var targets = _roster.ModdedPeerActors();
            if (targets.Length == 0) return;

            var payload = $"hello|{Core.Version}|{SelfCheck.ShortHash}";
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
