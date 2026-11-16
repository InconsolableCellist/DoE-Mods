using System;
using System.Collections.Generic;
using System.Globalization;
using Il2CppPhoton.Pun;
using Il2CppPhoton.Realtime;
using Il2CppExitGames.Client.Photon;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace VisualCues.Net
{
    /// <summary>
    /// The mod's whole network surface: one Photon custom event, code <see cref="CodeSummon"/>,
    /// in the reserved block 160–169 (CustomAvatars owns 140–149, LootOverhaul 150–159, the
    /// game was measured on 1, 2, 50 and 70, PUN reserves 200 and up).
    ///
    /// There is no gate on the local markers: they touch nothing and send nothing. The call is
    /// sent only to the players who announced VisualCues with the <c>vc.v</c> player property
    /// (0.1.7; before, it was broadcast to the whole room, vanilla players included, which was
    /// never tested). A vanilla player never receives the event.
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
            Announce();
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

        // ---- who has the mod ------------------------------------------------------------------
        // 0.1.7: every copy announces itself with a player property, and the call goes only to the
        // players who announced one. A vanilla player never receives event 160, in any room, and
        // the local markers (noise, gates, puzzle slots) keep working whoever is in the room.

        private const string PropKey = "vc.v";
        private static string _announcedRoom;
        private static float _announceRetryAt;

        /// <summary>Set our property once per room. Called every frame from <see cref="Pump"/>; cheap when done.</summary>
        private static void Announce()
        {
            try
            {
                if (!PhotonNetwork.InRoom || PhotonNetwork.LocalPlayer == null) { _announcedRoom = null; return; }
                var room = PhotonNetwork.CurrentRoom?.Name ?? "";
                if (room == _announcedRoom) return;
                var now = Time.unscaledTime;
                if (now < _announceRetryAt) return;
                _announceRetryAt = now + 2f;
                var props = new Hashtable();
                props[(Il2CppSystem.String)PropKey] = (Il2CppSystem.String)Core.Version;
                PhotonNetwork.LocalPlayer.SetCustomProperties(props, null, null);
                _announcedRoom = room;
                if (ModConfig.VerboseLogging.Value) Core.Log.Msg($"Announced VisualCues {Core.Version} in room {room}.");
            }
            catch (Exception e) { Core.Log.Warning($"Announcing the mod failed: {e.GetType().Name}: {e.Message}"); }
        }

        /// <summary>Actor numbers of the other players in the room who announced VisualCues.</summary>
        private static List<int> ModdedOthers()
        {
            var list = new List<int>();
            var local = PhotonNetwork.LocalPlayer;
            var key = (Il2CppSystem.String)PropKey;
            foreach (var p in PhotonNetwork.PlayerList)
            {
                if (p == null || (local != null && p.ActorNumber == local.ActorNumber)) continue;
                try
                {
                    var props = p.CustomProperties;
                    if (!ReferenceEquals(props, null) && props.ContainsKey(key)) list.Add(p.ActorNumber);
                }
                catch { }
            }
            return list;
        }

        private static bool SendRaw(byte code, Il2CppSystem.Object payload)
        {
            try
            {
                if (!PhotonNetwork.InRoom) { if (ModConfig.VerboseLogging.Value) Core.Log.Msg("Not in a room; call not sent."); return false; }
                var targets = ModdedOthers();
                if (targets.Count == 0) { if (ModConfig.VerboseLogging.Value) Core.Log.Msg("Nobody else here has VisualCues; call not sent."); return false; }
                var options = new RaiseEventOptions { TargetActors = new Il2CppStructArray<int>(targets.Count) };
                for (var i = 0; i < targets.Count; i++) options.TargetActors[i] = targets[i];
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
