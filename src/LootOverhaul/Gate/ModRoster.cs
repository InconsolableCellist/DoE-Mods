using System;
using System.Collections.Generic;
using Il2CppPhoton.Pun;
using Il2CppPhoton.Realtime;
using Il2CppExitGames.Client.Photon;

namespace LootOverhaul.Gate
{
    /// <summary>
    /// Who else in this room is running LootOverhaul. Built from Photon player custom
    /// properties, which replicate to everyone automatically, late joiners included.
    ///
    /// Polls rather than implementing <c>IInRoomCallbacks</c>: registering an Il2Cpp
    /// interface from managed code is real interop pain, and a half-second poll over a room
    /// capped at four players costs nothing. Carried over from CustomAvatars with the key
    /// prefix changed to <c>lo.</c> so the two mods never read each other's identity.
    /// </summary>
    public class ModRoster
    {
        public const string KeyVersion = "lo.ver";
        public const string KeySha = "lo.sha";
        public const string KeyCaps = "lo.caps";

        private const float PollSeconds = 0.5f;

        private readonly Dictionary<int, ModPeer> _peers = new Dictionary<int, ModPeer>();
        private float _cooldown;
        private bool _advertised;
        private string _lastSignature = "";

        public IReadOnlyDictionary<int, ModPeer> Peers => _peers;

        public event Action<ModPeer> PeerJoined;
        public event Action<ModPeer> PeerLeft;
        /// <summary>Raised whenever any peer's identity or mod status changed.</summary>
        public event Action RosterChanged;

        public void Tick(float dt)
        {
            _cooldown -= dt;
            if (_cooldown > 0f) return;
            _cooldown = PollSeconds;

            try { Poll(); }
            catch (Exception e) { Core.Log.Warning($"ModRoster poll failed: {e.GetType().Name}: {e.Message}"); }
        }

        private void Poll()
        {
            if (!PhotonNetwork.InRoom)
            {
                if (_peers.Count > 0)
                {
                    _peers.Clear();
                    _advertised = false;
                    _lastSignature = "";
                    RosterChanged?.Invoke();
                }
                return;
            }

            Advertise();

            var players = PhotonNetwork.PlayerList;
            if (players == null) return;

            var seen = new HashSet<int>();
            var changed = false;

            for (var i = 0; i < players.Length; i++)
            {
                var p = players[i];
                if (ReferenceEquals(p, null)) continue;

                var peer = ReadPeer(p);
                seen.Add(peer.ActorNumber);

                if (!_peers.TryGetValue(peer.ActorNumber, out var existing))
                {
                    _peers[peer.ActorNumber] = peer;
                    changed = true;
                    Core.Log.Msg($"Roster + {peer}");
                    PeerJoined?.Invoke(peer);
                }
                else if (existing.Version != peer.Version || existing.Sha != peer.Sha || existing.Caps != peer.Caps)
                {
                    // Properties replicate a moment after join, so a peer commonly appears
                    // vanilla and then turns out to be modded. That's an update, not a churn.
                    _peers[peer.ActorNumber] = peer;
                    changed = true;
                    Core.Log.Msg($"Roster ~ {peer}");
                }
            }

            List<int> gone = null;
            foreach (var kv in _peers)
                if (!seen.Contains(kv.Key)) (gone ??= new List<int>()).Add(kv.Key);
            if (gone != null)
            {
                foreach (var actor in gone)
                {
                    var peer = _peers[actor];
                    _peers.Remove(actor);
                    changed = true;
                    Core.Log.Msg($"Roster - {peer}");
                    PeerLeft?.Invoke(peer);
                }
            }

            if (changed) RosterChanged?.Invoke();
        }

        private static ModPeer ReadPeer(Player p)
        {
            var peer = new ModPeer
            {
                ActorNumber = p.ActorNumber,
                NickName = SafeNick(p),
                IsLocal = p.IsLocal,
            };

            var props = p.CustomProperties;
            if (ReferenceEquals(props, null)) return peer;

            peer.Version = GetString(props, KeyVersion);
            peer.Sha = GetString(props, KeySha);
            peer.GameBuild = GetString(props, "Build");

            var capsRaw = GetString(props, KeyCaps);
            if (capsRaw != null && int.TryParse(capsRaw, out var caps)) peer.Caps = (ModCaps)caps;

            return peer;
        }

        private static string SafeNick(Player p)
        {
            try { return p.NickName; } catch { return "?"; }
        }

        private static string GetString(Hashtable ht, string key)
        {
            try
            {
                var k = (Il2CppSystem.String)key;
                if (!ht.ContainsKey(k)) return null;
                var v = ht[k];
                return ReferenceEquals(v, null) ? null : v.ToString();
            }
            catch { return null; }
        }

        /// <summary>
        /// Publish our identity — only in a private room. The one deliberate write before
        /// the gate opens: peers can't be discovered without someone speaking first. A public
        /// lobby sees nothing from this mod.
        /// </summary>
        private void Advertise()
        {
            if (_advertised) return;
            if (!ModGate.RoomIsPrivate(out _)) return;

            try
            {
                var props = new Hashtable();
                props[(Il2CppSystem.String)KeyVersion] = (Il2CppSystem.String)Core.Version;
                props[(Il2CppSystem.String)KeySha] = (Il2CppSystem.String)SelfCheck.ShortHash;
                props[(Il2CppSystem.String)KeyCaps] = (Il2CppSystem.String)((int)ModCapsInfo.Local).ToString();

                PhotonNetwork.LocalPlayer.SetCustomProperties(props, null, null);
                _advertised = true;
                Core.Log.Msg($"Advertised {KeyVersion}={Core.Version} {KeySha}={SelfCheck.ShortHash} " +
                             $"{KeyCaps}={(int)ModCapsInfo.Local} (private room).");
            }
            catch (Exception e)
            {
                Core.Log.Warning($"Advertise failed: {e.GetType().Name}: {e.Message}");
            }
        }

        /// <summary>Actor numbers of every modded peer other than us — the send list for ModNet.</summary>
        public int[] ModdedPeerActors()
        {
            var list = new List<int>();
            foreach (var kv in _peers)
                if (kv.Value.IsModded && !kv.Value.IsLocal) list.Add(kv.Key);
            return list.ToArray();
        }

        /// <summary>Same, but only peers advertising a capability.</summary>
        public int[] ModdedPeerActors(ModCaps required)
        {
            var list = new List<int>();
            foreach (var kv in _peers)
                if (kv.Value.IsModded && !kv.Value.IsLocal && (kv.Value.Caps & required) == required)
                    list.Add(kv.Key);
            return list.ToArray();
        }

        /// <summary>Human-readable roster, for logs and the gate's reason string.</summary>
        public string Describe()
        {
            if (_peers.Count == 0) return "(empty)";
            var parts = new List<string>();
            foreach (var kv in _peers) parts.Add(kv.Value.ToString());
            return string.Join("; ", parts);
        }

        public string Signature()
        {
            var parts = new List<string>();
            foreach (var kv in _peers) parts.Add($"{kv.Key}:{kv.Value.Version}:{kv.Value.Sha}");
            parts.Sort();
            return string.Join("|", parts);
        }

        public bool SignatureChanged()
        {
            var sig = Signature();
            if (sig == _lastSignature) return false;
            _lastSignature = sig;
            return true;
        }
    }
}
