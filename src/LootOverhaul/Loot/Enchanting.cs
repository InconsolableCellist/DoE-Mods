using System;
using System.Collections.Generic;
using Il2Cpp;
using LootOverhaul.Gate;
using LootOverhaul.Recon;
using Interop = LootOverhaul.Recon.Interop;

namespace LootOverhaul.Loot
{
    /// <summary>
    /// The enchanting table, on the game's own "manual" weapon module: a module that carries
    /// chosen perks, an element and a damage figure explicitly (the mythic path uses it; the
    /// game networks it as 11 values). Enchanting a bag weapon writes a new manual record
    /// with one more perk or an element, for gold and a reagent (a curio or artifact from
    /// the junk pile). Slots by rarity: Common 1, Unique 2, Rare 2, Legendary 3.
    ///
    /// The one unknown is how the game names a manual module. <see cref="SelfTest"/> builds
    /// candidates in the lobby, asks the game to serialise each, and keeps the first whose
    /// networked packet has the manual length; until one passes, the table is closed and
    /// the transcript says exactly what each candidate produced.
    /// </summary>
    public static class Enchanting
    {
        public const int ManualPacketLength = 11;
        public static string ManualNameFormat;     // "{0}" = base moduleName, "{1}" = prefab name; null = not yet known
        public static bool Ready => ManualNameFormat != null;
        public static string SelfTestReport = "not run";
        private static bool _tested;

        public static readonly string[] Elements = { "Fire", "Ice", "Poison" };

        // Perk ids from WeaponFactory.WeaponPerk. Generic ones apply to every type.
        private static readonly (int id, string name)[] GenericPerks =
        {
            (10, "Power"), (11, "Criticals"), (20, "Shatter"), (21, "Vanquish"), (22, "Exterminate"), (23, "Banish"), (24, "Dismantle"), (25, "Elite Slayer"),
        };
        private static readonly Dictionary<int, (int id, string name)[]> TypePerks = new Dictionary<int, (int, string)[]>
        {
            { LootTables.Sword,     new[] { (40, "Vampire"), (41, "Spire"), (42, "Pierce") } },
            { LootTables.Axe,       new[] { (50, "Distance"), (51, "Might"), (52, "Explode") } },
            { LootTables.Hammer,    new[] { (60, "Distance"), (61, "Slow"), (62, "Smash") } },
            { LootTables.Dagger,    new[] { (70, "Distance"), (71, "Vampire"), (72, "Poison") } },
            { LootTables.Bow,       new[] { (80, "Farshot"), (81, "Slow") } },
            { LootTables.Crossbow,  new[] { (90, "Slow"), (91, "Reload") } },
            { LootTables.Shield,    new[] { (100, "Knockback"), (101, "Absorb") } },
            { LootTables.Longsword, new[] { (110, "Knockback"), (111, "Vampire"), (112, "Smash"), (113, "Pierce") } },
            { LootTables.LongAxe,   new[] { (120, "Knockback"), (121, "Distance"), (122, "Might"), (123, "Explode") } },
            { LootTables.Spear,     new[] { (130, "Distance"), (131, "Might"), (132, "Explode") } },
        };

        public static int Slots(int weaponClass) => weaponClass switch { 0 => 1, 1 => 2, 2 => 2, _ => 3 };

        /// <summary>Gold cost of one enchantment by rarity, before the shop multiplier.</summary>
        public static int Price(LootItem item) => (int)Math.Round((item.WeaponClass switch { 0 => 150, 1 => 300, 2 => 600, _ => 1200 }) * ModConfig.ShopPriceMultiplier.Value);

        /// <summary>Reagent tier needed: a curio for Common/Unique, an artifact for Rare/Legendary.</summary>
        public static int ReagentTier(LootItem item) => item.WeaponClass >= 2 ? 2 : 1;

        public static string PerkName(int id)
        {
            foreach (var p in GenericPerks) if (p.id == id) return p.name;
            foreach (var kv in TypePerks) foreach (var p in kv.Value) if (p.id == id) return p.name;
            return id == 0 ? "" : $"perk {id}";
        }

