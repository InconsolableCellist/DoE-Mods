using System;
using System.Collections.Generic;
using DoEFriendsMod.Gate;
using Il2CppPhoton.Pun;
using Il2CppPhoton.Realtime;
using Il2CppExitGames.Client.Photon;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace DoEFriendsMod.Net
{
    /// <summary>
    /// The mod's whole network surface: a thin wrapper over Photon <c>RaiseEvent</c> plus a
    /// dispatch table for our reserved event codes.
    ///
    /// **Every send is refused unless <see cref="ModGate.Active"/>.** That check lives here, at
    /// the one place bytes can leave the process, rather than being repeated (and eventually
    /// forgotten) at each call site — it's what makes "the mod is inert in a vanilla lobby" a
    /// structural property instead of a promise.
    /// </summary>
    public static class ModNet
    {
        // Reserved block 140–149. Cleared empirically 2026-08-31 over a full dungeon run:
        // the game's own custom events cluster low (codes 1, 2, 50, 70).
        public const byte CodeHandshake = 140;
        public const byte CodeAvatarManifest = 141;
        public const byte CodeFaceStream = 142;
        public const byte CodeItems = 143;
        public const byte CodeMin = 140;
        public const byte CodeMax = 149;

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
                throw new ArgumentOutOfRangeException(nameof(code), $"Event code {code} is outside our reserved 140–149 block.");
            Handlers[code] = handler;
        }

        private static void OnRawEvent(byte code, int sender, Il2CppSystem.Object content)
        {
            if (code < CodeMin || code > CodeMax) return;

            // Queue rather than dispatch inline. Photon's dispatch is main-thread in PUN2, but
            // queueing costs a frame we don't care about and removes the question entirely.
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

                // Inbound is dropped while inert too: a stale event arriving during the
                // transition to a vanilla lobby must not reach a feature that just disarmed.
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

        /// <summary>
        /// Send to every modded peer, or to <paramref name="targetActors"/> if given.
        /// Returns false (and logs) when the gate refuses.
        /// </summary>
        public static bool Send(byte code, string payload, bool reliable, int[] targetActors = null)
            => SendRaw(code, (Il2CppSystem.String)payload, reliable, targetActors);

        public static bool SendRaw(byte code, Il2CppSystem.Object payload, bool reliable, int[] targetActors = null)
        {
            if (code < CodeMin || code > CodeMax)
            {
                Core.Log.Error($"Refusing to send on code {code}: outside our reserved 140–149 block.");
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
