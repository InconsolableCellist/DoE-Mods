using System;
using System.Collections.Generic;
using Il2Cpp;
using LootOverhaul.Recon;

namespace LootOverhaul.Loot
{
    /// <summary>
    /// The broker's stock (milestone L5): a handful of generated weapons at the player's
    /// loot tier, priced at the game's own cost figure, bought with mod gold into the bag.
    /// Stock is per player, persisted in the inventory file, and refreshed when it is older
    /// than <c>ShopRefreshMinutes</c> or when the player pays to restock. Generation uses the
    /// same generator as drops, so a shop weapon is a vanilla-legal weapon like any other.
    /// </summary>
    public static class Shop
    {
        private static readonly System.Random Rng = new System.Random();
        public static int Bought, Restocks;

        public static bool Stale(LootInventory inv) =>
            inv.ShopStock == null || inv.ShopStock.Count == 0 ||
            (DateTime.UtcNow - inv.ShopGeneratedAt).TotalMinutes >= Math.Max(1, ModConfig.ShopRefreshMinutes.Value);

        /// <summary>Regenerate stock if it is stale. Safe to call whenever the booth is shown.</summary>
        public static void EnsureStock(bool force = false)
        {
            var inv = BagManager.Inventory;
            if (!force && !Stale(inv)) return;
            try
            {
                var tier = 0;
                try { tier = GameManager.CalculateLootTierForLocalPlayer(false); } catch { }
                tier = Math.Max(0, Math.Min(6, tier));
                var slots = Math.Max(1, Math.Min(12, ModConfig.ShopSlots.Value));
                var stock = new List<LootItem>();
                ProfileWatch.Probe = "shop restock";
                try
                {
                    for (var i = 0; i < slots; i++)
                    {
                        var cls = RollClass();
                        var t = tier + (i >= slots - 2 && Rng.NextDouble() < 0.6 ? 1 : 0);   // the last two slots lean a tier up
                        t = Math.Min(6, t);
                        var types = Unlocks.DroppableTypes();
                        var type = types[Rng.Next(types.Length)];
                        var style = type == LootTables.Staff ? Unlocks.PickStaffStyle(Rng) : -1;
                        var wm = WeaponFactory.GenerateRandomWeaponModuleForLocalPlayer(
                            (WeaponFactory.WeaponClass)cls, (Prop.Type)type,
                            (WeaponFactory.WeaponTier)t, (WeaponFactory.WeaponStyle)style, -1, WeaponFactory.SeasonalKey.None);
                        if (wm == null) continue;
                        var item = WeaponCodec.FromModule(wm);
                        item.FoundBy = "Loot Broker";
                        item.Value = Price(wm, item);
                        stock.Add(item);
                    }
                }
                finally { ProfileWatch.Probe = null; }
                inv.ShopStock = stock;
                inv.ShopGeneratedAt = DateTime.UtcNow;
                inv.Save();
                Restocks++;
                ReconLog.Line($"shop: restocked {stock.Count} item(s) at tier {tier + 1} (force={force})");
            }
            catch (Exception e) { Core.Log.Warning($"Shop restock failed: {e.GetType().Name}: {e.Message}"); }
        }

        private static int RollClass()
        {
            // A little better than the floor: 50 / 30 / 15 / 5.
            var r = Rng.NextDouble() * 100.0;
            return r < 50 ? 0 : r < 80 ? 1 : r < 95 ? 2 : 3;
        }

        /// <summary>The game's cost figure for this exact weapon, times the shop multiplier. Stored in the stock item's Value.</summary>
        private static int Price(WeaponModule wm, LootItem item)
        {
            var cost = 0;
            try
            {
                var def = WeaponFactory.GetRandomWeaponStats(wm.GetWeaponType(), wm.GetWeaponClass(), wm.GetWeaponTier(), wm.GetWeaponStyle(), wm.GetRandomSeed());
                cost = def == null ? 0 : def.cost;
            }
            catch { }
            if (cost <= 0) cost = Math.Max(50, item.Value * 5);
            return Math.Max(1, (int)Math.Round(cost * ModConfig.ShopPriceMultiplier.Value));
        }

