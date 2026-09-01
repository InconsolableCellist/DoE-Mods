using System;
using System.Collections.Generic;
using CustomAvatars.Gate;
using Il2CppPhoton.Pun;
using Il2CppPhoton.Realtime;
using Il2CppExitGames.Client.Photon;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace CustomAvatars.Net
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
        /// <summary>Finger curls, 10 bytes. Unreliable — a dropped frame is one stale pose.</summary>
        public const byte CodeHandPose = 144;
        /// <summary>Full-body tracker targets (hip + feet), 32 bytes. Unreliable, like 144.</summary>
        public const byte CodeTrackerPose = 145;
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

        /// <summary>
        /// Binary payload. The pose and face streams are byte-packed rather than stringly
        /// typed: they run at 10-15 Hz forever, and a few bytes per tick is the difference
        /// between negligible and noticeable next to voice.
        /// </summary>
        public static bool SendBytes(byte code, byte[] payload, bool reliable, int[] targetActors = null)
        {
            if (payload == null) return false;
            var array = new Il2CppStructArray<byte>(payload.Length);
            for (var i = 0; i < payload.Length; i++) array[i] = payload[i];
            // An Il2Cpp array is an il2cpp object, but the interop wrapper isn't in
            // Il2CppSystem.Object's managed hierarchy — cast through the pointer.
            return SendRaw(code, new Il2CppSystem.Object(array.Pointer), reliable, targetActors);
        }

        /// <summary>Unpack a received binary payload, or null if it isn't one.</summary>
        public static byte[] AsBytes(Il2CppSystem.Object content)
        {
            try
            {
                var array = content?.TryCast<Il2CppStructArray<byte>>();
                if (array == null) return null;
                var managed = new byte[array.Length];
                for (var i = 0; i < array.Length; i++) managed[i] = array[i];
                return managed;
            }
            catch { return null; }
        }

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
