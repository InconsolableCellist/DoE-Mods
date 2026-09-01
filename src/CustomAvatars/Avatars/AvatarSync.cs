using System;
using CustomAvatars.Gate;
using CustomAvatars.Net;

namespace CustomAvatars.Avatars
{
    /// <summary>
    /// Tells peers which avatar you're wearing, and puts theirs on their bodies.
    ///
    /// Only the avatar's NAME and a hash prefix go over the wire — never the model. Everyone
    /// already has the files; sending an identifier keeps the message tiny and means a peer
    /// missing a file gets a clear explanation rather than a broken download.
    ///
    /// Sent reliably on event 141, because missing this message means someone looks wrong for
    /// the rest of the session — unlike the face stream, where a dropped packet is one stale
    /// frame.
    /// </summary>
    public class AvatarSync
    {
        private readonly AvatarSwapManager _manager;
        private readonly AvatarLibrary _library;
        private readonly ModRoster _roster;

        public AvatarSync(AvatarSwapManager manager, AvatarLibrary library, ModRoster roster)
        {
            _manager = manager;
            _library = library;
            _roster = roster;

            ModNet.RegisterHandler(ModNet.CodeAvatarManifest, OnAvatarMessage);
            ModGate.ActiveChanged += active => { if (active) Broadcast("gate opened"); };
            _roster.PeerJoined += peer => { if (!peer.IsLocal && ModGate.Active) Broadcast($"{peer.NickName} joined"); };
            _manager.SelfAvatarChanged += () => Broadcast("we changed avatar");
            _roster.PeerLeft += peer => _manager.RevertRemote(peer.ActorNumber, "peer left the room");
        }

        /// <summary>Announce what we're wearing — or that we've taken it off.</summary>
        public void Broadcast(string why)
        {
            if (!ModGate.Active) return;

            var targets = _roster.ModdedPeerActors();
            if (targets.Length == 0) return;

            var name = _manager.SelfAvatarName;
            string payload;
            if (string.IsNullOrEmpty(name))
            {
                payload = "avatar|";
            }
            else
            {
                var manifest = _library.Get(name);
                var sha = manifest?.sha256 ?? "";
                if (sha.Length > 16) sha = sha.Substring(0, 16);
                payload = $"avatar|{name}|{sha}";
            }

            if (ModNet.Send(ModNet.CodeAvatarManifest, payload, reliable: true, targetActors: targets))
                Core.Log.Msg($"Told {targets.Length} peer(s) we're wearing " +
                             $"{(string.IsNullOrEmpty(name) ? "nothing custom" : $"`{name}`")} ({why}).");
        }

        private void OnAvatarMessage(int senderActor, Il2CppSystem.Object content)
        {
            try
            {
                var text = content?.ToString() ?? "";
                var parts = text.Split('|');
                if (parts.Length < 2 || parts[0] != "avatar")
                {
                    Core.Log.Warning($"Unrecognised avatar message from actor {senderActor}: `{text}`");
                    return;
                }

                var name = parts[1];
                var sha = parts.Length > 2 ? parts[2] : "";

                Core.Log.Msg(string.IsNullOrEmpty(name)
                    ? $"Actor {senderActor} took their custom avatar off."
                    : $"Actor {senderActor} is wearing `{name}`.");

                _manager.SetRemoteAvatar(senderActor, name, sha);
            }
            catch (Exception e)
            {
                Core.Log.Error($"Avatar message from actor {senderActor} failed: {e}");
            }
        }
    }
}
