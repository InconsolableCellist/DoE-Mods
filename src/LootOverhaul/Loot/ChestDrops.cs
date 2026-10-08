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
    /// key turning, before anyone presses A on the loot) the host rolls once for that chest and
    /// spawns the piece as a floor item. Small chests rarely, medium ones sometimes, epic and boss
    /// chests always; the chest size sets the rarity. Mimics (<c>Mimic : Chest</c>, named like their size,
    /// e.g. chest_medium_mimic) roll like a chest of that size. Enemies drop nothing, and the crypt (realm Crypts / dark
    /// rooms, its rings and runes) gets no armor.
    /// </summary>
    public static class ChestDrops
    {
        private static readonly System.Random Rng = new System.Random();
        // EV_ChestOpened may run on every client and more than once; the host rolls each chest once.
        private static readonly HashSet<int> Rolled = new HashSet<int>();
        public static int Dropped;

        /// <summary>A new scene means new chests, and the game may reuse object ids: forget which ones have rolled.</summary>
        public static void OnSceneChanged() => Rolled.Clear();

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

            var roll = Rng.NextDouble();
            if (roll >= chance) { ReconLog.Line($"chest armor: `{name}` ({size}) no drop, roll={roll:0.###} chance={chance:0.###}"); return; }

            var realm = -1; try { realm = (int)GameManager.CurrentRealm; } catch { }
            var armor = Armor.Roll(cls, realm, Rng);
            if (armor == null) { ReconLog.Line($"chest armor: `{name}` ({size}) rolled a drop but no perk is unlocked to build armor on"); return; }
            armor.Source = "chest";

            // In front of the chest (the side the lid opens toward), not on top of it: the piece
            // starts ChestArmorDistance metres out and is tossed further along the same line.
            var fwd = chest.transform.forward; fwd.y = 0f;
            fwd = fwd.sqrMagnitude < 0.0001f ? Vector3.forward : fwd.normalized;
            var side = new Vector3(fwd.z, 0f, -fwd.x) * (float)(Rng.NextDouble() - 0.5) * 0.6f;
            var pos = chest.transform.position + fwd * ModConfig.ChestArmorDistance.Value + Vector3.up * 1.0f;
            var kick = Vector3.up * 1.0f + (fwd * 1.1f) + side;
            var tag = DropRoller.SpawnLoot(armor, pos, kick);
            if (tag == null) return;
            Dropped++;
            ReconLog.Line($"ARMOR (chest) #{Dropped}: {armor.Name} [{LootTables.ClassName(cls)} {Armor.SlotNames[armor.ArmorSlot]}] {Armor.DescribeStats(armor)} from `{name}` size={size} roll={roll:0.###} chance={chance:0.###} view={tag.ViewId}");
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
