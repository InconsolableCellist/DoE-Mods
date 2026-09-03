using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppTMPro;
using LootOverhaul.Gate;
using LootOverhaul.Recon;
using MelonLoader;
using UnityEngine;
using Interop = LootOverhaul.Recon.Interop;

namespace LootOverhaul.Loot
{
    /// <summary>
    /// Milestone L3: the Loot Broker, a stall in the lobby. Left panel sells bag items for
    /// mod gold at the game's salvage value; right panel is the loadout screen for modded
    /// play — three slots, each filled from the vanilla armory or the bag, or cleared back to
    /// vanilla. Placed from config (press = in the lobby to move it to where you stand),
    /// built once, kept across scene loads, shown only in the lobby with the gate open.
    /// </summary>
    public static class Booth
    {
        private const int RowsPerPage = 6;
        private const float RowHeight = 0.11f;
        private const float PanelWidth = 1.0f;

        private static GameObject _root;
        private static Transform _sell, _gear;
        private static int _sellPage, _gearPage;
        private static int _choosing = -1;   // slot being picked for, or -1 for the slot overview

        public static bool IsShown => Interop.Alive(_root) && _root.activeSelf;

        public static void ShowIfLobby(string sceneName)
        {
            if (sceneName != GameManager.LOBBY_SCENE || !ModGate.Active) { Hide(); return; }
            UiKit.CaptureTemplates();
            if (!UiKit.Ready) { Core.Log.Warning("Booth: UI templates not ready; will not build this time."); return; }
            EnsureRoot();
            PlaceFromConfig();
            _root.SetActive(true);
            Rebuild();
        }

        public static void Hide()
        {
            if (Interop.Alive(_root)) _root.SetActive(false);
        }

        public static void Refresh()
        {
            if (IsShown) Rebuild();
        }

        /// <summary>Hotkey: put the booth 1.5 m in front of where you stand, facing you, and remember it.</summary>
        public static void PlaceHere()
        {
            try
            {
                var local = AvatarPlayer.LocalAvatar;
                if (!Interop.Alive(local)) { BagManager.Toast("No avatar."); return; }
                var head = local.Head;
                var fwd = head.forward; fwd.y = 0f; fwd.Normalize();
                var floorY = local.transform.position.y;
                var pos = head.position + fwd * 1.5f;
                pos.y = floorY;
                var yaw = Quaternion.LookRotation(-fwd, Vector3.up).eulerAngles.y;
                ModConfig.BoothX.Value = pos.x; ModConfig.BoothY.Value = pos.y; ModConfig.BoothZ.Value = pos.z; ModConfig.BoothYaw.Value = yaw;
                MelonPreferences.Save();
                BagManager.Toast($"Booth placed at ({pos.x:0.0}, {pos.y:0.0}, {pos.z:0.0}), yaw {yaw:0}.");
                if (GameManager.IsLobbyScene) ShowIfLobby(GameManager.LOBBY_SCENE);
            }
            catch (Exception e) { Core.Log.Warning($"PlaceHere failed: {e.GetType().Name}: {e.Message}"); }
        }

        private static void EnsureRoot()
        {
            if (Interop.Alive(_root)) return;
            _root = new GameObject("LootOverhaul_Booth");
            UnityEngine.Object.DontDestroyOnLoad(_root);
            var sell = new GameObject("Sell"); sell.transform.SetParent(_root.transform, false);
            var gear = new GameObject("Gear"); gear.transform.SetParent(_root.transform, false);
            _sell = sell.transform; _gear = gear.transform;
            // Two panels side by side at counter height, angled slightly toward the visitor.
            _sell.localPosition = new Vector3(-0.56f, 1.25f, 0f);
            _sell.localRotation = Quaternion.Euler(0f, -12f, 0f);
            _gear.localPosition = new Vector3(0.56f, 1.25f, 0f);
            _gear.localRotation = Quaternion.Euler(0f, 12f, 0f);
            UiKit.Text(_root.transform, new Vector3(-0.5f, 2.05f, 0f), 1.0f, 0.12f, 3.0f, "<b>LOOT BROKER</b>", TextAlignmentOptions.Center);
        }

