using System;
using System.Collections.Generic;
using Il2Cpp;
using Realm = Il2CppOthergate.Biome.Realm;
using Il2CppPhoton.Pun;
using LootOverhaul.Gate;
using UnityEngine;

namespace LootOverhaul.Recon
{
    /// <summary>
    /// The two probes that decide whether the design works at all.
    ///
    /// <see cref="Survey"/> (Insert): generate one weapon per type × class through the
    /// game's own generator, log everything the bag and booth would need (prefab name,
    /// coloured name, stats, cost, salvage, save string round-trip), and let
    /// <see cref="ProfileWatch"/> say whether any of it touched PlayFab.
    ///
    /// <see cref="SpawnTest"/> (Delete): spawn one generated weapon in front of you through
    /// the same networked path the mod would use for drops, and log what came out.
    /// </summary>
    public static class GeneratorProbe
    {
        private static readonly Prop.Type[] WeaponTypes =
        {
            Prop.Type.Sword, Prop.Type.Axe, Prop.Type.Dagger, Prop.Type.Spear, Prop.Type.Bow,
            Prop.Type.Staff, Prop.Type.Crossbow, Prop.Type.Longsword, Prop.Type.LongAxe,
            Prop.Type.Hammer, Prop.Type.Shield,
        };

        private static readonly WeaponFactory.WeaponClass[] Classes =
        {
            WeaponFactory.WeaponClass.Common, WeaponFactory.WeaponClass.Unique,
            WeaponFactory.WeaponClass.Rare, WeaponFactory.WeaponClass.Legendary,
        };

        private static int _spawnCursor;
        private static readonly List<(GameObject go, string label, string scene)> Spawned = new List<(GameObject, string, string)>();

        public static void Survey()
        {
            if (!(ModGate.LocalOnly || ModGate.Active))
            {
                ReconLog.Headline($"Survey refused: gate says {ModGate.LocalReason}");
                return;
            }

            ReconLog.Section("Generator survey");
            int uncraftedBefore = -1, unlockedBefore = -1;
            ReconLog.TryKeyValue("player level", () => PlayerProfile.GetLevel());
            ReconLog.Try("counts before", () =>
            {
                uncraftedBefore = PlayerProfile.GetUncraftedWeapons().Count;
                unlockedBefore = PlayerProfile.GetUnlockedWeapons().Count;
                ReconLog.KeyValue("uncrafted weapons before", uncraftedBefore);
                ReconLog.KeyValue("unlocked (armory) weapons before", unlockedBefore);
            });
            ReconLog.TryKeyValue("WeaponFactory.Instance", () => Interop.Name(WeaponFactory.Instance));

            ProfileWatch.Probe = "generator survey";
            try
            {
                foreach (var type in WeaponTypes)
                    foreach (var cls in Classes)
                        ReconLog.Try($"{type}/{cls}", () => Describe(type, cls));

                ReconLog.Line();
                ReconLog.Line("Rarity roll distribution, 200 rolls per realm at the current level:");
                var level = 1;
                try { level = PlayerProfile.GetLevel(); } catch { }
                foreach (var realm in new[] { Realm.Underworld, Realm.Sandstorm, Realm.Vilehalls, Realm.LavaForge })
                {
                    ReconLog.Try($"rolls {realm}", () =>
                    {
                        var counts = new Dictionary<WeaponFactory.WeaponClass, int>();
                        for (var i = 0; i < 200; i++)
                        {
                            var c = WeaponFactory.GetRandomWeaponClass(level, realm, false, false);
                            counts.TryGetValue(c, out var n);
                            counts[c] = n + 1;
                        }
                        var parts = new List<string>();
                        foreach (var kv in counts) parts.Add($"{kv.Key}={kv.Value}");
                        ReconLog.Line($"- {realm}: {string.Join(", ", parts)}");
                    });
                }
            }
            finally { ProfileWatch.Probe = null; }

            ReconLog.Try("counts after", () =>
            {
                var uncraftedAfter = PlayerProfile.GetUncraftedWeapons().Count;
                var unlockedAfter = PlayerProfile.GetUnlockedWeapons().Count;
                ReconLog.KeyValue("uncrafted weapons after", uncraftedAfter);
                ReconLog.KeyValue("unlocked (armory) weapons after", unlockedAfter);
                var grew = uncraftedAfter != uncraftedBefore || unlockedAfter != unlockedBefore;
                ReconLog.Headline(grew
                    ? "VERDICT: the generator ADDED to the profile weapon lists — build DTOs by hand instead."
                    : "VERDICT: generator left the profile weapon lists unchanged.");
            });
            ReconLog.Headline($"Survey written to {ReconLog.CurrentFile}");
        }

