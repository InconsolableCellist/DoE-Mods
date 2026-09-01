using System;
using System.Collections.Generic;
using CustomAvatars.Gate;
using CustomAvatars.Recon;
using Il2Cpp;
using Interop = CustomAvatars.Recon.Interop;

namespace CustomAvatars.Avatars
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
        /// <summary>Which custom avatar a given player is wearing right now, or null.</summary>
        public string AvatarNameFor(int actorNumber)
        {
            if (_self.IsActive && _self.ActorNumber == actorNumber) return _self.AvatarName;
            return _remote.TryGetValue(actorNumber, out var swapper) && swapper.IsActive
                ? swapper.AvatarName : null;
        }

        /// <summary>A peer's face driver, the destination for their incoming face stream.</summary>
        public Face.FaceDriver RemoteFaceDriver(int actorNumber) =>
            _remote.TryGetValue(actorNumber, out var swapper) && swapper.IsActive ? swapper.Face : null;

        public HandPoser RemoteHandPoser(int actorNumber) =>
            _remote.TryGetValue(actorNumber, out var swapper) && swapper.IsActive ? swapper.Hands : null;
        public string SelfAvatarName => _self.AvatarName;

        /// <summary>Raised when the avatar we are wearing changes, including taking it off.</summary>
        public event Action SelfAvatarChanged;

        /// <summary>F4. Returns the avatar now worn, or null if it was taken off or refused.</summary>
        public string ToggleSelf()
        {
            _self.Toggle(_library);
            _selfWanted = _self.AvatarName;   // null once taken off, so we don't re-apply it
            // Whichever way F4 went, that was a deliberate choice — don't put the avatar back on
            // over the top of someone who has just taken it off.
            _autoWearDone = true;
            SelfAvatarChanged?.Invoke();
            return _self.AvatarName;
        }

        /// <summary>
        /// Put the chosen avatar on as soon as there is somewhere to put it.
        ///
        /// Waiting for F4 means the first thing you see in the equipment room is the stock
        /// character, next to a mannequin already wearing your avatar. Once per session is
        /// enough: after that, taking it off has to stick.
        /// </summary>
        private bool _autoWearDone;

        private void AutoWear()
        {
            if (_autoWearDone || !ModConfig.AutoWear.Value || !ModGate.Active) return;
            if (_self.IsActive || !string.IsNullOrEmpty(_selfWanted)) { _autoWearDone = true; return; }

            AvatarPlayer local = null;
            try { local = AvatarPlayer.LocalAvatar; } catch { }
            if (!Interop.Alive(local)) return;   // not spawned yet; try again next frame

            var name = _library.SelectedName;
            if (name == null) { _autoWearDone = true; return; }

            _autoWearDone = true;
            Core.Log.Msg($"Putting `{name}` on for you (AutoWear). Press F4 to take it off.");
            _selfWanted = name;                  // HealSelf does the rest, and keeps doing it
        }

        /// <summary>
        /// Put the avatar back on after the player object underneath it has been replaced.
        ///
        /// Changing scene — finishing a mission, returning to the hub — destroys the old
        /// AvatarPlayer and builds a new one. The swapper was left holding the dead one, which
        /// is how a death ended with no body at all and F4 refusing to help: the old model was
        /// gone, the vanilla mesh was still hidden on an object nobody could reach, and every
        /// fresh attempt threw on the stale reference.
        /// </summary>
        private void HealSelf()
        {
            if (string.IsNullOrEmpty(_selfWanted) || !ModGate.Active) return;

            AvatarPlayer local = null;
            try { local = AvatarPlayer.LocalAvatar; } catch { }
            if (!Interop.Alive(local)) return;

            // Still attached to a live player object: nothing to do.
            if (_self.IsActive && _self.IsAttachedTo(local)) return;

            var manifest = _library.Get(_selfWanted);
            if (manifest == null) { _selfWanted = null; return; }

            var wasActive = _self.IsActive;
            if (wasActive) Core.Log.Msg($"Re-applying `{_selfWanted}` — the player object was replaced.");
            _self.Revert("player object replaced");
            _self.Apply(local, manifest, isSelf: true);
            // Peers only hear about the avatar when we tell them, and coming back from a scene
            // change is exactly when a friend who joined meanwhile has heard nothing.
            SelfAvatarChanged?.Invoke();
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
        /// <summary>What we were wearing before a scene change invalidated the player object.</summary>
        private string _selfWanted;

        public void Tick(float deltaTime)
        {
            AutoWear();
            HealSelf();

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
