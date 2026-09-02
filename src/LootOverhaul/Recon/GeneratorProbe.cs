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

        /// <summary>On every scene change: are the weapons we spawned still alive? (Design item 5.)</summary>
        public static void CheckSpawned(string newScene)
        {
            if (Spawned.Count == 0) return;
            foreach (var s in Spawned)
                ReconLog.Line($"spawned weapon `{s.label}` (from scene {s.scene}) after entering {newScene}: {(Interop.Alive(s.go) ? "ALIVE" : "destroyed")}");
        }
    }
}
