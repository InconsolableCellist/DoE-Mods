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
    /// The Loot Broker, a stall in the lobby: a sell counter (bag items for mod gold at the
    /// game's salvage value) and a shop (<see cref="Shop"/>: generated weapons at your loot
    /// tier for the game's cost figure). Equipping is at the game's own fabricator (see
    /// <see cref="FabricatorBridge"/>).
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
        private static Transform _sell, _buy;
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
            var buy = new GameObject("Buy"); buy.transform.SetParent(_root.transform, false);
            _sell = sell.transform; _buy = buy.transform;
            // Two panels side by side, each a little over a metre wide, angled toward the visitor.
            _sell.localPosition = new Vector3(-0.70f, 1.3f, 0f);
            _sell.localRotation = Quaternion.Euler(0f, -14f, 0f);
            _buy.localPosition = new Vector3(0.70f, 1.3f, 0f);
            _buy.localRotation = Quaternion.Euler(0f, 14f, 0f);
            UiKit.Text(_root.transform, new Vector3(0f, 2.0f, 0f), 1.2f, 0.15f, 0.9f, "<b>LOOT BROKER</b>", TextAlignmentOptions.Center);
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
            BuildBuy();
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

            var height = 0.36f + RowsPerPage * RowHeight;
            UiKit.Backdrop(_sell, new Vector3(0f, 0f, 0.01f), PanelWidth, height, new Color(0.08f, 0.06f, 0.04f, 1f));
            var top = height * 0.5f;
            var left = -PanelWidth * 0.5f + 0.04f;
            UiKit.Text(_sell, new Vector3(left, top - 0.05f, 0f), PanelWidth - 0.08f, 0.06f, 0.5f,
                $"<b>SELL</b>   {inv.Items.Count} item(s)   <color=#F5C542>{inv.Gold} gold</color>");
            var junkCount = 0; var junkValue = 0;
            foreach (var j in inv.Items) if (!j.IsWeapon) { junkCount++; junkValue += SellPrice(j); }
            if (junkCount > 0)
                UiKit.Button(_sell, new Vector3(PanelWidth * 0.5f - 0.04f - BtnW * 0.5f, top - 0.05f, 0f), $"SELL {junkCount} JUNK · {junkValue}g", SellAllJunk, BtnScale);

            var y0 = top - 0.19f;
            var start = _sellPage * RowsPerPage;
            var sellX = PanelWidth * 0.5f - 0.04f - BtnW * 0.5f;
            var textW = sellX - BtnW * 0.5f - 0.02f - (left + 0.16f);
            for (var i = 0; i < RowsPerPage && start + i < items.Count; i++)
            {
                var item = items[start + i];
                var row = new GameObject($"SellRow_{i}"); row.transform.SetParent(_sell, false);
                row.transform.localPosition = new Vector3(0f, y0 - i * RowHeight, 0f);
                UiKit.Preview(row.transform, new Vector3(left + 0.07f, 0f, -0.03f), item, 0.11f);
                var equipped = item.EquippedSlot >= 0 ? $"   <color=#F5C542>equipped: {Loadout.SlotNames[item.EquippedSlot]}</color>" : "";
                var kind = item.IsWeapon ? $"{LootTables.TypeName(item.PropType)}  tier {item.WeaponTier + 1}" : LootTables.JunkTierName(item.WeaponClass);
                UiKit.Text(row.transform, new Vector3(left + 0.16f, 0.025f, 0f), textW, 0.05f, 0.38f, $"{item.ColoredName}{equipped}");
                UiKit.Text(row.transform, new Vector3(left + 0.16f, -0.025f, 0f), textW, 0.045f, 0.3f,
                    $"<color=#9A9A9A>{kind}   wt {item.Weight:0.#}</color>   <color=#F5C542>{SellPrice(item)} gold</color>");
                var captured = item;
                if (item.EquippedSlot < 0)
                    UiKit.Button(row.transform, new Vector3(sellX, 0f, 0f), "SELL", () => Sell(captured), BtnScale);
            }

            var bottom = -top + 0.06f;
            UiKit.Text(_sell, new Vector3(0f, bottom, 0f), 0.3f, 0.06f, 0.35f, $"page {_sellPage + 1} / {pages}", TextAlignmentOptions.Center);
            if (pages > 1)
            {
                UiKit.Button(_sell, new Vector3(-0.22f - BtnW * 0.5f, bottom, 0f), "<", () => { _sellPage--; BuildSell(); }, BtnScale);
                UiKit.Button(_sell, new Vector3(0.22f + BtnW * 0.5f, bottom, 0f), ">", () => { _sellPage++; BuildSell(); }, BtnScale);
            }
            if (items.Count == 0)
                UiKit.Text(_sell, new Vector3(0f, y0 - RowHeight, 0f), 0.8f, 0.06f, 0.4f, "Nothing to sell. Bring me something shiny.", TextAlignmentOptions.Center);
        }

        // ---- the shop --------------------------------------------------------------------------

        private static void BuildBuy()
        {
            if (!Interop.Alive(_buy)) return;
            UiKit.DestroyChildren(_buy);
            Shop.EnsureStock();
            var inv = BagManager.Inventory;
            var stock = inv.ShopStock ?? new List<LootItem>();

            var height = 0.36f + RowsPerPage * RowHeight;
            UiKit.Backdrop(_buy, new Vector3(0f, 0f, 0.01f), PanelWidth, height, new Color(0.04f, 0.06f, 0.09f, 1f));
            var top = height * 0.5f;
            var left = -PanelWidth * 0.5f + 0.04f;
            var minutesLeft = Math.Max(0, ModConfig.ShopRefreshMinutes.Value - (int)(DateTime.UtcNow - inv.ShopGeneratedAt).TotalMinutes);
            UiKit.Text(_buy, new Vector3(left, top - 0.05f, 0f), PanelWidth - 0.08f, 0.06f, 0.5f,
                $"<b>BUY</b>   {stock.Count} in stock   <color=#9A9A9A>new stock in {minutesLeft} min</color>");
            UiKit.Button(_buy, new Vector3(PanelWidth * 0.5f - 0.04f - BtnW * 0.5f, top - 0.05f, 0f), $"RESTOCK ({Shop.RestockPrice(inv)})", () => { if (Shop.Restock()) Rebuild(); }, BtnScale);

            var y0 = top - 0.19f;
            var buyX = PanelWidth * 0.5f - 0.04f - BtnW * 0.5f;
            var textW = buyX - BtnW * 0.5f - 0.02f - (left + 0.16f);
            for (var i = 0; i < RowsPerPage && i < stock.Count; i++)
            {
                var item = stock[i];
                var row = new GameObject($"BuyRow_{i}"); row.transform.SetParent(_buy, false);
                row.transform.localPosition = new Vector3(0f, y0 - i * RowHeight, 0f);
                UiKit.Preview(row.transform, new Vector3(left + 0.07f, 0f, -0.03f), item, 0.11f);
                string stats = "";
                try { stats = Interop.OneLine(WeaponCodec.ToModule(item).GetStatsText()); } catch { }
                var afford = inv.Gold >= item.Value;
                var priceColor = afford ? "#F5C542" : "#B04040";
                UiKit.Text(row.transform, new Vector3(left + 0.16f, 0.025f, 0f), textW, 0.05f, 0.38f,
                    $"{item.ColoredName}   <color={priceColor}>{item.Value} gold</color>");
                UiKit.Text(row.transform, new Vector3(left + 0.16f, -0.025f, 0f), textW, 0.045f, 0.3f,
                    $"<color=#9A9A9A>{LootTables.TypeName(item.PropType)}  tier {item.WeaponTier + 1}   wt {item.Weight:0.#}</color>   {stats}");
                var captured = item;
                UiKit.Button(row.transform, new Vector3(buyX, 0f, 0f), afford ? "BUY" : "TOO DEAR", () => { if (Shop.Buy(captured)) Rebuild(); }, BtnScale);
            }
            if (stock.Count == 0)
                UiKit.Text(_buy, new Vector3(0f, y0 - RowHeight, 0f), 0.8f, 0.06f, 0.4f, "Sold out. Restock, or come back later.", TextAlignmentOptions.Center);
        }

        public static int SellPrice(LootItem item) => Math.Max(1, (int)Math.Round(item.Value * ModConfig.SellMultiplier.Value));

        private static void SellAllJunk()
        {
            var inv = BagManager.Inventory;
            var total = 0; var n = 0;
            foreach (var j in new List<LootItem>(inv.Items))
            {
                if (j.IsWeapon) continue;
                total += SellPrice(j); n++;
                inv.Remove(j.Id);
            }
            if (n == 0) { BagManager.Toast("No junk to sell."); return; }
            inv.Gold += total;
            inv.Save();
            BagManager.Toast($"Sold {n} piece(s) of junk for <color=#F5C542>{total} gold</color>  (now {inv.Gold})");
            ReconLog.Line($"sold {n} junk for {total} -> gold {inv.Gold}");
            Rebuild();
            BagPanel.Refresh();
        }

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
