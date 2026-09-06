using System;
using System.Collections.Generic;
using System.Globalization;
using Il2CppPhoton.Pun;
using UnityEngine;
using LootOverhaul.Gate;
using LootOverhaul.Net;
using LootOverhaul.Recon;
using Interop = LootOverhaul.Recon.Interop;

namespace LootOverhaul.Loot
{
    /// <summary>
    /// The loot protocol on event code 151. String payloads, first char is the opcode:
    ///   S  spawned   — "S|viewId|<item record>[|px,py,pz|qx,qy,qz,qw]"  spawner → everyone
    ///                  (the pose is only added when re-sent to a late joiner: the cached
    ///                  spawn leaves their copy upright in the air where it was born)
    ///   C  claim     — "C|viewId"                     claimant → master
    ///   G  granted   — "G|viewId|actor"               master → everyone (claimant bags it, others untag)
    ///   D  denied    — "D|viewId"                     master → claimant
    ///   R  drop req. — "R|<item record>|px,py,pz|vx,vy,vz"  dropper → master (only the master
    ///                  may spawn a room object, and only room objects can be grabbed by all)
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

        public static void SendSpawned(LootTag tag, int[] targets = null, bool withPose = false)
        {
            var payload = $"S{Sep}{tag.ViewId}{Sep}{WeaponCodec.Encode(tag.Item)}";
            if (withPose && Interop.Alive(tag.Object))
            {
                try
                {
                    var t = tag.Object.transform;
                    payload += $"{Sep}{V(t.position)}{Sep}{Q(t.rotation)}";
                }
                catch { }
            }
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

        /// <summary>Ask the master to put this item on the floor for us. Returns false if there is no master to ask.</summary>
        public static bool SendDropRequest(LootItem item, Vector3 pos, Vector3 velocity)
        {
            var master = MasterActor();
            if (master < 0) return false;
            return ModNet.Send(ModNet.CodeLoot, $"R{Sep}{WeaponCodec.Encode(item)}{Sep}{V(pos)}{Sep}{V(velocity)}", reliable: true, targetActors: new[] { master });
        }

        private static void OnPeerJoined(ModPeer peer)
        {
            if (peer.IsLocal || !ModGate.Active) return;
            try { if (!PhotonNetwork.IsMasterClient) return; } catch { return; }
            var n = 0;
            foreach (var tag in LootRegistry.All)
            {
                if (tag.Claimed) continue;
                SendSpawned(tag, new[] { peer.ActorNumber }, withPose: true);
                n++;
            }
            if (n > 0) Core.Log.Msg($"Sent {n} loot tag(s) with resting poses to late joiner {peer.NickName}.");
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
                    var rest = parts[2].Split(Sep);
                    var item = WeaponCodec.Decode(rest[0]);
                    var tag = LootRegistry.Add(viewId, item, null);
                    if (rest.Length >= 3 && !tag.Claimed)
                    {
                        try
                        {
                            tag.PosePosition = PV(rest[1]);
                            tag.PoseRotation = PQ(rest[2]);
                            LootRegistry.ApplyPose(tag);
                        }
                        catch (Exception e) { Core.Log.Warning($"Loot pose for view {viewId} unreadable: {e.GetType().Name}"); tag.PosePosition = null; }
                    }
                    Core.Log.Msg($"Loot tagged from actor {sender}: view {viewId} {item.Name} ({(tag.Object == null ? "object not here yet" : "object found")}{(rest.Length >= 3 ? ", with pose" : "")})");
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
                case "R":
                {
                    if (!PhotonNetwork.IsMasterClient) return;
                    var p = text.Split(Sep);
                    if (p.Length < 4) { Core.Log.Warning($"Drop request from actor {sender} is short ({p.Length} fields)."); return; }
                    var item = WeaponCodec.Decode(p[1]);
                    var pos = PV(p[2]); var vel = PV(p[3]);
                    var tag = DropRoller.SpawnLoot(item, pos, vel);
                    ReconLog.Line($"drop request from actor {sender}: {item.Name} at {Interop.Vec(pos)} -> {(tag == null ? "spawn failed" : $"view {tag.ViewId}")}");
                    if (tag == null) Core.Log.Warning($"Could not spawn {item.Name} for actor {sender}'s drop request; the item is lost.");
                    break;
                }
                default:
                    Core.Log.Warning($"Unknown loot opcode '{parts[0]}' from actor {sender}.");
                    break;
            }
        }

        private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
        private static string V(Vector3 v) => $"{F(v.x)},{F(v.y)},{F(v.z)}";
        private static string Q(Quaternion q) => $"{F(q.x)},{F(q.y)},{F(q.z)},{F(q.w)}";
        private static float P(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
        private static Vector3 PV(string s) { var a = s.Split(','); return new Vector3(P(a[0]), P(a[1]), P(a[2])); }
        private static Quaternion PQ(string s) { var a = s.Split(','); return new Quaternion(P(a[0]), P(a[1]), P(a[2]), P(a[3])); }
    }