        private static void PlaceFromConfig()
        {
            _root.transform.position = new Vector3(ModConfig.BoothX.Value, ModConfig.BoothY.Value, ModConfig.BoothZ.Value);
            // The panels face -Z of the root; yaw is the direction the booth looks toward the visitor.
            _root.transform.rotation = Quaternion.Euler(0f, ModConfig.BoothYaw.Value + 180f, 0f);
        }

        private static void Rebuild()
        {
            BuildSell();
            BuildGear();
        }

        // ---- sell counter -------------------------------------------------------------------

        private static void BuildSell()
        {
            if (!Interop.Alive(_sell)) return;
            UiKit.DestroyChildren(_sell);
            var inv = BagManager.Inventory;
            var items = new List<LootItem>(inv.Items);
            items.Sort((a, b) => b.Value.CompareTo(a.Value));
            var pages = Math.Max(1, (items.Count + RowsPerPage - 1) / RowsPerPage);
            _sellPage = Math.Max(0, Math.Min(_sellPage, pages - 1));

            var height = 0.32f + RowsPerPage * RowHeight;
            UiKit.Backdrop(_sell, new Vector3(0f, 0f, 0.01f), PanelWidth, height, new Color(0.08f, 0.06f, 0.04f, 1f));
            var top = height * 0.5f;
            UiKit.Text(_sell, new Vector3(-PanelWidth * 0.5f + 0.03f, top - 0.06f, 0f), PanelWidth - 0.06f, 0.08f, 1.6f,
                $"<b>SELL</b>   {inv.Items.Count} item(s)   you have <color=#F5C542>{inv.Gold} gold</color>");
            UiKit.Text(_sell, new Vector3(-PanelWidth * 0.5f + 0.03f, top - 0.15f, 0f), PanelWidth - 0.06f, 0.06f, 1.0f,
                $"<size=90%>Prices are the game's salvage value × {ModConfig.SellMultiplier.Value:0.##}. Equipped items must be unequipped first.</size>");

            var y0 = top - 0.28f;
            var start = _sellPage * RowsPerPage;
            for (var i = 0; i < RowsPerPage && start + i < items.Count; i++)
            {
                var item = items[start + i];
                var row = new GameObject($"SellRow_{i}"); row.transform.SetParent(_sell, false);
                row.transform.localPosition = new Vector3(0f, y0 - i * RowHeight, 0f);
                UiKit.WeaponPreview(row.transform, new Vector3(-0.43f, 0f, -0.02f), item, 0.12f);
                var equipped = item.EquippedSlot >= 0 ? $"  <color=#F5C542>[{Loadout.SlotNames[item.EquippedSlot]}]</color>" : "";
                UiKit.Text(row.transform, new Vector3(-0.34f, 0.022f, 0f), 0.62f, 0.05f, 1.2f, $"{item.ColoredName}{equipped}");
                UiKit.Text(row.transform, new Vector3(-0.34f, -0.026f, 0f), 0.62f, 0.045f, 0.95f,
                    $"<size=90%>{LootTables.TypeName(item.PropType)} t{item.WeaponTier + 1}   wt {item.Weight:0.#}</size>   <color=#F5C542>{SellPrice(item)} gold</color>");
                var captured = item;
                if (item.EquippedSlot < 0)
                    UiKit.Button(row.transform, new Vector3(0.40f, 0f, 0f), "SELL", () => Sell(captured), 0.5f);
            }

            var bottom = -top + 0.06f;
            UiKit.Text(_sell, new Vector3(-0.08f, bottom, 0f), 0.3f, 0.06f, 1.1f, $"page {_sellPage + 1} / {pages}", TextAlignmentOptions.Center);
            if (pages > 1)
            {
                UiKit.Button(_sell, new Vector3(-0.30f, bottom, 0f), "<", () => { _sellPage--; BuildSell(); }, 0.45f);
                UiKit.Button(_sell, new Vector3(0.14f, bottom, 0f), ">", () => { _sellPage++; BuildSell(); }, 0.45f);
            }
            if (items.Count == 0)
                UiKit.Text(_sell, new Vector3(-0.3f, y0 - RowHeight, 0f), 0.6f, 0.06f, 1.2f, "Nothing to sell.", TextAlignmentOptions.Center);
        }

