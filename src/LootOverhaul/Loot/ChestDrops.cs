using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppPhoton.Pun;
using LootOverhaul.Gate;
using LootOverhaul.Recon;
using UnityEngine;
using Interop = LootOverhaul.Recon.Interop;

namespace LootOverhaul.Loot
{
    /// <summary>
    /// Armor comes from chests (0.10.3). The moment a chest opens (<c>Chest.EV_ChestOpened</c>, the
    /// key turning, before anyone presses A on the loot) the host rolls for that chest, once for
    /// every player in the room (0.10.7, ChestArmorScalesWithPlayers), and spawns each piece as a floor item.
    /// Small chests rarely, medium ones sometimes, epic and boss chests always; the chest size
    /// sets the rarity. Mimics (<c>Mimic : Chest</c>, named like their size,
    /// e.g. chest_medium_mimic) roll like a chest of that size. Enemies drop nothing, and the crypt (realm Crypts / dark
    /// rooms, its rings and runes) gets no armor.
    /// </summary>
    public static class ChestDrops
    {
        private static readonly System.Random Rng = new System.Random();
        // EV_ChestOpened may run on every client and more than once; the host rolls each chest once.
        private static readonly HashSet<int> Rolled = new HashSet<int>();
        public static int Dropped;

        private class Delayed { public LootItem Armor; public Vector3 Pos, Kick; public float Due; public string Desc; }
        /// <summary>Pieces rolled but not spawned yet (ChestArmorDelay); forgotten with the scene.</summary>
        private static readonly List<Delayed> Waiting = new List<Delayed>();

        /// <summary>A new scene means new chests, and the game may reuse object ids: forget which ones have rolled.</summary>
        public static void OnSceneChanged() { Rolled.Clear(); Waiting.Clear(); }

        public static void Install() =>
            Hooks.Patch(typeof(Chest), "EV_ChestOpened", null, Hooks.Of(typeof(ChestDrops), nameof(Chest_Opened)));

        private static void Chest_Opened(Chest __instance)
        {
            try
            {
                if (ModConfig.ReconEnabled.Value) GameplayHooks.LogChest("EV_ChestOpened", __instance);
                OnOpened(__instance);
            }
            catch (Exception e) { Core.Log.Error($"Chest drop failed: {e}"); }
        }

