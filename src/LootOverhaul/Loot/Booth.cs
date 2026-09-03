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
    /// Milestone L3: the Loot Broker, a stall in the lobby that buys bag items for mod gold
    /// at the game's salvage value. Equipping moved into the game's own fabricator (see
    /// <see cref="FabricatorBridge"/>), so the booth is a sell counter and, later, a shop.
    /// Placed from config (press = in the lobby to move it to where you stand), built once,
    /// kept across scene loads, shown only in the lobby with the gate open.
    /// </summary>
    public static class Booth
    {
        private const int RowsPerPage = 6;
        private const float RowHeight = 0.12f;
        private const float PanelWidth = 1.25f;
        private const float BtnScale = 0.34f;
        private static float BtnW => UiKit.ButtonSize.x * BtnScale;

        private static GameObject _root;
        private static Transform _sell;
        private static int _sellPage;

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
            _sell = sell.transform;
            _sell.localPosition = new Vector3(0f, 1.3f, 0f);
            UiKit.Text(_root.transform, new Vector3(0f, 2.0f, 0f), 1.2f, 0.15f, 0.9f, "<b>LOOT BROKER</b>", TextAlignmentOptions.Center);
            UiKit.Text(_root.transform, new Vector3(0f, 1.9f, 0f), 1.2f, 0.06f, 0.3f, "buys anything you dug up   ·   equip loot at any fabricator", TextAlignmentOptions.Center);
        }

        private static void PlaceFromConfig()
        {
            _root.transform.position = new Vector3(ModConfig.BoothX.Value, ModConfig.BoothY.Value, ModConfig.BoothZ.Value);
            // The panels face -Z of the root; yaw is the direction the booth looks toward the visitor.
            _root.transform.rotation = Quaternion.Euler(0f, ModConfig.BoothYaw.Value + 180f, 0f);
        }

        private static void Rebuild() => BuildSell();

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

            var height = 0.36f + RowsPerPage * RowHeight;
            UiKit.Backdrop(_sell, new Vector3(0f, 0f, 0.01f), PanelWidth, height, new Color(0.08f, 0.06f, 0.04f, 1f));
            var top = height * 0.5f;
            var left = -PanelWidth * 0.5f + 0.04f;
            UiKit.Text(_sell, new Vector3(left, top - 0.05f, 0f), PanelWidth - 0.08f, 0.06f, 0.5f,
                $"<b>SELL</b>   {inv.Items.Count} item(s) in your bag   you have <color=#F5C542>{inv.Gold} gold</color>");
            UiKit.Text(_sell, new Vector3(left, top - 0.10f, 0f), PanelWidth - 0.08f, 0.05f, 0.3f,
                $"The broker pays the game's salvage value × {ModConfig.SellMultiplier.Value:0.##}. Weapons you have equipped at a fabricator stay yours until you unequip them.");

            var y0 = top - 0.22f;
            var start = _sellPage * RowsPerPage;
            var sellX = PanelWidth * 0.5f - 0.04f - BtnW * 0.5f;
            var textW = sellX - BtnW * 0.5f - 0.02f - (left + 0.16f);
            for (var i = 0; i < RowsPerPage && start + i < items.Count; i++)
            {
                var item = items[start + i];
                var row = new GameObject($"SellRow_{i}"); row.transform.SetParent(_sell, false);
                row.transform.localPosition = new Vector3(0f, y0 - i * RowHeight, 0f);
                UiKit.WeaponPreview(row.transform, new Vector3(left + 0.07f, 0f, -0.03f), item, 0.28f);
                var equipped = item.EquippedSlot >= 0 ? $"   <color=#F5C542>equipped: {Loadout.SlotNames[item.EquippedSlot]}</color>" : "";
                UiKit.Text(row.transform, new Vector3(left + 0.16f, 0.025f, 0f), textW, 0.05f, 0.38f, $"{item.ColoredName}{equipped}");
                UiKit.Text(row.transform, new Vector3(left + 0.16f, -0.025f, 0f), textW, 0.045f, 0.3f,
                    $"<color=#9A9A9A>{LootTables.TypeName(item.PropType)}  tier {item.WeaponTier + 1}   wt {item.Weight:0.#}</color>   <color=#F5C542>{SellPrice(item)} gold</color>");
                var captured = item;
                if (item.EquippedSlot < 0)
                    UiKit.Button(row.transform, new Vector3(sellX, 0f, 0f), "SELL", () => Sell(captured), BtnScale);
            }

            var bottom = -top + 0.06f;
            UiKit.Text(_sell, new Vector3(0f, bottom, 0f), 0.3f, 0.06f, 0.35f, $"page {_sellPage + 1} / {pages}", TextAlignmentOptions.Center);
            if (pages > 1)
            {
                UiKit.Button(_sell, new Vector3(-0.2f - BtnW * 0.5f, bottom, 0f), "<", () => { _sellPage--; BuildSell(); }, BtnScale);
                UiKit.Button(_sell, new Vector3(0.2f + BtnW * 0.5f, bottom, 0f), ">", () => { _sellPage++; BuildSell(); }, BtnScale);
            }
            if (items.Count == 0)
                UiKit.Text(_sell, new Vector3(0f, y0 - RowHeight, 0f), 0.8f, 0.06f, 0.4f, "Nothing to sell. Bring me something shiny.", TextAlignmentOptions.Center);
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


    }
}
