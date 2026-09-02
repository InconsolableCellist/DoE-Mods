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

        public static bool CanCarry(LootItem item) => Inventory.CanCarry(item.Weight, ModConfig.BagWeightCapacity.Value);

        public static void Bag(LootItem item)
        {
            var inv = Inventory;
            item.FoundAt = DateTime.UtcNow;
            inv.Items.Add(item);
            inv.Save();
            Toast($"Bagged {item.ColoredName}  ({item.Weight:0.#} wt, {item.Value} value)  bag {inv.TotalWeight:0.#}/{ModConfig.BagWeightCapacity.Value:0}");
            ReconLog.Line($"bag + {item.Name} [{LootTables.ClassName(item.WeaponClass)} {LootTables.TypeName(item.PropType)} t{item.WeaponTier + 1} seed {item.RandomSeed}] value={item.Value} weight={item.Weight:0.#} -> {inv.Items.Count} items, {inv.TotalWeight:0.#} wt");
        }

        /// <summary>Drop the most recently bagged item at your feet as tagged loot (trade, or just try it).</summary>
        public static void DropLast()
        {
            var inv = Inventory;
            if (inv.Items.Count == 0) { Toast("Bag is empty."); return; }
            if (!Gate.ModGate.Active) { Toast("Not in a modded room."); return; }
            var item = inv.Items[inv.Items.Count - 1];
            try
            {
                var local = AvatarPlayer.LocalAvatar;
                if (!Interop.Alive(local)) { Toast("No avatar to drop from."); return; }
                var head = local.Head;
                var pos = head.position + head.forward * 0.6f;
                var tag = DropRoller.SpawnLoot(item, pos, Vector3.up * 1.5f + head.forward * 1.0f);
                if (tag == null) { Toast("Drop failed — see log."); return; }
                inv.Remove(item.Id);
                inv.Save();
                Toast($"Dropped {item.ColoredName}");
            }
            catch (Exception e) { Core.Log.Error($"DropLast failed: {e}"); }
        }

        public static void SummaryToast()
        {
            var inv = Inventory;
            var sb = new StringBuilder();
            sb.Append($"Bag: {inv.Items.Count} item(s), {inv.TotalWeight:0.#}/{ModConfig.BagWeightCapacity.Value:0} wt, {inv.Gold} gold, {inv.KillsSinceLegendary} kills since legendary");
            Toast(sb.ToString());
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
