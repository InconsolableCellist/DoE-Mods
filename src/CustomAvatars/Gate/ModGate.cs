using System;
using CustomAvatars.Recon;
using Il2CppPhoton.Pun;

namespace CustomAvatars.Gate
{
    /// <summary>
    /// The single master switch every feature consults. Nothing in this mod may change the
    /// game or put a byte on the wire unless <see cref="Active"/> is true.
    ///
    /// There are two switches, because there are two different risks.
    ///
    /// <see cref="Active"/> governs anything that touches other people: sending, receiving,
    /// putting an avatar on a peer. It requires ALL of:
    ///   1. we're in a **private** room,
    ///   2. every occupant advertises the identical mod version AND DLL hash,
    ///   3. our own DLL self-checksum succeeded.
    ///
    /// <see cref="LocalVisuals"/> governs what THIS machine draws for itself — your own avatar,
    /// the preview, the mannequins. None of that reaches anybody else, so it is also allowed
    /// when we are not in a room at all: the menu, and the moments between lobbies. There is
    /// nobody there to affect. It stays off in a public room or one with a vanilla player, not
    /// because it would leak anything, but because "friends, in private lobbies" is the rule
    /// this mod is built around and quietly making an exception is how rules stop meaning
    /// anything.
    ///
    /// Active implies LocalVisuals. Never the reverse.
    ///
    /// Honest threat model: peer checksums are self-reported, so this is **anti-footgun, not
    /// anti-malice**. It stops version-skew bugs and accidental mixed-lobby activation. The
    /// hard guarantees — no PlayFab writes, no stat-affecting patches — are structural
    /// properties of the code, not of this check.
    /// </summary>
    public static class ModGate
    {
        private static bool _active;
        private static bool _localVisuals;
        private static string _reason = "not evaluated";
        private static string _localReason = "not evaluated";

        /// <summary>Fired on every transition. Networked features hook this to arm and disarm.</summary>
        public static event Action<bool> ActiveChanged;

        /// <summary>Fired when permission to change what we draw for ourselves changes.</summary>
        public static event Action<bool> LocalVisualsChanged;

        public static bool Active => _active;

        /// <summary>May we change what this machine draws for itself? See the type remarks.</summary>
        public static bool LocalVisuals => _localVisuals;

        /// <summary>Why local visuals are in their current state.</summary>
        public static string LocalReason => _localReason;

        /// <summary>Why the gate is in its current state — for logs and the debug HUD.</summary>
        public static string Reason => _reason;

        /// <summary>
        /// Private-lobby test. **`IsVisible` only.** `IsOpen` flips to false the moment a run
        /// is in progress (measured 2026-08-31), so testing it would make the mod go inert on
        /// entering a dungeon — precisely where it needs to be live.
        /// </summary>
        public static bool RoomIsPrivate(out string why)
        {
            try
            {
                if (!PhotonNetwork.InRoom) { why = "not in a room"; return false; }
                var room = PhotonNetwork.CurrentRoom;
                if (ReferenceEquals(room, null)) { why = "CurrentRoom is null"; return false; }
                if (room.IsVisible) { why = $"room `{room.Name}` is VISIBLE (public)"; return false; }
                why = null;
                return true;
            }
            catch (Exception e)
            {
                why = $"room check threw {e.GetType().Name}";
                return false;
            }
        }

        public static void Evaluate(ModRoster roster)
        {
            var shouldBeActive = Compute(roster, out var reason);

            if (shouldBeActive == _active)
            {
                _reason = reason;
                EvaluateLocalVisuals();
                return;
            }

            _active = shouldBeActive;
            _reason = reason;

            if (_active)
                Core.Log.Msg($"*** ModGate ACTIVE — {reason}");
            else
                Core.Log.Msg($"*** ModGate INERT — {reason}");

            try { ActiveChanged?.Invoke(_active); }
            catch (Exception e) { Core.Log.Error($"ActiveChanged handler threw: {e}"); }

            EvaluateLocalVisuals();
        }