        /// <summary>What may still be added to this weapon: perks valid for its type it does not have, and an element if it has none.</summary>
        public static List<(string label, int perkId, int element)> Options(LootItem item)
        {
            var list = new List<(string, int, int)>();
            var have = new HashSet<int> { item.PerkA, item.PerkB, item.PerkC };
            if (UsedSlots(item) < Slots(item.WeaponClass))
            {
                foreach (var p in GenericPerks) if (!have.Contains(p.id)) list.Add((p.name, p.id, -1));
                if (TypePerks.TryGetValue(item.PropType, out var tp)) foreach (var p in tp) if (!have.Contains(p.id)) list.Add((p.name, p.id, -1));
            }
            if (item.DamageType < 0) for (var e = 0; e < Elements.Length; e++) list.Add((Elements[e], 0, e));
            return list;
        }

        public static int UsedSlots(LootItem item) => (item.PerkA > 0 ? 1 : 0) + (item.PerkB > 0 ? 1 : 0) + (item.PerkC > 0 ? 1 : 0);

        /// <summary>The cheapest junk item of at least the needed tier, or null.</summary>
        public static LootItem FindReagent(LootInventory inv, int tier)
        {
            LootItem best = null;
            foreach (var j in inv.Items)
                if (!j.IsWeapon && !j.IsBuff && j.WeaponClass >= tier && (best == null || j.Value < best.Value)) best = j;
            return best;
        }

        /// <summary>Apply one enchantment. Returns the new item (the old one is replaced in the bag) or null with a toast.</summary>
        public static LootItem Enchant(LootItem item, int perkId, int element)
        {
            var inv = BagManager.Inventory;
            if (!Ready) { BagManager.Toast("The table is cold: the game has not accepted a manual weapon yet (see the log)."); return null; }
            if (!ModGate.Active) { BagManager.Toast("Not in a modded room."); return null; }
            var live = inv.Find(item.Id);
            if (live == null || !live.IsWeapon) { BagManager.Toast("That weapon is gone."); return null; }
            if (live.EquippedSlot >= 0) { BagManager.Toast("Unequip it at the pedestal first."); return null; }
            var price = Price(live);
            if (inv.Gold < price) { BagManager.Toast($"Enchanting costs {price} gold; you have {inv.Gold}."); return null; }
            var reagent = FindReagent(inv, ReagentTier(live));
            if (reagent == null) { BagManager.Toast($"Needs a {LootTables.JunkTierName(ReagentTier(live))} from your junk as a reagent."); return null; }

            var enchanted = Clone(live);
            enchanted.Id = Guid.NewGuid().ToString("N");
            enchanted.Manual = true;
            if (perkId > 0)
            {
                if (enchanted.PerkA <= 0) enchanted.PerkA = perkId;
                else if (enchanted.PerkB <= 0) enchanted.PerkB = perkId;
                else if (enchanted.PerkC <= 0) enchanted.PerkC = perkId;
                else { BagManager.Toast("No free slot."); return null; }
            }
            if (element >= 0) enchanted.DamageType = element;
            enchanted.ModuleName = string.Format(ManualNameFormat, live.ModuleName, live.PrefabName);
            enchanted.WeaponGuid = Guid.NewGuid().ToString("N");

            // Ask the game to build it; if it cannot, nothing is spent.
            try
            {
                var wm = WeaponCodec.ToModule(enchanted);
                var prefab = WeaponModule.GetWeaponPrefabData(wm, out var data);
                if (data == null || data.Length != ManualPacketLength) { BagManager.Toast("The game refused that enchantment (see the log)."); ReconLog.Line($"enchant: packet length {(data == null ? -1 : data.Length)} for {enchanted.ModuleName}"); return null; }
                enchanted.Name = wm.GetDisplayName(false).Replace(FabricatorBridge.Marker, "");
                enchanted.ColoredName = wm.GetDisplayName(true).Replace(FabricatorBridge.Marker, "");
                try { var def = WeaponFactory.GetRandomWeaponStats(wm.GetWeaponType(), wm.GetWeaponClass(), wm.GetWeaponTier(), wm.GetWeaponStyle(), wm.GetRandomSeed()); enchanted.Value = def == null ? live.Value : (int)Math.Round(def.salvageValue * 1.5f); } catch { }
            }
            catch (Exception e) { BagManager.Toast("The game refused that enchantment (see the log)."); Core.Log.Warning($"Enchant failed: {e.GetType().Name}: {e.Message}"); return null; }

            inv.Gold -= price;
            inv.Remove(reagent.Id);
            inv.Remove(live.Id);
            inv.Items.Add(enchanted);
            inv.Save();
            var what = perkId > 0 ? PerkName(perkId) : Elements[element];
            BagManager.Toast($"Enchanted: {enchanted.ColoredName} gains <b>{what}</b>  (−{price} gold, −{reagent.Name})");
            ReconLog.Line($"enchant: {live.Name} + {what} -> {enchanted.Name} [{enchanted.ModuleName}] perks {enchanted.PerkA}/{enchanted.PerkB}/{enchanted.PerkC} element {enchanted.DamageType}; paid {price} + {reagent.Name}");
            BagPanel.Refresh(); Booth.Refresh();
            return enchanted;
        }

