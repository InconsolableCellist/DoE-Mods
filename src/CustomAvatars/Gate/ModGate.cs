using System;
using CustomAvatars.Recon;
using Il2CppPhoton.Pun;

namespace CustomAvatars.Gate
{
    /// <summary>
    /// The single master switch every feature consults. Nothing in this mod may change the
    /// game or put a byte on the wire unless <see cref="Active"/> is true.
    ///
    /// Active requires ALL of:
    ///   1. we're in a **private** room,
    ///   2. every occupant advertises the identical mod version AND DLL hash,
    ///   3. our own DLL self-checksum succeeded.
    ///
    /// Honest threat model: peer checksums are self-reported, so this is **anti-footgun, not
    /// anti-malice**. It stops version-skew bugs and accidental mixed-lobby activation. The
    /// hard guarantees — no PlayFab writes, no stat-affecting patches — are structural
    /// properties of the code, not of this check.
    /// </summary>
    public static class ModGate
    {
        private static bool _active;
        private static string _reason = "not evaluated";

        /// <summary>Fired on every transition. Features hook this to arm and disarm.</summary>
        public static event Action<bool> ActiveChanged;

        public static bool Active => _active;

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

        /// <summary>Force-inert, e.g. on scene teardown or application quit.</summary>
        public static void ForceInert(string reason)
        {
            if (!_active) { _reason = reason; return; }
            _active = false;
            _reason = reason;
            Core.Log.Msg($"*** ModGate INERT — {reason}");
            try { ActiveChanged?.Invoke(false); }
            catch (Exception e) { Core.Log.Error($"ActiveChanged handler threw: {e}"); }
        }
    }
}