    /// <summary>
    /// Master-arbitrated pickup. Whoever asks first gets the item; the copies are hidden at
    /// once everywhere and whichever client controls the networked object (the master for
    /// a room object, or the claimant if the grab took ownership) destroys it a moment later.
    /// </summary>
    public static class Claims
    {
        // Destroying the object in the same frame the hand let go of it is the other half of
        // the frozen-hand story: hide it at once, destroy it a moment later.
        private static readonly List<(GameObject go, float at, float giveUp)> Doomed = new List<(GameObject, float, float)>();

        public static void Tick()
        {
            if (Doomed.Count == 0) return;
            var now = UnityEngine.Time.unscaledTime;
            for (var i = Doomed.Count - 1; i >= 0; i--)
            {
                var (go, at, giveUp) = Doomed[i];
                if (now < at) continue;
                try
                {
                    if (go == null || go.Pointer == IntPtr.Zero || !Interop.Alive(go)) { Doomed.RemoveAt(i); continue; }
                    var pv = go.GetComponent<PhotonView>();
                    if (Interop.Alive(pv) && !pv.IsMine)
                    {
                        // Someone else controls it (a remote grab took ownership); they destroy it. Keep checking for a while.
                        if (now < giveUp) continue;
                        ReconLog.Line($"claimed loot view {pv.ViewID} is not ours to destroy; leaving it hidden");
                        Doomed.RemoveAt(i);
                        continue;
                    }
                    Doomed.RemoveAt(i);
                    PhotonNetwork.Destroy(go);
                }
                catch (Exception e) { Doomed.RemoveAt(i); Core.Log.Warning($"Delayed destroy of claimed loot failed: {e.GetType().Name}: {e.Message}"); }
            }
        }

        private static void Doom(GameObject go)
        {
            if (go == null) return;
            foreach (var d in Doomed) if (ReferenceEquals(d.go, go) || (d.go != null && d.go.Pointer == go.Pointer)) return;
            var now = UnityEngine.Time.unscaledTime;
            Doomed.Add((go, now + 2.5f, now + 12f));
        }

        public static void HideNow(GameObject go)
        {
            // Renderers and colliders off, physics frozen, object left exactly where it is:
            // moving it (0.8) dragged the still-attached hand into the floor.
            try
            {
                if (go == null) return;
                foreach (var r in go.GetComponentsInChildren<Renderer>()) if (r != null) r.enabled = false;
                foreach (var c in go.GetComponentsInChildren<Collider>()) if (c != null) c.enabled = false;
                var rb = go.GetComponent<Rigidbody>();
                if (rb != null) { rb.velocity = Vector3.zero; rb.isKinematic = true; }
            }
            catch { }
        }

        public static void Request(LootTag tag)
        {
            if (tag.ClaimPending || tag.Claimed) return;
            tag.ClaimPending = true;
            if (PhotonNetwork.IsMasterClient) MasterGrant(tag.ViewId, PhotonNetwork.LocalPlayer.ActorNumber);
            else LootNet.SendClaim(tag.ViewId);
        }

        public static void MasterGrant(int viewId, int actor)
        {
            var me = PhotonNetwork.LocalPlayer.ActorNumber;
            if (!LootRegistry.TryGet(viewId, out var tag) || tag.Claimed)
            {
                // The same player's pickup reaches the master twice (the game's pickup RPC and
                // our claim); the second is not a loss, so no "Taken." for it.
                var same = tag != null && tag.Claimed && tag.ClaimedBy == actor;
                if (actor != me && !same) LootNet.SendDenied(viewId, actor);
                return;
            }
            tag.Claimed = true;
            tag.ClaimedBy = actor;
            LootNet.SendGranted(viewId, actor);
            var obj = tag.Object ?? LootRegistry.FindObject(viewId);
            if (obj != null) { HideNow(obj); Doom(obj); }
            OnGranted(viewId, actor);
        }

        public static void OnGranted(int viewId, int actor)
        {
            if (!LootRegistry.TryGet(viewId, out var tag)) return;
            var mine = actor == PhotonNetwork.LocalPlayer.ActorNumber;
            tag.Claimed = true; tag.ClaimedBy = actor;
            var obj = tag.Object ?? LootRegistry.FindObject(viewId);
            if (!PhotonNetwork.IsMasterClient) HideNow(obj);
            // If our grab took ownership of the object, the master cannot destroy it; we can.
            if (mine && obj != null) Doom(obj);
            LootRegistry.Remove(viewId);
            if (mine) BagManager.Bag(tag.Item);
            else Core.Log.Msg($"Loot view {viewId} ({tag.Item.Name}) taken by actor {actor}.");
        }

        public static void OnDenied(int viewId)
        {
            if (!LootRegistry.TryGet(viewId, out var tag)) return;   // already granted to us or gone: nothing to say
            tag.ClaimPending = false;
            BagManager.Toast("Taken.");
        }
    }
}
