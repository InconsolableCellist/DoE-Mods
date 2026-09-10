using System;
using System.Collections.Generic;
using Descent.Gate;
using Il2CppPhoton.Pun;
using Il2CppPhoton.Realtime;
using Il2CppExitGames.Client.Photon;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace Descent.Net
{
    /// <summary>
    /// The mod's whole network surface: a thin wrapper over Photon <c>RaiseEvent</c> plus a
    /// dispatch table for our reserved event codes.
    ///
    /// **Every send is refused unless <see cref="ModGate.Active"/>.** That check lives here, at
    /// the one place bytes can leave the process.
    ///
    /// Reserved block **170–179**. CustomAvatars owns 140–149, LootOverhaul 150–159,
    /// VisualCues 160–169; the game's own custom events were measured at codes 1, 2, 50 and 70
    /// (GameEvent: ReturnToLobby = 1, SyncDungeonData = 50, RoomPrivacyChanged = 70), and PUN
    /// reserves 200 and up.
    /// </summary>
    public static class ModNet
    {
        public const byte CodeHandshake = 170;
        /// <summary>
        /// All run traffic, as a JSON string with an <c>op</c> field: <c>state</c> (the run
        /// record, for peers to keep a copy), <c>arm</c> / <c>cancel</c> (the hub countdown),
        /// <c>launch</c> (everyone runs LoadDungeon for floor N), <c>descend</c> (everyone runs
        /// the floor hand-off for floor N+1). One code with an opcode rather than five codes.
        /// </summary>
        public const byte CodeRun = 171;
        public const byte CodeMin = 170;
        public const byte CodeMax = 179;

        private static readonly Dictionary<byte, Action<int, Il2CppSystem.Object>> Handlers =
            new Dictionary<byte, Action<int, Il2CppSystem.Object>>();

        private static readonly Queue<(byte code, int sender, Il2CppSystem.Object content)> Inbox =
            new Queue<(byte, int, Il2CppSystem.Object)>();

        private static readonly object InboxGate = new object();

        public static long SentCount { get; private set; }
        public static long ReceivedCount { get; private set; }
        public static long RefusedCount { get; private set; }

        public static void Init()
        {
            PhotonHook.RawEvent += OnRawEvent;
        }

        public static void RegisterHandler(byte code, Action<int, Il2CppSystem.Object> handler)
        {
            if (code < CodeMin || code > CodeMax)
                throw new ArgumentOutOfRangeException(nameof(code), $"Event code {code} is outside our reserved {CodeMin}–{CodeMax} block.");
            Handlers[code] = handler;
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

                if (!ModGate.Active)
                {
                    Core.Log.Warning($"Dropped inbound event {item.code} from actor {item.sender}: gate is inert ({ModGate.Reason}).");
                    continue;
                }

                if (!Handlers.TryGetValue(item.code, out var handler))
                {
                    Core.Log.Warning($"No handler for event code {item.code} from actor {item.sender}.");
                    continue;
                }

                try { handler(item.sender, item.content); }
                catch (Exception e) { Core.Log.Error($"Handler for code {item.code} threw: {e}"); }
            }
        }

        /// <summary>Send to every other player in the room, or to <paramref name="targetActors"/> if given.</summary>
        public static bool Send(byte code, string payload, bool reliable, int[] targetActors = null)
            => SendRaw(code, (Il2CppSystem.String)payload, reliable, targetActors);

        public static string AsString(Il2CppSystem.Object content)
        {
            try { return ReferenceEquals(content, null) ? null : content.ToString(); }
            catch { return null; }
        }

        public static bool SendRaw(byte code, Il2CppSystem.Object payload, bool reliable, int[] targetActors = null)
        {
            if (code < CodeMin || code > CodeMax)
            {
                Core.Log.Error($"Refusing to send on code {code}: outside our reserved {CodeMin}–{CodeMax} block.");
                return false;
            }

            if (!ModGate.Active)
            {
                RefusedCount++;
                Core.Log.Warning($"Refused to send event {code}: gate is inert ({ModGate.Reason}).");
                return false;
            }

            try
            {
                var options = new RaiseEventOptions();
                if (targetActors != null && targetActors.Length > 0)
                {
                    options.TargetActors = new Il2CppStructArray<int>(targetActors.Length);
                    for (var i = 0; i < targetActors.Length; i++) options.TargetActors[i] = targetActors[i];
                }
                else
                {
                    options.Receivers = ReceiverGroup.Others;
                }

                var send = reliable ? SendOptions.SendReliable : SendOptions.SendUnreliable;
                var ok = PhotonNetwork.RaiseEvent(code, payload, options, send);
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

        public static string Stats() => $"sent {SentCount}, received {ReceivedCount}, refused {RefusedCount}";
    }
}
