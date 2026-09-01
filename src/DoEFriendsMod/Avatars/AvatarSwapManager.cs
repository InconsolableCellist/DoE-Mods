using System;
using System.Collections.Generic;
using DoEFriendsMod.Gate;
using DoEFriendsMod.Recon;
using Il2Cpp;
using Interop = DoEFriendsMod.Recon.Interop;

namespace DoEFriendsMod.Avatars
{
    /// <summary>
    /// Owns one <see cref="AvatarSwapper"/> per player — yours plus one for each peer wearing a
    /// custom avatar. Everything a swapper needs is per-player already; this just keeps track
    /// of which is which and makes sure nothing is left behind when someone leaves.
    /// </summary>
    public class AvatarSwapManager
    {
        private readonly AvatarLibrary _library;
        private readonly AvatarSwapper _self = new AvatarSwapper();
        private readonly Dictionary<int, AvatarSwapper> _remote = new Dictionary<int, AvatarSwapper>();

        /// <summary>Peers who told us their avatar before their AvatarPlayer existed yet.</summary>
        private readonly Dictionary<int, (string name, string sha)> _pending =
            new Dictionary<int, (string, string)>();

        public AvatarSwapManager(AvatarLibrary library)
        {
            _library = library;
            ModGate.ActiveChanged += active => { if (!active) RevertAll("gate closed"); };
        }

        public bool SelfActive => _self.IsActive;

        /// <summary>Our own finger poser, the source for the outgoing hand stream.</summary>
        public HandPoser SelfHandPoser => _self.IsActive ? _self.Hands : null;

        /// <summary>A peer's finger poser, the destination for their incoming hand stream.</summary>
        public HandPoser RemoteHandPoser(int actorNumber) =>
            _remote.TryGetValue(actorNumber, out var swapper) && swapper.IsActive ? swapper.Hands : null;
        public string SelfAvatarName => _self.AvatarName;

        /// <summary>F4. Returns the avatar now worn, or null if it was taken off or refused.</summary>
        public string ToggleSelf()
        {
            _self.Toggle(_library);
            return _self.AvatarName;
        }

        /// <summary>A peer told us what they're wearing. Apply it if we have that avatar.</summary>
        public void SetRemoteAvatar(int actorNumber, string avatarName, string sha)
        {
            if (string.IsNullOrEmpty(avatarName))
            {
                RevertRemote(actorNumber, "peer took their avatar off");
                _pending.Remove(actorNumber);
                return;
            }

            var manifest = _library.Get(avatarName);
            if (manifest == null)
            {
                // Not an error on our side — they have a file we don't. Say which, because the
                // fix is simply copying it over, and it's the most likely thing to go wrong.
                Core.Log.Warning($"Peer (actor {actorNumber}) is wearing `{avatarName}`, which isn't installed here. " +
                                 $"They'll look vanilla to you until that avatar is copied into {AvatarLibrary.AvatarsDir}");
                _pending.Remove(actorNumber);
                return;
            }

            if (!string.IsNullOrEmpty(sha) && !manifest.sha256.StartsWith(sha, StringComparison.OrdinalIgnoreCase))
            {
                Core.Log.Error($"Peer (actor {actorNumber}) has a DIFFERENT build of `{avatarName}` " +
                               $"(theirs {sha}…, ours {manifest.sha256.Substring(0, 16)}…). Refusing to use ours — " +
                               "you would be looking at a different model than they are. Re-share the avatar files.");
                _pending.Remove(actorNumber);
                return;
            }

            _pending[actorNumber] = (avatarName, sha);
            TryApplyPending();
        }

        /// <summary>
        /// Called every frame. A peer's avatar message usually arrives before their
        /// AvatarPlayer has spawned, so the request is held and retried rather than dropped.
        /// </summary>
        public void Tick(float deltaTime)
        {
            _self.LateUpdate(deltaTime);
            foreach (var kv in _remote) kv.Value.LateUpdate(deltaTime);

            if (_pending.Count > 0) TryApplyPending();
            PruneDeparted();
        }

        private void TryApplyPending()
        {
            if (_pending.Count == 0 || !ModGate.Active) return;

            List<int> applied = null;
            foreach (var kv in _pending)
            {
                var player = FindPlayer(kv.Key);
                if (!Interop.Alive(player)) continue;   // not spawned yet; try again next frame

                var manifest = _library.Get(kv.Value.name);
                if (manifest == null) { (applied ??= new List<int>()).Add(kv.Key); continue; }

                RevertRemote(kv.Key, "replacing with a newer choice");

                var swapper = new AvatarSwapper();
                swapper.Apply(player, manifest, isSelf: false);
                if (swapper.IsActive) _remote[kv.Key] = swapper;

                (applied ??= new List<int>()).Add(kv.Key);
            }

            if (applied != null) foreach (var actor in applied) _pending.Remove(actor);
        }

        /// <summary>Drop swappers whose player has gone, so a leaver doesn't leave a body behind.</summary>
        private void PruneDeparted()
        {
            if (_remote.Count == 0) return;
            List<int> gone = null;
            foreach (var kv in _remote)
                if (!Interop.Alive(FindPlayer(kv.Key))) (gone ??= new List<int>()).Add(kv.Key);
            if (gone == null) return;
            foreach (var actor in gone) RevertRemote(actor, "player left");
        }

        private static AvatarPlayer FindPlayer(int actorNumber)
        {
            try
            {
                var all = AvatarPlayer.AllPlayers;
                if (ReferenceEquals(all, null)) return null;
                for (var i = 0; i < all.Count; i++)
                {
                    var ap = all[i];
                    if (!Interop.Alive(ap)) continue;
                    try { if (ap.ActorNumber == actorNumber) return ap; } catch { }
                }
            }
            catch { }
            return null;
        }

        public void RevertRemote(int actorNumber, string why)
        {
            if (!_remote.TryGetValue(actorNumber, out var swapper)) return;
            swapper.Revert($"actor {actorNumber}: {why}");
            _remote.Remove(actorNumber);
        }

        public void RevertAll(string why)
        {
            _self.Revert(why);
            foreach (var kv in _remote) kv.Value.Revert(why);
            _remote.Clear();
            _pending.Clear();
        }

        public string Describe() =>
            $"self: {(_self.IsActive ? _self.AvatarName : "vanilla")}, " +
            $"{_remote.Count} peer avatar(s), {_pending.Count} waiting";
    }
}
