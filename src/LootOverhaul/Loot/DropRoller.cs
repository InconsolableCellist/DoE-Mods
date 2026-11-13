using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppPhoton.Pun;
using LootOverhaul.Gate;
using LootOverhaul.Recon;
using UnityEngine;
using Interop = LootOverhaul.Recon.Interop;
using AI = Il2CppSauron.AI;

namespace LootOverhaul.Loot
{
    /// <summary>
    /// The loot goblin's pile. A postfix on the enemy-death RPC, acting only on the master
    /// client (the host decides loot), only when the gate is open, and only for kills with a
    /// real killer. Since 0.11.0 nothing else drops on death: the enemy weapon roll, boss piles
    /// and junk are gone. The goblin drops armor only, one pile per player in the room
    /// (GoblinDropsScaleWithPlayers), the first piece of each at least GoblinGuaranteedClass.
    /// </summary>
    public static class DropRoller
    {
        private static readonly System.Random Rng = new System.Random();

        // Boss and goblin piles are rolled at once but spawned a little apart (0.10.8): a goblin
        // in a four-player room is up to two dozen pieces, and each one is a weapon generation,
        // a network instantiate, a label and a sparkle, which in one frame is a visible hitch.
        private const float PileStagger = 0.06f;
        private static readonly List<(float Due, Action Spawn)> PileQueue = new List<(float, Action)>();
        private static float _pileNext;

        /// <summary>Queue one pile piece; the first of a burst spawns this frame.</summary>
        private static void QueuePile(Action spawn)
        {
            var now = Time.unscaledTime;
            if (PileQueue.Count == 0 && _pileNext <= now) { _pileNext = now + PileStagger; RunPile(spawn); return; }
            if (_pileNext < now) _pileNext = now;
            PileQueue.Add((_pileNext, spawn));
            _pileNext += PileStagger;
        }

        private static void RunPile(Action spawn)
        {
            try
            {
                if (!ModGate.Active || !PhotonNetwork.IsMasterClient) { ReconLog.Line("pile piece dropped from the queue (gate closed or no longer the host)"); return; }
                spawn();
            }
            catch (Exception e) { Core.Log.Error($"Pile drop failed: {e}"); }
        }

        /// <summary>Called every frame from Core: spawn the queued pile pieces whose turn has come.</summary>
        public static void Tick()
        {
            if (PileQueue.Count == 0) return;
            var now = Time.unscaledTime;
            while (PileQueue.Count > 0 && PileQueue[0].Due <= now)
            {
                var next = PileQueue[0];
                PileQueue.RemoveAt(0);
                RunPile(next.Spawn);
            }
        }

        /// <summary>A queued piece never lands in the next scene.</summary>
        public static void OnSceneChanged()
        {
            if (PileQueue.Count > 0) ReconLog.Line($"pile: {PileQueue.Count} queued piece(s) dropped on scene change");
            PileQueue.Clear();
        }
        private static readonly HashSet<string> BadPrefabs = new HashSet<string>();
        public static int RollsSeen, Dropped;

        /// <summary>Relative rarity weights for every piece after a pile's guaranteed first one.</summary>
        private static readonly float[] ClassWeights = { 70f, 22f, 7f, 1f };

        public static void OnKilled(AI __instance, int __0, int __1)
        {
            try
            {
                if (!ModConfig.Enabled.Value || !ModConfig.GoblinDropsEnabled.Value || !ModGate.Active) return;
                if (!PhotonNetwork.IsMasterClient) return;
                if (__0 < 0) return;
                if (!Interop.Alive(__instance)) return;
                if (!Goblins.IsLootGoblin(__instance)) return;
                if (InSandbox(__instance))
                {
                    if (!_sandboxLogged) { _sandboxLogged = true; ReconLog.Line("goblin pile skipped: sandbox kill"); }
                    return;
                }

                RollsSeen++;
                var players = Players();
                var pieces = Math.Max(1, ModConfig.GoblinDrops.Value);
                var floor = Math.Max(0, Math.Min(3, ModConfig.GoblinGuaranteedClass.Value));
                var piles = ModConfig.GoblinDropsScaleWithPlayers.Value ? players : 1;
                var pos = __instance.transform.position + Vector3.up * 1.0f;
                var total = pieces * piles;
                for (var p = 0; p < piles; p++)
                for (var i = 0; i < pieces; i++)
                {
                    var cls = i == 0 ? Math.Max(floor, RollClass()) : RollClass();
                    var kick = ScatterKick(p * pieces + i, total);
                    var why = $"loot goblin pile={p + 1}/{piles} piece={i + 1}/{pieces} players={players}";
                    var killer = __0; var source = __instance;
                    QueuePile(() => DropArmor(cls, pos, kick, killer, source, why));
                }
            }
            catch (Exception e) { Core.Log.Error($"Goblin pile failed: {e}"); }
        }

        private static bool _sandboxLogged;

        /// <summary>The practice arena: its own scene, or an enemy the sandbox UI spawned.</summary>
        private static bool InSandbox(AI ai)
        {
            try { if (GameManager.IsSandboxScene) return true; } catch { }
            try { if (ai.isSandboxSpawned) return true; } catch { }
            return false;
        }

