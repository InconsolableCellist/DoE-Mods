using System;
using Il2Cpp;
using Il2CppPhoton.Pun;
using LootOverhaul.Gate;
using LootOverhaul.Recon;
using Interop = LootOverhaul.Recon.Interop;
using UnityEngine;
using AI = Il2CppSauron.AI;
using Realm = Il2CppOthergate.Biome.Realm;

namespace LootOverhaul.Loot
{
    /// <summary>
    /// The drop roll. A postfix on the enemy-death RPC, acting only on the master client
    /// (mirrors vanilla: the host decides loot), only when the gate is open, and only for
    /// kills with a real killer — the run-end cleanup arrives as killer -1 / damage type 8
    /// (recon 2026-09-02) and must not shower the exit with swords.
    ///
    /// Rarity comes from the game's own realm-weighted roll, nudged by the mod: bosses never
    /// drop below Rare, and a pity counter guarantees a Legendary eventually, since the
    /// vanilla roll never produced one at level 15 in 800 tries.
    /// </summary>
    public static class DropRoller
    {
        private static readonly System.Random Rng = new System.Random();
        public static int RollsSeen, Dropped;

        public static void Install()
        {
            Hooks.Patch(typeof(AI), "OnKilled", null, Hooks.Of(typeof(DropRoller), nameof(OnKilled)), paramCount: 2);
        }

        private static void OnKilled(AI __instance, int __0, int __1)
        {
            try
            {
                if (!ModConfig.Enabled.Value || !ModConfig.EnemyDropsEnabled.Value || !ModGate.Active) return;
                if (!PhotonNetwork.IsMasterClient) return;
                if (__0 < 0) return;                       // no killer: cleanup, scripted, environmental
                if (!Interop.Alive(__instance)) return;

                RollsSeen++;
                var aiType = 0; var boss = false;
                try { aiType = (int)__instance.type; boss = __instance.IsBoss; } catch { }

                var inv = BagManager.Inventory;
                inv.KillsSinceLegendary++;

                var chance = boss ? ModConfig.BossDropChance.Value
                                  : ModConfig.BaseDropChance.Value * LootTables.EnemyMultiplier(aiType);
                var roll = Rng.NextDouble();
                var pity = ModConfig.LegendaryPityKills.Value > 0 && inv.KillsSinceLegendary >= ModConfig.LegendaryPityKills.Value;
                if (roll >= chance && !pity) { inv.Save(); return; }

                var cls = RollClass(boss, pity);
                if (cls == 3) inv.KillsSinceLegendary = 0;
                inv.Save();

                var type = LootTables.DroppableTypes[Rng.Next(LootTables.DroppableTypes.Length)];
                var wm = WeaponFactory.GenerateRandomWeaponModuleForLocalPlayer(
                    (WeaponFactory.WeaponClass)cls, (Prop.Type)type,
                    (WeaponFactory.WeaponTier)(-1), (WeaponFactory.WeaponStyle)(-1), -1, WeaponFactory.SeasonalKey.None);
                if (wm == null) { Core.Log.Warning($"Generator returned null for {LootTables.TypeName(type)}/{cls}."); return; }

                var item = WeaponCodec.FromModule(wm);
                try { item.FoundInRealm = (int)GameManager.CurrentRealm; } catch { }
                try { item.FoundBy = AvatarPlayer.FindByActorNo(__0)?.name; } catch { }

                var pos = __instance.transform.position + Vector3.up * 1.0f;
                var kick = Vector3.up * 2.5f + new Vector3((float)(Rng.NextDouble() - 0.5), 0f, (float)(Rng.NextDouble() - 0.5)) * 1.5f;
                var tag = SpawnLoot(item, pos, kick);
                if (tag == null) return;
                Dropped++;
                ReconLog.Line($"DROP #{Dropped}: {item.Name} [{LootTables.ClassName(cls)}] from `{__instance.name}` type={aiType} boss={boss} chance={chance:0.###} roll={roll:0.###} pity={pity} view={tag.ViewId}");
            }
            catch (Exception e) { Core.Log.Error($"Drop roll failed: {e}"); }
        }

        private static int RollClass(bool boss, bool pity)
        {
            if (pity) return 3;
            var cls = 0;
            try
            {
                var level = PlayerProfile.GetLevel();
                var realm = GameManager.CurrentRealm;
                if ((int)realm < 0) realm = Realm.Underworld;
                cls = (int)WeaponFactory.GetRandomWeaponClass(level, realm, false, false);
            }
            catch (Exception e) { Core.Log.Warning($"Vanilla rarity roll failed ({e.GetType().Name}); using Common."); }
            if (cls < 0 || cls > 3) cls = 0;
            if (boss) cls = Math.Max(cls, 2) + (Rng.NextDouble() < 0.25 ? 1 : 0);
            return Math.Min(cls, 3);
        }

        /// <summary>
        /// Spawn an item as a tagged, networked weapon on the floor. Used by the drop roll on
        /// the master and by "drop from bag" on any client. The spawner owns the object; the
        /// master destroys it on claim.
        /// </summary>
        public static LootTag SpawnLoot(LootItem item, Vector3 pos, Vector3 velocity)
        {
            try
            {
                var wm = WeaponCodec.ToModule(item);
                var prefab = WeaponModule.GetWeaponPrefabData(wm, out var data);
                var go = PhotonNetwork.Instantiate(prefab, pos, Quaternion.identity, 0, data);
                if (!Interop.Alive(go)) { Core.Log.Warning($"Instantiate returned null for `{prefab}`."); return null; }
                var pv = go.GetComponent<PhotonView>();
                if (!Interop.Alive(pv)) { Core.Log.Warning($"Spawned `{prefab}` has no PhotonView."); return null; }
                try { var rb = go.GetComponent<Rigidbody>(); if (Interop.Alive(rb)) rb.velocity = velocity; } catch { }

                var tag = LootRegistry.Add(pv.ViewID, item, go);
                LootNet.SendSpawned(tag);
                BagManager.Toast($"Loot dropped: {item.ColoredName}");
                return tag;
            }
            catch (Exception e)
            {
                Core.Log.Error($"SpawnLoot failed for {item.Name}: {e}");
                return null;
            }
        }
    }
}