        private static void Describe(Prop.Type type, WeaponFactory.WeaponClass cls)
        {
            var wm = WeaponFactory.GenerateRandomWeaponModuleForLocalPlayer(
                cls, type, (WeaponFactory.WeaponTier)(-1), (WeaponFactory.WeaponStyle)(-1), -1, WeaponFactory.SeasonalKey.None);
            if (wm == null) { ReconLog.Line($"- {type}/{cls}: generator returned null"); return; }

            var prefab = wm.GetPrefabName();
            var name = Interop.OneLine(wm.GetDisplayName(false));
            var colored = Interop.OneLine(wm.GetDisplayName(true));
            var tier = wm.GetWeaponTier();
            var style = wm.GetWeaponStyle();
            var seed = wm.GetRandomSeed();
            var genV = wm.GetGenVersion();
            var save = wm.GetSaveString();
            var stats = Interop.OneLine(wm.GetStatsText());

            ReconLog.Line($"- **{type}/{cls}** `{prefab}` \"{name}\" tier={tier} style={style} seed={seed} genV={genV}");
            ReconLog.Line($"  - colored: {colored}");
            ReconLog.Line($"  - stats: {stats}");
            ReconLog.Line($"  - save: `{save}`");

            ReconLog.Try("  stats table", () =>
            {
                var def = WeaponFactory.GetRandomWeaponStats(type, wm.GetWeaponClass(), tier, style, seed);
                ReconLog.Line($"  - def: dmg {def.damage}-{def.damageMax} cost={def.cost} salvage={def.salvageValue} " +
                              $"perks={def.primaryPerk}/{def.secondaryPerk} elem={def.elemental} superior={def.isSuperior}");
            });

            ReconLog.Try("  prefab data", () =>
            {
                var prefabFromData = WeaponModule.GetWeaponPrefabData(wm, out var data);
                ReconLog.Line($"  - instantiate: prefab=`{prefabFromData}` data.Length={(data == null ? -1 : data.Length)}");
            });

            ReconLog.Try("  round-trip", () =>
            {
                var back = WeaponModule.GetWeaponModule(save);
                var same = back != null && Interop.OneLine(back.GetDisplayName(false)) == name && back.GetRandomSeed() == seed;
                ReconLog.Line($"  - GetWeaponModule(save) round-trip: {(same ? "OK" : "MISMATCH")}");
                var dto = new PlayerData.WeaponModuleDTO(wm);
                var viaDto = new WeaponModule(dto);
                ReconLog.Line($"  - DTO round-trip: {(viaDto.GetSaveString() == save ? "OK" : "differs: `" + viaDto.GetSaveString() + "`")}");
            });
        }

