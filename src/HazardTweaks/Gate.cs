using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using Il2CppExitGames.Client.Photon;
using Il2CppPhoton.Pun;
using Il2CppPhoton.Realtime;

namespace HazardTweaks
{
    /// <summary>
    /// The lobby check, and whose settings count.
    ///
    /// Hazards are rolled from the dungeon seed on every machine, so every player has to filter
    /// the same list the same way. The mod therefore changes anything only when:
    ///   - not in a room at all (offline): this player's own settings, or
    ///   - the room is private AND every player in it advertises the same <c>ht.ver</c> and
    ///     <c>ht.sha</c> (DLL hash): then everyone uses the HOST's settings (the Photon master
    ///     client), published as <c>ht.rm</c> (removed hazards by name), <c>ht.x</c> (bonus
    ///     multiplier) and <c>ht.r</c> (rounding). A guest's own settings do not matter there.
    /// Otherwise the game stays vanilla and the log says why.
    ///
    /// Identity and settings travel as Photon player custom properties (the CustomAvatars /
    /// Descent roster pattern, own <c>ht.</c> prefix), which replicate to everyone, late joiners
    /// included, and are published only in a private room. <see cref="Evaluate"/> reads them
    /// fresh, so the hooks get the answer as of the moment a mission board is rolled;
    /// <see cref="Tick"/> re-checks twice a second so a join, a leave, a host change or a
    /// settings change is logged and the bonus table follows straight away.
    /// </summary>
    public static class Gate
    {
        public const string KeyVersion = "ht.ver";
        public const string KeySha = "ht.sha";
        public const string KeyRemoved = "ht.rm";
        public const string KeyMult = "ht.x";
        public const string KeyRound = "ht.r";

        public static string DllHash { get; private set; } = "unknown";

        /// <summary>The settings in force after the last <see cref="Evaluate"/> that said yes.</summary>
        public static HazardSettings Effective { get; private set; }

        private static string _advertisedSig;
        private static float _cooldown;
        private static bool? _lastActive;
        private static string _lastReason;
        private static string _lastSettings;

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

        /// <summary>Is the mod allowed to change hazards right now, and if not, why. On yes, <see cref="Effective"/> holds the settings to use.</summary>
        public static bool Evaluate(out string reason)
        {
            reason = null;
            if (!ModConfig.Enabled.Value) { reason = "disabled"; return false; }
            try
            {
                var own = HazardSettings.Local();
                if (!PhotonNetwork.InRoom) { _advertisedSig = null; Effective = own; reason = "offline"; return true; }
                var room = PhotonNetwork.CurrentRoom;
                if (ReferenceEquals(room, null)) { reason = "no current room"; return false; }
                if (room.IsVisible) { reason = "public lobby"; return false; }

                Advertise(own);

                var players = PhotonNetwork.PlayerList;
                if (players == null || players.Length == 0) { reason = "no player list"; return false; }

                Player host = null;
                for (int i = 0; i < players.Length; i++)
                {
                    var p = players[i];
                    if (ReferenceEquals(p, null)) continue;
                    if (p.IsMasterClient) host = p;
                    if (p.IsLocal) continue;
                    var props = p.CustomProperties;
                    var ver = Get(props, KeyVersion);
                    var sha = Get(props, KeySha);
                    var who = Nick(p);
                    if (ver == null) { reason = $"{who} has no HazardTweaks"; return false; }
                    if (ver != Core.Version || sha != DllHash) { reason = $"{who} runs HazardTweaks {ver}/{sha}, you run {Core.Version}/{DllHash}"; return false; }
                }

                if (host == null) { reason = "no host in the player list"; return false; }
                if (host.IsLocal)
                {
                    Effective = own;
                    reason = players.Length == 1 ? "private lobby, solo" : $"private lobby, you host, all {players.Length} players run this build";
                    return true;
                }

                var hp = host.CustomProperties;
                var hostSettings = HazardSettings.FromProps(Get(hp, KeyRemoved), Get(hp, KeyMult), Get(hp, KeyRound));
                if (hostSettings == null) { reason = $"{Nick(host)} (host) has not published hazard settings yet"; return false; }
                Effective = hostSettings;
                reason = $"private lobby, all {players.Length} players run this build, using {Nick(host)}'s (host) settings";
                return true;
            }
            catch (Exception e) { reason = "lobby check threw " + e.GetType().Name; return false; }
        }