        /// <summary>Piece <paramref name="i"/> of <paramref name="n"/> kicked out along its own slice of a circle, so a pile lands spread out.</summary>
        private static Vector3 ScatterKick(int i, int n)
        {
            if (n <= 1) return Vector3.up * 2.5f + new Vector3((float)(Rng.NextDouble() - 0.5), 0f, (float)(Rng.NextDouble() - 0.5)) * 1.5f;
            var a = (i + 0.25 + Rng.NextDouble() * 0.5) * (2.0 * Math.PI / n);
            var r = 1.2f + (float)Rng.NextDouble() * 0.8f;
            return Vector3.up * 2.5f + new Vector3((float)Math.Cos(a), 0f, (float)Math.Sin(a)) * r;
        }

        private static bool DropArmor(int cls, Vector3 pos, Vector3 kick, int killerActor, AI source, string why)
        {
            var sourceName = "?"; try { sourceName = source.name; } catch { }
            var realm = -1; try { realm = (int)GameManager.CurrentRealm; } catch { }
            var armor = Armor.Roll(cls, realm, Rng);
            if (armor == null) return false;
            try { armor.FoundBy = AvatarPlayer.FindByActorNo(killerActor)?.name; } catch { }
            var tag = SpawnLoot(armor, pos, kick * 0.6f);
            if (tag == null) return false;
            Dropped++;
            ReconLog.Line($"ARMOR #{Dropped}: {armor.Name} [{LootTables.ClassName(cls)} {Armor.SlotNames[armor.ArmorSlot]}] {Armor.DescribeStats(armor)} from `{sourceName}` {why} view={tag.ViewId}");
            return true;
        }

        /// <summary>Players in the room (at least 1), or DropPlayersOverride when it is set.</summary>
        public static int Players()
        {
            var o = ModConfig.DropPlayersOverride.Value;
            if (o > 0) return Math.Min(8, o);
            try { return Math.Max(1, (int)PhotonNetwork.CurrentRoom.PlayerCount); } catch { return 1; }
        }

        private static int RollClass()
        {
            var total = 0f; foreach (var x in ClassWeights) total += x;
            var r = Rng.NextDouble() * total;
            for (var cls = 0; cls < 3; cls++) { r -= ClassWeights[cls]; if (r < 0) return cls; }
            return 3;
        }

        /// <summary>
        /// Spawn an item as a tagged, networked **room object** on the floor, the way the
        /// game's own <c>LootSpawner</c> does it. Weapons go through the generator's prefab +
        /// data; junk rides on a plain vanilla prop with the game's room data.
        ///
        /// Room objects because of the hand's grab rule (VRControllerProps.OnHold, read from
        /// the assembly 2026-09-04): a prop is only grabbable when it is a scene/room view or
        /// this client owns it. A plain <c>PhotonNetwork.Instantiate</c> made every drop the
        /// master's personal property, and two two-player runs saw the remote grab nothing.
        /// Only the master may create room objects, so a non-master drop asks the master.
        /// </summary>
        public static LootTag SpawnLoot(LootItem item, Vector3 pos, Vector3 velocity)
        {
            try
            {
                if (!PhotonNetwork.IsMasterClient) { Core.Log.Warning($"SpawnLoot({item.Name}) called on a non-master; room objects need the master."); return null; }
                GameObject go;
                if (item.IsWeapon)
                {
                    var wm = WeaponCodec.ToModule(item);
                    var prefab = WeaponModule.GetWeaponPrefabData(wm, out var data);
                    go = PhotonNetwork.InstantiateRoomObject(prefab, pos, Quaternion.identity, 0, data);
                }
                else
                {
                    Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppSystem.Object> data = null;
                    try
                    {
                        var room = SceneOcclusion.FindRoom(pos, false);
                        if (Interop.Alive(room)) data = Prop.GetInstantiationData(room, false);
                    }
                    catch (Exception e) { ReconLog.Line($"spawn: room lookup threw {e.GetType().Name}; junk spawns without room data"); }
                    go = PhotonNetwork.InstantiateRoomObject(item.PrefabName, pos, Quaternion.identity, 0, data);
                    if (!Interop.Alive(go))
                    {
                        BadPrefabs.Add(item.PrefabName);
                        Core.Log.Warning($"Junk prefab `{item.PrefabName}` would not instantiate; retired for this session.");
                        return null;
                    }
                }
                if (!Interop.Alive(go)) { Core.Log.Warning($"Instantiate returned null for {item.Name}."); return null; }
                var pv = go.GetComponent<PhotonView>();
                if (!Interop.Alive(pv)) { Core.Log.Warning($"Spawned {item.Name} has no PhotonView."); try { UnityEngine.Object.Destroy(go); } catch { } return null; }

                // A kick and a tumble, then the game's own throw/spin audio (networked by the game).
                var angular = Vector3.zero;
                try
                {
                    var rb = go.GetComponent<Rigidbody>();
                    if (Interop.Alive(rb))
                    {
                        rb.velocity = velocity;
                        angular = UnityEngine.Random.onUnitSphere * (6f + (float)Rng.NextDouble() * 8f);
                        rb.angularVelocity = angular;
                    }
                }
                catch { }
                DropSound.OnSpawned(go, velocity, angular);

                var tag = LootRegistry.Add(pv.ViewID, item, go);
                LootNet.SendSpawned(tag);
                return tag;
            }
            catch (Exception e)
            {
                Core.Log.Error($"SpawnLoot failed for {item.Name}: {e}");
                if (!item.IsWeapon) BadPrefabs.Add(item.PrefabName);
                return null;
            }
        }
    }
}