        public static void SpawnTest()
        {
            if (!ModGate.Active)
            {
                ReconLog.Headline($"Spawn test refused: gate is inert ({ModGate.Reason}). Needs a private room (solo is fine).");
                return;
            }

            ReconLog.Section("Networked spawn test");
            var type = WeaponTypes[_spawnCursor++ % WeaponTypes.Length];
            ProfileWatch.Probe = $"spawn test {type}";
            try
            {
                var local = AvatarPlayer.LocalAvatar;
                if (!Interop.Alive(local)) { ReconLog.Headline("No local avatar — spawn in a lobby or dungeon."); return; }
                var head = local.Head;
                var pos = head.position + head.forward * 1.0f + Vector3.up * 0.2f;
                var rot = Quaternion.LookRotation(head.forward, Vector3.up);

                var wm = WeaponFactory.GenerateRandomWeaponModuleForLocalPlayer(
                    WeaponFactory.WeaponClass.Rare, type, (WeaponFactory.WeaponTier)(-1), (WeaponFactory.WeaponStyle)(-1), -1, WeaponFactory.SeasonalKey.None);
                var prefab = WeaponModule.GetWeaponPrefabData(wm, out var data);
                ReconLog.Line($"spawning `{prefab}` \"{Interop.OneLine(wm.GetDisplayName(false))}\" data.Length={(data == null ? -1 : data.Length)} at {Interop.Vec(pos)} [{(PhotonNetwork.IsMasterClient ? "MASTER" : "client")}]");

                var go = PhotonNetwork.Instantiate(prefab, pos, rot, 0, data);
                if (!Interop.Alive(go)) { ReconLog.Headline("PhotonNetwork.Instantiate returned null."); return; }

                var label = $"{prefab}/{Interop.OneLine(wm.GetDisplayName(false))}";
                Spawned.Add((go, label, UnityEngine.SceneManagement.SceneManager.GetActiveScene().name));

                ReconLog.KeyValue("object", $"`{go.name}` at {Interop.ScenePath(go.transform)}");
                ReconLog.Try("components", () =>
                {
                    var comps = go.GetComponents<Component>();
                    var names = new List<string>();
                    foreach (var c in comps) if (Interop.Alive(c)) names.Add(c.GetIl2CppType().Name);
                    ReconLog.KeyValue("components", string.Join(", ", names));
                });
                ReconLog.Try("weapon", () =>
                {
                    var w = go.GetComponent<Weapon>();
                    if (!Interop.Alive(w)) { ReconLog.KeyValue("Weapon component", "none"); return; }
                    ReconLog.KeyValue("Weapon.RandomSeed", w.RandomSeed);
                    ReconLog.KeyValue("Weapon.isRandomlyGenerated", w.isRandomlyGenerated);
                    ReconLog.KeyValue("Weapon.type", w.type);
                    ReconLog.KeyValue("Weapon.outlineColor", w.outlineColor);
                    ReconLog.KeyValue("seed matches module", w.RandomSeed == wm.GetRandomSeed());
                });
                ReconLog.Try("photon view", () =>
                {
                    var pv = go.GetComponent<PhotonView>();
                    if (!Interop.Alive(pv)) { ReconLog.KeyValue("PhotonView", "none"); return; }
                    ReconLog.KeyValue("PhotonView", $"viewID={pv.ViewID} mine={pv.IsMine} owner={(pv.Owner == null ? "null" : pv.Owner.NickName)}");
                });
                ReconLog.Headline($"Spawned {label}. Pick it up, drop it, hand it to a friend: the Prop.PickUp/Drop lines show what fires and where.");
            }
            catch (Exception e) { ReconLog.Error("spawn test", e); }
            finally { ProfileWatch.Probe = null; }
        }

        private static readonly string[] JunkCandidates =
        {
            "Dice", "Wolf_Treat", "Trophy_SkullCrown", "Trophy_NovaGuild", "Trophy_Chest", "Trophy_Zombie",
            "Chalice", "Chalice_01", "Goblet", "Mug", "Cup", "Skull", "Horn", "Candle", "Book", "Bottle",
            "Gem_White", "Crystal", "Key_01_Skull", "Bone", "Bones", "Coin_Pile_01",
        };
        private static int _junkCursor;