        public static int SellPrice(LootItem item) => Math.Max(1, (int)Math.Round(item.Value * ModConfig.SellMultiplier.Value));

        private static void Sell(LootItem item)
        {
            var inv = BagManager.Inventory;
            var live = inv.Find(item.Id);
            if (live == null) { BagManager.Toast("Already gone."); Refresh(); return; }
            if (live.EquippedSlot >= 0) { BagManager.Toast("Unequip it first."); return; }
            var price = SellPrice(live);
            inv.Remove(live.Id);
            inv.Gold += price;
            inv.Save();
            BagManager.Toast($"Sold {live.ColoredName} for <color=#F5C542>{price} gold</color>  (now {inv.Gold})");
            ReconLog.Line($"sold {live.Name} for {price} -> gold {inv.Gold}");
            Rebuild();
            BagPanel.Refresh();
        }

        // ---- loadout picker -----------------------------------------------------------------

        private static void BuildGear()
        {
            if (!Interop.Alive(_gear)) return;
            UiKit.DestroyChildren(_gear);
            var height = 0.32f + RowsPerPage * RowHeight;
            UiKit.Backdrop(_gear, new Vector3(0f, 0f, 0.01f), PanelWidth, height, new Color(0.04f, 0.06f, 0.09f, 1f));
            var top = height * 0.5f;

            if (_choosing < 0)
            {
                var inv = BagManager.Inventory;
                UiKit.Text(_gear, new Vector3(-PanelWidth * 0.5f + 0.03f, top - 0.06f, 0f), PanelWidth - 0.06f, 0.08f, 1.6f,
                    "<b>BATTLE LOADOUT</b>   what you carry into the dungeon");
                UiKit.Text(_gear, new Vector3(-PanelWidth * 0.5f + 0.03f, top - 0.15f, 0f), PanelWidth - 0.06f, 0.06f, 1.0f,
                    "<size=90%>Each slot takes a weapon from your armory or your bag. VANILLA hands the slot back to the game.</size>");
                for (var slot = 0; slot < 3; slot++)
                {
                    var y = top - 0.32f - slot * 0.19f;
                    var row = new GameObject($"Slot_{slot}"); row.transform.SetParent(_gear, false);
                    row.transform.localPosition = new Vector3(0f, y, 0f);
                    var item = inv.Loadout[slot];
                    UiKit.Text(row.transform, new Vector3(-0.46f, 0.03f, 0f), 0.5f, 0.05f, 1.3f, $"<b>{Loadout.SlotNames[slot]}</b>");
                    if (item != null)
                    {
                        UiKit.WeaponPreview(row.transform, new Vector3(-0.02f, 0.0f, -0.02f), item, 0.11f);
                        UiKit.Text(row.transform, new Vector3(-0.46f, -0.03f, 0f), 0.6f, 0.05f, 1.0f, $"{item.ColoredName}  <size=80%>[{item.Source}]</size>");
                    }
                    else
                        UiKit.Text(row.transform, new Vector3(-0.46f, -0.03f, 0f), 0.6f, 0.05f, 1.0f, "<color=#9A9A9A>vanilla loadout</color>");
                    var s = slot;
                    UiKit.Button(row.transform, new Vector3(0.26f, 0f, 0f), "CHOOSE", () => { _choosing = s; _gearPage = 0; BuildGear(); }, 0.5f);
                    if (item != null)
                        UiKit.Button(row.transform, new Vector3(0.42f, 0f, 0f), "VANILLA", () => { Loadout.Set(s, null); BuildGear(); BuildSell(); }, 0.45f);
                }
                UiKit.Text(_gear, new Vector3(-PanelWidth * 0.5f + 0.03f, -top + 0.08f, 0f), PanelWidth - 0.06f, 0.06f, 0.9f,
                    "<size=85%>Applied after every spawn. Dungeon hazard weapons override this, as in the base game.</size>");
                return;
            }

            // Candidate list for one slot: armory first, then bag, filtered by what the holster accepts.
            var slotIdx = _choosing;
            var candidates = new List<LootItem>();
            foreach (var a in Loadout.ArmoryItems()) if (Loadout.Accepts(slotIdx, a.PropType)) candidates.Add(a);
            foreach (var b in BagManager.Inventory.Items) if (Loadout.Accepts(slotIdx, b.PropType)) candidates.Add(b);
            var pages = Math.Max(1, (candidates.Count + RowsPerPage - 1) / RowsPerPage);
            _gearPage = Math.Max(0, Math.Min(_gearPage, pages - 1));

            UiKit.Text(_gear, new Vector3(-PanelWidth * 0.5f + 0.03f, top - 0.06f, 0f), PanelWidth - 0.06f, 0.08f, 1.6f,
                $"<b>{Loadout.SlotNames[slotIdx]}</b>   {candidates.Count} candidate(s)");
            UiKit.Button(_gear, new Vector3(0.42f, top - 0.06f, 0f), "BACK", () => { _choosing = -1; BuildGear(); }, 0.5f);

            var y0 = top - 0.24f;
            var start = _gearPage * RowsPerPage;
            for (var i = 0; i < RowsPerPage && start + i < candidates.Count; i++)
            {
                var item = candidates[start + i];
                var row = new GameObject($"Cand_{i}"); row.transform.SetParent(_gear, false);
                row.transform.localPosition = new Vector3(0f, y0 - i * RowHeight, 0f);
                UiKit.WeaponPreview(row.transform, new Vector3(-0.43f, 0f, -0.02f), item, 0.12f);
                string stats = "";
                try { stats = Interop.OneLine(WeaponCodec.ToModule(item).GetStatsText()); } catch { }
                UiKit.Text(row.transform, new Vector3(-0.34f, 0.022f, 0f), 0.62f, 0.05f, 1.2f, $"{item.ColoredName}  <size=80%>[{item.Source}]</size>");
                UiKit.Text(row.transform, new Vector3(-0.34f, -0.026f, 0f), 0.62f, 0.045f, 0.95f, $"<size=90%>{stats}</size>");
                var captured = item;
                UiKit.Button(row.transform, new Vector3(0.40f, 0f, 0f), "USE", () => { Loadout.Set(slotIdx, captured); _choosing = -1; BuildGear(); BuildSell(); }, 0.5f);
            }
            var bottom = -top + 0.06f;
            UiKit.Text(_gear, new Vector3(-0.08f, bottom, 0f), 0.3f, 0.06f, 1.1f, $"page {_gearPage + 1} / {pages}", TextAlignmentOptions.Center);
            if (pages > 1)
            {
                UiKit.Button(_gear, new Vector3(-0.30f, bottom, 0f), "<", () => { _gearPage--; BuildGear(); }, 0.45f);
                UiKit.Button(_gear, new Vector3(0.14f, bottom, 0f), ">", () => { _gearPage++; BuildGear(); }, 0.45f);
            }
            if (candidates.Count == 0)
                UiKit.Text(_gear, new Vector3(-0.3f, y0 - RowHeight, 0f), 0.6f, 0.06f, 1.2f, "Nothing fits this slot.", TextAlignmentOptions.Center);
        }
    }
}
