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
    /// The Kobold Traveler, a stall in the lobby: a sell counter (bag items for tokens at the
    /// game's salvage value; tokens are the mod's own currency, never the game's gold) and a
    /// shop (<see cref="Shop"/>: generated weapons at your loot tier for the game's cost
    /// figure). Equipping is at the game's own fabricator (see <see cref="FabricatorBridge"/>).
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
        private static int _sellPage, _tonicPage, _enchantPage, _helpPage, _armorPage;
        private static int _buyMode;            // 0 weapons, 1 tonics, 2 enchant, 3 armor
        private static int _armorSlot;          // ARMOR tab: which slot is being compared (0 head, 1 chest, 2 legs)
        private static bool _enchantHelp;       // ENCHANT tab: the "what do they do" page
        private static string _enchantTarget;   // bag item id being enchanted, or null for the list
        /// <summary>Text width left of a button column whose centre is at <paramref name="btnX"/>, from <paramref name="textX"/>.</summary>
        private static float WidthBefore(float btnX, float textX, float scale = 1f) => btnX - BtnW * scale * 0.6f - 0.02f - textX;

        /// <summary>The built-in lobby spot, chosen by the mod's author with = on 2026-09-03. Everyone gets this unless they place it themselves.</summary>
        public static readonly Vector3 DefaultPosition = new Vector3(42.075f, -1.930f, 16.224f);
        public const float DefaultYaw = 1.536f;

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
                ModConfig.BoothPlaced.Value = true;
                MelonPreferences.Save();
                BagManager.Toast($"Kobold moved.");
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
            UiKit.Text(_root.transform, new Vector3(0f, 2.02f, 0f), 1.6f, 0.15f, 0.9f, "<b>KOBOLD TRAVELER</b>", TextAlignmentOptions.Center);
            UiKit.Text(_root.transform, new Vector3(0f, 1.9f, 0f), 1.6f, 0.06f, 0.36f, "<color=#9A9A9A>shinies from the dungeon for tokens</color>", TextAlignmentOptions.Center);
        }

        private static void PlaceFromConfig()
        {
            var placed = ModConfig.BoothPlaced.Value;
            var pos = placed ? new Vector3(ModConfig.BoothX.Value, ModConfig.BoothY.Value, ModConfig.BoothZ.Value) : DefaultPosition;
            var yaw = placed ? ModConfig.BoothYaw.Value : DefaultYaw;
            _root.transform.position = pos;
            // The panels face -Z of the root; yaw is the direction the booth looks toward the visitor.
            _root.transform.rotation = Quaternion.Euler(0f, yaw + 180f, 0f);
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
            var perPage = RowsPerPage - 1;
            var pages = Math.Max(1, (items.Count + perPage - 1) / perPage);
            _sellPage = Math.Max(0, Math.Min(_sellPage, pages - 1));

            var height = 0.36f + RowsPerPage * RowHeight;
            UiKit.Backdrop(_sell, new Vector3(0f, 0f, 0.01f), PanelWidth, height, new Color(0.08f, 0.06f, 0.04f, 1f));
            var top = height * 0.5f;
            var left = -PanelWidth * 0.5f + 0.04f;
            UiKit.Text(_sell, new Vector3(left, top - 0.05f, 0f), PanelWidth - 0.08f, 0.06f, 0.5f,
                $"<b>SELL</b>   {inv.Items.Count} item(s)   <color=#F5C542>{inv.Gold} tokens</color>", fit: true);

            var y0 = top - 0.19f;
            var start = _sellPage * perPage;
            var sellX = PanelWidth * 0.5f - 0.04f - BtnW * 0.5f;
            var lockX = sellX - BtnW * 1.2f - 0.03f;
            // One row fewer so the bag line fits above the pager; two buttons per row (LOCK, SELL).
            var textW = lockX - BtnW * 0.5f - 0.02f - (left + 0.16f);
            for (var i = 0; i < RowsPerPage - 1 && start + i < items.Count; i++)
            {
                var item = items[start + i];
                var row = new GameObject($"SellRow_{i}"); row.transform.SetParent(_sell, false);
                row.transform.localPosition = new Vector3(0f, y0 - i * RowHeight, 0f);
                UiKit.Preview(row.transform, new Vector3(left + 0.07f, 0f, -0.03f), item, 0.11f);
                var slot = inv.EquippedSlotOf(item);
                var equipped = slot >= 0 ? $"   <color=#F5C542>equipped: {Loadout.SlotNames[slot]}</color>" : item.WornSlot >= 0 ? "   <color=#C9A86A>worn</color>" : "";
                string kind;
                if (item.IsWeapon) { string stats = ""; try { stats = UiKit.StatsLine(WeaponCodec.ToModule(item).GetStatsText()); } catch { } kind = $"<color=#9A9A9A>{LootTables.TypeName(item.PropType)} t{item.WeaponTier + 1}</color>  {stats}"; }
                else if (item.IsBuff) kind = "<color=#9A9A9A>tonic</color>";
                else if (item.IsArmor) kind = $"<color=#9A9A9A>{Armor.SlotNames[item.ArmorSlot].ToLowerInvariant()} armor · {Armor.DescribeStats(item)}</color>";
                else kind = $"<color=#9A9A9A>{LootTables.JunkTierName(item.WeaponClass)}</color>";
                UiKit.Text(row.transform, new Vector3(left + 0.16f, 0.025f, 0f), textW, 0.05f, 0.38f, $"{item.ColoredName}{equipped}   <color=#F5C542>{SellPrice(item)} tokens</color>", fit: true);
                UiKit.Text(row.transform, new Vector3(left + 0.16f, -0.025f, 0f), textW, 0.045f, item.IsWeapon ? 0.27f : 0.3f, kind, fit: true);
                var captured = item;
                UiKit.Button(row.transform, new Vector3(lockX, 0f, 0f), item.Locked ? "UNLOCK" : "LOCK", () => ToggleLock(captured), BtnScale);
                if (item.Locked)
                    UiKit.Text(row.transform, new Vector3(sellX - BtnW * 0.5f, 0f, 0f), BtnW, 0.05f, 0.3f, "<color=#9A9A9A>locked</color>", TextAlignmentOptions.Center);
                else if (inv.InUse(item))
                    UiKit.Text(row.transform, new Vector3(sellX - BtnW * 0.5f, 0f, 0f), BtnW, 0.05f, 0.3f, slot >= 0 ? "<color=#9A9A9A>equipped</color>" : "<color=#9A9A9A>worn</color>", TextAlignmentOptions.Center);
                else
                    UiKit.Button(row.transform, new Vector3(sellX, 0f, 0f), "SELL", () => Sell(captured), BtnScale);
            }

            var bottom = -top + 0.06f;
            UiKit.Text(_sell, new Vector3(0f, bottom, 0f), 0.3f, 0.06f, 0.35f, $"page {_sellPage + 1} / {pages}", TextAlignmentOptions.Center);
            if (pages > 1)
            {
                UiKit.Button(_sell, new Vector3(-0.22f - BtnW * 0.5f, bottom, 0f), "<", () => { _sellPage--; BuildSell(); }, BtnScale);
                UiKit.Button(_sell, new Vector3(0.22f + BtnW * 0.5f, bottom, 0f), ">", () => { _sellPage++; BuildSell(); }, BtnScale);
            }
            // SELL ALL, in the band between the last row and the bag line: junk, then weapons and
            // armor by rarity up to Rare (a Legendary is sold one at a time, on purpose). Locked,
            // equipped and worn items are never swept. The buttons say SELL so they are not taken
            // for sort buttons (report 2026-09-12); the caption carries the token totals.
            var groups = new[] { (label: "JUNK", cls: -1), (label: "COMMON", cls: 0), (label: "UNIQUE", cls: 1), (label: "RARE", cls: 2) };
            var caption = new System.Text.StringBuilder("<b>SELL ALL</b> <color=#9A9A9A>of a kind — never locked, equipped or worn:</color>");
            var gx = left + BtnW * 0.5f;
            var ggap = BtnW * 1.2f + 0.03f;
            foreach (var g in groups)
            {
                var (n, value) = SweepTotal(inv, g.cls);
                caption.Append($"   {g.label.ToLowerInvariant()} {n} · <color=#F5C542>{value} tk</color>");
                var cls = g.cls;
                UiKit.Button(_sell, new Vector3(gx, bottom + 0.18f, 0f), n > 0 ? $"SELL {n} {g.label}" : $"SELL {g.label}", () => SellAll(cls), BtnScale, enabled: n > 0);
                gx += ggap;
            }
            UiKit.Text(_sell, new Vector3(left, bottom + 0.245f, 0f), PanelWidth - 0.08f, 0.045f, 0.28f, caption.ToString(), fit: true);
            // A bigger bag, the kobold's token sink, on the row above the pager.
            var bagY = bottom + 0.09f;
            if (inv.BagLevel < BagManager.BagUpgrades.Length)
            {
                var next = BagManager.BagUpgrades[inv.BagLevel];
                var price = (int)Math.Round(next.price * ModConfig.ShopPriceMultiplier.Value);
                var bagBtnX = PanelWidth * 0.5f - 0.04f - BtnW * 0.5f;
                UiKit.Text(_sell, new Vector3(left, bagY, 0f), WidthBefore(bagBtnX, left), 0.05f, 0.32f,
                    $"<color=#9A9A9A>bag {inv.TotalWeight:0.#} / {BagManager.Capacity:0} wt   ·   {next.name} (+{next.bonus:0} wt)  <color=#F5C542>{price} tokens</color></color>", fit: true);
                UiKit.Button(_sell, new Vector3(bagBtnX, bagY, 0f), "BUY", () => { BagManager.BuyBagUpgrade(); }, BtnScale, enabled: inv.Gold >= price);
            }
            else
                UiKit.Text(_sell, new Vector3(left, bagY, 0f), PanelWidth - 0.08f, 0.05f, 0.32f, $"<color=#9A9A9A>bag {inv.TotalWeight:0.#} / {BagManager.Capacity:0} wt   ·   {BagManager.BagUpgrades[^1].name}, the biggest there is</color>", fit: true);
            if (items.Count == 0)
                UiKit.Text(_sell, new Vector3(0f, y0 - RowHeight, 0f), 0.8f, 0.06f, 0.4f, "Nothing to sell.", TextAlignmentOptions.Center);
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
            BuyTabs(top, left);
            if (_buyMode == 1) { BuildTonics(top, left); return; }
            if (_buyMode == 2) { BuildEnchant(top, left); return; }
            if (_buyMode == 3) { BuildArmor(top, left); return; }
            var restockX = PanelWidth * 0.5f - 0.04f - BtnW * 0.5f;
            UiKit.Text(_buy, new Vector3(left, top - 0.05f, 0f), WidthBefore(restockX, left), 0.06f, 0.5f,
                $"<b>WEAPONS</b>   {Tokens(inv)}   <color=#9A9A9A>{stock.Count} in stock · new in {minutesLeft} min</color>", fit: true);
            UiKit.Button(_buy, new Vector3(restockX, top - 0.05f, 0f), $"RESTOCK {Shop.RestockPrice(inv)} tk", () => { if (Shop.Restock()) Rebuild(); }, BtnScale);

            var y0 = top - 0.27f;
            var buyX = PanelWidth * 0.5f - 0.04f - BtnW * 0.5f;
            var textW = buyX - BtnW * 0.5f - 0.02f - (left + 0.16f);
            for (var i = 0; i < RowsPerPage && i < stock.Count; i++)
            {
                var item = stock[i];
                var row = new GameObject($"BuyRow_{i}"); row.transform.SetParent(_buy, false);
                row.transform.localPosition = new Vector3(0f, y0 - i * RowHeight, 0f);
                UiKit.Preview(row.transform, new Vector3(left + 0.07f, 0f, -0.03f), item, 0.11f);
                string stats = "";
                try { stats = UiKit.StatsLine(WeaponCodec.ToModule(item).GetStatsText()); } catch { }
                var afford = inv.Gold >= item.Value;
                var priceColor = afford ? "#F5C542" : "#B04040";
                UiKit.Text(row.transform, new Vector3(left + 0.16f, 0.025f, 0f), textW, 0.05f, 0.38f,
                    $"{item.ColoredName}   <color={priceColor}>{item.Value} tokens</color>", fit: true);
                UiKit.Text(row.transform, new Vector3(left + 0.16f, -0.025f, 0f), textW, 0.045f, 0.27f,
                    $"<color=#9A9A9A>{LootTables.TypeName(item.PropType)} t{item.WeaponTier + 1}</color>  {stats}", fit: true);
                var captured = item;
                UiKit.Button(row.transform, new Vector3(buyX, 0f, 0f), afford ? "BUY" : $"NEED {item.Value} tk", () => { if (Shop.Buy(captured)) Rebuild(); }, BtnScale, enabled: afford);
            }
            if (stock.Count == 0)
                UiKit.Text(_buy, new Vector3(0f, y0 - RowHeight, 0f), 0.8f, 0.06f, 0.4f, "Sold out.", TextAlignmentOptions.Center);
        }

        private static string Tokens(LootInventory inv) => $"<color=#F5C542>{inv.Gold} tokens</color>";

        /// <summary>The mode tabs on their own row: measured widths lie about the glow, so space them generously.</summary>
        private static void BuyTabs(float top, float left)
        {
            var y = top - 0.15f;
            var tabScale = BtnScale * 0.8f;
            var w = Mathf.Max(BtnW * 0.8f, 0.2f);
            var gap = w + 0.09f;
            var x = left + w * 0.5f;
            UiKit.Button(_buy, new Vector3(x, y, 0f), _buyMode == 0 ? "• WEAPONS" : "WEAPONS", () => { _buyMode = 0; BuildBuy(); }, tabScale); x += gap;
            UiKit.Button(_buy, new Vector3(x, y, 0f), _buyMode == 1 ? "• TONICS" : "TONICS", () => { _buyMode = 1; BuildBuy(); }, tabScale); x += gap;
            UiKit.Button(_buy, new Vector3(x, y, 0f), _buyMode == 2 ? "• ENCHANT" : "ENCHANT", () => { _buyMode = 2; _enchantTarget = null; _enchantHelp = false; BuildBuy(); }, tabScale); x += gap;
            UiKit.Button(_buy, new Vector3(x, y, 0f), _buyMode == 3 ? "• ARMOR" : "ARMOR", () => { _buyMode = 3; BuildBuy(); }, tabScale);
        }

        private static void BuildTonics(float top, float left)
        {
            var inv = BagManager.Inventory;
            var offered = Buffs.Offered();
            UiKit.Text(_buy, new Vector3(left, top - 0.05f, 0f), PanelWidth - 0.08f, 0.06f, 0.5f,
                $"<b>TONICS</b>   {Tokens(inv)}   <color=#9A9A9A>good for one excursion · {offered.Count} brew(s)</color>", fit: true);
            if (offered.Count == 0)
            {
                UiKit.Text(_buy, new Vector3(0f, top - 0.4f, 0f), 1.1f, 0.06f, 0.36f, "Unlock exosuit perks to use potions with those perks.", TextAlignmentOptions.Center);
                return;
            }
            var rows = 4;
            var pages = Math.Max(1, (offered.Count + rows - 1) / rows);
            _tonicPage = Math.Max(0, Math.Min(_tonicPage, pages - 1));
            var y0 = top - 0.28f;
            var start = _tonicPage * rows;
            // Three price buttons on the right; the text stops where the leftmost one begins
            // (0.9.14 drew 0.6 m of text under it, report 2026-09-12).
            var btnScale = BtnScale * 0.85f;
            var step = Mathf.Max(BtnW, 0.24f) * 0.85f + 0.05f;
            var rightX = PanelWidth * 0.5f - 0.04f - BtnW * 0.5f;
            var leftmostX = rightX - 2f * step;
            var textW = WidthBefore(leftmostX, left, 0.85f);
            for (var i = 0; i < rows && start + i < offered.Count; i++)
            {
                var def = offered[start + i];
                var row = new GameObject($"TonicRow_{i}"); row.transform.SetParent(_buy, false);
                row.transform.localPosition = new Vector3(0f, y0 - i * 0.14f, 0f);
                UiKit.Text(row.transform, new Vector3(left, 0.03f, 0f), textW, 0.05f, 0.38f, $"<color=#7FD8FF>{def.Name}</color>", fit: true);
                UiKit.Text(row.transform, new Vector3(left, -0.03f, 0f), textW, 0.045f, 0.3f, $"<color=#9A9A9A>{def.Flavor} · ×{def.Mults[0]:0.00} / ×{def.Mults[1]:0.00} / ×{def.Mults[2]:0.00}</color>", fit: true);
                var captured = def;
                var x = rightX;
                for (var t = 2; t >= 0; t--)
                {
                    var tier = t;
                    var price = Shop.TonicPrice(def, tier);
                    UiKit.Button(row.transform, new Vector3(x, 0f, 0f), $"{Buffs.TierNames[tier].ToUpperInvariant()} {price} tk", () => { if (Shop.BuyTonic(captured, tier)) Rebuild(); }, btnScale, enabled: inv.Gold >= price);
                    x -= step;
                }
            }
            var bottom = -top + 0.06f;
            UiKit.Text(_buy, new Vector3(0f, bottom, 0f), 0.3f, 0.06f, 0.35f, $"page {_tonicPage + 1} / {pages}", TextAlignmentOptions.Center);
            if (pages > 1)
            {
                UiKit.Button(_buy, new Vector3(-0.22f - BtnW * 0.5f, bottom, 0f), "<", () => { _tonicPage--; BuildBuy(); }, BtnScale);
                UiKit.Button(_buy, new Vector3(0.22f + BtnW * 0.5f, bottom, 0f), ">", () => { _tonicPage++; BuildBuy(); }, BtnScale);
            }
        }

        private static void BuildEnchant(float top, float left)
        {
            var inv = BagManager.Inventory;
            var bx = PanelWidth * 0.5f - 0.04f - BtnW * 0.5f;
            if (_enchantHelp) { BuildEnchantHelp(top, left, bx); return; }
            if (_enchantTarget != null)
                UiKit.Button(_buy, new Vector3(bx, top - 0.05f, 0f), "BACK", () => { _enchantTarget = null; BuildBuy(); }, BtnScale);
            else
                UiKit.Button(_buy, new Vector3(bx, top - 0.05f, 0f), "HELP", () => { _enchantHelp = true; _helpPage = 0; BuildBuy(); }, BtnScale);
            var headerW = WidthBefore(bx, left);
            if (!Enchanting.Ready)
            {
                UiKit.Text(_buy, new Vector3(left, top - 0.05f, 0f), headerW, 0.06f, 0.5f, "<b>ENCHANT</b>   <color=#9A9A9A>unavailable</color>", fit: true);
                UiKit.Text(_buy, new Vector3(0f, top - 0.4f, 0f), 1.1f, 0.06f, 0.34f, "Enchanting is unavailable in this game version.", TextAlignmentOptions.Center);
                return;
            }
            var target = _enchantTarget == null ? null : inv.Find(_enchantTarget);
            if (target == null)
            {
                // Every bag weapon, equipped ones included: an enchanted weapon keeps its slot.
                var weapons = new List<LootItem>();
                foreach (var w in inv.Items) if (w.IsWeapon) weapons.Add(w);
                weapons.Sort((a, b) => b.WeaponClass != a.WeaponClass ? b.WeaponClass.CompareTo(a.WeaponClass) : b.Value.CompareTo(a.Value));
                var pages = Math.Max(1, (weapons.Count + RowsPerPage - 1) / RowsPerPage);
                _enchantPage = Math.Max(0, Math.Min(_enchantPage, pages - 1));
                UiKit.Text(_buy, new Vector3(left, top - 0.05f, 0f), headerW, 0.06f, 0.5f, $"<b>ENCHANT</b>   {Tokens(inv)}   <color=#9A9A9A>pick a weapon</color>", fit: true);
                var y0 = top - 0.27f;
                var start = _enchantPage * RowsPerPage;
                var textX = left + 0.16f;
                var textW = WidthBefore(bx, textX);
                for (var i = 0; i < RowsPerPage && start + i < weapons.Count; i++)
                {
                    var item = weapons[start + i];
                    Enchanting.ReadRolledPerks(item);
                    var row = new GameObject($"EnchRow_{i}"); row.transform.SetParent(_buy, false);
                    row.transform.localPosition = new Vector3(0f, y0 - i * RowHeight, 0f);
                    UiKit.Preview(row.transform, new Vector3(left + 0.07f, 0f, -0.03f), item, 0.11f);
                    var slot = inv.EquippedSlotOf(item);
                    var equipped = slot >= 0 ? $"   <color=#F5C542>equipped: {Loadout.SlotNames[slot]}</color>" : "";
                    var price = Enchanting.Price(item);
                    var afford = inv.Gold >= price;
                    UiKit.Text(row.transform, new Vector3(textX, 0.025f, 0f), textW, 0.05f, 0.38f, $"{item.ColoredName}{equipped}", fit: true);
                    UiKit.Text(row.transform, new Vector3(textX, -0.025f, 0f), textW, 0.045f, 0.3f,
                        $"<color=#9A9A9A>slots {Enchanting.UsedSlots(item)}/{Enchanting.Slots(item.WeaponClass)}   element {(item.DamageType < 0 ? "none" : Enchanting.Elements[Math.Min(2, item.DamageType)])}   <color={(afford ? "#F5C542" : "#B04040")}>{price} tokens</color> per enchantment</color>", fit: true);
                    var captured = item;
                    UiKit.Button(row.transform, new Vector3(bx, 0f, 0f), "SELECT", () => { _enchantTarget = captured.Id; BuildBuy(); }, BtnScale);
                }
                var bottom = -top + 0.06f;
                UiKit.Text(_buy, new Vector3(0f, bottom, 0f), 0.3f, 0.06f, 0.35f, $"page {_enchantPage + 1} / {pages}", TextAlignmentOptions.Center);
                if (pages > 1)
                {
                    UiKit.Button(_buy, new Vector3(-0.22f - BtnW * 0.5f, bottom, 0f), "<", () => { _enchantPage--; BuildBuy(); }, BtnScale);
                    UiKit.Button(_buy, new Vector3(0.22f + BtnW * 0.5f, bottom, 0f), ">", () => { _enchantPage++; BuildBuy(); }, BtnScale);
                }
                if (weapons.Count == 0) UiKit.Text(_buy, new Vector3(0f, y0 - RowHeight, 0f), 0.8f, 0.06f, 0.4f, "The bag contains no weapons.", TextAlignmentOptions.Center);
                return;
            }

            Enchanting.ReadRolledPerks(target);
            var tslot = inv.EquippedSlotOf(target);
            UiKit.Text(_buy, new Vector3(left, top - 0.05f, 0f), headerW, 0.06f, 0.5f, $"<b>ENCHANT</b>   {target.ColoredName}{(tslot >= 0 ? $"   <color=#F5C542>equipped: {Loadout.SlotNames[tslot]}</color>" : "")}", fit: true);
            var tprice = Enchanting.Price(target);
            UiKit.Text(_buy, new Vector3(left, top - 0.22f, 0f), PanelWidth - 0.08f, 0.05f, 0.3f,
                $"<color=#9A9A9A>has: {Enchanting.PerkName(target.PerkA)} {Enchanting.PerkName(target.PerkB)} {Enchanting.PerkName(target.PerkC)}   element {(target.DamageType < 0 ? "none" : Enchanting.Elements[Math.Min(2, target.DamageType)])}   " +
                $"each costs <color={(inv.Gold >= tprice ? "#F5C542" : "#B04040")}>{tprice} tokens</color> · you have {inv.Gold}{(tslot >= 0 ? " · stays in your hand" : "")}</color>", fit: true);
            var options = Enchanting.Options(target);
            var y1 = top - 0.28f;
            var col = 0; var rowI = 0;
            foreach (var (label, perkId, element) in options)
            {
                var x = left + Mathf.Max(BtnW, 0.24f) * 0.5f + col * (Mathf.Max(BtnW, 0.24f) + 0.06f);
                var y = y1 - rowI * 0.08f;
                var pid = perkId; var el = element;
                UiKit.Button(_buy, new Vector3(x, y, 0f), (element >= 0 ? "+ " : "") + label, () => { var r = Enchanting.Enchant(target, pid, el); if (r != null) _enchantTarget = r.Id; Rebuild(); }, BtnScale * 0.9f, enabled: inv.Gold >= tprice);
                col++; if (col >= 3) { col = 0; rowI++; }
                if (rowI > 7) break;
            }
            if (options.Count == 0) UiKit.Text(_buy, new Vector3(0f, y1 - 0.1f, 0f), 0.8f, 0.06f, 0.4f, "Nothing more can be added to this weapon.", TextAlignmentOptions.Center);
        }

        /// <summary>
        /// The table's HELP page (report 2026-09-12: nothing said what the perks do): every
        /// enchantment with the game's own name and description for it, and the weapon types
        /// that can carry it. Paged; BACK returns to the weapon list.
        /// </summary>
        private static void BuildEnchantHelp(float top, float left, float bx)
        {
            UiKit.Button(_buy, new Vector3(bx, top - 0.05f, 0f), "BACK", () => { _enchantHelp = false; BuildBuy(); }, BtnScale);
            UiKit.Text(_buy, new Vector3(left, top - 0.05f, 0f), WidthBefore(bx, left), 0.06f, 0.5f, "<b>ENCHANT</b>   <color=#9A9A9A>what the enchantments do</color>", fit: true);
            var docs = Enchanting.Documentation();
            const int perPage = 9;
            var pages = Math.Max(1, (docs.Count + perPage - 1) / perPage);
            _helpPage = Math.Max(0, Math.Min(_helpPage, pages - 1));
            UiKit.Text(_buy, new Vector3(left, top - 0.215f, 0f), PanelWidth - 0.08f, 0.045f, 0.28f,
                $"<color=#9A9A9A>Common weapons hold 1 perk, Unique and Rare 2, Legendary 3, plus one element. The game's own words for each:</color>", fit: true);
            var y0 = top - 0.29f;
            var start = _helpPage * perPage;
            for (var i = 0; i < perPage && start + i < docs.Count; i++)
            {
                var d = docs[start + i];
                var y = y0 - i * 0.078f;
                UiKit.Text(_buy, new Vector3(left, y + 0.018f, 0f), PanelWidth - 0.08f, 0.045f, 0.32f,
                    $"<color=#7FD8FF>{d.Title}</color>   <size=80%><color=#9A9A9A>{d.Types}</color></size>", fit: true);
                UiKit.Text(_buy, new Vector3(left, y - 0.022f, 0f), PanelWidth - 0.08f, 0.04f, 0.27f, $"<color=#D0D0D0>{d.Description}</color>", fit: true);
            }
            var bottom = -top + 0.06f;
            UiKit.Text(_buy, new Vector3(0f, bottom, 0f), 0.3f, 0.06f, 0.35f, $"page {_helpPage + 1} / {pages}", TextAlignmentOptions.Center);
            if (pages > 1)
            {
                UiKit.Button(_buy, new Vector3(-0.22f - BtnW * 0.5f, bottom, 0f), "<", () => { _helpPage--; BuildBuy(); }, BtnScale);
                UiKit.Button(_buy, new Vector3(0.22f + BtnW * 0.5f, bottom, 0f), ">", () => { _helpPage++; BuildBuy(); }, BtnScale);
            }
        }

        /// <summary>
        /// Armor has no vanilla screen, so the kobold is where it is worn. One slot at a time
        /// (HEAD / CHEST / LEGS): what is worn there with TAKE OFF, then every piece in the bag
        /// for that slot with WEAR, each stat marked against the worn piece (green better, red
        /// worse, blue new). Report 2026-09-12: the mixed list gave no way to compare.
        /// </summary>
        private static void BuildArmor(float top, float left)
        {
            var inv = BagManager.Inventory;
            _armorSlot = Math.Max(0, Math.Min(2, _armorSlot));
            var bx = PanelWidth * 0.5f - 0.04f - BtnW * 0.5f;
            UiKit.Text(_buy, new Vector3(left, top - 0.05f, 0f), PanelWidth - 0.08f, 0.06f, 0.5f, "<b>ARMOR</b>   <color=#9A9A9A>compare and wear, one slot at a time</color>", fit: true);

            // Slot tabs, under the mode tabs.
            var ty = top - 0.245f;
            var tabScale = BtnScale * 0.8f;
            var tw = Mathf.Max(BtnW * 0.8f, 0.2f);
            var tx = left + tw * 0.5f;
            for (var s2 = 0; s2 < 3; s2++)
            {
                var slot = s2;
                var count = 0; foreach (var i in inv.Items) if (i.IsArmor && i.WornSlot < 0 && i.ArmorSlot == slot) count++;
                var label = (slot == _armorSlot ? "• " : "") + Armor.SlotNames[slot].ToUpperInvariant() + (count > 0 ? $" ({count})" : "");
                UiKit.Button(_buy, new Vector3(tx, ty, 0f), label, () => { _armorSlot = slot; _armorPage = 0; BuildBuy(); }, tabScale);
                tx += tw + 0.09f;
            }

            // The worn piece for this slot.
            var worn = Armor.Worn(_armorSlot);
            var wy = top - 0.36f;
            var textW = WidthBefore(bx, left);
            UiKit.Text(_buy, new Vector3(left, wy + 0.025f, 0f), textW, 0.05f, 0.36f,
                $"<color=#C9A86A>wearing:</color>   {(worn == null ? "<color=#9A9A9A>nothing</color>" : worn.ColoredName)}", fit: true);
            if (worn != null)
            {
                UiKit.Text(_buy, new Vector3(left, wy - 0.025f, 0f), textW, 0.045f, 0.28f, $"<color=#9A9A9A>{Armor.DescribeStats(worn)}</color>", fit: true);
                var capturedWorn = worn;
                UiKit.Button(_buy, new Vector3(bx, wy, 0f), "TAKE OFF", () => { Armor.Remove(capturedWorn); BuildBuy(); }, BtnScale);
            }

            // Candidates in the bag for this slot, best rarity first.
            var pieces = new List<LootItem>();
            foreach (var i in inv.Items) if (i.IsArmor && i.WornSlot < 0 && i.ArmorSlot == _armorSlot) pieces.Add(i);
            pieces.Sort((a, b) => b.WeaponClass != a.WeaponClass ? b.WeaponClass.CompareTo(a.WeaponClass) : b.Value.CompareTo(a.Value));
            const int rows = 5;
            const float rowH = 0.1f;
            var pages = Math.Max(1, (pieces.Count + rows - 1) / rows);
            _armorPage = Math.Max(0, Math.Min(_armorPage, pages - 1));
            var y1 = wy - 0.1f;
            UiKit.Text(_buy, new Vector3(left, y1 + 0.035f, 0f), PanelWidth - 0.08f, 0.045f, 0.3f,
                pieces.Count == 0 ? $"<color=#9A9A9A>No {Armor.SlotNames[_armorSlot].ToLowerInvariant()} armor in the bag.</color>"
                                  : $"<color=#9A9A9A>in the bag ({pieces.Count}){(worn == null ? "" : " — against what you wear: <color=#5BD75B>better</color> <color=#E06060>worse</color> <color=#7FD8FF>new</color>")}</color>", fit: true);
            var start = _armorPage * rows;
            for (var i = 0; i < rows && start + i < pieces.Count; i++)
            {
                var item = pieces[start + i];
                var row = new GameObject($"ArmorPiece_{i}"); row.transform.SetParent(_buy, false);
                row.transform.localPosition = new Vector3(0f, y1 - 0.04f - i * rowH, 0f);
                UiKit.Text(row.transform, new Vector3(left, 0.02f, 0f), textW, 0.05f, 0.34f, $"{item.ColoredName}{(item.Locked ? "   <color=#9A9A9A>locked</color>" : "")}", fit: true);
                UiKit.Text(row.transform, new Vector3(left, -0.028f, 0f), textW, 0.045f, 0.28f, $"<color=#9A9A9A>{Armor.Compare(item, worn)}</color>", fit: true);
                var captured = item;
                UiKit.Button(row.transform, new Vector3(bx, 0f, 0f), "WEAR", () => { Armor.Wear(captured); BuildBuy(); }, BtnScale);
            }
            var bottom = -top + 0.06f;
            if (pages > 1)
            {
                UiKit.Text(_buy, new Vector3(0f, bottom, 0f), 0.3f, 0.06f, 0.35f, $"page {_armorPage + 1} / {pages}", TextAlignmentOptions.Center);
                UiKit.Button(_buy, new Vector3(-0.22f - BtnW * 0.5f, bottom, 0f), "<", () => { _armorPage--; BuildBuy(); }, BtnScale);
                UiKit.Button(_buy, new Vector3(0.22f + BtnW * 0.5f, bottom, 0f), ">", () => { _armorPage++; BuildBuy(); }, BtnScale);
            }
        }

        public static int SellPrice(LootItem item) => Math.Max(1, (int)Math.Round(item.Value * ModConfig.SellMultiplier.Value));

        /// <summary>
        /// Is this item swept by the SELL ALL button for <paramref name="cls"/>? -1 is junk;
        /// 0–2 are weapons and armor of that rarity. Never anything locked, equipped, worn, or a
        /// tonic (drink it), never a Legendary.
        /// </summary>
        private static bool Sweepable(LootItem i, int cls)
        {
            if (i.Locked || i.IsBuff || BagManager.Inventory.InUse(i)) return false;
            if (cls < 0) return !i.IsWeapon && !i.IsArmor;
            return (i.IsWeapon || i.IsArmor) && i.WeaponClass == cls;
        }

        private static (int count, int value) SweepTotal(LootInventory inv, int cls)
        {
            var n = 0; var v = 0;
            foreach (var i in inv.Items) if (Sweepable(i, cls)) { n++; v += SellPrice(i); }
            return (n, v);
        }

        private static string SweepName(int cls) => cls < 0 ? "junk" : LootTables.ClassName(cls).ToLowerInvariant();

        private static void SellAll(int cls)
        {
            var inv = BagManager.Inventory;
            var total = 0; var n = 0;
            foreach (var j in new List<LootItem>(inv.Items))
            {
                if (!Sweepable(j, cls)) continue;
                total += SellPrice(j); n++;
                inv.Remove(j.Id);
            }
            if (n == 0) { BagManager.Toast($"No {SweepName(cls)} to sell."); return; }
            inv.Gold += total;
            inv.Save();
            BagManager.Toast($"Sold {n} {SweepName(cls)} item(s) for <color=#F5C542>{total} tokens</color>  (now {inv.Gold})");
            ReconLog.Line($"sold {n} {SweepName(cls)} for {total} -> tokens {inv.Gold}");
            Rebuild();
            BagPanel.Refresh();
        }

        /// <summary>A locked item cannot be sold, dropped or trashed until it is unlocked here.</summary>
        private static void ToggleLock(LootItem item)
        {
            var inv = BagManager.Inventory;
            var live = inv.Find(item.Id);
            if (live == null) { BagManager.Toast("Already gone."); Refresh(); return; }
            live.Locked = !live.Locked;
            inv.Save();
            BagManager.Toast(live.Locked ? $"Locked {live.ColoredName}" : $"Unlocked {live.ColoredName}");
            BuildSell();
            BagPanel.Refresh();
        }

        private static void Sell(LootItem item)
        {
            var inv = BagManager.Inventory;
            var live = inv.Find(item.Id);
            if (live == null) { BagManager.Toast("Already gone."); Refresh(); return; }
            if (inv.EquippedSlotOf(live) >= 0) { BagManager.Toast("Unequip it first."); return; }
            if (live.WornSlot >= 0) { BagManager.Toast("Take it off first."); return; }
            if (live.Locked) { BagManager.Toast("Locked."); return; }
            var price = SellPrice(live);
            inv.Remove(live.Id);
            inv.Gold += price;
            inv.Save();
            BagManager.Toast($"Sold {live.ColoredName} for <color=#F5C542>{price} tokens</color>  (now {inv.Gold})");
            ReconLog.Line($"sold {live.Name} for {price} -> tokens {inv.Gold}");
            Rebuild();
            BagPanel.Refresh();
        }


    }
}
