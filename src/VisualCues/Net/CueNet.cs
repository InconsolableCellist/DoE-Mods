using System;
using System.Collections.Generic;
using System.Globalization;
using Il2CppPhoton.Pun;
using Il2CppPhoton.Realtime;
using Il2CppExitGames.Client.Photon;
using UnityEngine;

namespace VisualCues.Net
{
    /// <summary>
    /// The mod's whole network surface: one Photon custom event, code <see cref="CodeSummon"/>,
    /// in the reserved block 160–169 (CustomAvatars owns 140–149, LootOverhaul 150–159, the
    /// game was measured on 1, 2, 50 and 70, PUN reserves 200 and up).
    ///
    /// There is deliberately no gate. The event is a broadcast to everyone in the room; a
    /// player without the mod receives a code their game does not know and ignores it, a
    /// player with the mod shows the arrow. Nothing here touches game state or the profile,
    /// so a public lobby is as safe a place to send it as a private one. What is untested is
    /// the vanilla client's tolerance of a foreign code: the other two mods never send one to
    /// a vanilla peer by design, so the first mixed session should watch the vanilla player
    /// for a disconnect (see README).
    ///
    /// Payload: the string <c>"S|&lt;mod version&gt;|x,y,z"</c>, the caller's head position at the
    /// moment of the call. Receivers follow the caller's live avatar when they can find it and
    /// fall back to the position when they cannot (the avatar can be mid-respawn).
    /// </summary>
    public static class CueNet
    {
        public const byte CodeSummon = 160;
        public const byte CodeMin = 160;
        public const byte CodeMax = 169;
        private const char Sep = '|';

        private static readonly Queue<(byte code, int sender, Il2CppSystem.Object content)> Inbox =
            new Queue<(byte, int, Il2CppSystem.Object)>();
        private static readonly object InboxGate = new object();

        /// <summary>(actorNumber, version, headPosition) — raised on the main thread from <see cref="Pump"/>.</summary>
        public static event Action<int, string, Vector3> SummonReceived;

        public static long SentCount { get; private set; }
        public static long ReceivedCount { get; private set; }

        public static void Init()
        {
            PhotonHook.RawEvent += OnRawEvent;
        }

        private static void OnRawEvent(byte code, int sender, Il2CppSystem.Object content)
        {
            if (code < CodeMin || code > CodeMax) return;
            lock (InboxGate) Inbox.Enqueue((code, sender, content));
        }

        /// <summary>Drain the inbox. Called once per frame from Core.OnUpdate.</summary>
        public static void Pump()
        {
            while (true)
            {
                (byte code, int sender, Il2CppSystem.Object content) item;
                lock (InboxGate)
                {
                    if (Inbox.Count == 0) return;
                    item = Inbox.Dequeue();
                }
                ReceivedCount++;
                try { Dispatch(item.code, item.sender, item.content); }
                catch (Exception e) { Core.Log.Warning($"Inbound event {item.code} from actor {item.sender} threw: {e.GetType().Name}: {e.Message}"); }
            }
        }

        private static void Dispatch(byte code, int sender, Il2CppSystem.Object content)
        {
            if (code != CodeSummon) { if (ModConfig.VerboseLogging.Value) Core.Log.Msg($"Ignoring event {code} from actor {sender} (in our block, no handler)."); return; }
            var text = content?.ToString();
            if (string.IsNullOrEmpty(text) || text[0] != 'S') { Core.Log.Warning($"Summon from actor {sender}: unreadable payload."); return; }
            var parts = text.Split(Sep);
            var version = parts.Length > 1 ? parts[1] : "?";
            var pos = Vector3.zero;
            var hasPos = parts.Length > 2 && TryParseVector(parts[2], out pos);
            SummonReceived?.Invoke(sender, version, hasPos ? pos : new Vector3(float.NaN, float.NaN, float.NaN));
        }

        public static bool SendSummon(Vector3 headPos)
        {
            var payload = $"S{Sep}{Core.Version}{Sep}{V(headPos)}";
            return SendRaw(CodeSummon, (Il2CppSystem.String)payload);
        }

        private static bool SendRaw(byte code, Il2CppSystem.Object payload)
        {
            try
            {
                if (!PhotonNetwork.InRoom) { if (ModConfig.VerboseLogging.Value) Core.Log.Msg("Not in a room; call not sent."); return false; }
                var options = new RaiseEventOptions { Receivers = ReceiverGroup.Others };
                var ok = PhotonNetwork.RaiseEvent(code, payload, options, SendOptions.SendReliable);
                if (ok) SentCount++;
                else Core.Log.Warning($"RaiseEvent({code}) returned false.");
                return ok;
            }
            catch (Exception e)
            {
                Core.Log.Error($"RaiseEvent({code}) threw: {e}");
                return false;
            }
        }

        private static string V(Vector3 v) => string.Format(CultureInfo.InvariantCulture, "{0:0.###},{1:0.###},{2:0.###}", v.x, v.y, v.z);

        private static bool TryParseVector(string s, out Vector3 v)
        {
            v = Vector3.zero;
            var p = s.Split(',');
            if (p.Length != 3) return false;
            if (!float.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)) return false;
            if (!float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)) return false;
            if (!float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z)) return false;
            v = new Vector3(x, y, z);
            return true;
        }

        public static string Stats() => $"sent {SentCount}, received {ReceivedCount}";
    }
}