        private static void OnOpened(Chest chest)
        {
            if (!ModConfig.Enabled.Value || !ModConfig.ChestArmorEnabled.Value || !ModGate.Active) return;
            if (!PhotonNetwork.IsMasterClient) return;
            if (!Interop.Alive(chest)) return;

            // Once per chest: the first call claims it, whatever the outcome of the roll.
            if (!Rolled.Add(chest.GetInstanceID())) return;

            var name = "?"; try { name = chest.name; } catch { }
            if (InCrypt()) { Skip(name, "crypt"); return; }
            var avail = 0; try { avail = (int)chest.availLoot; } catch { }
            const int cryptBits = (int)(Chest.AvailLootTypes.Ring | Chest.AvailLootTypes.CryptGem
                                      | Chest.AvailLootTypes.CryptRune | Chest.AvailLootTypes.CryptWeapons);
            if ((avail & cryptBits) != 0) { Skip(name, $"crypt loot (availLoot={avail})"); return; }
            if (GameManager.IsSandboxScene) { Skip(name, "sandbox"); return; }

            var boss = false; try { boss = chest.IsBossChest; } catch { }
            var lower = name.ToLowerInvariant();
            // The respawner chest reports IsBossChest=True (recon 2026-10-08), so this check comes before the boss rule.
            if (lower.Contains("_tut") || lower.Contains("respawner") || lower.Contains("soulharvest"))
            { Skip(name, "tutorial / respawner / soul-harvest chest"); return; }

            int cls; float chance; string size;
            if (boss) { cls = 3; chance = ModConfig.ChestArmorBoss.Value; size = "boss"; }
            else if (lower.Contains("epic")) { cls = 2; chance = ModConfig.ChestArmorEpic.Value; size = "epic"; }
            else if (lower.Contains("medium")) { cls = 1; chance = ModConfig.ChestArmorMedium.Value; size = "medium"; }
            else if (lower.Contains("small")) { cls = 0; chance = ModConfig.ChestArmorSmall.Value; size = "small"; }
            else { Skip(name, "unknown chest size"); return; }

            // One roll for every player in the room (ChestArmorScalesWithPlayers), each its own draw at
            // the full chance: two players at a small chest give two separate 10% rolls. Shared floor loot.
            var players = DropRoller.Players();
            var rolls = ModConfig.ChestArmorScalesWithPlayers.Value ? players : 1;
            var realm = -1; try { realm = (int)GameManager.CurrentRealm; } catch { }
            var pieces = new List<(LootItem Armor, double Roll, int Of)>();
            for (var r = 0; r < rolls; r++)
            {
                var roll = Rng.NextDouble();
                if (roll >= chance) { ReconLog.Line($"chest armor: `{name}` ({size}) roll {r + 1}/{rolls} no drop, roll={roll:0.###} chance={chance:0.###}"); continue; }
                var armor = Armor.Roll(cls, realm, Rng);
                if (armor == null) { ReconLog.Line($"chest armor: `{name}` ({size}) rolled a drop but no perk is unlocked to build armor on"); break; }
                armor.Source = "chest";
                pieces.Add((armor, roll, r + 1));
            }
            if (pieces.Count == 0) return;

            // In front of the chest (the side the lid opens toward), not on top of it: a piece
            // starts ChestArmorDistance metres out and is tossed further along the same line.
            // Several pieces fan out ChestDropSpread degrees apart around that line, and leave
            // ChestDropStagger seconds apart, so they neither stack nor collide in the air.
            var fwd = chest.transform.forward; fwd.y = 0f;
            fwd = fwd.sqrMagnitude < 0.0001f ? Vector3.forward : fwd.normalized;
            var n = pieces.Count;
            var spread = Math.Max(0f, Math.Min(90f, ModConfig.ChestDropSpread.Value));
            var stagger = Math.Max(0f, ModConfig.ChestDropStagger.Value);
            // The person who turned the key is standing at the chest with a hand out: a piece that
            // appears at once gets grabbed by accident. It appears ChestArmorDelay seconds later instead.
            var delay = Math.Max(0f, ModConfig.ChestArmorDelay.Value);
            for (var i = 0; i < n; i++)
            {
                var (armor, roll, of) = pieces[i];
                var angle = (i - (n - 1) * 0.5f) * spread;
                var dir = Quaternion.AngleAxis(angle, Vector3.up) * fwd;
                var side = new Vector3(dir.z, 0f, -dir.x) * (float)(Rng.NextDouble() - 0.5) * (n > 1 ? 0.2f : 0.6f);
                var pos = chest.transform.position + dir * ModConfig.ChestArmorDistance.Value + Vector3.up * 1.0f;
                var kick = Vector3.up * 1.0f + (dir * 1.1f) + side;
                var desc = $"{armor.Name} [{LootTables.ClassName(cls)} {Armor.SlotNames[armor.ArmorSlot]}] {Armor.DescribeStats(armor)} from `{name}` size={size} roll {of}/{rolls}={roll:0.###} chance={chance:0.###} players={players}";
                var wait = delay + i * stagger;
                if (wait <= 0f) { Spawn(armor, pos, kick, desc); continue; }
                Waiting.Add(new Delayed { Armor = armor, Pos = pos, Kick = kick, Due = Time.unscaledTime + wait, Desc = desc });
                ReconLog.Line($"chest armor: `{name}` queued, {armor.Name} appears in {wait:0.##} s");
            }
        }

        private static void Spawn(LootItem armor, Vector3 pos, Vector3 kick, string desc)
        {
            var tag = DropRoller.SpawnLoot(armor, pos, kick);
            if (tag == null) return;
            Dropped++;
            ReconLog.Line($"ARMOR (chest) #{Dropped}: {desc} view={tag.ViewId}");
        }

        /// <summary>Called every frame from Core: spawn the pieces whose delay is up.</summary>
        public static void Tick()
        {
            if (Waiting.Count == 0) return;
            var now = Time.unscaledTime;
            for (var i = Waiting.Count - 1; i >= 0; i--)
            {
                var w = Waiting[i];
                if (now < w.Due) continue;
                Waiting.RemoveAt(i);
                try
                {
                    if (!ModGate.Active || !PhotonNetwork.IsMasterClient) { ReconLog.Line($"chest armor: {w.Armor.Name} dropped from the queue (gate closed or no longer the host)"); continue; }
                    Spawn(w.Armor, w.Pos, w.Kick, w.Desc);
                }
                catch (Exception e) { Core.Log.Error($"Delayed chest drop failed: {e}"); }
            }
        }

        private static void Skip(string name, string why) => ReconLog.Line($"chest armor: `{name}` skipped: {why}");

        /// <summary>The crypt: realm Crypts, or a dark-room dungeon.</summary>
        private static bool InCrypt()
        {
            try { if ((int)GameManager.CurrentRealm == 6) return true; } catch { }   // 6 = Realm.Crypts
            try { if (DarkRoomManager.Instance != null) return true; } catch { }
            return false;
        }
    }
}