        /// <summary>Twice a second: re-check, log a change of state or of the settings in force, bring the bonus table along.</summary>
        public static void Tick(float dt)
        {
            _cooldown -= dt;
            if (_cooldown > 0f) return;
            _cooldown = 0.5f;
            var on = Evaluate(out var reason);
            var settings = on && Effective != null ? Effective.Describe() : null;
            if (_lastActive == on && _lastReason == reason && _lastSettings == settings) return;
            _lastActive = on;
            _lastReason = reason;
            _lastSettings = settings;
            Core.Log.Msg(on ? $"ACTIVE: {reason}: {settings}." : $"INERT: {reason}. Hazards and bonuses stay vanilla.");
            Bonus.Sync(null);
        }

        /// <summary>Publish our identity and settings in a private room; again whenever the settings change.</summary>
        private static void Advertise(HazardSettings own)
        {
            var rm = own.RemovedNames();
            var x = own.Multiplier.ToString("0.####", CultureInfo.InvariantCulture);
            var r = own.RoundUpTo.ToString(CultureInfo.InvariantCulture);
            var sig = $"{Core.Version}|{DllHash}|{rm}|{x}|{r}";
            if (sig == _advertisedSig) return;
            try
            {
                var props = new Hashtable();
                props[(Il2CppSystem.String)KeyVersion] = (Il2CppSystem.String)Core.Version;
                props[(Il2CppSystem.String)KeySha] = (Il2CppSystem.String)DllHash;
                props[(Il2CppSystem.String)KeyRemoved] = (Il2CppSystem.String)rm;
                props[(Il2CppSystem.String)KeyMult] = (Il2CppSystem.String)x;
                props[(Il2CppSystem.String)KeyRound] = (Il2CppSystem.String)r;
                PhotonNetwork.LocalPlayer.SetCustomProperties(props, null, null);
                _advertisedSig = sig;
                Core.Log.Msg($"Advertised {KeyVersion}={Core.Version} {KeySha}={DllHash} {KeyRemoved}=[{rm}] {KeyMult}={x} {KeyRound}={r} (private lobby).");
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

    /// <summary>One set of hazard settings: this player's own, or the host's as published.</summary>
    public sealed class HazardSettings
    {
        public HashSet<int> Removed { get; }
        public float Multiplier { get; }
        public int RoundUpTo { get; }

        private HazardSettings(HashSet<int> removed, float mult, int round)
        {
            Removed = removed;
            Multiplier = Math.Max(0f, mult);
            RoundUpTo = Math.Max(0, round);
        }

        public static HazardSettings Local() =>
            new HazardSettings(Hazards.LocalRemoved(), ModConfig.BonusMultiplier.Value, ModConfig.RoundUpTo.Value);

        /// <summary>The host's published values, or null if any is missing or unreadable.</summary>
        public static HazardSettings FromProps(string rm, string x, string r)
        {
            if (rm == null || x == null || r == null) return null;
            if (!float.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out var mult)) return null;
            if (!int.TryParse(r, NumberStyles.Integer, CultureInfo.InvariantCulture, out var round)) return null;
            return new HazardSettings(Hazards.ParseNames(rm), mult, round);
        }

        public string RemovedNames()
        {
            var ids = new List<int>(Removed);
            ids.Sort();
            var names = new List<string>();
            foreach (var id in ids) names.Add(((Il2Cpp.GameManager.HazardModifier)id).ToString());
            return string.Join(",", names);
        }

        public string Describe()
        {
            var rm = RemovedNames();
            return $"removing [{(rm.Length == 0 ? "nothing" : rm.Replace(",", ", "))}], hazard bonus x{Multiplier.ToString("0.##", CultureInfo.InvariantCulture)}" +
                   (RoundUpTo > 0 && Math.Abs(Multiplier - 1f) >= 0.0001f ? $", rounded up to {RoundUpTo}%" : "");
        }
    }
}
