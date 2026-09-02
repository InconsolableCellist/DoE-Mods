using System;
using System.Collections.Generic;
using LootOverhaul.Gate;
using Il2CppPhoton.Pun;
using Il2CppPhoton.Realtime;
using Il2CppExitGames.Client.Photon;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace LootOverhaul.Net
{
    /// <summary>
    /// The mod's whole network surface: a thin wrapper over Photon <c>RaiseEvent</c> plus a
    /// dispatch table for our reserved event codes.
    ///
    /// **Every send is refused unless <see cref="ModGate.Active"/>.** That check lives here, at
    /// the one place bytes can leave the process, which is what makes "inert in a vanilla
    /// lobby" a structural property instead of a promise.
    ///
    /// Reserved block **150–159**. CustomAvatars owns 140–149; the game's own custom events
    /// were measured at codes 1, 2, 50 and 70 over a full run (2026-08-31), and PUN reserves
    /// 200 and up. The block itself has not yet been sniffed with a second player present —
    /// the CustomAvatars recon tally covers every inbound code below 200, so a mixed session
    /// with both mods installed will settle it.
    /// </summary>
    public static class ModNet
    {
        public const byte CodeHandshake = 150;
        /// <summary>
        /// All loot traffic, with a one-byte sub-opcode as the first payload byte:
        /// spawned / claim / claim-granted / claim-denied / table-snapshot / sold.
        /// One code with sub-opcodes rather than six codes, so the block stays roomy.
        /// </summary>
        public const byte CodeLoot = 151;
        public const byte CodeMin = 150;
        public const byte CodeMax = 159;

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

        /// <summary>Send to every modded peer, or to <paramref name="targetActors"/> if given.</summary>
        public static bool Send(byte code, string payload, bool reliable, int[] targetActors = null)
            => SendRaw(code, (Il2CppSystem.String)payload, reliable, targetActors);

        /// <summary>Binary payload, for the loot messages (view IDs, seeds, sub-opcodes).</summary>
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
