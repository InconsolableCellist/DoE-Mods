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
        private readonly Dictionary<int, (string name, string sha, float height, float size)> _pending =
            new Dictionary<int, (string, string, float, float)>();

        public AvatarSwapManager(AvatarLibrary library)
        {
            _library = library;
            // Our own fit is measured a second after the swap, and again whenever we resize
            // ourselves — both are moments peers need to hear about, or they keep drawing us
            // at the size we were when we put the avatar on.
            _self.HeightScaleChanged += () => SelfHeightChanged?.Invoke();
            // Deferred to the next Tick: the request comes from inside the swapper's own
            // LateUpdate, and replacing it there would pull the floor out from under it.
            _self.RebindRequested += why => _rewearWhy = why;
            ModGate.ActiveChanged += active => { if (!active) RevertRemotes("gate closed"); };
            ModGate.LocalVisualsChanged += allowed => { if (!allowed) RevertAll("local visuals off"); };
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

        /// <summary>Raised when our avatar has been resized — a different fit, or a resized us.</summary>
        public event Action SelfHeightChanged;

        /// <summary>How much our own avatar is scaled, for peers who have to draw it.</summary>
        public float SelfHeightScale => _self.IsActive ? _self.HeightScale : 1f;

        /// <summary>How much a peer's avatar is scaled, as they told us; 1 when they wear none.</summary>
        public float RemoteHeightScale(int actorNumber) =>
            _remote.TryGetValue(actorNumber, out var swapper) && swapper.IsActive ? swapper.HeightScale : 1f;

        /// <summary>
        /// We have just become a different size (PlayerSize). The avatar's fit, its spring
        /// forces and its arm geometry were all built for the old one, so it comes off and
        /// goes straight back on — the same re-bind a held T-pose does — once the keys have
        /// stopped: a run of PageUp presses is one change, not ten.
        /// </summary>
        public void OnSelfSizeChanged(float size)
        {
            if (!_self.IsActive) return;
            _rewearAt = UnityEngine.Time.unscaledTime + 0.6f;
            _rewearSizeWhy = $"you are now x{size:0.00}";
        }

        /// <summary>F4. Returns the avatar now worn, or null if it was taken off or refused.</summary>
        public string ToggleSelf()
        {
            // F4 in the gap of a re-wear is the second press itself; nothing is owed afterwards.
            _rewearOnAt = 0f;
            _rewearName = null;
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
            if (_autoWearDone || !ModConfig.AutoWear.Value || !ModGate.LocalVisuals) return;
            if (_self.IsActive || !string.IsNullOrEmpty(_selfWanted)) { _autoWearDone = true; return; }

            var name = _library.SelectedName;
            if (name == null) { _autoWearDone = true; return; }

            // Recorded as an intention, not applied here. There may be no body to put it on yet
            // — in the menu there often isn't — and HealSelf is already the thing that watches
            // for one and dresses it. This also lets the menu mannequin know what we mean to
            // wear before we are wearing it.
            _autoWearDone = true;
            _selfWanted = name;
            Core.Log.Msg($"Wearing `{name}` (AutoWear). Press F4 to take it off.");
        }

        /// <summary>What we mean to be wearing, whether or not there is a body to put it on.</summary>
        public string WantedSelfAvatar => _selfWanted;

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
            if (string.IsNullOrEmpty(_selfWanted) || !ModGate.LocalVisuals) return;
            // Half way through a re-wear the avatar is off on purpose; FinishReWear puts it back.
            if (_rewearOnAt > 0f) return;

            AvatarPlayer local = null;
            try { local = AvatarPlayer.LocalAvatar; } catch { }
            if (!Interop.Alive(local)) return;

            // Still attached to a live player object: nothing to do.
            if (_self.IsActive && _self.IsAttachedTo(local)) return;

            var manifest = _library.Get(_selfWanted);
            if (manifest == null) { _selfWanted = null; return; }

            // A swap that failed (bundle not loadable, body not built) is retried once a
            // second, not once a frame: the failure logs an error each time, and 162 of them
            // in two seconds buried the line that explained it.
            if (UnityEngine.Time.unscaledTime < _nextHealAt) return;

            var wasActive = _self.IsActive;
            if (wasActive) Core.Log.Msg($"Re-applying `{_selfWanted}` — the player object was replaced.");
            _self.Revert("player object replaced");
            _self.Apply(local, manifest, isSelf: true);
            if (!_self.IsActive) { _nextHealAt = UnityEngine.Time.unscaledTime + 1f; return; }
            // Peers only hear about the avatar when we tell them, and coming back from a scene
            // change is exactly when a friend who joined meanwhile has heard nothing.
            SelfAvatarChanged?.Invoke();
        }

        private float _nextHealAt;

        /// <summary>
        /// F4 twice: take the avatar off, and put it back on a moment later.
        ///
        /// A re-bind that only re-captured the reference pose and rebuilt the solvers in place
        /// was tried first and was not the same thing — the avatar didn't come back to the
        /// position and fit a fresh swap gives it. A fresh swap is a new model instance in its
        /// bind pose, a new reference, a new fit, new springs, new everything; there is no
        /// cheaper equivalent, so this does the real thing.
        ///
        /// In two steps, with a gap, not off-and-on inside one frame. Two F4 presses have a
        /// second or so between them, and in that second the game has the vanilla body back:
        /// it solves it, sizes it and draws it, so the second press captures its reference off
        /// a body that has settled without us. Off-and-on in one frame captured it off the
        /// body as we had just left it, and testers could tell: a held T-pose did not fix
        /// what two F4s fixed. So the T-pose now does exactly what the two presses do, with
        /// `RebindGapSeconds` between them.
        /// </summary>
        private void ReWearSelf(string why)
        {
            var name = _self.AvatarName ?? _selfWanted;
            if (string.IsNullOrEmpty(name) || !ModGate.LocalVisuals) return;
            if (_library.Get(name) == null) { Fbt.FbtAudio.Error(); return; }

            var gap = UnityEngine.Mathf.Max(0f, ModConfig.RebindGapSeconds.Value);
            Core.Log.Msg($"*** Re-wearing `{name}` ({why}) — off now, back on in {gap:0.0} s.");
            // Start from the fit we had, moved by however much our size changed since it was
            // measured, so the second before the new measurement doesn't leave the feet in
            // the air. Exact when nothing but the size changed.
            _rewearFit = _self.IsActive && _self.HeightScale > 0f
                ? _self.HeightScale * PlayerSize.Applied / UnityEngine.Mathf.Max(0.05f, _self.SizeAtFit)
                : 0f;
            _rewearName = name;
            _self.Revert(why);
            // Tell peers it came off BEFORE it goes back on, exactly as two F4 presses do. A
            // single "still wearing X" afterwards is read on their side as a resize, which
            // keeps their copy of us — springs, face and all — and with it whatever was wrong
            // with it. Off then on is the one message that makes them build a fresh one, and a
            // fresh one is the whole point of re-wearing. Both messages are reliable and
            // ordered, so they cannot land the other way round.
            SelfAvatarChanged?.Invoke();
            _rewearOnAt = UnityEngine.Time.unscaledTime + gap;
        }

        /// <summary>The second F4 of the two: put the avatar back on after the gap.</summary>
        private void FinishReWear()
        {
            _rewearOnAt = 0f;
            var name = _rewearName;
            _rewearName = null;
            if (string.IsNullOrEmpty(name) || !ModGate.LocalVisuals) return;
            // Something else dressed us during the gap (F4, or a scene change healed): done.
            if (_self.IsActive) return;

            AvatarPlayer local = null;
            try { local = AvatarPlayer.LocalAvatar; } catch { }
            // No body to put it on: HealSelf dresses the next one, since it is still wanted.
            if (!Interop.Alive(local)) { _selfWanted = name; return; }

            var manifest = _library.Get(name);
            if (manifest == null) { Fbt.FbtAudio.Error(); return; }

            _self.Apply(local, manifest, isSelf: true, initialFit: _rewearFit);
            _selfWanted = _self.AvatarName ?? _selfWanted;
            SelfAvatarChanged?.Invoke();

            // You are in a headset holding a T-pose; this is how you know it took. The same
            // rise FBT plays when a calibration locks in.
            if (_self.IsActive) Fbt.FbtAudio.Locked(); else Fbt.FbtAudio.Error();
        }

        /// <summary>A peer told us what they're wearing, and how big they are wearing it.</summary>
        public void SetRemoteAvatar(int actorNumber, string avatarName, string sha, float height, float size)
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

            // Already wearing that one: this is a resize, not a change of avatar. Rebuilding
            // the whole model to apply a number would drop their springs, face and hand poses
            // for a frame, and they resize far more often than they change avatars.
            if (_remote.TryGetValue(actorNumber, out var current) && current.IsActive &&
                string.Equals(current.AvatarName, avatarName, StringComparison.Ordinal))
            {
                current.ApplyRemoteHeight(height);
                current.ApplyRemoteBodySize(size);
                _pending.Remove(actorNumber);
                return;
            }

            _pending[actorNumber] = (avatarName, sha, height, size);
            TryApplyPending();
        }

        /// <summary>
        /// Called every frame. A peer's avatar message usually arrives before their
        /// AvatarPlayer has spawned, so the request is held and retried rather than dropped.
        /// </summary>
        /// <summary>What we were wearing before a scene change invalidated the player object.</summary>
        private string _selfWanted;
        private string _rewearWhy;

        private float _rewearAt;
        private string _rewearSizeWhy;
        // The re-wear's second half: what goes back on, at what fit, and when.
        private float _rewearOnAt;
        private string _rewearName;
        private float _rewearFit;

        public void Tick(float deltaTime)
        {
            AutoWear();
            HealSelf();
            if (_rewearOnAt > 0f)
            {
                if (UnityEngine.Time.unscaledTime >= _rewearOnAt) FinishReWear();
            }
            else if (_rewearWhy != null) { var why = _rewearWhy; _rewearWhy = null; ReWearSelf(why); }
            else if (_rewearAt > 0f && UnityEngine.Time.unscaledTime >= _rewearAt)
            {
                _rewearAt = 0f;
                ReWearSelf(_rewearSizeWhy ?? "size changed");
            }

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
                if (swapper.IsActive)
                {
                    swapper.ApplyRemoteHeight(kv.Value.height);
                    swapper.ApplyRemoteBodySize(kv.Value.size);
                    _remote[kv.Key] = swapper;
                }

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

        /// <summary>Actor number → live AvatarPlayer, or null. Also used by the FBT layer.</summary>
        public static AvatarPlayer FindPlayer(int actorNumber)
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

        /// <summary>Take peers' avatars off, leaving our own alone.</summary>
        public void RevertRemotes(string why)
        {
            if (_remote.Count == 0) return;
            var actors = new List<int>(_remote.Keys);
            foreach (var actor in actors) RevertRemote(actor, why);
            _pending.Clear();
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
