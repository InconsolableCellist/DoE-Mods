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
    /// The drop roll. A postfix on the enemy-death RPC, acting only on the master client
    /// (mirrors vanilla: the host decides loot), only when the gate is open, and only for
    /// kills with a real killer — the run-end cleanup arrives as killer -1 / damage type 8
    /// (recon 2026-09-02) and must not shower the exit with swords.
    ///
    /// Tuning after the 2026-09-02 19:30 playtest: critters never drop weapons (a scorpion
    /// has no sword), tiers come from the game's own loot tier for the player instead of a
    /// uniform roll over all seven, rarity is weighted toward Common with bosses and the
    /// pity counter pushing upward, and a failed weapon roll can still drop a trinket.
    /// </summary>
    public static class DropRoller
    {
        private static readonly System.Random Rng = new System.Random();
        private static readonly HashSet<string> BadPrefabs = new HashSet<string>();
        public static int RollsSeen, Dropped, JunkDropped;
        private static bool _tierLogged;

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
                var aiType = 0; var boss = false; var family = -1;
                try { aiType = (int)__instance.type; boss = __instance.IsBoss; } catch { }
                try { family = (int)__instance.references.family; } catch { }

                var inv = BagManager.Inventory;
                inv.KillsSinceLegendary++;

                // Undead=0, Critter=1, Sorcerer=2, Monster=3. Critters carry nothing; bosses always may.
                var canCarryWeapon = boss || family != 1;
                var chance = boss ? ModConfig.BossDropChance.Value
                                  : ModConfig.BaseDropChance.Value * LootTables.EnemyMultiplier(aiType);
                var roll = Rng.NextDouble();
                var pity = ModConfig.LegendaryPityKills.Value > 0 && inv.KillsSinceLegendary >= ModConfig.LegendaryPityKills.Value;

                var pos = __instance.transform.position + Vector3.up * 1.0f;
                var kick = Vector3.up * 2.5f + new Vector3((float)(Rng.NextDouble() - 0.5), 0f, (float)(Rng.NextDouble() - 0.5)) * 1.5f;

                if (canCarryWeapon && (roll < chance || pity))
                {
                    var cls = RollClass(boss, pity);
                    if (cls == 3) inv.KillsSinceLegendary = 0;
                    inv.Save();

                    // Some of the weapon budget is armor, when the player has any perk to build it on.
                    if (Rng.NextDouble() < ModConfig.ArmorShare.Value)
                    {
                        var realmA = -1; try { realmA = (int)GameManager.CurrentRealm; } catch { }
                        var armor = Armor.Roll(cls, realmA, Rng);
                        if (armor != null)
                        {
                            try { armor.FoundBy = AvatarPlayer.FindByActorNo(__0)?.name; } catch { }
                            var atag = SpawnLoot(armor, pos, kick * 0.6f);
                            if (atag != null) { Dropped++; ReconLog.Line($"ARMOR #{Dropped}: {armor.Name} [{LootTables.ClassName(cls)} {Armor.SlotNames[armor.ArmorSlot]}] {Armor.DescribeStats(armor)} from `{__instance.name}` view={atag.ViewId}"); }
                            return;
                        }
                    }
                    var tier = RollTier();
                    var types = Unlocks.DroppableTypes();
                    var type = types[Rng.Next(types.Length)];
                    // Staff style is the spell: only styles the player already owns (lock-step). Never seasonal.
                    var style = type == LootTables.Staff ? Unlocks.PickStaffStyle(Rng) : -1;
                    var wm = WeaponFactory.GenerateRandomWeaponModuleForLocalPlayer(
                        (WeaponFactory.WeaponClass)cls, (Prop.Type)type,
                        (WeaponFactory.WeaponTier)tier, (WeaponFactory.WeaponStyle)style, -1, WeaponFactory.SeasonalKey.None);
                    if (wm == null) { Core.Log.Warning($"Generator returned null for {LootTables.TypeName(type)}/{cls}/t{tier + 1}."); return; }

                    var item = WeaponCodec.FromModule(wm);
                    try { item.FoundInRealm = (int)GameManager.CurrentRealm; } catch { }
                    try { item.FoundBy = AvatarPlayer.FindByActorNo(__0)?.name; } catch { }
                    var tag = SpawnLoot(item, pos, kick);
                    if (tag == null) return;
                    Dropped++;
                    ReconLog.Line($"DROP #{Dropped}: {item.Name} [{LootTables.ClassName(cls)} t{tier + 1}] from `{__instance.name}` family={family} type={aiType} boss={boss} chance={chance:0.###} roll={roll:0.###} pity={pity} view={tag.ViewId}");
                    return;
                }
                inv.Save();

                // No weapon: maybe a trinket. Critters included — a scorpion can sit on a bone.
                var junkChance = ModConfig.JunkDropChance.Value * (family == 1 ? 0.5f : 1f) * (boss ? 3f : 1f);
                if (Rng.NextDouble() < junkChance)
                {
                    // A prefab that refuses is retired inside SpawnLoot; try up to three bodies so the drop is not lost.
                    var gentle = Vector3.up * 1.2f + new Vector3((float)(Rng.NextDouble() - 0.5), 0f, (float)(Rng.NextDouble() - 0.5)) * 0.6f;
                    for (var attempt = 0; attempt < 3; attempt++)
                    {
                        var item = MakeJunk(__0);
                        if (item == null) return;
                        var tag = SpawnLoot(item, pos, gentle);
                        if (tag == null) continue;
                        JunkDropped++;
                        ReconLog.Line($"JUNK #{JunkDropped}: {item.Name} [{LootTables.JunkTierName(item.WeaponClass)} on `{item.PrefabName}`] from `{__instance.name}` family={family} value={item.Value} view={tag.ViewId}");
                        return;
                    }
                }
            }
            catch (Exception e) { Core.Log.Error($"Drop roll failed: {e}"); }
        }

        private static int RollClass(bool boss, bool pity)
        {
            if (pity) return 3;
            var w = new[] { ModConfig.WeightCommon.Value, ModConfig.WeightUnique.Value, ModConfig.WeightRare.Value, ModConfig.WeightLegendary.Value };
            var total = 0f; foreach (var x in w) total += Math.Max(0f, x);
            var cls = 0;
            if (total > 0f)
            {
                var r = Rng.NextDouble() * total;
                for (cls = 0; cls < 3; cls++) { r -= Math.Max(0f, w[cls]); if (r < 0) break; }
            }
            if (boss) cls = Math.Max(cls, 1) + (Rng.NextDouble() < 0.35 ? 1 : 0);
            return Math.Min(cls, 3);
        }

        /// <summary>The game's loot tier for this player, occasionally one up. 0-based, clamped to the seven weapon tiers.</summary>
        private static int RollTier()
        {
            var tier = 0;
            try { tier = GameManager.CalculateLootTierForLocalPlayer(false); }
            catch (Exception e) { Core.Log.Warning($"CalculateLootTierForLocalPlayer threw {e.GetType().Name}; using tier 1."); }
            if (!_tierLogged) { _tierLogged = true; ReconLog.Line($"loot tier for local player (game's own): {tier}"); }
            if (Rng.NextDouble() < ModConfig.TierUpChance.Value) tier++;
            return Math.Max(0, Math.Min(6, tier));
        }

        private static LootItem MakeJunk(int killerActor)
        {
            var tier = LootTables.RollJunkTier(Rng.NextDouble());
            var pool = new List<LootTables.Junk>();
            foreach (var j in LootTables.JunkTable) if (j.Tier == tier && !BadPrefabs.Contains(j.Prefab)) pool.Add(j);
            if (pool.Count == 0) foreach (var j in LootTables.JunkTable) if (!BadPrefabs.Contains(j.Prefab)) pool.Add(j);
            if (pool.Count == 0) return null;
            var def = pool[Rng.Next(pool.Count)];
            var realm = -1;
            try { realm = (int)GameManager.CurrentRealm; } catch { }
            var (name, mult) = JunkNamer.Roll(def.Prefab, def.Tier, realm, Rng);
            var item = new LootItem
            {
                Kind = "junk",
                PrefabName = def.Prefab,
                Name = name,
                ColoredName = $"<color={LootTables.JunkColor(def.Tier)}>{name}</color>",
                WeaponClass = def.Tier,       // reused as the junk tier for sorting/colour
                Value = Math.Max(1, (int)Math.Round(Rng.Next(def.MinValue, def.MaxValue + 1) * mult)),
                Weight = def.Weight,
                PropType = -1,
                FoundInRealm = realm,
            };
            try { item.FoundBy = AvatarPlayer.FindByActorNo(killerActor)?.name; } catch { }
            return item;
        }

        /// <summary>
        /// Spawn an item as a tagged, networked object on the floor. Weapons go through the
        /// generator's prefab + data; junk rides on a plain vanilla prop. The spawner owns
        /// the object; the master destroys it on claim.
        /// </summary>
        public static LootTag SpawnLoot(LootItem item, Vector3 pos, Vector3 velocity)
        {
            try
            {
                GameObject go;
                if (item.IsWeapon)
                {
                    var wm = WeaponCodec.ToModule(item);
                    var prefab = WeaponModule.GetWeaponPrefabData(wm, out var data);
                    go = PhotonNetwork.Instantiate(prefab, pos, Quaternion.identity, 0, data);
                }
                else
                {
                    go = PhotonNetwork.Instantiate(item.PrefabName, pos, Quaternion.identity, 0, null);
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
                try { var rb = go.GetComponent<Rigidbody>(); if (Interop.Alive(rb)) rb.velocity = velocity; } catch { }

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
