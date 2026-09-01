using System;
using System.Collections.Generic;
using UnityEngine;
using Il2CppPhoton.Pun;
using Il2CppPhoton.Realtime;
using Il2CppExitGames.Client.Photon;

namespace CustomAvatars.Recon
{
    /// <summary>
    /// Photon-side recon, answering two things Phase 1 depends on:
    ///
    /// 1. How a private party maps to room options (IsVisible / IsOpen / PlayerTtl) — the
    ///    ModGate.Active room check is written against whatever this reports.
    /// 2. Which custom event codes the game itself already uses. GAME-INTERNALS.md assumed
    ///    none, but GameManager implements IOnEventCallback and AvatarPlayer carries a
    ///    cachedEventCodes[] field, so the 140–149 block needs empirical clearance before we
    ///    claim it.
    /// </summary>
    public class PhotonRecon
    {
        private string _lastRoomSignature;
        private float _cooldown;

        /// <summary>Event codes seen this session → how many times. Codes 200+ are PUN's own.</summary>
        private static readonly Dictionary<byte, int> SeenCodes = new Dictionary<byte, int>();
        private static readonly object CodeGate = new object();

        public void Tick()
        {
            _cooldown -= Time.unscaledDeltaTime;
            if (_cooldown > 0f) return;
            _cooldown = 2f;

            try
            {
                var signature = RoomSignature();
                if (signature == _lastRoomSignature) return;
                _lastRoomSignature = signature;
                DumpRoomNow();
            }
            catch (Exception e) { ReconLog.Error("Photon room poll", e); }
        }

        /// <summary>Cheap change-detector so we only write a full room dump when something moved.</summary>
        private static string RoomSignature()
        {
            try
            {
                if (!PhotonNetwork.InRoom) return $"state:{PhotonNetwork.NetworkClientState}";
                var room = PhotonNetwork.CurrentRoom;
                if (ReferenceEquals(room, null)) return "room:null";
                return $"{room.Name}|{room.PlayerCount}|{room.IsVisible}|{room.IsOpen}";
            }
            catch (Exception e) { return $"err:{e.GetType().Name}"; }
        }

        public void DumpRoomNow()
        {
            ReconLog.Section($"Photon room state ({DateTime.Now:HH:mm:ss})");

            ReconLog.TryKeyValue("NetworkClientState", () => PhotonNetwork.NetworkClientState);
            ReconLog.TryKeyValue("InRoom", () => PhotonNetwork.InRoom);
            ReconLog.TryKeyValue("IsMasterClient", () => PhotonNetwork.IsMasterClient);
            ReconLog.TryKeyValue("LocalPlayer", () =>
            {
                var p = PhotonNetwork.LocalPlayer;
                return ReferenceEquals(p, null) ? "<null>" : $"{p.NickName} (actor {p.ActorNumber})";
            });

            try
            {
                if (!PhotonNetwork.InRoom)
                {
                    ReconLog.Line("_not in a room_");
                    return;
                }

                var room = PhotonNetwork.CurrentRoom;
                if (ReferenceEquals(room, null)) { ReconLog.Line("_CurrentRoom is null_"); return; }

                ReconLog.Line();
                ReconLog.Line("### Room options (the Phase 1 private-lobby test)");
                ReconLog.TryKeyValue("Name", () => room.Name);
                ReconLog.TryKeyValue("IsVisible", () => room.IsVisible);
                ReconLog.TryKeyValue("IsOpen", () => room.IsOpen);
                ReconLog.TryKeyValue("MaxPlayers", () => room.MaxPlayers);
                ReconLog.TryKeyValue("PlayerCount", () => room.PlayerCount);
                ReconLog.TryKeyValue("PlayerTtl", () => room.PlayerTtl);
                ReconLog.TryKeyValue("EmptyRoomTtl", () => room.EmptyRoomTtl);
                ReconLog.Try("room custom properties", () =>
                    ReconLog.KeyValue("CustomProperties", Describe(room.CustomProperties)));

                ReconLog.Line();
                ReconLog.Line("### Players");
                var players = PhotonNetwork.PlayerList;
                if (players == null) { ReconLog.Line("_PlayerList null_"); return; }
                for (var i = 0; i < players.Length; i++)
                {
                    var p = players[i];
                    if (ReferenceEquals(p, null)) continue;
                    ReconLog.Line($"- actor {p.ActorNumber} `{p.NickName}`" +
                                  $"{(p.IsMasterClient ? " [master]" : "")}{(p.IsLocal ? " [local]" : "")}");
                    // Phase 1 will advertise ca.ver / ca.sha / ca.caps here; seeing what the
                    // game already puts in this bag tells us whether we'd collide.
                    ReconLog.Try($"actor {p.ActorNumber} props", () =>
                        ReconLog.Line($"  - props: {Describe(p.CustomProperties)}"));
                }
            }
            catch (Exception e) { ReconLog.Error("Photon room dump", e); }

            DumpSeenEventCodes();
        }

        private static string Describe(Hashtable ht)
        {
            if (ReferenceEquals(ht, null)) return "<null>";
            try { return SupportClass.DictionaryToString(ht.Cast<Il2CppSystem.Collections.IDictionary>(), true); }
            catch { }
            try { return $"<{ht.Count} entries, contents unreadable>"; }
            catch { return "<unreadable>"; }
        }

        // ---- event code sniffer -------------------------------------------------------

        /// <summary>
        /// Subscribes to the shared <see cref="Net.PhotonHook"/> rather than patching
        /// separately — one read-only prefix on Photon's dispatch, several listeners.
        /// </summary>
        public void InstallEventSniffer()
        {
            Net.PhotonHook.RawEvent += OnRawEvent;
        }

        private static void OnRawEvent(byte code, int sender, Il2CppSystem.Object content)
        {
            if (!ModConfig.LogPhotonEvents.Value) return;
            try
            {
                bool first;
                lock (CodeGate)
                {
                    first = !SeenCodes.ContainsKey(code);
                    SeenCodes[code] = (first ? 0 : SeenCodes[code]) + 1;
                }

                // PUN reserves 200+; only the sub-200 range can collide with our 140-149 block,
                // and only the first sighting is worth a line in the transcript.
                if (first && code < 200)
                    ReconLog.Headline($"Photon event code {code} seen (sender {sender})" +
                                      (code >= Net.ModNet.CodeMin && code <= Net.ModNet.CodeMax
                                          ? " — COLLIDES WITH OUR RESERVED 140-149 BLOCK"
                                          : " — in the mod-usable range, outside our block"));
            }
            catch { /* never let recon break the network dispatch */ }
        }

        private static void DumpSeenEventCodes()
        {
            lock (CodeGate)
            {
                if (SeenCodes.Count == 0) { ReconLog.Line("_no Photon events observed yet_"); return; }
                ReconLog.Line();
                ReconLog.Line("### Photon event codes seen this session");
                var codes = new List<byte>(SeenCodes.Keys);
                codes.Sort();
                foreach (var c in codes)
                    ReconLog.Line($"- code {c} ×{SeenCodes[c]}{(c < 200 ? "   ← mod-usable range" : "   (PUN internal)")}");
            }
        }
    }
}