        private static LootItem Clone(LootItem a)
        {
            return new LootItem
            {
                Kind = a.Kind, PrefabName = a.PrefabName, GenV = a.GenV, WeaponClass = a.WeaponClass, WeaponTier = a.WeaponTier,
                RandomSeed = a.RandomSeed, WeaponGuid = a.WeaponGuid, WeaponStyle = a.WeaponStyle, NameIDs = a.NameIDs, DtoName = a.DtoName,
                ModuleName = a.ModuleName, ModuleType = a.ModuleType, PropType = a.PropType, Name = a.Name, ColoredName = a.ColoredName,
                Weight = a.Weight, Value = a.Value, FoundInRealm = a.FoundInRealm, FoundAt = a.FoundAt, FoundBy = a.FoundBy, Source = a.Source,
                Manual = a.Manual, PerkA = a.PerkA, PerkB = a.PerkB, PerkC = a.PerkC, DamageMin = a.DamageMin, DamageType = a.DamageType, Superior = a.Superior,
            };
        }

        /// <summary>Read the perks/element the game rolled for a weapon, so the table knows what it already has.</summary>
        public static void ReadRolledPerks(LootItem item)
        {
            if (!item.IsWeapon) return;
            try
            {
                var wm = WeaponCodec.ToModule(item);
                var def = WeaponFactory.GetRandomWeaponStats(wm.GetWeaponType(), wm.GetWeaponClass(), wm.GetWeaponTier(), wm.GetWeaponStyle(), wm.GetRandomSeed());
                if (def == null) return;
                if (!item.Manual)
                {
                    item.PerkA = (int)def.primaryPerk; item.PerkB = (int)def.secondaryPerk; item.PerkC = 0;
                    item.DamageType = (int)def.elemental; item.DamageMin = def.damage; item.Superior = def.isSuperior;
                }
            }
            catch { }
        }

