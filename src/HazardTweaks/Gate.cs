using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Il2CppExitGames.Client.Photon;
using Il2CppPhoton.Pun;
using Il2CppPhoton.Realtime;

namespace HazardTweaks
{
    /// <summary>
    /// The lobby check. Hazards are rolled from the dungeon seed on every machine, so players
    /// only agree on a dungeon's hazards and bonus when all of them run this mod with the same
    /// build and the same hazard settings. The mod therefore changes anything only when:
    ///   - not in a room at all (offline), or
    ///   - the room is private AND every player in it advertises the same <c>ht.ver</c>,
    ///     <c>ht.sha</c> (DLL hash) and <c>ht.cfg</c> (hash of the settings that change hazards).
    /// Otherwise the game stays vanilla and the log says why.
    ///
    /// Identity travels as Photon player custom properties (the CustomAvatars / Descent roster
    /// pattern, own <c>ht.</c> prefix), which replicate to everyone, late joiners included, and
    /// are published only in a private room. <see cref="Evaluate"/> reads them fresh, so the
    /// hooks get the answer as of the moment a mission board is rolled; <see cref="Tick"/>
    /// re-checks twice a second so a join, a leave or a settings change is logged and the bonus
    /// table follows straight away.
    /// </summary>
    public static class Gate
    {
        public const string KeyVersion = "ht.ver";
        public const string KeySha = "ht.sha";
        public const string KeyConfig = "ht.cfg";

        public static string DllHash { get; private set; } = "unknown";

        private static string _advertisedSig;
        private static float _cooldown;
        private static bool? _lastActive;
        private static string _lastReason;

        public static void ComputeDllHash()
        {
            try
            {
                var path = Assembly.GetExecutingAssembly().Location;
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) { Core.Log.Warning("DLL hash unavailable: assembly has no on-disk location."); return; }
                using var sha = SHA256.Create();
                using var fs = File.OpenRead(path);
                DllHash = Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant().Substring(0, 16);
                Core.Log.Msg($"Mod DLL SHA-256: {DllHash}");
            }
            catch (Exception e) { Core.Log.Warning($"DLL hash failed: {e.GetType().Name}: {e.Message}"); }
        }

        /// <summary>Hash of exactly the settings that change hazards or bonuses (removed set, multiplier, rounding), after parsing.</summary>
        public static string ConfigHash()
        {
            var removed = new List<int>(Hazards.RemovedSet());
            removed.Sort();
            var canon = $"rm={string.Join(",", removed)}|x={Math.Max(0f, ModConfig.BonusMultiplier.Value):0.####}|r={Math.Max(0, ModConfig.RoundUpTo.Value)}";
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(canon))).ToLowerInvariant().Substring(0, 12);
        }

        /// <summary>Is the mod allowed to change hazards right now, and if not, why.</summary>
        public static bool Evaluate(out string reason)
        {
            reason = null;
            if (!ModConfig.Enabled.Value) { reason = "disabled"; return false; }
            try
            {
                if (!PhotonNetwork.InRoom) { _advertisedSig = null; reason = "offline"; return true; }
                var room = PhotonNetwork.CurrentRoom;
                if (ReferenceEquals(room, null)) { reason = "no current room"; return false; }
                if (room.IsVisible) { reason = "public lobby"; return false; }

                Advertise();

                var players = PhotonNetwork.PlayerList;
                if (players == null || players.Length == 0) { reason = "no player list"; return false; }

                string myCfg = ConfigHash();
                for (int i = 0; i < players.Length; i++)
                {
                    var p = players[i];
                    if (ReferenceEquals(p, null)) continue;
                    if (p.IsLocal) continue;
                    var props = p.CustomProperties;
                    var ver = Get(props, KeyVersion);
                    var sha = Get(props, KeySha);
                    var cfg = Get(props, KeyConfig);
                    var who = Nick(p);
                    if (ver == null) { reason = $"{who} has no HazardTweaks"; return false; }
                    if (ver != Core.Version || sha != DllHash) { reason = $"{who} runs HazardTweaks {ver}/{sha}, you run {Core.Version}/{DllHash}"; return false; }
                    if (cfg != myCfg) { reason = $"settings differ from {who}'s (RemoveWeaponRestrictions / AlsoRemove / BonusMultiplier / RoundUpTo)"; return false; }
                }
                reason = players.Length == 1 ? "private lobby, solo" : $"private lobby, all {players.Length} players match";
                return true;
            }
            catch (Exception e) { reason = "lobby check threw " + e.GetType().Name; return false; }
        }

        /// <summary>Twice a second: re-check, log a change of state, bring the bonus table along.</summary>
        public static void Tick(float dt)
        {
            _cooldown -= dt;
            if (_cooldown > 0f) return;
            _cooldown = 0.5f;
            var on = Evaluate(out var reason);
            if (_lastActive == on && _lastReason == reason) return;
            _lastActive = on;
            _lastReason = reason;
            Core.Log.Msg(on ? $"ACTIVE: {reason}." : $"INERT: {reason}. Hazards and bonuses stay vanilla.");
            Bonus.Sync(null);
        }

        /// <summary>Publish our identity in a private room; again whenever the settings change.</summary>
        private static void Advertise()
        {
            var cfg = ConfigHash();
            var sig = $"{Core.Version}|{DllHash}|{cfg}";
            if (sig == _advertisedSig) return;
            try
            {
                var props = new Hashtable();
                props[(Il2CppSystem.String)KeyVersion] = (Il2CppSystem.String)Core.Version;
                props[(Il2CppSystem.String)KeySha] = (Il2CppSystem.String)DllHash;
                props[(Il2CppSystem.String)KeyConfig] = (Il2CppSystem.String)cfg;
                PhotonNetwork.LocalPlayer.SetCustomProperties(props, null, null);
                _advertisedSig = sig;
                Core.Log.Msg($"Advertised {KeyVersion}={Core.Version} {KeySha}={DllHash} {KeyConfig}={cfg} (private lobby).");
            }
            catch (Exception e) { Core.Log.Warning($"Advertise failed: {e.GetType().Name}: {e.Message}"); }
        }


        private static string Get(Hashtable ht, string key)
        {
            try
            {
                if (ReferenceEquals(ht, null)) return null;
                var k = (Il2CppSystem.String)key;
                if (!ht.ContainsKey(k)) return null;
                var v = ht[k];
                return ReferenceEquals(v, null) ? null : v.ToString();
            }
            catch { return null; }
        }

        private static string Nick(Player p)
        {
            try { return string.IsNullOrEmpty(p.NickName) ? $"player {p.ActorNumber}" : p.NickName; }
            catch { return "a player"; }
        }
    }
}
