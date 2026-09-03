using System;
using System.Text;
using Il2Cpp;
using Il2CppPhoton.Pun;
using LootOverhaul.Recon;
using Interop = LootOverhaul.Recon.Interop;
using UnityEngine;

namespace LootOverhaul.Loot
{
    /// <summary>
    /// The player's bag: the <see cref="LootInventory"/> on disk, loaded per account, plus the
    /// bag-side verbs the loop needs before the panel exists (L2): bag an item with a toast,
    /// refuse when over weight, drop an item back onto the floor as tagged loot, and a
    /// summary toast for the hotkey.
    /// </summary>
    public static class BagManager
    {
        private static LootInventory _inv;
        private static string _account;

        public static LootInventory Inventory
        {
            get
            {
                var account = AccountId();
                if (_inv == null || account != _account)
                {
                    _account = account;
                    _inv = LootInventory.Load(ModPaths.InventoryFile(account));
                    Core.Log.Msg($"Bag loaded for `{account}`: {_inv.Items.Count} item(s), {_inv.TotalWeight:0.#} wt, {_inv.Gold} gold.");
                    try { Buffs.RebuildWorn(); } catch { }
                }
                return _inv;
            }
        }

        private static string AccountId()
        {
            try
            {
                var pf = PlayFabManager.instance;
                if (Interop.Alive(pf))
                {
                    var id = pf.playfabPlayerID;
                    if (!string.IsNullOrEmpty(id)) return id;
                }
            }
            catch { }
            try { var n = PhotonNetwork.NickName; if (!string.IsNullOrEmpty(n)) return "nick-" + n; } catch { }
            return "default";
        }

        public static readonly (string name, float bonus, int price)[] BagUpgrades =
        {
            ("Satchel", 30f, 400), ("Traveller's Pack", 60f, 1200), ("Porter's Harness", 110f, 3000),
        };

        /// <summary>Bag capacity: the setting plus whatever the player has bought.</summary>
        public static float Capacity
        {
            get
            {
                var lvl = Math.Max(0, Math.Min(BagUpgrades.Length, Inventory.BagLevel));
                return ModConfig.BagWeightCapacity.Value + (lvl == 0 ? 0f : BagUpgrades[lvl - 1].bonus);
            }
        }

        public static bool CanCarry(LootItem item) => Inventory.CanCarry(item.Weight, Capacity);

        /// <summary>Buy the next bag upgrade with mod gold.</summary>
        public static bool BuyBagUpgrade()
        {
            var inv = Inventory;
            if (inv.BagLevel >= BagUpgrades.Length) { Toast("You already carry the biggest bag the broker sells."); return false; }
            var next = BagUpgrades[inv.BagLevel];
            var price = (int)Math.Round(next.price * ModConfig.ShopPriceMultiplier.Value);
            if (inv.Gold < price) { Toast($"The {next.name} costs {price} gold; you have {inv.Gold}."); return false; }
            inv.Gold -= price;
            inv.BagLevel++;
            inv.Save();
            Toast($"Bought a {next.name}: bag capacity is now {Capacity:0} wt.");
            ReconLog.Line($"bag upgrade {inv.BagLevel} ({next.name}) for {price} -> gold {inv.Gold}");
            BagPanel.Refresh(); Booth.Refresh();
            return true;
        }

        public static void Bag(LootItem item)
        {
            var inv = Inventory;
            item.FoundAt = DateTime.UtcNow;
            inv.Items.Add(item);
            inv.Save();
            Toast($"Bagged {item.ColoredName}  ({item.Weight:0.#} wt, {item.Value} value)  bag {inv.TotalWeight:0.#}/{Capacity:0}");
            BagPanel.Refresh();
            ReconLog.Line($"bag + {item.Name} [{LootTables.ClassName(item.WeaponClass)} {LootTables.TypeName(item.PropType)} t{item.WeaponTier + 1} seed {item.RandomSeed}] value={item.Value} weight={item.Weight:0.#} -> {inv.Items.Count} items, {inv.TotalWeight:0.#} wt");
        }

        /// <summary>Drop the most recently bagged item at your feet as tagged loot (trade, or just try it).</summary>
        public static void DropLast()
        {
            var inv = Inventory;
            if (inv.Items.Count == 0) { Toast("Bag is empty."); return; }
            Drop(inv.Items[inv.Items.Count - 1]);
        }

        /// <summary>Drop one bag item at your feet as tagged loot. The panel's per-row Drop uses this.</summary>
        public static void Drop(LootItem item)
        {
            var inv = Inventory;
            if (item == null || inv.Find(item.Id) == null) { Toast("That item is no longer in the bag."); return; }
            if (inv.Find(item.Id).EquippedSlot >= 0) { Toast("Unequip it at the booth first."); return; }
            if (!Gate.ModGate.Active) { Toast("Not in a modded room."); return; }
            try
            {
                var local = AvatarPlayer.LocalAvatar;
                if (!Interop.Alive(local)) { Toast("No avatar to drop from."); return; }
                var head = local.Head;
                var fwd = head.forward; fwd.y = 0f; fwd.Normalize();
                var pos = head.position + fwd * 0.5f + Vector3.down * 0.3f;
                var tag = DropRoller.SpawnLoot(item, pos, Vector3.up * 1.0f + fwd * 1.2f);
                if (tag == null) { Toast("Drop failed — see log."); return; }
                inv.Remove(item.Id);
                inv.Save();
                Toast($"Dropped {item.ColoredName}");
                BagPanel.Refresh();
                Booth.Refresh();
            }
            catch (Exception e) { Core.Log.Error($"Drop failed: {e}"); }
        }

        public static void SummaryToast()
        {
            var inv = Inventory;
            var sb = new StringBuilder();
            sb.Append($"Bag: {inv.Items.Count} item(s), {inv.TotalWeight:0.#}/{Capacity:0} wt, {inv.Gold} gold, {inv.KillsSinceLegendary} kills since legendary");
            Toast(sb.ToString());
            DumpToTranscript();
        }

        public static void DumpToTranscript()
        {
            var inv = Inventory;
            ReconLog.Section("Bag contents");
            foreach (var i in inv.Items)
                ReconLog.Line($"- {i.Name} [{LootTables.ClassName(i.WeaponClass)} {LootTables.TypeName(i.PropType)} t{i.WeaponTier + 1}] value={i.Value} weight={i.Weight:0.#} found {i.FoundAt:u} by {i.FoundBy}");
            ReconLog.Line($"- floor loot tagged right now: {LootRegistry.Count}");
        }

        public static void Toast(string text)
        {
            try { FXNotifications.AddQuickNotification(text, 0f); }
            catch (Exception e) { Core.Log.Msg($"[toast] {text} (notification failed: {e.GetType().Name})"); }
        }
    }
}
