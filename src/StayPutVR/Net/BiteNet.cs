using System;
using System.Collections.Generic;
using Il2CppPhoton.Pun;
using Il2CppPhoton.Realtime;
using Il2CppExitGames.Client.Photon;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace StayPutVR.Net
{
    /// <summary>
    /// The mod's whole network surface: two Photon custom events in the reserved block 180–189
    /// (CustomAvatars owns 140–149, LootOverhaul 150–159, VisualCues 160–169, Descent 170–179;
    /// the game itself was measured on 1, 2, 50 and 70, and PUN reserves 200 and up).
    ///
    /// * <b>180, presence.</b> A broadcast saying "I run StayPutVR, and here is whether I am
    ///   willing to be bitten". Sent on entering a room, on every change, and on a slow
    ///   heartbeat; also sent straight back at anyone whose presence we hear, so a late joiner
    ///   learns about everyone within a frame instead of waiting out a heartbeat.
    /// * <b>181, bite.</b> Sent to <em>one</em> actor, and only to an actor whose own presence
    ///   said bites are allowed.
    ///
    /// **This is the consent layer, and it is the reason the feature is networked at all.** A
    /// bite fires someone else's shock device. So a bite is never broadcast, never sent to a
    /// player who has not advertised that they accept them, and — because a hostile or buggy
    /// peer could send 181 anyway — the receiving side re-checks its own switch and its own rate
    /// limit before anything fires. Nothing here can touch a player who does not run the mod:
    /// a vanilla client receives an event code it does not know and drops it, and no game state
    /// is ever changed from this side of the wire. The damage a bite causes is applied by the
    /// bitten player's own client to itself.
    /// </summary>
    public static class BiteNet
    {
        public const byte CodePresence = 180;
        public const byte CodeBite = 181;
        public const byte CodeMin = 180;
        public const byte CodeMax = 189;
        private const char Sep = '|';

        /// <summary>One room occupant known to run the mod.</summary>
        public sealed class Peer
        {
            public int ActorNumber;
            public string Version = "?";
            public bool AcceptsBites;
            public float LastSeen;
        }

        private static readonly Dictionary<int, Peer> Peers = new Dictionary<int, Peer>();
        private static readonly Queue<(byte code, int sender, Il2CppSystem.Object content)> Inbox =
            new Queue<(byte, int, Il2CppSystem.Object)>();
        private static readonly object InboxGate = new object();
        private static readonly List<int> Scratch = new List<int>();

        private static float _nextHeartbeatAt = -1f;
        private static bool _lastAdvertised;
        private static bool _everAdvertised;

        /// <summary>(actorNumber) — a bite arrived from this actor. Raised on the main thread from <see cref="Pump"/>.</summary>
        public static event Action<int> BiteReceived;

        public static long SentCount { get; private set; }
        public static long ReceivedCount { get; private set; }
        public static int BitesRefusedInbound { get; private set; }

        public static void Init() => PhotonHook.RawEvent += OnRawEvent;

        private static void OnRawEvent(byte code, int sender, Il2CppSystem.Object content)
        {
            if (code < CodeMin || code > CodeMax) return;
            lock (InboxGate) Inbox.Enqueue((code, sender, content));
        }

        // ---- per-frame ---------------------------------------------------------------------

        /// <summary>Drain the inbox, expire stale peers, and keep our own presence current.</summary>
        public static void Pump()
        {
            while (true)
            {
                (byte code, int sender, Il2CppSystem.Object content) item;
                lock (InboxGate)
                {
                    if (Inbox.Count == 0) break;
                    item = Inbox.Dequeue();
                }
                ReceivedCount++;
                try { Dispatch(item.code, item.sender, item.content); }
                catch (Exception e) { Core.Log.Warning($"Inbound event {item.code} from actor {item.sender} threw: {e.GetType().Name}: {e.Message}"); }
            }

            Expire();
            Advertise();
        }

        /// <summary>A peer that has gone quiet for three heartbeats is assumed gone; fail closed, no bites to a ghost.</summary>
        private static void Expire()
        {
            var now = UnityEngine.Time.unscaledTime;
            Scratch.Clear();
            foreach (var kv in Peers)
                if (now - kv.Value.LastSeen > 18f) Scratch.Add(kv.Key);
            foreach (var actor in Scratch)
            {
                Peers.Remove(actor);
                ShockLog.Line($"peer actor {actor} went quiet; no longer bitable");
            }
        }

        /// <summary>Broadcast our presence on a change, and otherwise every six seconds.</summary>
        private static void Advertise()
        {
            var accepts = ModConfig.Enabled.Value && ModConfig.BiteVictimEnabled.Value;
            var now = UnityEngine.Time.unscaledTime;
            if (_everAdvertised && accepts == _lastAdvertised && now < _nextHeartbeatAt) return;
            if (!InRoom()) { _everAdvertised = false; return; }

            _lastAdvertised = accepts;
            _everAdvertised = true;
            _nextHeartbeatAt = now + 6f;
            SendPresence(null);
        }

        // ---- sending ----------------------------------------------------------------------

        private static void SendPresence(int? toActor)
        {
            var accepts = ModConfig.Enabled.Value && ModConfig.BiteVictimEnabled.Value;
            var payload = $"P{Sep}{Core.Version}{Sep}{(accepts ? 1 : 0)}";
            SendRaw(CodePresence, payload, toActor);
        }

        /// <summary>
        /// Send one bite to one actor. Refuses unless that actor has advertised that it accepts
        /// them — the receiver checks again, but a bite should not be on the wire at all unless
        /// the person at the other end asked for it.
        /// </summary>
        public static bool SendBite(int toActor)
        {
            if (!Peers.TryGetValue(toActor, out var peer))
            {
                ShockLog.Line($"bite not sent: actor {toActor} is not running the mod");
                return false;
            }
            if (!peer.AcceptsBites)
            {
                ShockLog.Line($"bite not sent: actor {toActor} does not accept bites");
                return false;
            }
            var ok = SendRaw(CodeBite, $"B{Sep}{Core.Version}", toActor);
            if (ok) ShockLog.Line($"bite sent to actor {toActor} (StayPutVR {peer.Version})");
            return ok;
        }

        private static bool SendRaw(byte code, string payload, int? toActor)
        {
            try
            {
                if (!InRoom()) return false;
                var options = new RaiseEventOptions();
                if (toActor.HasValue)
                {
                    options.TargetActors = new Il2CppStructArray<int>(1);
                    options.TargetActors[0] = toActor.Value;
                }
                else
                {
                    options.Receivers = ReceiverGroup.Others;
                }
                var ok = PhotonNetwork.RaiseEvent(code, (Il2CppSystem.String)payload, options, SendOptions.SendReliable);
                if (ok) SentCount++;
                else Core.Log.Warning($"RaiseEvent({code}) returned false.");
                return ok;
            }
            catch (Exception e)
            {
                Core.Log.Warning($"RaiseEvent({code}) threw: {e.GetType().Name}: {e.Message}");
                return false;
            }
        }

        private static bool InRoom()
        {
            try { return PhotonNetwork.InRoom; }
            catch { return false; }
        }

        // ---- receiving --------------------------------------------------------------------

        private static void Dispatch(byte code, int sender, Il2CppSystem.Object content)
        {
            var text = content?.ToString();
            if (string.IsNullOrEmpty(text)) { Core.Log.Warning($"Event {code} from actor {sender}: empty payload."); return; }
            var parts = text.Split(Sep);

            if (code == CodePresence && text[0] == 'P')
            {
                var known = Peers.ContainsKey(sender);
                if (!Peers.TryGetValue(sender, out var peer)) { peer = new Peer { ActorNumber = sender }; Peers[sender] = peer; }
                peer.Version = parts.Length > 1 ? parts[1] : "?";
                peer.AcceptsBites = parts.Length > 2 && parts[2] == "1";
                peer.LastSeen = UnityEngine.Time.unscaledTime;
                if (!known)
                {
                    ShockLog.Headline($"Peer actor {sender} runs StayPutVR {peer.Version}; bites {(peer.AcceptsBites ? "accepted" : "refused")}.");
                    // Answer immediately so they learn about us without waiting for a heartbeat.
                    SendPresence(sender);
                }
                return;
            }

            if (code == CodeBite && text[0] == 'B')
            {
                // Never trusted: the receiving side re-checks its own switch and rate limit.
                // A peer could send this without ever having heard our presence.
                if (!ModConfig.Enabled.Value || !ModConfig.BiteVictimEnabled.Value)
                {
                    BitesRefusedInbound++;
                    ShockLog.Line($"bite from actor {sender} refused: BiteVictimEnabled is off");
                    return;
                }
                BiteReceived?.Invoke(sender);
                return;
            }

            if (ModConfig.VerboseLogging.Value) Core.Log.Msg($"Ignoring event {code} from actor {sender} (in our block, no handler).");
        }

        // ---- queries ----------------------------------------------------------------------

        public static bool Accepts(int actorNumber) => Peers.TryGetValue(actorNumber, out var p) && p.AcceptsBites;
        public static int PeerCount => Peers.Count;

        public static int BitablePeerCount
        {
            get
            {
                var n = 0;
                foreach (var kv in Peers) if (kv.Value.AcceptsBites) n++;
                return n;
            }
        }

        public static void Clear(string why)
        {
            if (Peers.Count > 0) ShockLog.Line($"peer roster cleared ({why})");
            Peers.Clear();
            _everAdvertised = false;
        }

        public static string Stats() => $"net sent {SentCount}, received {ReceivedCount}, {Peers.Count} mod peer(s), {BitesRefusedInbound} inbound bite(s) refused";
    }
}