        /// <summary>What a paid restock costs: a tenth of the current stock's asking prices, at least 25.</summary>
        public static int RestockPrice(LootInventory inv)
        {
            var total = 0;
            if (inv.ShopStock != null) foreach (var i in inv.ShopStock) total += i.Value;
            return Math.Max(25, total / 10);
        }

        public static bool Buy(LootItem stockItem)
        {
            var inv = BagManager.Inventory;
            if (inv.ShopStock == null) return false;
            var idx = inv.ShopStock.FindIndex(i => i.Id == stockItem.Id);
            if (idx < 0) { BagManager.Toast("That one is gone."); return false; }
            var price = stockItem.Value;
            if (inv.Gold < price) { BagManager.Toast($"Not enough gold: {price} needed, you have {inv.Gold}."); return false; }
            if (!inv.CanCarry(stockItem.Weight, BagManager.Capacity)) { BagManager.Toast("Your bag is too full to carry it."); return false; }

            inv.ShopStock.RemoveAt(idx);
            inv.Gold -= price;
            // Into the bag as ordinary loot: the value field goes back to salvage for resale.
            var bought = stockItem;
            bought.Id = Guid.NewGuid().ToString("N");
            try
            {
                var wm = WeaponCodec.ToModule(bought);
                var def = WeaponFactory.GetRandomWeaponStats(wm.GetWeaponType(), wm.GetWeaponClass(), wm.GetWeaponTier(), wm.GetWeaponStyle(), wm.GetRandomSeed());
                bought.Value = def == null ? price / 5 : def.salvageValue;
            }
            catch { bought.Value = price / 5; }
            bought.FoundAt = DateTime.UtcNow;
            inv.Items.Add(bought);
            inv.Save();
            Bought++;
            BagManager.Toast($"Bought {bought.ColoredName} for <color=#F5C542>{price} gold</color>  (now {inv.Gold})");
            ReconLog.Line($"shop: bought {bought.Name} for {price} -> gold {inv.Gold}");
            BagPanel.Refresh();
            return true;
        }

        /// <summary>Tonics are brewed to order: unlimited, priced by tier, gated by unlocked perks.</summary>
        public static bool BuyTonic(Buffs.Def def, int tier)
        {
            var inv = BagManager.Inventory;
            if (!Unlocks.PerkUnlocked(def.Stat)) { BagManager.Toast("The broker won't sell what you haven't earned yet."); return false; }
            var item = Buffs.MakeItem(def, tier);
            var price = (int)Math.Round(item.Value * ModConfig.ShopPriceMultiplier.Value);
            if (inv.Gold < price) { BagManager.Toast($"Not enough gold: {price} needed, you have {inv.Gold}."); return false; }
            if (!inv.CanCarry(item.Weight, BagManager.Capacity)) { BagManager.Toast("Your bag is too full."); return false; }
            inv.Gold -= price;
            item.Value = Math.Max(1, price / 4);   // resale
            inv.Items.Add(item);
            inv.Save();
            Bought++;
            BagManager.Toast($"Bought {item.ColoredName} for <color=#F5C542>{price} gold</color>  (now {inv.Gold})");
            ReconLog.Line($"shop: bought tonic {item.Name} for {price} -> gold {inv.Gold}");
            BagPanel.Refresh();
            return true;
        }

        public static int TonicPrice(Buffs.Def def, int tier) => (int)Math.Round(def.Prices[Math.Max(0, Math.Min(2, tier))] * ModConfig.ShopPriceMultiplier.Value);

        public static bool Restock()
        {
            var inv = BagManager.Inventory;
            var price = RestockPrice(inv);
            if (inv.Gold < price) { BagManager.Toast($"Restock costs {price} gold; you have {inv.Gold}."); return false; }
            inv.Gold -= price;
            inv.Save();
            EnsureStock(force: true);
            BagManager.Toast($"Restocked for <color=#F5C542>{price} gold</color>");
            return true;
        }
    }
}
