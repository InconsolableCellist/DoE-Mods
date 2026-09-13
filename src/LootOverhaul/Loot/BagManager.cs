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
        /// <summary>
        /// Ids of items this player put on the floor on purpose (DROP, ], the bag-full toss),
        /// so the walk-over pickup does not scoop them straight back up. A hand grab still takes
        /// them. Forgotten on every scene change.
        /// </summary>
        public static readonly System.Collections.Generic.HashSet<string> DroppedByMe = new System.Collections.Generic.HashSet<string>();

        public static LootInventory Inventory
        {
            get
            {
                var account = AccountId();
                if (_inv == null || account != _account)
                {
                    if (_inv != null) Core.Log.Msg($"Bag: account changed `{_account}` -> `{account}`; loading that bag.");
                    _account = account;
                    _inv = LootInventory.Load(ModPaths.InventoryFile(account));
                    Core.Log.Msg($"Bag loaded for `{account}`: {_inv.Items.Count} item(s), {_inv.TotalWeight:0.#} wt, {_inv.Gold} tokens.");
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
            // PlayFab's id can be momentarily unreadable (a reconnect, a join). Once a real
            // account's bag is open, keep it rather than swap in an empty nickname bag, which
            // would make every list built meanwhile (armory, booth) show no loot (0.9.15).
            if (!string.IsNullOrEmpty(_account) && !_account.StartsWith("nick-") && _account != "default") return _account;
            try { var n = PhotonNetwork.NickName; if (!string.IsNullOrEmpty(n)) return "nick-" + n; } catch { }
            return "default";
        }

        /// <summary>
        /// The bag tiers, in order; <see cref="LootInventory.BagLevel"/> counts how many have been
        /// bought and the capacity is the setting plus the bonus of the last one. The first three
        /// are the 0.9.9 tiers; 0.9.15 added three more above them, so a saved level keeps its
        /// meaning.
        /// </summary>
        public static readonly (string name, float bonus, int price)[] BagUpgrades =
        {
            ("Satchel", 15f, 400), ("Backpack", 30f, 1200), ("Bag of Holding", 55f, 3000),
            ("Traveller's Pack", 85f, 6000), ("Porter's Harness", 120f, 12000), ("Caravan Trunk", 160f, 24000),
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

        /// <summary>
        /// Make room for a weapon or armor that does not fit by tossing the cheapest unlocked
        /// trinkets onto the floor, fewest tokens first, until it does. Nothing equipped, worn,
        /// locked, or a weapon, armor or tonic is ever tossed; a trinket never makes room for
        /// another trinket. Returns true when the item now fits.
        /// </summary>
        public static bool MakeRoomFor(LootItem item)
        {
            var inv = Inventory;
            if (inv.CanCarry(item.Weight, Capacity)) return true;
            if (!ModConfig.TossJunkWhenFull.Value || !(item.IsWeapon || item.IsArmor)) return false;
            var junk = new System.Collections.Generic.List<LootItem>();
            foreach (var j in inv.Items) if (!j.IsWeapon && !j.IsArmor && !j.IsBuff && !j.Locked && !inv.InUse(j)) junk.Add(j);
            junk.Sort((a, b) => a.Value != b.Value ? a.Value.CompareTo(b.Value) : b.Weight.CompareTo(a.Weight));
            var need = inv.TotalWeight + item.Weight - Capacity;
            var toss = new System.Collections.Generic.List<LootItem>();
            var freed = 0f;
            foreach (var j in junk) { if (freed >= need) break; toss.Add(j); freed += j.Weight; }
            if (freed < need) return false;
            var tossed = 0; var value = 0;
            foreach (var j in toss)
            {
                if (!Drop(j, quiet: true)) break;
                tossed++; value += j.Value;
            }
            if (tossed > 0) Toast($"Tossed {tossed} trinket(s) worth {value} T to make room for {item.ColoredName}");
            ReconLog.Line($"bag: tossed {tossed}/{toss.Count} trinket(s) ({freed:0.#} wt) to make room for {item.Name} ({item.Weight:0.#} wt)");
            return inv.CanCarry(item.Weight, Capacity);
        }

        /// <summary>Buy the next bag upgrade with tokens.</summary>
        public static bool BuyBagUpgrade()
        {
            var inv = Inventory;
            if (inv.BagLevel >= BagUpgrades.Length) { Toast("You already carry the biggest bag there is."); return false; }
            var next = BagUpgrades[inv.BagLevel];
            var price = (int)Math.Round(next.price * ModConfig.ShopPriceMultiplier.Value);
            if (inv.Gold < price) { Toast($"The {next.name} costs {price} tokens; you have {inv.Gold}."); return false; }
            inv.Gold -= price;
            inv.BagLevel++;
            inv.Save();
            Toast($"Bought a {next.name}: bag capacity is now {Capacity:0} wt.");
            ReconLog.Line($"bag upgrade {inv.BagLevel} ({next.name}) for {price} -> tokens {inv.Gold}");
            BagPanel.Refresh(); Booth.Refresh();
            return true;
        }

        public static void Bag(LootItem item)
        {
            var inv = Inventory;
            item.FoundAt = DateTime.UtcNow;
            inv.Items.Add(item);
            inv.Save();
            if (item.IsWeapon) { try { FabricatorBridge.RefreshArmories($"{item.Name} bagged"); } catch { } }
            Toast($"Bagged {item.ColoredName}  ({item.Value} T)  bag {inv.TotalWeight:0.#}/{Capacity:0}");
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
        public static bool Drop(LootItem item, bool quiet = false)
        {
            var inv = Inventory;
            var live = item == null ? null : inv.Find(item.Id);
            if (live == null) { Toast("That's gone."); return false; }
            if (inv.EquippedSlotOf(live) >= 0) { Toast("Unequip it at the pedestal first."); return false; }
            if (live.WornSlot >= 0) { Toast("Take it off first."); return false; }
            if (live.Locked) { Toast("Locked. Unlock it at the kobold first."); return false; }
            if (!Gate.ModGate.Active) { Toast("Not in a modded room."); return false; }
            try
            {
                var local = AvatarPlayer.LocalAvatar;
                if (!Interop.Alive(local)) { Toast("No avatar to drop from."); return false; }
                var head = local.Head;
                var fwd = head.forward; fwd.y = 0f; fwd.Normalize();
                var pos = head.position + fwd * 0.5f + Vector3.down * 0.3f;
                var vel = Vector3.up * 1.0f + fwd * 1.2f;
                // The walk-over pickup must not take it straight back; a hand grab still may.
                DroppedByMe.Add(live.Id);
                // Floor loot is a room object, which only the master can create; everyone else asks.
                if (PhotonNetwork.IsMasterClient)
                {
                    var tag = DropRoller.SpawnLoot(live, pos, vel);
                    if (tag == null) { Toast("Drop failed — see log."); return false; }
                }
                else if (!LootNet.SendDropRequest(live, pos, vel)) { Toast("Drop failed — no host to ask."); return false; }
                inv.Remove(live.Id);
                inv.Save();
                if (!quiet) Toast($"Dropped {live.ColoredName}");
                BagPanel.Refresh();
                Booth.Refresh();
                return true;
            }
            catch (Exception e) { Core.Log.Error($"Drop failed: {e}"); return false; }
        }

        public static void SummaryToast()
        {
            var inv = Inventory;
            var sb = new StringBuilder();
            sb.Append($"Bag: {inv.Items.Count} item(s), {inv.TotalWeight:0.#}/{Capacity:0} wt, {inv.Gold} tokens, {inv.KillsSinceLegendary} kills since legendary");
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
