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
    ///
    /// 0.9.12: bosses and mini-bosses drop a pile (`BossDrops` / `MiniBossDrops` pieces), the
    /// first piece at least `BossGuaranteedClass` (Rare), plus their junk roll on top. The rank
    /// comes from the class the game spawned the enemy as, since `AI.IsBoss` ignores mini-bosses.
    ///
    /// 0.9.13: the pile grows with the party, with the boss's health bars (`Specs.stages`) and
    /// with its strength type (Elite, Legend); Elite and Legend regulars always drop; the loot
    /// goblin is a pile of its own with guaranteed trinkets; sandbox kills roll nothing.
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
                if (!ModConfig.SandboxDrops.Value && InSandbox(__instance))
                {
                    // The practice arena despawns its enemies and their bodies; loot there is free
                    // loot and the despawn left our decorations behind (report 2026-09-08).
                    if (!_sandboxLogged) { _sandboxLogged = true; ReconLog.Line("drop roll skipped: sandbox kill (SandboxDrops=false)"); }
                    return;
                }

                RollsSeen++;
                var aiType = 0; var family = -1;
                try { aiType = (int)__instance.type; } catch { }
                try { family = (int)__instance.references.family; } catch { }
                var rank = BossRank(__instance);
                var boss = rank > 0;
                var goblin = Goblins.IsLootGoblin(__instance);
                var stages = HealthBars(__instance);

                var inv = BagManager.Inventory;
                inv.KillsSinceLegendary++;

                // More players, more loot to share: both chances scale with the room.
                var players = 1;
                try { players = Math.Max(1, (int)PhotonNetwork.CurrentRoom.PlayerCount); } catch { }
                var partyScale = 1f + Math.Max(0f, ModConfig.DropChancePerExtraPlayer.Value) * (players - 1);
                var pity = ModConfig.LegendaryPityKills.Value > 0 && inv.KillsSinceLegendary >= ModConfig.LegendaryPityKills.Value;
                var pos = __instance.transform.position + Vector3.up * 1.0f;
                var who = $"family={family} type={aiType} ({LootTables.AiTypeName(aiType)}) stages={stages} players={players}";

                if (boss || goblin)
                {
                    // A boss is a pile: the first piece is guaranteed and at least the class floor
                    // (pity still forces Legendary), every further piece rolls BossDropChance and
                    // the boss rarity curve. Pieces are kicked around a circle so they do not stack.
                    // The pile grows with the party, with extra health bars, and with the boss's
                    // strength type (Elite, Legend): the many-skull bosses of the 2026-09-08 report.
                    int pieces, floor; string rankName;
                    if (goblin)
                    {
                        pieces = Math.Max(1, ModConfig.GoblinDrops.Value);
                        floor = ModConfig.GoblinGuaranteedClass.Value;
                        rankName = "loot goblin";
                    }
                    else
                    {
                        pieces = BossPieces(rank, aiType, players, stages);
                        floor = ModConfig.BossGuaranteedClass.Value;
                        rankName = rank == 2 ? "boss" : "mini-boss";
                    }
                    floor = Math.Max(0, Math.Min(3, floor));
                    for (var i = 0; i < pieces; i++)
                    {
                        if (i > 0 && Rng.NextDouble() >= ModConfig.BossDropChance.Value) continue;
                        var cls = i == 0 ? Math.Max(floor, RollClass(true, pity)) : RollClass(true, false);
                        if (cls == 3) inv.KillsSinceLegendary = 0;
                        DropPiece(cls, pos, ScatterKick(i, pieces), __0, __instance,
                            $"{who} {rankName} piece={i + 1}/{pieces} pity={i == 0 && pity}");
                    }
                    inv.Save();
                    // Bosses roll their trinket three times as often; the goblin always leaves a few.
                    var junkRolls = goblin ? Math.Max(1, ModConfig.GoblinJunkRolls.Value) : 1;
                    for (var j = 0; j < junkRolls; j++)
                        RollJunk(__instance, __0, pos, family, boss || goblin, partyScale, goblin ? 1f : -1f);
                    return;
                }

                // Undead=0, Critter=1, Sorcerer=2, Monster=3. Critters carry nothing.
                var canCarryWeapon = family != 1;

                // Elites and Legends always leave something (report 2026-09-08), on the normal curve.
                var sure = aiType == 3 ? ModConfig.EliteDrops.Value : aiType == 4 ? ModConfig.LegendDrops.Value : 0;
                if (canCarryWeapon && sure > 0)
                {
                    for (var i = 0; i < sure; i++)
                    {
                        var cls = RollClass(false, i == 0 && pity);
                        if (cls == 3) inv.KillsSinceLegendary = 0;
                        DropPiece(cls, pos, ScatterKick(i, sure), __0, __instance,
                            $"{who} {LootTables.AiTypeName(aiType).ToLowerInvariant()} piece={i + 1}/{sure} pity={i == 0 && pity}");
                    }
                    inv.Save();
                    RollJunk(__instance, __0, pos, family, false, partyScale);
                    return;
                }

                var chance = Math.Min(1f, ModConfig.BaseDropChance.Value * LootTables.EnemyMultiplier(aiType) * partyScale);
                var roll = Rng.NextDouble();
                if (canCarryWeapon && (roll < chance || pity))
                {
                    var cls = RollClass(false, pity);
                    if (cls == 3) inv.KillsSinceLegendary = 0;
                    inv.Save();
                    var kick = Vector3.up * 2.5f + new Vector3((float)(Rng.NextDouble() - 0.5), 0f, (float)(Rng.NextDouble() - 0.5)) * 1.5f;
                    DropPiece(cls, pos, kick, __0, __instance,
                        $"{who} chance={chance:0.###} roll={roll:0.###} pity={pity}");
                    return;
                }
                inv.Save();
                RollJunk(__instance, __0, pos, family, boss, partyScale);
            }
            catch (Exception e) { Core.Log.Error($"Drop roll failed: {e}"); }
        }

        private static bool _sandboxLogged;

        /// <summary>The practice arena: its own scene, or an enemy the sandbox UI spawned.</summary>
        private static bool InSandbox(AI ai)
        {
            try { if (GameManager.IsSandboxScene) return true; } catch { }
            try { if (ai.isSandboxSpawned) return true; } catch { }
            return false;
        }

        /// <summary>How many health bars the enemy came with (<c>Specs.stages</c>): 1 for almost everyone, more for the multi-bar bosses.</summary>
        private static int HealthBars(AI ai)
        {
            try
            {
                var specs = ai.specs;
                if (specs == null) return 1;
                var st = specs.stages;
                return st == null ? 1 : Math.Max(1, st.Length);
            }
            catch { return 1; }
        }

        /// <summary>
        /// Pile size for a boss (rank 2) or mini-boss (rank 1): the base count, plus a share per
        /// extra player, plus one per extra health bar, plus the strength-type bonus (Elite once,
        /// Legend twice). Never fewer than one.
        /// </summary>
        public static int BossPieces(int rank, int aiType, int players, int stages)
        {
            var isBoss = rank == 2;
            var basePieces = isBoss ? ModConfig.BossDrops.Value : ModConfig.MiniBossDrops.Value;
            var perPlayer = isBoss ? ModConfig.BossDropsPerExtraPlayer.Value : ModConfig.MiniBossDropsPerExtraPlayer.Value;
            var pieces = basePieces
                + (int)Math.Floor(Math.Max(0f, perPlayer) * Math.Max(0, players - 1))
                + Math.Max(0, ModConfig.BossDropsPerExtraHealthBar.Value) * Math.Max(0, stages - 1)
                + (aiType == 3 ? 1 : aiType == 4 ? 2 : 0) * Math.Max(0, ModConfig.EliteBossExtraDrops.Value);
            return Math.Max(1, Math.Min(12, pieces));
        }

        /// <summary>
        /// 0 for a regular enemy, 1 for a mini-boss, 2 for a boss, from the class the game
        /// spawned it as (<c>References.spawnedAsClass</c>: Miniboss = 4, Boss = 5). The game's
        /// own <c>AI.IsBoss</c> is exactly <c>spawnedAsClass == Boss</c> (read from the assembly
        /// 2026-09-07), so it is only the fallback and never sees a mini-boss.
        /// </summary>
        private static int BossRank(AI ai)
        {
            try
            {
                var c = (int)ai.references.spawnedAsClass;
                if (c == 5) return 2;
                if (c == 4) return 1;
            }
            catch { }
            try { if (ai.IsBoss) return 2; } catch { }
            return 0;
        }

        /// <summary>Piece <paramref name="i"/> of <paramref name="n"/> kicked out along its own slice of a circle, so a boss's pile lands spread out.</summary>
        private static Vector3 ScatterKick(int i, int n)
        {
            if (n <= 1) return Vector3.up * 2.5f + new Vector3((float)(Rng.NextDouble() - 0.5), 0f, (float)(Rng.NextDouble() - 0.5)) * 1.5f;
            var a = (i + 0.25 + Rng.NextDouble() * 0.5) * (2.0 * Math.PI / n);
            var r = 1.2f + (float)Rng.NextDouble() * 0.8f;
            return Vector3.up * 2.5f + new Vector3((float)Math.Cos(a), 0f, (float)Math.Sin(a)) * r;
        }

        /// <summary>
        /// One weapon or armor drop of the given rarity: some of the budget is armor when the
        /// player has any perk to build it on, otherwise a generated weapon at the player's
        /// loot tier. <paramref name="why"/> is the log's account of the roll.
        /// </summary>
        private static bool DropPiece(int cls, Vector3 pos, Vector3 kick, int killerActor, AI source, string why)
        {
            var sourceName = "?"; try { sourceName = source.name; } catch { }
            if (Rng.NextDouble() < ModConfig.ArmorShare.Value)
            {
                var realmA = -1; try { realmA = (int)GameManager.CurrentRealm; } catch { }
                var armor = Armor.Roll(cls, realmA, Rng);
                if (armor != null)
                {
                    try { armor.FoundBy = AvatarPlayer.FindByActorNo(killerActor)?.name; } catch { }
                    var atag = SpawnLoot(armor, pos, kick * 0.6f);
                    if (atag == null) return false;
                    Dropped++;
                    ReconLog.Line($"ARMOR #{Dropped}: {armor.Name} [{LootTables.ClassName(cls)} {Armor.SlotNames[armor.ArmorSlot]}] {Armor.DescribeStats(armor)} from `{sourceName}` {why} view={atag.ViewId}");
                    return true;
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
            if (wm == null) { Core.Log.Warning($"Generator returned null for {LootTables.TypeName(type)}/{cls}/t{tier + 1}."); return false; }

            var item = WeaponCodec.FromModule(wm);
            try { item.FoundInRealm = (int)GameManager.CurrentRealm; } catch { }
            try { item.FoundBy = AvatarPlayer.FindByActorNo(killerActor)?.name; } catch { }
            var tag = SpawnLoot(item, pos, kick);
            if (tag == null) return false;
            Dropped++;
            ReconLog.Line($"DROP #{Dropped}: {item.Name} [{LootTables.ClassName(cls)} t{tier + 1}] from `{sourceName}` {why} view={tag.ViewId}");
            return true;
        }

        /// <summary>Maybe a trinket. Critters included (a scorpion can sit on a bone); bosses three times as often, on top of their pile. <paramref name="chance"/> ≥ 0 overrides the roll (1 = always).</summary>
        private static void RollJunk(AI source, int killerActor, Vector3 pos, int family, bool boss, float partyScale, float chance = -1f)
        {
            var junkChance = chance >= 0f ? chance : Math.Min(1f, ModConfig.JunkDropChance.Value * (family == 1 ? 0.5f : 1f) * (boss ? 3f : 1f) * partyScale);
            if (Rng.NextDouble() >= junkChance) return;
            var sourceName = "?"; try { sourceName = source.name; } catch { }
            // A prefab that refuses is retired inside SpawnLoot; try up to three bodies so the drop is not lost.
            var gentle = Vector3.up * 1.2f + new Vector3((float)(Rng.NextDouble() - 0.5), 0f, (float)(Rng.NextDouble() - 0.5)) * 0.6f;
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var item = MakeJunk(killerActor);
                if (item == null) return;
                var tag = SpawnLoot(item, pos, gentle);
                if (tag == null) continue;
                JunkDropped++;
                ReconLog.Line($"JUNK #{JunkDropped}: {item.Name} [{LootTables.JunkTierName(item.WeaponClass)} on `{item.PrefabName}`] from `{sourceName}` family={family} boss={boss} value={item.Value} view={tag.ViewId}");
                return;
            }
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

        /// <summary>The game's loot tier for this player, occasionally one up, never above the dungeon's own tier. 0-based, clamped to the seven weapon tiers.</summary>
        private static int RollTier()
        {
            var tier = 0;
            try { tier = GameManager.CalculateLootTierForLocalPlayer(false); }
            catch (Exception e) { Core.Log.Warning($"CalculateLootTierForLocalPlayer threw {e.GetType().Name}; using tier 1."); }
            if (Rng.NextDouble() < ModConfig.TierUpChance.Value) tier++;
            var cap = DungeonTier();
            if (cap >= 0) tier = Math.Min(tier, cap);
            if (!_tierLogged) { _tierLogged = true; ReconLog.Line($"loot tier for local player (game's own): {tier}, dungeon tier cap: {cap}"); }
            return Math.Max(0, Math.Min(6, tier));
        }

        /// <summary>The tier this dungeon was entered at: the room's `lvl_tier` property, else the game's difficulty tier; -1 if neither is set (lobby).</summary>
        public static int DungeonTier()
        {
            try
            {
                var room = PhotonNetwork.CurrentRoom;
                if (room != null && room.CustomProperties != null)
                {
                    var key = (Il2CppSystem.String)"lvl_tier";
                    if (room.CustomProperties.ContainsKey(key))
                    {
                        var v = room.CustomProperties[key];
                        if (v != null && int.TryParse(v.ToString(), out var t) && t >= 0) return Math.Min(6, t);
                    }
                }
            }
            catch { }
            try { var dt = (int)GameManager.DifficultyTier; if (dt >= 0) return Math.Min(6, dt); } catch { }
            return -1;
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