        /// <summary>
        /// Run once in the lobby: build a manual module from a generated sword under several
        /// naming conventions and see which one the game serialises as manual (11 values).
        /// </summary>
        public static void SelfTest()
        {
            if (_tested || !ModGate.Active) return;
            _tested = true;
            ProfileWatch.Probe = "enchanting self-test";
            try
            {
                ReconLog.Section("Enchanting self-test");
                var wm = WeaponFactory.GenerateRandomWeaponModuleForLocalPlayer(WeaponFactory.WeaponClass.Rare, (Prop.Type)LootTables.Sword,
                    (WeaponFactory.WeaponTier)1, (WeaponFactory.WeaponStyle)(-1), -1, WeaponFactory.SeasonalKey.None);
                var baseDto = new PlayerData.WeaponModuleDTO(wm);
                var basePrefab = WeaponModule.GetWeaponPrefabData(wm, out var baseData);
                ReconLog.Line($"base random module: name=`{baseDto.name}` moduleName=`{baseDto.moduleName}` moduleType={baseDto.moduleType} prefab=`{basePrefab}` packet={(baseData == null ? -1 : baseData.Length)} display=`{Interop.OneLine(wm.GetDisplayName(false))}`");
                var mythicSample = "";
                try
                {
                    var perks = new Il2CppSystem.Collections.Generic.List<WeaponFactory.WeaponPerk>();
                    perks.Add(WeaponFactory.WeaponPerk.AttackPower);
                    var my = WeaponFactory.GenerateMythicWeaponModuleForLocalPlayer((Prop.Type)LootTables.Sword, perks);
                    if (my != null) { var md = new PlayerData.MythicWeaponDTO(my); var mp = WeaponModule.GetWeaponPrefabData(my, out var mdata); mythicSample = $"mythic sample: moduleName=`{md.moduleName}` name=`{md.name}` prefab=`{mp}` packet={(mdata == null ? -1 : mdata.Length)}"; }
                }
                catch (Exception e) { mythicSample = $"mythic sample failed: {e.GetType().Name}"; }
                ReconLog.Line(mythicSample);

                var candidates = new[]
                {
                    ("{0}", "base name unchanged"), ("{0}_Manual", "base + _Manual"), ("{1}_Manual", "prefab + _Manual"),
                    ("Manual", "just Manual"), ("{0}Manual", "base + Manual"), ("Manual_{0}", "Manual_ + base"),
                };
                string winner = null;
                foreach (var (fmt, label) in candidates)
                {
                    try
                    {
                        var m = new PlayerData.ManualWeaponDTO
                        {
                            name = baseDto.name, moduleName = string.Format(fmt, baseDto.moduleName, baseDto.prefabName), moduleType = baseDto.moduleType,
                            prefabName = baseDto.prefabName, genV = baseDto.genV, weaponClass = baseDto.weaponClass, weaponTier = baseDto.weaponTier,
                            randomSeed = baseDto.randomSeed, guid = Guid.NewGuid().ToString("N"), weaponStyle = baseDto.weaponStyle, nameIDs = baseDto.nameIDs,
                            perkA = 40, perkB = 10, perkC = 0, damageMin = 20, damageType = 0, superior = false,
                        };
                        var wm2 = new WeaponModule(m);
                        var prefab = WeaponModule.GetWeaponPrefabData(wm2, out var data);
                        var len = data == null ? -1 : data.Length;
                        string perkA = "?", dmgType = "?";
                        try { perkA = wm2.GetData(WeaponModule.Keys.PerkA)?.ToString(); dmgType = wm2.GetData(WeaponModule.Keys.DamageType)?.ToString(); } catch { }
                        var stats = Interop.OneLine(wm2.GetStatsText());
                        ReconLog.Line($"- {label} (`{m.moduleName}`): packet={len} perkA={perkA} damageType={dmgType} display=`{Interop.OneLine(wm2.GetDisplayName(false))}` stats=`{stats}`");
                        if (winner == null && len == ManualPacketLength) winner = fmt;
                    }
                    catch (Exception e) { ReconLog.Line($"- {label}: threw {e.GetType().Name}: {e.Message}"); }
                }
                ManualNameFormat = winner;
                SelfTestReport = winner == null ? "no naming produced an 11-value packet" : $"manual naming `{winner}` works";
                ReconLog.Headline($"Enchanting self-test: {SelfTestReport}.");
            }
            catch (Exception e) { SelfTestReport = $"threw {e.GetType().Name}"; ReconLog.Error("enchanting self-test", e); }
            finally { ProfileWatch.Probe = null; }
        }
    }
}
