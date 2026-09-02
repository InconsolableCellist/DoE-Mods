using System;
using Il2CppPhoton.Pun;
using LootOverhaul.Gate;
using LootOverhaul.Net;

namespace LootOverhaul.Loot
{
    /// <summary>
    /// The loot protocol on event code 151. String payloads, first char is the opcode:
    ///   S  spawned   — "S|viewId|<item record>"      spawner → everyone
    ///   C  claim     — "C|viewId"                     claimant → master
    ///   G  granted   — "G|viewId|actor"               master → everyone (claimant bags it, others untag)
    ///   D  denied    — "D|viewId"                     master → claimant
    /// Late joiners get one S per live tag from the master when the roster sees them.
    /// </summary>
    public static class LootNet
    {
        private const char Sep = '|';
        private static ModRoster _roster;

        public static void Init(ModRoster roster)
        {
            _roster = roster;
            ModNet.RegisterHandler(ModNet.CodeLoot, OnMessage);
            roster.PeerJoined += OnPeerJoined;
        }

        private static int[] Others() => _roster.ModdedPeerActors();
        private static int MasterActor()
        {
            try { return PhotonNetwork.MasterClient.ActorNumber; } catch { return -1; }
        }

        public static void SendSpawned(LootTag tag, int[] targets = null)
        {
            var payload = $"S{Sep}{tag.ViewId}{Sep}{WeaponCodec.Encode(tag.Item)}";
            ModNet.Send(ModNet.CodeLoot, payload, reliable: true, targetActors: targets ?? Others());
        }

        public static void SendClaim(int viewId)
        {
            var master = MasterActor();
            if (master < 0) return;
            ModNet.Send(ModNet.CodeLoot, $"C{Sep}{viewId}", reliable: true, targetActors: new[] { master });
        }

        public static void SendGranted(int viewId, int actor) =>
            ModNet.Send(ModNet.CodeLoot, $"G{Sep}{viewId}{Sep}{actor}", reliable: true, targetActors: Others());

        public static void SendDenied(int viewId, int actor) =>
            ModNet.Send(ModNet.CodeLoot, $"D{Sep}{viewId}", reliable: true, targetActors: new[] { actor });

        private static void OnPeerJoined(ModPeer peer)
        {
            if (peer.IsLocal || !ModGate.Active) return;
            try { if (!PhotonNetwork.IsMasterClient) return; } catch { return; }
            var n = 0;
            foreach (var tag in LootRegistry.All)
            {
                if (tag.Claimed) continue;
                SendSpawned(tag, new[] { peer.ActorNumber });
                n++;
            }
            if (n > 0) Core.Log.Msg($"Sent {n} loot tag(s) to late joiner {peer.NickName}.");
        }

        private static void OnMessage(int sender, Il2CppSystem.Object content)
        {
            var text = content?.ToString();
            if (string.IsNullOrEmpty(text) || text.Length < 3) return;
            var parts = text.Split(Sep, 3);
            switch (parts[0])
            {
                case "S":
                {
                    var viewId = int.Parse(parts[1]);
                    var item = WeaponCodec.Decode(parts[2]);
                    var tag = LootRegistry.Add(viewId, item, null);
                    BagManager.Toast($"Loot dropped: {item.ColoredName}");
                    Core.Log.Msg($"Loot tagged from actor {sender}: view {viewId} {item.Name} ({(tag.Object == null ? "object not here yet" : "object found")})");
                    break;
                }
                case "C":
                {
                    if (!PhotonNetwork.IsMasterClient) return;
                    Claims.MasterGrant(int.Parse(parts[1]), sender);
                    break;
                }
                case "G":
                {
                    var p = text.Split(Sep);
                    Claims.OnGranted(int.Parse(p[1]), int.Parse(p[2]));
                    break;
                }
                case "D":
                {
                    Claims.OnDenied(int.Parse(parts[1]));
                    break;
                }
                default:
                    Core.Log.Warning($"Unknown loot opcode '{parts[0]}' from actor {sender}.");
                    break;
            }
        }
    }

    /// <summary>
    /// Master-arbitrated pickup. Whoever asks first gets the item; the master destroys the
    /// networked object (it may destroy anything, whoever owns it) and tells the room.
    /// </summary>
    public static class Claims
    {
        public static void Request(LootTag tag)
        {
            if (tag.ClaimPending || tag.Claimed) return;
            tag.ClaimPending = true;
            if (PhotonNetwork.IsMasterClient) MasterGrant(tag.ViewId, PhotonNetwork.LocalPlayer.ActorNumber);
            else LootNet.SendClaim(tag.ViewId);
        }

        public static void MasterGrant(int viewId, int actor)
        {
            if (!LootRegistry.TryGet(viewId, out var tag) || tag.Claimed)
            {
                if (actor != PhotonNetwork.LocalPlayer.ActorNumber) LootNet.SendDenied(viewId, actor);
                return;
            }
            tag.Claimed = true;
            LootNet.SendGranted(viewId, actor);
            try
            {
                var obj = tag.Object ?? LootRegistry.FindObject(viewId);
                if (obj != null) PhotonNetwork.Destroy(obj);
            }
            catch (Exception e) { Core.Log.Warning($"Could not destroy claimed loot view {viewId}: {e.GetType().Name}: {e.Message}"); }
            OnGranted(viewId, actor);
        }

        public static void OnGranted(int viewId, int actor)
        {
            if (!LootRegistry.TryGet(viewId, out var tag)) return;
            var mine = actor == PhotonNetwork.LocalPlayer.ActorNumber;
            LootRegistry.Remove(viewId);
            if (mine) BagManager.Bag(tag.Item);
            else Core.Log.Msg($"Loot view {viewId} ({tag.Item.Name}) taken by actor {actor}.");
        }

        public static void OnDenied(int viewId)
        {
            if (LootRegistry.TryGet(viewId, out var tag)) tag.ClaimPending = false;
            BagManager.Toast("Someone else got it.");
        }
    }
}
