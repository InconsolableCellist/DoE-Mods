using System;
using Il2CppPhoton.Pun;

namespace Descent.Gate
{
    /// <summary>
    /// The single master switch every feature consults. Nothing in this mod may change the
    /// game or put a byte on the wire unless <see cref="Active"/> is true.
    ///
    /// Two switches, for two different risks.
    ///
    /// <see cref="Active"/> governs anything that touches other people or the shared world:
    /// drop rolls, loot tags, claims, the booth, equipping a bag weapon. It requires ALL of:
    ///   1. we're in a **private** room,
    ///   2. every occupant advertises the identical Descent version AND DLL hash,
    ///   3. our own DLL self-checksum succeeded.
    ///
    /// <see cref="LocalOnly"/> governs what THIS machine may do for itself when nobody else
    /// is affected: browsing your own bag in the menu, reading your inventory file. It is also
    /// allowed when not in a room at all. It stays off in a public room or one with a vanilla
    /// player, because "friends, in private lobbies" is the rule and quiet exceptions are how
    /// rules stop meaning anything.
    ///
    /// Active implies LocalOnly. Never the reverse.
    ///
    /// Honest threat model: peer checksums are self-reported, so this is anti-footgun, not
    /// anti-malice. The hard guarantee — no PlayFab writes — is a structural property of the
    /// code, not of this check.
    /// </summary>
    public static class ModGate
    {
        private static bool _active;
        private static bool _localOnly;
        private static string _reason = "not evaluated";
        private static string _localReason = "not evaluated";

        /// <summary>Fired on every transition. Networked features hook this to arm and disarm.</summary>
        public static event Action<bool> ActiveChanged;

        /// <summary>Fired when permission for purely local features changes.</summary>
        public static event Action<bool> LocalOnlyChanged;

        public static bool Active => _active;

        /// <summary>May this machine do things that affect only itself? See the type remarks.</summary>
        public static bool LocalOnly => _localOnly;

        public static string LocalReason => _localReason;

        /// <summary>Why the gate is in its current state — for logs and any debug HUD.</summary>
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
                EvaluateLocalOnly();
                return;
            }

            _active = shouldBeActive;
            _reason = reason;

            Core.Log.Msg(_active ? $"*** ModGate ACTIVE — {reason}" : $"*** ModGate INERT — {reason}");

            try { ActiveChanged?.Invoke(_active); }
            catch (Exception e) { Core.Log.Error($"ActiveChanged handler threw: {e}"); }

            EvaluateLocalOnly();
        }

        private static void EvaluateLocalOnly()
        {
            var allowed = ComputeLocalOnly(out var reason);
            _localReason = reason;
            if (allowed == _localOnly) return;

            _localOnly = allowed;
            Core.Log.Msg(allowed ? $"*** Local features ON — {reason}" : $"*** Local features OFF — {reason}");

            try { LocalOnlyChanged?.Invoke(_localOnly); }
            catch (Exception e) { Core.Log.Error($"LocalOnlyChanged handler threw: {e}"); }
        }

        private static bool ComputeLocalOnly(out string reason)
        {
            if (_active) { reason = _reason; return true; }

            if (SelfCheck.ModHash == null)
            {
                reason = "local self-checksum unavailable — refusing to activate";
                return false;
            }

            try
            {
                if (!PhotonNetwork.InRoom) { reason = "not in a room — your own bag only"; return true; }
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

            // The roster polls at 2 Hz; Photon's PlayerCount is live. If they disagree there is
            // an occupant we haven't vetted, and an unvetted occupant means inert. Fail closed.
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
                reason = $"{vanilla} player(s) without Descent — {roster.Describe()}";
                return false;
            }
            if (skewed > 0)
            {
                reason = $"build skew across {skewed + 1} peers — {roster.Describe()}";
                return false;
            }

            reason = roster.Peers.Count == 1
                ? $"private room, solo, build {firstVer}/{firstSha}"
                : $"private room, all {roster.Peers.Count} peers on {firstVer}/{firstSha}";
            return true;
        }

        /// <summary>Force-inert, e.g. on application quit. Closes both switches.</summary>
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

            if (_localOnly)
            {
                _localOnly = false;
                try { LocalOnlyChanged?.Invoke(false); }
                catch (Exception e) { Core.Log.Error($"LocalOnlyChanged handler threw: {e}"); }
            }
        }
    }
}
