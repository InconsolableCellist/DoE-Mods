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
    /// with one more perk or an element, for gold alone (0.9.15; until then a curio or
    /// artifact from the junk pile was consumed too, and the price was half). An equipped
    /// weapon may be enchanted in place: the new record takes its slot and goes into the
    /// hand at once. Slots by rarity: Common 1, Unique, Rare and Legendary 2 (the game keeps two perks).
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

        /// <summary>
        /// Perk slots by rarity: Common 1, Unique, Rare and Legendary 2. The game's weapon holds two
        /// perks (0.13.2: <c>Weapon.ApplyManualParameters</c> stores perkA and perkB and drops perkC,
        /// and <c>GetStatsText</c> lists two), so a third perk never showed and never worked.
        /// </summary>
        public static int Slots(int weaponClass) => weaponClass <= 0 ? 1 : 2;

        /// <summary>
        /// A third perk saved by an earlier version (Legendary had 3 slots before 0.13.2). The game
        /// ignores it while two perks sit in front of it; taking off an active perk moves it up.
        /// </summary>
        public static bool IsInactive(LootItem item, int perkId) => perkId > 0 && item.PerkC == perkId && item.PerkA > 0 && item.PerkB > 0;

        private static readonly int[] LootBase = { 300, 600, 1200, 2400 };
        // Stock weapons and de-enchanting (0.12.0) are priced against what a run earns now that
        // tokens came only from the end-screen payout and chest armor: about 1,300 tokens for a
        // typical solo run that keeps its Legendary (see the 0.12.0 CHANGELOG entry). One stock
        // enchantment is half a run (Common) to three runs (Legendary); removing one is 40% of that.
        private static readonly int[] StockBase = { 200, 400, 800, 1600 };
        /// <summary>Taking one perk, the element or Boomerang off costs half of adding one to that weapon (0.13.2).</summary>
        public const float DisenchantShare = 0.5f;

        private static int Scaled(int[] table, int cls, float mult)
            => Math.Max(1, (int)Math.Round(table[Math.Max(0, Math.Min(3, cls))] * ModConfig.ShopPriceMultiplier.Value * Math.Max(0f, mult)));

        /// <summary>
        /// Gold cost of one enchantment by rarity. Loot: the 0.9.14 price doubled, since the reagent
        /// (a curio or artifact from the junk pile) is no longer consumed, times the shop multiplier
        /// and <c>EnchantCostMultiplier</c>: 750 / 1,500 / 3,000 / 6,000 at the defaults. A stock
        /// weapon (bound or not yet): <see cref="StockPrice"/>.
        /// </summary>
        public static int Price(LootItem item) => item.IsBound ? StockPrice(item.WeaponClass) : Scaled(LootBase, item.WeaponClass, ModConfig.EnchantCostMultiplier.Value);

        /// <summary>One enchantment on a stock (armory) weapon: 500 / 1,000 / 2,000 / 4,000 at the defaults.</summary>
        public static int StockPrice(int weaponClass) => Scaled(StockBase, weaponClass, ModConfig.StockEnchantCostMultiplier.Value);

        /// <summary>
        /// Removing one perk, the element or Boomerang: half the price of adding one to that weapon
        /// (0.13.2), times <c>DisenchantCostMultiplier</c>. Bag weapons 375 / 750 / 1,500 / 3,000,
        /// armory weapons 250 / 500 / 1,000 / 2,000 at the defaults. Was a table of its own (200 to 1,600).
        /// </summary>
        public static int DisenchantPrice(LootItem item)
            => Math.Max(1, (int)Math.Round(Price(item) * DisenchantShare * Math.Max(0f, ModConfig.DisenchantCostMultiplier.Value)));

        /// <summary>
        /// Taking off a third perk that never worked (see <see cref="IsInactive"/>) is free, with no
        /// gold back.
        /// </summary>
        public static int RemovePrice(LootItem item, int perkId) => IsInactive(item, perkId) ? 0 : DisenchantPrice(item);

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
            list.Add(new PerkDoc { Name = Boomerang.Name, Key = "", GameName = Boomerang.Name,
                Description = "A thrown weapon flies back to your hand at the top of its flight, or when you pull an empty hand back while gripping. Grip to catch it",
                Types = "axe, dagger, spear, hammer, long axe, sword with Spire" });
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
                // 0.13.2: only perks the game itself can roll on this weapon type (no Criticals on a shield).
                var natural = NaturalPerks(item.PropType);
                foreach (var p in GenericPerks) if (!have.Contains(p.id) && (natural == null || natural.Contains(p.id))) list.Add((p.name, p.id, -1));
                if (TypePerks.TryGetValue(item.PropType, out var tp))
                    foreach (var p in tp) if (!have.Contains(p.id) && (natural == null || natural.Contains(p.id))) list.Add((p.name, p.id, -1));
            }
            if (item.DamageType < 0) for (var e = 0; e < Elements.Length; e++) list.Add((Elements[e], 0, e));
            return list;
        }

        private static readonly Dictionary<int, HashSet<int>> Natural = new Dictionary<int, HashSet<int>>();

        /// <summary>
        /// The perks the game's generator can roll on a weapon type: every <c>WeaponPerkDef</c> in the
        /// four rarity tables of <c>WeaponFactory</c> (common, unique, rare, legendary) that is valid for
        /// the type (<c>LookUpWeaponType</c>, <c>IsValidFor</c>). Read once per type and logged; null when
        /// the factory cannot be read (the table then offers what it did before, and says so in the log).
        /// </summary>
        public static HashSet<int> NaturalPerks(int propType)
        {
            if (Natural.TryGetValue(propType, out var cached)) return cached;
            HashSet<int> set = null;
            try
            {
                WeaponFactory wf = null;
                foreach (var f in UnityEngine.Resources.FindObjectsOfTypeAll<WeaponFactory>()) if (Interop.Alive(f)) { wf = f; break; }
                if (wf != null)
                {
                    var wt = WeaponFactory.LookUpWeaponType((Prop.Type)propType);
                    set = new HashSet<int>();
                    foreach (var gen in new[] { wf.common, wf.unique, wf.rare, wf.legendary })
                    {
                        if (gen == null) continue;
                        foreach (var list in new[] { gen.weaponPerks, gen.AllPerks })
                        {
                            if (list == null) continue;
                            foreach (var d in list) if (d != null && d.IsValidFor(wt)) set.Add((int)d.perk);
                        }
                    }
                    if (set.Count == 0) set = null;
                }
            }
            catch (Exception e) { Core.Log.Warning($"Enchanting: perk table unreadable: {e.GetType().Name}: {e.Message}"); set = null; }
            if (set == null) { Core.Log.Warning($"Enchanting: no game perk table for {LootTables.TypeName(propType)}; offering every perk the table knows."); return null; }
            Natural[propType] = set;
            var names = new List<string>(); foreach (var p in set) names.Add(PerkName(p));
            Core.Log.Msg($"Enchanting: {LootTables.TypeName(propType)} can roll {string.Join(", ", names)}");
            return set;
        }

        public static int UsedSlots(LootItem item) => (item.PerkA > 0 ? 1 : 0) + (item.PerkB > 0 ? 1 : 0) + (item.PerkC > 0 ? 1 : 0);

        /// <summary>What can be taken off this weapon: each perk it carries and its element.</summary>
        public static List<(string label, int perkId, int element)> Removable(LootItem item)
        {
            var list = new List<(string, int, int)>();
            foreach (var p in new[] { item.PerkA, item.PerkB, item.PerkC }) if (p > 0) list.Add((PerkName(p), p, -1));
            if (item.DamageType >= 0) list.Add((ElementName(item.DamageType), 0, item.DamageType));
            if (item.Boomerang) list.Add((Boomerang.Name, BoomerangId, -1));
            return list;
        }

        /// <summary>Stands for Boomerang in <see cref="Removable"/>; not a game perk id.</summary>
        public const int BoomerangId = -100;

        public static string ElementName(int damageType) => damageType < 0 ? "none" : Elements[Math.Min(Elements.Length - 1, damageType)];

        /// <summary>
        /// Apply one enchantment. Returns the new item (the old one is replaced in the bag) or
        /// null with a toast. An equipped weapon keeps its slot: the new record is put into the
        /// loadout and the hand straight away (report 2026-09-12: "unequip it first" was the
        /// most common thing the table said). <paramref name="item"/> may be a stock weapon from
        /// <see cref="StockWeapons"/>: the first enchantment binds it (see <see cref="LootItem.StockGuid"/>).
        /// </summary>
        public static LootItem Enchant(LootItem item, int perkId, int element)
        {
            var what = perkId > 0 ? PerkName(perkId) : ElementName(element);
            return Rework(item, Price(item), "enchant", "+ " + what, next =>
            {
                if (perkId > 0)
                {
                    if (next.PerkA == perkId || next.PerkB == perkId || next.PerkC == perkId) return "It already has that.";
                    if (UsedSlots(next) >= Slots(next.WeaponClass)) return "No free slot.";
                    var natural = NaturalPerks(next.PropType);
                    if (natural != null && !natural.Contains(perkId)) return $"A {LootTables.TypeName(next.PropType).ToLowerInvariant()} can't carry {PerkName(perkId)}.";
                    if (next.PerkA <= 0) next.PerkA = perkId;
                    else if (next.PerkB <= 0) next.PerkB = perkId;
                    else if (next.PerkC <= 0) next.PerkC = perkId;
                    else return "No free slot.";
                }
                if (element >= 0)
                {
                    if (next.DamageType >= 0) return "It already has an element.";
                    next.DamageType = element;
                }
                return null;
            }, newModule: true);
        }

        /// <summary>
        /// Remove one perk or the element, for <see cref="DisenchantPrice"/>. Rolled perks can be
        /// removed as well as added ones; the result is a new manual record like an enchantment.
        /// A stock weapon de-enchanted back to exactly what it had goes back to the armory.
        /// </summary>
        public static LootItem Disenchant(LootItem item, int perkId, int element)
        {
            if (perkId == BoomerangId) return SetBoomerang(item, false);
            var what = perkId > 0 ? PerkName(perkId) : ElementName(element);
            return Rework(item, RemovePrice(item, perkId), "de-enchant", "- " + what, next =>
            {
                if (perkId > 0)
                {
                    var perks = new List<int>();
                    var found = false;
                    foreach (var p in new[] { next.PerkA, next.PerkB, next.PerkC })
                    {
                        if (p <= 0) continue;
                        if (p == perkId && !found) { found = true; continue; }
                        perks.Add(p);
                    }
                    if (!found) return "It doesn't have that.";
                    next.PerkA = perks.Count > 0 ? perks[0] : 0;
                    next.PerkB = perks.Count > 1 ? perks[1] : 0;
                    next.PerkC = perks.Count > 2 ? perks[2] : 0;
                }
                if (element >= 0)
                {
                    if (next.DamageType < 0) return "It has no element.";
                    next.DamageType = -1;
                }
                return null;
            }, newModule: true);
        }

        /// <summary>
        /// The shared path for every change at the table. <paramref name="mutate"/> changes a copy
        /// and returns null, or a toast saying why not; nothing is spent until the game has built
        /// the result. <paramref name="newModule"/>: the change is in the weapon itself (a new
        /// manual record with a new GUID); otherwise only a mod-side flag changes.
        /// </summary>
        internal static LootItem Rework(LootItem item, int price, string verb, string what, Func<LootItem, string> mutate, bool newModule)
        {
            var inv = BagManager.Inventory;
            if (newModule && !Ready) { BagManager.Toast("Enchanting is unavailable in this game version."); return null; }
            if (!ModGate.Active) { BagManager.Toast("Not in a modded room."); return null; }
            LootItem live;
            bool inBag;
            int slot;
            if (item.IsBound && inv.Find(item.Id) == null)
            {
                // A stock weapon not bound yet: the preview from StockWeapons, never in the bag.
                if (FindBound(inv, item.StockGuid) != null) { BagManager.Toast("That weapon is already at the table."); return null; }
                live = item;
                inBag = false;
                slot = StockSlot(item.StockGuid);
            }
            else
            {
                live = inv.Find(item.Id);
                if (live == null || !live.IsWeapon) { BagManager.Toast("That's gone."); return null; }
                inBag = true;
                slot = inv.EquippedSlotOf(live);
            }
            ReadRolledPerks(live);
            if (!Wallet.CanAfford(price)) { BagManager.Toast($"That costs {UiKit.Money(price)}; you have {UiKit.Number(Wallet.Balance)}."); return null; }

            var next = Clone(live);
            var err = mutate(next);
            if (err != null) { BagManager.Toast(err); return null; }

            if (!newModule && inBag)
            {
                // A flag on the record only: changed in place, same id and GUID, so nothing is retired.
                if (live.IsBound && AtBase(next)) return ReturnToStock(live, price, verb, what, mutate, newModule);
                if (!Wallet.TrySpend(price, "That")) return null;
                mutate(live);
                inv.Save();
                Core.Log.Msg($"Enchanting: {verb} {live.Name} {what}; paid {price} gold{(live.IsBound ? " (stock)" : "")}");
                BagPanel.Refresh();
                return live;
            }

            next.Id = Guid.NewGuid().ToString("N");
            // A bound record never shares the armory weapon's GUID: the armory copy keeps its own.
            if (newModule || !inBag) next.WeaponGuid = Guid.NewGuid().ToString("N");
            if (newModule)
            {
                next.Manual = true;
                next.ModuleName = string.Format(ManualNameFormat, live.ModuleName, live.PrefabName);
            }
            if (inBag && next.IsBound && AtBase(next)) return ReturnToStock(live, price, verb, what, mutate, newModule);

            // Ask the game to build it; if it cannot, nothing is spent.
            try
            {
                var wm = WeaponCodec.ToModule(next);
                if (next.Manual)
                {
                    WeaponModule.GetWeaponPrefabData(wm, out var data);
                    if (data == null || data.Length != ManualPacketLength)
                    {
                        BagManager.Toast("That enchantment didn't take.");
                        Core.Log.Warning($"Enchanting: {verb} {what} refused, packet length {(data == null ? -1 : data.Length)} for {next.ModuleName}");
                        return null;
                    }
                }
                next.Name = wm.GetDisplayName(false).Replace(FabricatorBridge.Marker, "");
                next.ColoredName = wm.GetDisplayName(true).Replace(FabricatorBridge.Marker, "");
                if (newModule)
                    try { var def = WeaponFactory.GetRandomWeaponStats(wm.GetWeaponType(), wm.GetWeaponClass(), wm.GetWeaponTier(), wm.GetWeaponStyle(), wm.GetRandomSeed()); next.Value = def == null ? live.Value : (int)Math.Round(def.salvageValue * 1.5f); } catch { }
            }
            catch (Exception e) { BagManager.Toast("That enchantment didn't take."); Core.Log.Warning($"Enchanting: {verb} failed: {e.GetType().Name}: {e.Message}"); return null; }

            if (!Wallet.TrySpend(price, "That")) return null;
            if (next.IsBound) { next.Locked = true; next.Weight = 0f; }
            if (inBag) inv.Remove(live.Id);            // clears its loadout slot and retires the old GUID
            inv.Items.Add(next);
            inv.Save();
            var origin = inBag ? (live.IsBound ? "stock (bound)" : "bag") : "stock, now bound: the armory copy is hidden";
            Core.Log.Msg($"Enchanting: {verb} {live.Name} {what} -> {next.Name} [{next.ModuleName}] perks {next.PerkA}/{next.PerkB}/{next.PerkC} element {next.DamageType}; " +
                         $"paid {price} gold; {origin}; slot {(slot >= 0 ? Loadout.SlotNames[slot] : "none")}");
            // The armory rebuilt its lists when the old record left; the new one is in the bag now.
            try { FabricatorBridge.RefreshArmories($"{next.Name} {verb}ed"); } catch { }
            if (slot >= 0) Loadout.Set(slot, next, apply: true);
            BagPanel.Refresh();
            return next;
        }

        /// <summary>A bound stock weapon is back to exactly what it had when it was bound.</summary>
        private static bool AtBase(LootItem i)
        {
            var now = new List<int>(); var was = new List<int>();
            foreach (var p in new[] { i.PerkA, i.PerkB, i.PerkC }) if (p > 0) now.Add(p);
            foreach (var p in new[] { i.BasePerkA, i.BasePerkB, i.BasePerkC }) if (p > 0) was.Add(p);
            now.Sort(); was.Sort();
            if (now.Count != was.Count || i.DamageType != i.BaseDamageType || ExtraFlags(i)) return false;
            for (var k = 0; k < now.Count; k++) if (now[k] != was[k]) return false;
            return true;
        }

        /// <summary>Mod-side enchantments that are not in the weapon itself.</summary>
        private static bool ExtraFlags(LootItem i) => i.Boomerang;

        public static int BoomerangPrice(LootItem item) => Math.Max(1, (int)Math.Round(Price(item) * Math.Max(0f, ModConfig.BoomerangCostMultiplier.Value)));

        /// <summary>
        /// Add or remove Boomerang. It is a flag on the bag record, so the weapon, its GUID and
        /// its slot do not change (a stock weapon is bound by the first one, like any enchantment).
        /// Adding costs <see cref="BoomerangPrice"/>, removing <see cref="DisenchantPrice"/>.
        /// </summary>
        public static LootItem SetBoomerang(LootItem item, bool on)
        {
            if (on && !Boomerang.CanTake(item)) { BagManager.Toast("Only throwing weapons take Boomerang (a sword needs Spire)."); return null; }
            return Rework(item, on ? BoomerangPrice(item) : DisenchantPrice(item), on ? "enchant" : "de-enchant", (on ? "+ " : "- ") + Boomerang.Name, next =>
            {
                if (next.Boomerang == on) return on ? "It already has Boomerang." : "It doesn't have Boomerang.";
                next.Boomerang = on;
                return null;
            }, newModule: false);
        }

        /// <summary>
        /// The bound record is removed and the armory weapon, which was only hidden, is back: as it
        /// was, same GUID, nothing written to the profile. If the armory no longer has it (sold or
        /// salvaged in an unmodded session meanwhile), the record stays in the bag as plain loot.
        /// Returns the stock preview, so the booth can keep showing it.
        /// </summary>
        private static LootItem ReturnToStock(LootItem live, int price, string verb, string what, Func<LootItem, string> mutate, bool newModule)
        {
            var inv = BagManager.Inventory;
            var original = StockModule(live.StockGuid);
            if (original == null)
            {
                var name = live.Name;
                live.StockGuid = null;
                live.Locked = false;
                live.Weight = LootTables.Weight(live.PropType, live.WeaponTier);
                inv.Save();
                Core.Log.Warning($"Enchanting: {name} is back to its stock state but the armory no longer has the original; it stays in the bag as loot");
                // Carry on as an ordinary change on a loot weapon.
                return Rework(live, price, verb, what, mutate, newModule);
            }
            if (!Wallet.TrySpend(price, "That")) return null;
            var slot = inv.EquippedSlotOf(live);
            inv.Remove(live.Id);
            inv.Save();
            Core.Log.Msg($"Enchanting: {verb} {live.Name} {what} -> back to stock: bound record removed, armory weapon `{live.StockGuid}` shown again; paid {price} gold; slot {(slot >= 0 ? Loadout.SlotNames[slot] : "none")}");
            BagManager.Toast($"{live.ColoredName} is back in your armory as it was.");
            try { FabricatorBridge.RefreshArmories($"{live.Name} back to stock"); } catch { }
            if (slot >= 0) Loadout.ApplyVanilla(slot);
            BagPanel.Refresh();
            return StockPreview(original, live.StockGuid);
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
                Locked = a.Locked, StockGuid = a.StockGuid, BasePerkA = a.BasePerkA, BasePerkB = a.BasePerkB, BasePerkC = a.BasePerkC,
                BaseDamageType = a.BaseDamageType, Boomerang = a.Boomerang,
            };
        }

        // ---- stock (armory) weapons ------------------------------------------------------------

        /// <summary>A stock weapon the table can work on, with the battle slot the profile has it in (-1 none).</summary>
        public class StockEntry { public LootItem Item; public int Slot = -1; }

        private static readonly HashSet<string> SkippedLogged = new HashSet<string>();

        /// <summary>Normalised GUIDs of armory weapons that are bound at the table, so hidden from the game's gear list.</summary>
        public static HashSet<string> BoundStockSet(LootInventory inv)
        {
            var set = new HashSet<string>();
            foreach (var i in inv.Items) if (i.IsBound) set.Add(FabricatorBridge.Norm(i.StockGuid));
            return set;
        }

        public static LootItem FindBound(LootInventory inv, string stockGuid)
        {
            var g = FabricatorBridge.Norm(stockGuid);
            foreach (var i in inv.Items) if (i.IsBound && FabricatorBridge.Norm(i.StockGuid) == g) return i;
            return null;
        }

        /// <summary>
        /// The armory's own weapons (the profile's unlocked list without the bag's, plus loadout
        /// weapons the list leaves out, like starter weapons), as previews the table can show and
        /// enchant. Read-only: nothing here touches the profile. Mythics and anything that is not
        /// a generated ("random") module are left out; the log names what was skipped, once.
        /// </summary>
        public static List<StockEntry> StockWeapons()
        {
            var inv = BagManager.Inventory;
            var list = new List<StockEntry>();
            var seen = new HashSet<string>();
            var bound = BoundStockSet(inv);
            void Consider(WeaponModule wm)
            {
                if (wm == null || FabricatorBridge.IsBagWeapon(wm)) return;
                string guid;
                try { guid = wm.GetGuid().ToString(); } catch { return; }
                var norm = FabricatorBridge.Norm(guid);
                if (norm.Length == 0 || !seen.Add(norm) || bound.Contains(norm) || FabricatorBridge.IsRetired(norm)) return;
                var item = StockPreview(wm, guid);
                if (item != null) list.Add(new StockEntry { Item = item, Slot = StockSlot(guid) });
            }
            try
            {
                var unlocked = PlayerProfile.GetUnlockedWeapons();
                if (unlocked != null) for (var i = 0; i < unlocked.Count; i++) { try { Consider(unlocked[i]); } catch { } }
            }
            catch (Exception e) { Core.Log.Warning($"Enchanting: armory read failed: {e.GetType().Name}: {e.Message}"); }
            for (var s = 0; s < 3; s++)
            {
                try
                {
                    var data = PlayerProfile.GetLoadoutData(Loadout.SlotValue(s))?.ToString();
                    if (string.IsNullOrEmpty(data) || !data.StartsWith("#")) continue;
                    Consider(PlayerProfile.GetWeaponModule(new Il2CppSystem.Guid(data.Substring(1))));
                }
                catch { }
            }
            return list;
        }

        /// <summary>A preview record for a stock weapon: what it would be in the bag once bound. Not in the bag.</summary>
        public static LootItem StockPreview(WeaponModule wm, string guid)
        {
            LootItem item;
            try
            {
                if ((int)wm.GetWeaponClass() > 3) { SkipOnce(guid, "mythic"); return null; }
                var dto = new PlayerData.WeaponModuleDTO(wm);
                if (!string.Equals(dto.moduleName, "random", StringComparison.OrdinalIgnoreCase)) { SkipOnce(guid, $"module `{dto.moduleName}`"); return null; }
                item = WeaponCodec.FromModule(wm);
            }
            catch (Exception e) { SkipOnce(guid, $"unreadable ({e.GetType().Name})"); return null; }
            item.Id = "stock-" + FabricatorBridge.Norm(guid);
            item.StockGuid = guid;
            item.Source = "loot";
            item.FoundBy = "armory";
            item.Weight = 0f;
            item.Locked = true;
            ReadRolledPerks(item);
            item.BasePerkA = item.PerkA; item.BasePerkB = item.PerkB; item.BasePerkC = item.PerkC;
            item.BaseDamageType = item.DamageType;
            return item;
        }

        private static void SkipOnce(string guid, string why)
        {
            if (SkippedLogged.Add(guid ?? "")) Core.Log.Msg($"Enchanting: stock weapon `{guid}` left off the table: {why}");
        }

        /// <summary>The armory weapon itself, from the profile, or null when it is no longer there.</summary>
        public static WeaponModule StockModule(string guid)
        {
            try
            {
                var wm = PlayerProfile.GetWeaponModule(new Il2CppSystem.Guid(FabricatorBridge.Norm(guid)));
                if (wm == null) return null;
                return FabricatorBridge.Norm(wm.GetGuid().ToString()) == FabricatorBridge.Norm(guid) ? wm : null;
            }
            catch { return null; }
        }

        /// <summary>The battle slot the profile has this stock weapon in, where no loot weapon overrides it; -1 otherwise.</summary>
        public static int StockSlot(string guid)
        {
            var inv = BagManager.Inventory;
            var g = FabricatorBridge.Norm(guid);
            for (var s = 0; s < 3; s++)
            {
                if (inv.Loadout[s] != null) continue;
                try
                {
                    var data = PlayerProfile.GetLoadoutData(Loadout.SlotValue(s))?.ToString();
                    if (!string.IsNullOrEmpty(data) && data.StartsWith("#") && FabricatorBridge.Norm(data.Substring(1)) == g) return s;
                }
                catch { }
            }
            return -1;
        }

        /// <summary>Why a locked record cannot leave the bag, for the toasts.</summary>
        public static string LockedReason(LootItem i) => i.IsBound
            ? $"{i.ColoredName} is a stock weapon at the enchanting table. De-enchant it at the kobold to put it back in your armory."
            : $"{i.ColoredName} is locked. Unlock it at the kobold first.";

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
