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
        private static int _sellPage, _tonicPage, _enchantPage;
        private static int _buyMode;            // 0 weapons, 1 tonics, 2 enchant, 3 armor
        private static string _enchantTarget;   // bag item id being enchanted, or null for the list

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
                $"<b>SELL</b>   {inv.Items.Count} item(s)   <color=#F5C542>{inv.Gold} tokens</color>");

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
                var equipped = item.EquippedSlot >= 0 ? $"   <color=#F5C542>equipped: {Loadout.SlotNames[item.EquippedSlot]}</color>" : "";
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
                else if (item.EquippedSlot < 0 && item.WornSlot < 0)
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
            // equipped and worn items are never swept. The caption carries the token totals.
            var groups = new[] { (label: "JUNK", cls: -1), (label: "COMMON", cls: 0), (label: "UNIQUE", cls: 1), (label: "RARE", cls: 2) };
            var caption = new System.Text.StringBuilder("<color=#9A9A9A>sell all (unlocked):</color>");
            var gx = left + BtnW * 0.5f;
            var ggap = BtnW * 1.2f + 0.03f;
            foreach (var g in groups)
            {
                var (n, value) = SweepTotal(inv, g.cls);
                caption.Append($"   {g.label.ToLowerInvariant()} {n} · <color=#F5C542>{value} tk</color>");
                var cls = g.cls;
                UiKit.Button(_sell, new Vector3(gx, bottom + 0.18f, 0f), $"{g.label} ×{n}", () => SellAll(cls), BtnScale, enabled: n > 0);
                gx += ggap;
            }
            UiKit.Text(_sell, new Vector3(left, bottom + 0.245f, 0f), PanelWidth - 0.08f, 0.045f, 0.28f, caption.ToString(), fit: true);
            // A bigger bag, the kobold's token sink, on the row above the pager.
            var bagY = bottom + 0.09f;
            if (inv.BagLevel < BagManager.BagUpgrades.Length)
            {
                var next = BagManager.BagUpgrades[inv.BagLevel];
                var price = (int)Math.Round(next.price * ModConfig.ShopPriceMultiplier.Value);
                UiKit.Text(_sell, new Vector3(left, bagY, 0f), 0.8f, 0.05f, 0.32f,
                    $"<color=#9A9A9A>bag {inv.TotalWeight:0.#} / {BagManager.Capacity:0} wt   ·   {next.name} (+{next.bonus:0} wt)  <color=#F5C542>{price} tokens</color></color>");
                UiKit.Button(_sell, new Vector3(PanelWidth * 0.5f - 0.04f - BtnW * 0.5f, bagY, 0f), $"BUY {next.name.ToUpperInvariant()}", () => { BagManager.BuyBagUpgrade(); }, BtnScale, enabled: inv.Gold >= price);
            }
            else
                UiKit.Text(_sell, new Vector3(left, bagY, 0f), 0.8f, 0.05f, 0.32f, $"<color=#9A9A9A>bag {inv.TotalWeight:0.#} / {BagManager.Capacity:0} wt   ·   {BagManager.BagUpgrades[^1].name}</color>");
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
            UiKit.Text(_buy, new Vector3(left, top - 0.05f, 0f), PanelWidth - 0.08f, 0.06f, 0.5f,
                $"<b>WEAPONS</b>   {stock.Count} in stock   <color=#9A9A9A>new stock in {minutesLeft} min</color>");
            UiKit.Button(_buy, new Vector3(PanelWidth * 0.5f - 0.04f - BtnW * 0.5f, top - 0.05f, 0f), $"RESTOCK {Shop.RestockPrice(inv)} tk", () => { if (Shop.Restock()) Rebuild(); }, BtnScale);

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
            UiKit.Button(_buy, new Vector3(x, y, 0f), _buyMode == 2 ? "• ENCHANT" : "ENCHANT", () => { _buyMode = 2; _enchantTarget = null; BuildBuy(); }, tabScale); x += gap;
            UiKit.Button(_buy, new Vector3(x, y, 0f), _buyMode == 3 ? "• ARMOR" : "ARMOR", () => { _buyMode = 3; BuildBuy(); }, tabScale);
        }

        private static void BuildTonics(float top, float left)
        {
            var inv = BagManager.Inventory;
            var offered = Buffs.Offered();
            UiKit.Text(_buy, new Vector3(left, top - 0.05f, 0f), PanelWidth - 0.08f, 0.06f, 0.5f,
                $"<b>TONICS</b>   (good for one excursion)   <color=#9A9A9A>{offered.Count} brew(s)</color>");
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
            for (var i = 0; i < rows && start + i < offered.Count; i++)
            {
                var def = offered[start + i];
                var row = new GameObject($"TonicRow_{i}"); row.transform.SetParent(_buy, false);
                row.transform.localPosition = new Vector3(0f, y0 - i * 0.14f, 0f);
                UiKit.Text(row.transform, new Vector3(left, 0.03f, 0f), 0.6f, 0.05f, 0.38f, $"<color=#7FD8FF>{def.Name}</color>   <size=80%><color=#9A9A9A>{def.Flavor}</color></size>");
                UiKit.Text(row.transform, new Vector3(left, -0.03f, 0f), 0.6f, 0.045f, 0.3f, $"<color=#9A9A9A>×{def.Mults[0]:0.00} / ×{def.Mults[1]:0.00} / ×{def.Mults[2]:0.00}</color>");
                var captured = def;
                var x = PanelWidth * 0.5f - 0.04f - BtnW * 0.5f;
                for (var t = 2; t >= 0; t--)
                {
                    var tier = t;
                    var price = Shop.TonicPrice(def, tier);
                    UiKit.Button(row.transform, new Vector3(x, 0f, 0f), $"{Buffs.TierNames[tier].ToUpperInvariant()} {price} tk", () => { if (Shop.BuyTonic(captured, tier)) BuildBuy(); }, BtnScale * 0.85f, enabled: inv.Gold >= price);
                    x -= Mathf.Max(BtnW, 0.24f) * 0.85f + 0.05f;
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
            if (_enchantTarget != null)
                UiKit.Button(_buy, new Vector3(PanelWidth * 0.5f - 0.04f - BtnW * 0.5f, top - 0.05f, 0f), "BACK", () => { _enchantTarget = null; BuildBuy(); }, BtnScale);
            if (!Enchanting.Ready)
            {
                UiKit.Text(_buy, new Vector3(left, top - 0.05f, 0f), PanelWidth - 0.08f, 0.06f, 0.5f, "<b>ENCHANT</b>   <color=#9A9A9A>unavailable</color>");
                UiKit.Text(_buy, new Vector3(0f, top - 0.4f, 0f), 1.1f, 0.06f, 0.34f, "Enchanting is unavailable in this game version.", TextAlignmentOptions.Center);
                return;
            }
            var target = _enchantTarget == null ? null : inv.Find(_enchantTarget);
            if (target == null)
            {
                var weapons = new List<LootItem>();
                foreach (var w in inv.Items) if (w.IsWeapon && w.EquippedSlot < 0) weapons.Add(w);
                weapons.Sort((a, b) => b.WeaponClass != a.WeaponClass ? b.WeaponClass.CompareTo(a.WeaponClass) : b.Value.CompareTo(a.Value));
                var pages = Math.Max(1, (weapons.Count + RowsPerPage - 1) / RowsPerPage);
                _enchantPage = Math.Max(0, Math.Min(_enchantPage, pages - 1));
                UiKit.Text(_buy, new Vector3(left, top - 0.05f, 0f), PanelWidth - 0.08f, 0.06f, 0.5f, $"<b>ENCHANT</b>   pick a weapon   <color=#9A9A9A>tokens + a curio or artifact</color>");
                var y0 = top - 0.27f;
                var start = _enchantPage * RowsPerPage;
                var bx = PanelWidth * 0.5f - 0.04f - BtnW * 0.5f;
                for (var i = 0; i < RowsPerPage && start + i < weapons.Count; i++)
                {
                    var item = weapons[start + i];
                    Enchanting.ReadRolledPerks(item);
                    var row = new GameObject($"EnchRow_{i}"); row.transform.SetParent(_buy, false);
                    row.transform.localPosition = new Vector3(0f, y0 - i * RowHeight, 0f);
                    UiKit.Preview(row.transform, new Vector3(left + 0.07f, 0f, -0.03f), item, 0.11f);
                    UiKit.Text(row.transform, new Vector3(left + 0.16f, 0.025f, 0f), 0.7f, 0.05f, 0.38f, $"{item.ColoredName}");
                    UiKit.Text(row.transform, new Vector3(left + 0.16f, -0.025f, 0f), 0.7f, 0.045f, 0.3f,
                        $"<color=#9A9A9A>slots {Enchanting.UsedSlots(item)}/{Enchanting.Slots(item.WeaponClass)}   element {(item.DamageType < 0 ? "none" : Enchanting.Elements[Math.Min(2, item.DamageType)])}   {Enchanting.Price(item)} tokens + {LootTables.JunkTierName(Enchanting.ReagentTier(item))}</color>");
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
                if (weapons.Count == 0) UiKit.Text(_buy, new Vector3(0f, y0 - RowHeight, 0f), 0.8f, 0.06f, 0.4f, "The bag contains no unequipped weapons.", TextAlignmentOptions.Center);
                return;
            }

            Enchanting.ReadRolledPerks(target);
            UiKit.Text(_buy, new Vector3(left, top - 0.05f, 0f), PanelWidth - 0.08f, 0.06f, 0.5f, $"<b>ENCHANT</b>   {target.ColoredName}");
            var reagent = Enchanting.FindReagent(inv, Enchanting.ReagentTier(target));
            UiKit.Text(_buy, new Vector3(left, top - 0.22f, 0f), PanelWidth - 0.08f, 0.05f, 0.3f,
                $"<color=#9A9A9A>has: {Enchanting.PerkName(target.PerkA)} {Enchanting.PerkName(target.PerkB)} {Enchanting.PerkName(target.PerkC)}   element {(target.DamageType < 0 ? "none" : Enchanting.Elements[Math.Min(2, target.DamageType)])}   " +
                $"price {Enchanting.Price(target)} tokens + {(reagent == null ? "<color=#B04040>no reagent</color>" : reagent.Name)}</color>");
            var options = Enchanting.Options(target);
            var y1 = top - 0.28f;
            var col = 0; var rowI = 0;
            foreach (var (label, perkId, element) in options)
            {
                var x = left + Mathf.Max(BtnW, 0.24f) * 0.5f + col * (Mathf.Max(BtnW, 0.24f) + 0.06f);
                var y = y1 - rowI * 0.08f;
                var pid = perkId; var el = element;
                UiKit.Button(_buy, new Vector3(x, y, 0f), (element >= 0 ? "+ " : "") + label, () => { var r = Enchanting.Enchant(target, pid, el); if (r != null) _enchantTarget = r.Id; BuildBuy(); }, BtnScale * 0.9f);
                col++; if (col >= 3) { col = 0; rowI++; }
                if (rowI > 7) break;
            }
            if (options.Count == 0) UiKit.Text(_buy, new Vector3(0f, y1 - 0.1f, 0f), 0.8f, 0.06f, 0.4f, "Nothing more can be added to this weapon.", TextAlignmentOptions.Center);
        }

        /// <summary>Armor has no vanilla screen, so the shopkeeper is where it is worn: three slots, then the pieces in the bag.</summary>
        private static void BuildArmor(float top, float left)
        {
            var inv = BagManager.Inventory;
            UiKit.Text(_buy, new Vector3(left, top - 0.05f, 0f), PanelWidth - 0.08f, 0.06f, 0.5f, "<b>ARMOR</b>   what you are wearing");
            var bx = PanelWidth * 0.5f - 0.04f - BtnW * 0.5f;
            var y = top - 0.26f;
            for (var slot = 0; slot < 3; slot++)
            {
                var worn = Armor.Worn(slot);
                var row = new GameObject($"ArmorSlot_{slot}"); row.transform.SetParent(_buy, false);
                row.transform.localPosition = new Vector3(0f, y - slot * 0.1f, 0f);
                UiKit.Text(row.transform, new Vector3(left, 0.02f, 0f), 0.75f, 0.05f, 0.36f,
                    $"<b>{Armor.SlotNames[slot]}</b>   {(worn == null ? "<color=#9A9A9A>nothing</color>" : worn.ColoredName)}");
                if (worn != null)
                {
                    UiKit.Text(row.transform, new Vector3(left, -0.028f, 0f), 0.75f, 0.045f, 0.28f, $"<color=#9A9A9A>{Armor.DescribeStats(worn)}</color>");
                    var captured = worn;
                    UiKit.Button(row.transform, new Vector3(bx, 0f, 0f), "TAKE OFF", () => { Armor.Remove(captured); BuildBuy(); }, BtnScale);
                }
            }
            var pieces = new List<LootItem>();
            foreach (var i in inv.Items) if (i.IsArmor && i.WornSlot < 0) pieces.Add(i);
            pieces.Sort((a, b) => a.ArmorSlot != b.ArmorSlot ? a.ArmorSlot.CompareTo(b.ArmorSlot) : b.WeaponClass.CompareTo(a.WeaponClass));
            var y1 = y - 0.36f;
            UiKit.Text(_buy, new Vector3(left, y1 + 0.06f, 0f), 0.8f, 0.05f, 0.32f, pieces.Count == 0 ? "<color=#9A9A9A>No armor in the bag.</color>" : "<color=#9A9A9A>in the bag:</color>");
            var rows = 4;
            for (var i = 0; i < rows && i < pieces.Count; i++)
            {
                var item = pieces[i];
                var row = new GameObject($"ArmorPiece_{i}"); row.transform.SetParent(_buy, false);
                row.transform.localPosition = new Vector3(0f, y1 - i * 0.1f, 0f);
                UiKit.Text(row.transform, new Vector3(left, 0.02f, 0f), 0.75f, 0.05f, 0.34f, $"{item.ColoredName}   <size=80%><color=#9A9A9A>{Armor.SlotNames[item.ArmorSlot].ToLowerInvariant()}</color></size>");
                UiKit.Text(row.transform, new Vector3(left, -0.028f, 0f), 0.75f, 0.045f, 0.28f, $"<color=#9A9A9A>{Armor.DescribeStats(item)}</color>");
                var captured = item;
                UiKit.Button(row.transform, new Vector3(bx, 0f, 0f), "WEAR", () => { Armor.Wear(captured); BuildBuy(); }, BtnScale);
            }
            if (pieces.Count > rows)
                UiKit.Text(_buy, new Vector3(left, y1 - rows * 0.1f, 0f), 0.8f, 0.05f, 0.3f, $"<color=#9A9A9A>… and {pieces.Count - rows} more in the bag (open it with the stick or [ to see them all).</color>");
        }

        public static int SellPrice(LootItem item) => Math.Max(1, (int)Math.Round(item.Value * ModConfig.SellMultiplier.Value));

        /// <summary>
        /// Is this item swept by the SELL ALL button for <paramref name="cls"/>? -1 is junk;
        /// 0–2 are weapons and armor of that rarity. Never anything locked, equipped, worn, or a
        /// tonic (drink it), never a Legendary.
        /// </summary>
        private static bool Sweepable(LootItem i, int cls)
        {
            if (i.Locked || i.EquippedSlot >= 0 || i.WornSlot >= 0 || i.IsBuff) return false;
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
            if (live.EquippedSlot >= 0) { BagManager.Toast("Unequip it first."); return; }
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
