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
    /// with one more perk or an element, for tokens alone (0.9.15; until then a curio or
    /// artifact from the junk pile was consumed too, and the price was half). An equipped
    /// weapon may be enchanted in place: the new record takes its slot and goes into the
    /// hand at once. Slots by rarity: Common 1, Unique 2, Rare 2, Legendary 3.
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

        /// <summary>
        /// Token cost of one enchantment by rarity: the 0.9.14 price doubled, since the reagent
        /// (a curio or artifact from the junk pile) is no longer consumed; times the shop
        /// multiplier and <c>EnchantCostMultiplier</c>.
        /// </summary>
        public static int Price(LootItem item) => Math.Max(1, (int)Math.Round((item.WeaponClass switch { 0 => 300, 1 => 600, 2 => 1200, _ => 2400 })
            * ModConfig.ShopPriceMultiplier.Value * Math.Max(0f, ModConfig.EnchantCostMultiplier.Value)));

        public static string PerkName(int id)
        {
            foreach (var p in GenericPerks) if (p.id == id) return p.name;
            foreach (var kv in TypePerks) foreach (var p in kv.Value) if (p.id == id) return p.name;
            return id == 0 ? "" : $"perk {id}";
        }

        /// <summary>
        /// One line of the table's HELP page: the mod's short perk name, the game's own name for
        /// it, what the game says it does, and which weapon types can carry it. The text is the
        /// game's (its language pack, `perk.&lt;id&gt;.name` / `.description`, read 2026-09-12);
        /// at runtime the current language is asked first and the English is the fallback.
        /// </summary>
        public class PerkDoc
        {
            public string Name;         // the mod's short name, as on the buttons
            public string Key;          // the game's localisation key stem, e.g. "perk.swordvampire"
            public string GameName;     // English name from the game's language pack
            public string Description;  // English description from the game's language pack
            public string Types;        // "every weapon" or a list of types
            public string Title => GameName == Name ? Name : $"{Name} <color=#9A9A9A>({GameName})</color>";
        }

        private static readonly PerkDoc[] Docs =
        {
            new PerkDoc { Name = "Power",        Key = "perk.attackpower",      GameName = "Power",              Description = "Increases damage by 5-35%", Types = "every weapon" },
            new PerkDoc { Name = "Criticals",    Key = "perk.attackcritical",   GameName = "Criticals",          Description = "Gives 20-32% chance to critically strike for 300% damage", Types = "every weapon" },
            new PerkDoc { Name = "Shatter",      Key = "perk.enemyshatter",     GameName = "Undead Damage",      Description = "Increases damage by 5-35% to undead enemies", Types = "every weapon" },
            new PerkDoc { Name = "Vanquish",     Key = "perk.enemyvanquish",    GameName = "Monster Damage",     Description = "Increases damage by 5-35% to monsters", Types = "every weapon" },
            new PerkDoc { Name = "Exterminate",  Key = "perk.enemyexterminate", GameName = "Critter Damage",     Description = "Increases damage by 5-35% to critters", Types = "every weapon" },
            new PerkDoc { Name = "Banish",       Key = "perk.enemybanish",      GameName = "Sorcerer Damage",    Description = "Increases damage by 5-35% to sorcerer enemies", Types = "every weapon" },
            new PerkDoc { Name = "Dismantle",    Key = "perk.enemydismantle",   GameName = "Elemental Damage",   Description = "Increases damage by 5-35% to elemental enemies", Types = "every weapon" },
            new PerkDoc { Name = "Elite Slayer", Key = "perk.enemyelite",       GameName = "Elite Damage",       Description = "Increases damage by 5-35% to elite enemies", Types = "every weapon" },
            new PerkDoc { Name = "Vampire",      Key = "perk.swordvampire",     GameName = "Vampire",            Description = "Gives 20-32% chance to heal 25% of player health", Types = "sword, dagger, longsword" },
            new PerkDoc { Name = "Spire",        Key = "perk.swordspire",       GameName = "Throwable",          Description = "Allows sword to be thrown", Types = "sword" },
            new PerkDoc { Name = "Pierce",       Key = "perk.swordpierce",      GameName = "Stab Damage",        Description = "Increases damage by 50%", Types = "sword, longsword" },
            new PerkDoc { Name = "Distance",     Key = "perk.axedistance",      GameName = "Throw Distance",     Description = "Increases throwing range by 10-70%", Types = "axe, hammer, dagger, long axe, spear" },
            new PerkDoc { Name = "Might",        Key = "perk.axemight",         GameName = "Throw Damage",       Description = "Increases throwing damage by 5-35%", Types = "axe, long axe, spear" },
            new PerkDoc { Name = "Explode",      Key = "perk.axeexplode",       GameName = "Explosions",         Description = "Gives 20-32% chance to explode for 200% damage", Types = "axe, long axe, spear" },
            new PerkDoc { Name = "Slow",         Key = "perk.hammerslow",       GameName = "Slowing",            Description = "Gives 20-32% chance to slow enemy", Types = "hammer, bow, crossbow" },
            new PerkDoc { Name = "Smash",        Key = "perk.hammersmash",      GameName = "Area Damage",        Description = "Gives 20-32% chance to explode for 200% damage", Types = "hammer" },
            new PerkDoc { Name = "Smash",        Key = "perk.longswordsmash",   GameName = "Unblockable",        Description = "Attacks cannot be blocked", Types = "longsword" },
            new PerkDoc { Name = "Poison",       Key = "perk.daggerpoison",     GameName = "Poison",             Description = "Gives 20-32% chance to poison enemy", Types = "dagger" },
            new PerkDoc { Name = "Farshot",      Key = "perk.bowfarshot",       GameName = "Shot Distance",      Description = "Increases shooting range by 10-70%", Types = "bow" },
            new PerkDoc { Name = "Reload",       Key = "perk.crossbowreload",   GameName = "Reload",             Description = "Gives 1-7 extra shots per reload", Types = "crossbow" },
            new PerkDoc { Name = "Knockback",    Key = "perk.shieldknockback",  GameName = "Knockback Distance", Description = "Increases shield knockback by 10-70%", Types = "shield" },
            new PerkDoc { Name = "Knockback",    Key = "perk.knockback",        GameName = "Knockback",          Description = "Increases knockback by 10-70%", Types = "longsword, long axe" },
            new PerkDoc { Name = "Absorb",       Key = "perk.shieldabsorb",     GameName = "Absorb",             Description = "Heals 4-29% of player health when blocking", Types = "shield" },
        };

        /// <summary>The elements, described in the mod's words: the game names them and colours them but has no perk text for them.</summary>
        private static readonly PerkDoc[] ElementDocs =
        {
            new PerkDoc { Name = "Fire",   Key = "", GameName = "Fire",   Description = "Hits set the enemy burning for damage over time", Types = "any weapon without an element" },
            new PerkDoc { Name = "Ice",    Key = "", GameName = "Ice",    Description = "Hits chill the enemy, slowing it", Types = "any weapon without an element" },
            new PerkDoc { Name = "Poison", Key = "", GameName = "Poison", Description = "Hits poison the enemy for damage over time", Types = "any weapon without an element" },
        };

        /// <summary>Every enchantment the table offers, documented: the perks (deduplicated by what they do), then the elements.</summary>
        public static List<PerkDoc> Documentation()
        {
            var list = new List<PerkDoc>();
            foreach (var d in Docs) list.Add(Localized(d));
            foreach (var d in ElementDocs) list.Add(d);
            return list;
        }

        /// <summary>The same entry in the game's current language when it has one; the English otherwise.</summary>
        private static PerkDoc Localized(PerkDoc d)
        {
            if (string.IsNullOrEmpty(d.Key)) return d;
            try
            {
                var name = LocalizationManager.GetLocalizedText(d.Key + ".name", d.GameName);
                var desc = LocalizationManager.GetLocalizedText(d.Key + ".description", d.Description);
                if (string.IsNullOrWhiteSpace(name) || name.Contains("{0}")) name = d.GameName;   // "Reload: {0} Shots" is a format string
                if (string.IsNullOrWhiteSpace(desc)) desc = d.Description;
                return new PerkDoc { Name = d.Name, Key = d.Key, GameName = name.Trim(), Description = desc.Trim(), Types = d.Types };
            }
            catch { return d; }
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

        /// <summary>
        /// Apply one enchantment. Returns the new item (the old one is replaced in the bag) or
        /// null with a toast. An equipped weapon keeps its slot: the new record is put into the
        /// loadout and the hand straight away (report 2026-09-12: "unequip it first" was the
        /// most common thing the table said).
        /// </summary>
        public static LootItem Enchant(LootItem item, int perkId, int element)
        {
            var inv = BagManager.Inventory;
            if (!Ready) { BagManager.Toast("Enchanting is unavailable in this game version."); return null; }
            if (!ModGate.Active) { BagManager.Toast("Not in a modded room."); return null; }
            var live = inv.Find(item.Id);
            if (live == null || !live.IsWeapon) { BagManager.Toast("That's gone."); return null; }
            var price = Price(live);
            if (inv.Gold < price) { BagManager.Toast($"Enchanting costs {price} tokens; you have {inv.Gold}."); return null; }
            var slot = inv.EquippedSlotOf(live);

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
                if (data == null || data.Length != ManualPacketLength) { BagManager.Toast("That enchantment didn't take."); ReconLog.Line($"enchant: packet length {(data == null ? -1 : data.Length)} for {enchanted.ModuleName}"); return null; }
                enchanted.Name = wm.GetDisplayName(false).Replace(FabricatorBridge.Marker, "");
                enchanted.ColoredName = wm.GetDisplayName(true).Replace(FabricatorBridge.Marker, "");
                try { var def = WeaponFactory.GetRandomWeaponStats(wm.GetWeaponType(), wm.GetWeaponClass(), wm.GetWeaponTier(), wm.GetWeaponStyle(), wm.GetRandomSeed()); enchanted.Value = def == null ? live.Value : (int)Math.Round(def.salvageValue * 1.5f); } catch { }
            }
            catch (Exception e) { BagManager.Toast("That enchantment didn't take."); Core.Log.Warning($"Enchant failed: {e.GetType().Name}: {e.Message}"); return null; }

            inv.Gold -= price;
            enchanted.Locked = live.Locked;
            inv.Remove(live.Id);            // clears its loadout slot and retires the old GUID
            inv.Items.Add(enchanted);
            inv.Save();
            var what = perkId > 0 ? PerkName(perkId) : Elements[element];
            BagManager.Toast($"Enchanted: {enchanted.ColoredName} gains <b>{what}</b>  (−{price} tokens, now {inv.Gold})");
            ReconLog.Line($"enchant: {live.Name} + {what} -> {enchanted.Name} [{enchanted.ModuleName}] perks {enchanted.PerkA}/{enchanted.PerkB}/{enchanted.PerkC} element {enchanted.DamageType}; paid {price}; slot {slot}");
            // The armory rebuilt its lists when the old record left; the new one is in the bag now.
            try { FabricatorBridge.RefreshArmories($"{enchanted.Name} enchanted"); } catch { }
            if (slot >= 0) Loadout.Set(slot, enchanted, apply: true);
            BagPanel.Refresh();
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
                Locked = a.Locked,
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

                // Recon 2026-09-02: random modules are named `random`, mythics `mythic` — lowercase
                // serialized-type names. So `manual` first; the rest are fallbacks.
                var candidates = new[]
                {
                    ("manual", "manual (lowercase)"), ("Manual", "Manual"), ("{1}_manual", "prefab + _manual"),
                    ("{0}", "base name unchanged"), ("{0}_Manual", "base + _Manual"), ("MANUAL", "MANUAL"),
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
                        var stats = Interop.OneLine(wm2.GetStatsText());
                        ReconLog.Line($"- {label} (`{m.moduleName}`): packet={len} display=`{Interop.OneLine(wm2.GetDisplayName(false))}` stats=`{stats}`");
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
