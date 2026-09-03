using System;
using System.Globalization;
using Il2Cpp;

namespace LootOverhaul.Loot
{
    /// <summary>
    /// Conversions between the game's <c>WeaponModule</c>, the bag's <see cref="LootItem"/>,
    /// and the wire string peers exchange. The DTO round-trip is the one the recon proved
    /// exact; nothing here goes through <c>GetSaveString</c>.
    /// </summary>
    public static class WeaponCodec
    {
        // ASCII unit separator: never appears in a weapon name or a GUID.
        private const char Sep = '\u001F';

        public static LootItem FromModule(WeaponModule wm)
        {
            var dto = new PlayerData.WeaponModuleDTO(wm);
            var item = new LootItem
            {
                PrefabName = dto.prefabName,
                GenV = dto.genV,
                WeaponClass = dto.weaponClass,
                WeaponTier = dto.weaponTier,
                RandomSeed = dto.randomSeed,
                WeaponGuid = dto.guid,
                WeaponStyle = dto.weaponStyle,
                NameIDs = ToManaged(dto.nameIDs),
                DtoName = dto.name,
                ModuleName = dto.moduleName,
                ModuleType = dto.moduleType,
                PropType = (int)wm.GetWeaponType(),
                Name = wm.GetDisplayName(false),
                ColoredName = wm.GetDisplayName(true),
            };
            item.Weight = LootTables.Weight(item.PropType, item.WeaponTier);
            try
            {
                var def = WeaponFactory.GetRandomWeaponStats(wm.GetWeaponType(), wm.GetWeaponClass(), wm.GetWeaponTier(), wm.GetWeaponStyle(), wm.GetRandomSeed());
                item.Value = def == null ? 0 : def.salvageValue;
            }
            catch { item.Value = 0; }
            return item;
        }

        public static WeaponModule ToModule(LootItem item)
        {
            if (item.Manual)
            {
                var m = new PlayerData.ManualWeaponDTO
                {
                    prefabName = item.PrefabName, genV = item.GenV, weaponClass = item.WeaponClass, weaponTier = item.WeaponTier,
                    randomSeed = item.RandomSeed, guid = item.WeaponGuid, weaponStyle = item.WeaponStyle, nameIDs = ToIl2Cpp(item.NameIDs),
                    name = item.DtoName, moduleName = item.ModuleName, moduleType = item.ModuleType,
                    perkA = item.PerkA, perkB = item.PerkB, perkC = item.PerkC, damageMin = item.DamageMin, damageType = item.DamageType, superior = item.Superior,
                };
                return new WeaponModule(m);
            }
            var dto = new PlayerData.WeaponModuleDTO
            {
                prefabName = item.PrefabName,
                genV = item.GenV,
                weaponClass = item.WeaponClass,
                weaponTier = item.WeaponTier,
                randomSeed = item.RandomSeed,
                guid = item.WeaponGuid,
                weaponStyle = item.WeaponStyle,
                nameIDs = ToIl2Cpp(item.NameIDs),
                name = item.DtoName,
                moduleName = item.ModuleName,
                moduleType = item.ModuleType,
            };
            return new WeaponModule(dto);
        }

        /// <summary>Everything a peer needs to tag and later bag the item, without the generator.</summary>
        public static string Encode(LootItem i) => string.Join(Sep.ToString(), new[]
        {
            i.Id, i.PrefabName, S(i.GenV), S(i.WeaponClass), S(i.WeaponTier), S(i.RandomSeed), i.WeaponGuid ?? "",
            S(i.WeaponStyle), i.NameIDs == null ? "" : string.Join(",", i.NameIDs), i.DtoName ?? "", i.ModuleName ?? "",
            S(i.ModuleType), S(i.PropType), i.Name ?? "", i.ColoredName ?? "", S(i.Value), i.Weight.ToString("R", CultureInfo.InvariantCulture),
            S(i.FoundInRealm), i.FoundBy ?? "", i.Kind ?? "weapon", i.BuffStat ?? "", i.BuffMult.ToString("R", CultureInfo.InvariantCulture),
            i.Manual ? "1" : "0", S(i.PerkA), S(i.PerkB), S(i.PerkC), S(i.DamageMin), S(i.DamageType), i.Superior ? "1" : "0",
        });

        public static LootItem Decode(string s)
        {
            var p = s.Split(Sep);
            if (p.Length < 19) throw new FormatException($"loot record has {p.Length} fields");
            var item = new LootItem
            {
                Id = p[0], PrefabName = p[1], GenV = I(p[2]), WeaponClass = I(p[3]), WeaponTier = I(p[4]), RandomSeed = I(p[5]),
                WeaponGuid = p[6], WeaponStyle = I(p[7]), DtoName = p[9], ModuleName = p[10], ModuleType = I(p[11]),
                PropType = I(p[12]), Name = p[13], ColoredName = p[14], Value = I(p[15]),
                Weight = float.Parse(p[16], CultureInfo.InvariantCulture), FoundInRealm = I(p[17]), FoundBy = p[18],
            };
            if (p.Length > 19 && p[19].Length > 0) item.Kind = p[19];
            if (p.Length > 21) { item.BuffStat = p[20].Length > 0 ? p[20] : null; float.TryParse(p[21], System.Globalization.NumberStyles.Float, CultureInfo.InvariantCulture, out item.BuffMult); }
            if (p.Length > 28) { item.Manual = p[22] == "1"; item.PerkA = I(p[23]); item.PerkB = I(p[24]); item.PerkC = I(p[25]); item.DamageMin = I(p[26]); item.DamageType = I(p[27]); item.Superior = p[28] == "1"; }
            if (p[8].Length > 0)
            {
                var ids = p[8].Split(',');
                item.NameIDs = new int[ids.Length];
                for (var k = 0; k < ids.Length; k++) item.NameIDs[k] = I(ids[k]);
            }
            return item;
        }

        private static string S(int v) => v.ToString(CultureInfo.InvariantCulture);
        private static int I(string s) => int.Parse(s, CultureInfo.InvariantCulture);

        private static int[] ToManaged(Il2CppSystem.Collections.Generic.List<int> list)
        {
            if (list == null) return null;
            var a = new int[list.Count];
            for (var k = 0; k < a.Length; k++) a[k] = list[k];
            return a;
        }

        private static Il2CppSystem.Collections.Generic.List<int> ToIl2Cpp(int[] a)
        {
            var list = new Il2CppSystem.Collections.Generic.List<int>();
            if (a != null) foreach (var v in a) list.Add(v);
            return list;
        }
    }
}