        /// <summary>Minus key: try the next candidate prop name through the networked pool and describe what came out.</summary>
        public static void JunkProbe()
        {
            if (!ModGate.Active) { ReconLog.Headline($"Junk probe refused: gate is inert ({ModGate.Reason})."); return; }
            var name = JunkCandidates[_junkCursor++ % JunkCandidates.Length];
            ReconLog.Section($"Junk prefab probe: `{name}`");
            try
            {
                var local = AvatarPlayer.LocalAvatar;
                if (!Interop.Alive(local)) { ReconLog.Headline("No local avatar."); return; }
                var head = local.Head;
                var pos = head.position + head.forward * 0.8f;
                var go = PhotonNetwork.Instantiate(name, pos, Quaternion.identity, 0, null);
                if (!Interop.Alive(go)) { ReconLog.Headline($"`{name}`: pool refused (null)."); return; }
                var comps = new List<string>();
                foreach (var c in go.GetComponents<Component>()) if (Interop.Alive(c)) comps.Add(c.GetIl2CppType().Name);
                var size = "?";
                try
                {
                    var rs = go.GetComponentsInChildren<Renderer>();
                    if (rs.Length > 0) { var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); size = $"{b.size.x:0.00}×{b.size.y:0.00}×{b.size.z:0.00} m"; }
                }
                catch { }
                var prop = go.GetComponent<Prop>();
                ReconLog.Headline($"`{name}`: spawned `{go.name}` size {size}; Prop={(Interop.Alive(prop) ? prop.type.ToString() : "none")}; components: {string.Join(", ", comps)}");
                Spawned.Add((go, $"junk probe {name}", UnityEngine.SceneManagement.SceneManager.GetActiveScene().name));
            }
            catch (Exception e) { ReconLog.Error($"junk probe {name}", e); }
        }

        /// <summary>
        /// Semicolon key: every GameObject the game can load by name from its Resources folders,
        /// with the components that matter to us. This is the set PhotonNetwork.Instantiate can
        /// reach by plain name, so it is the authoritative list of possible loot bodies.
        /// </summary>
        public static void ResourceCensus()
        {
            ReconLog.Section("Resources census (GameObjects)");
            try
            {
                var all = Resources.LoadAll("", Il2CppInterop.Runtime.Il2CppType.Of<GameObject>());
                ReconLog.KeyValue("GameObjects in Resources", all == null ? -1 : all.Length);
                if (all == null) return;
                var withProp = 0;
                foreach (var o in all)
                {
                    var go = o?.TryCast<GameObject>();
                    if (!Interop.Alive(go)) continue;
                    var prop = go.GetComponent<Prop>();
                    var pv = go.GetComponent<PhotonView>();
                    if (!Interop.Alive(prop) && !Interop.Alive(pv)) continue;
                    withProp++;
                    var size = "?";
                    try
                    {
                        var rs = go.GetComponentsInChildren<Renderer>(true);
                        if (rs.Length > 0) { var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); size = $"{b.size.x:0.00}×{b.size.y:0.00}×{b.size.z:0.00}"; }
                    }
                    catch { }
                    var comps = new List<string>();
                    foreach (var c in go.GetComponents<Component>()) if (Interop.Alive(c)) comps.Add(c.GetIl2CppType().Name);
                    ReconLog.Line($"- `{go.name}` prop={(Interop.Alive(prop) ? prop.type.ToString() : "-")} view={(Interop.Alive(pv) ? "yes" : "no")} size {size} : {string.Join(", ", comps)}");
                }
                ReconLog.Headline($"Resources census: {all.Length} GameObject(s), {withProp} with a Prop or PhotonView — see the transcript.");
            }
            catch (Exception e) { ReconLog.Error("resources census", e); }
        }

        /// <summary>On every scene change: are the weapons we spawned still alive? (Design item 5.)</summary>
        public static void CheckSpawned(string newScene)
        {
            if (Spawned.Count == 0) return;
            foreach (var s in Spawned)
                ReconLog.Line($"spawned weapon `{s.label}` (from scene {s.scene}) after entering {newScene}: {(Interop.Alive(s.go) ? "ALIVE" : "destroyed")}");
        }
    }
}