        private static void EvaluateLocalVisuals()
        {
            var allowed = ComputeLocalVisuals(out var reason);
            _localReason = reason;
            if (allowed == _localVisuals) return;

            _localVisuals = allowed;
            Core.Log.Msg(allowed
                ? $"*** Local visuals ON — {reason}"
                : $"*** Local visuals OFF — {reason}");

            try { LocalVisualsChanged?.Invoke(_localVisuals); }
            catch (Exception e) { Core.Log.Error($"LocalVisualsChanged handler threw: {e}"); }
        }

        private static bool ComputeLocalVisuals(out string reason)
        {
            if (_active) { reason = _reason; return true; }

            if (SelfCheck.ModHash == null)
            {
                reason = "local self-checksum unavailable — refusing to activate";
                return false;
            }

            // Not in a room means there is nobody else in the world to affect, so wearing your
            // own avatar in the menu is nobody's business but yours.
            try
            {
                if (!PhotonNetwork.InRoom) { reason = "not in a room — your own avatar only"; return true; }
            }
            catch (Exception e)
            {
                reason = $"room check threw {e.GetType().Name}";
                return false;
            }

            reason = _reason;
            return false;
        }

        private static bool Compute(ModRoster roster, out string reason)
        {
            if (SelfCheck.ModHash == null)
            {
                reason = "local self-checksum unavailable — refusing to activate";
                return false;
            }

            if (!RoomIsPrivate(out var why)) { reason = why; return false; }

            if (roster.Peers.Count == 0) { reason = "roster empty"; return false; }

            // The roster polls at 2 Hz, so for up to half a second after someone joins it
            // doesn't know they're there. Photon's own PlayerCount is live every frame:
            // if the two disagree, there is an occupant we haven't vetted, and an unvetted
            // occupant means inert. Fail closed on staleness rather than trusting a stale
            // "everyone here is modded".
            try
            {
                var count = PhotonNetwork.CurrentRoom.PlayerCount;
                if (count != roster.Peers.Count)
                {
                    reason = $"roster stale — room has {count} player(s), roster has {roster.Peers.Count}";
                    return false;
                }
            }
            catch (Exception e)
            {
                reason = $"PlayerCount check threw {e.GetType().Name}";
                return false;
            }

            var vanilla = 0;
            var skewed = 0;
            string firstVer = null, firstSha = null;

            foreach (var kv in roster.Peers)
            {
                var peer = kv.Value;
                if (!peer.IsModded) { vanilla++; continue; }
                if (firstVer == null) { firstVer = peer.Version; firstSha = peer.Sha; continue; }
                if (peer.Version != firstVer || peer.Sha != firstSha) skewed++;
            }

            if (vanilla > 0)
            {
                reason = $"{vanilla} vanilla player(s) present — {roster.Describe()}";
                return false;
            }
            if (skewed > 0)
            {
                // Exact match on version AND hash. A friends group is small enough that
                // "everyone runs the same build" is a reasonable thing to insist on, and
                // strict beats debugging a skew bug over voice chat at midnight.
                reason = $"build skew across {skewed + 1} peers — {roster.Describe()}";
                return false;
            }

            reason = roster.Peers.Count == 1
                ? $"private room, solo, build {firstVer}/{firstSha}"
                : $"private room, all {roster.Peers.Count} peers on {firstVer}/{firstSha}";
            return true;
        }

        /// <summary>Force-inert, e.g. on scene teardown or application quit. Closes both switches.</summary>
        public static void ForceInert(string reason)
        {
            _reason = reason;
            _localReason = reason;

            if (_active)
            {
                _active = false;
                Core.Log.Msg($"*** ModGate INERT — {reason}");
                try { ActiveChanged?.Invoke(false); }
                catch (Exception e) { Core.Log.Error($"ActiveChanged handler threw: {e}"); }
            }

            if (_localVisuals)
            {
                _localVisuals = false;
                try { LocalVisualsChanged?.Invoke(false); }
                catch (Exception e) { Core.Log.Error($"LocalVisualsChanged handler threw: {e}"); }
            }
        }
    }
}
