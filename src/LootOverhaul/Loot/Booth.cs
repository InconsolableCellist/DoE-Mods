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
        // Readability pass (report 2026-10-08: tiny text, cramped rows, hard to steer): roomier rows,
        // larger type, a price column of its own, striped rows, and the whole stall scaled up.
        private const float RowHeight = 0.14f;
        private const float PanelWidth = 1.45f;
        private const float PanelScale = 1.1f;
        private const float BtnScale = 0.36f;
        private const float TitleSize = 0.46f;
        private const float SubSize = 0.34f;
        private const float HeadSize = 0.56f;
        private const float PriceW = 0.2f;
        private static readonly Color StripeSell = new Color(0.16f, 0.12f, 0.08f, 1f);
        private static readonly Color StripeBuy = new Color(0.08f, 0.12f, 0.18f, 1f);
        private static float BtnW => UiKit.ButtonSize.x * BtnScale;

        /// <summary>Every other row gets a lit stripe so the eye can follow a row from name to button.</summary>
        private static void Stripe(Transform row, int i, Color color)
        {
            if (i % 2 == 0) UiKit.Bar(row, new Vector3(0f, 0f, 0.006f), PanelWidth - 0.03f, RowHeight - 0.008f, color);
        }

        /// <summary>A price centred on <paramref name="centerX"/>, in the middle of the row.</summary>
        private static void PriceText(Transform row, float centerX, string text)
            => UiKit.Text(row, new Vector3(centerX, 0f, 0f), PriceW, 0.05f, 0.4f, text, TextAlignmentOptions.Center, fit: true);

        /// <summary>One stat per line for a bag item.</summary>
        private static string StatList(LootItem item)
        {
            if (item.IsWeapon) { try { return UiKit.StatsList(WeaponCodec.ToModule(item).GetStatsText()); } catch { return ""; } }
            if (item.IsBuff) return $"<color=#B0B0B0>{Buffs.Effect(item.BuffStat, item.BuffMult)}</color>";
            if (item.IsArmor) return "<color=#B0B0B0>" + Armor.DescribeStatsShort(item).Replace(", ", "\n") + "</color>";
            return $"<color=#B0B0B0>{LootTables.JunkTierName(item.WeaponClass)}</color>";
        }
        private static GameObject _root;
        private static Transform _sell, _buy;
        private static int _sellPage, _tonicPage, _enchantPage, _helpPage, _armorPage;
        private static int _buyMode;            // 0 weapons, 1 tonics, 2 enchant, 3 armor
        private static int _armorSlot;          // ARMOR tab: which slot is being compared (0 head, 1 chest, 2 legs)
        private static bool _enchantHelp;       // ENCHANT tab: the "what do they do" page
        private static string _enchantTarget;   // bag item id being enchanted, or null for the list
        private static int _enchantKind = -1;   // ENCHANT tab: -1 the menu, 0 weapon, 1 armor
        private static int _upSlot;             // armor upgrade page: which slot (0 head, 1 chest, 2 legs)
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
            _sell.localPosition = new Vector3(-0.90f, 1.4f, 0f);
            _sell.localRotation = Quaternion.Euler(0f, -18f, 0f);
            _sell.localScale = Vector3.one * PanelScale;
            _buy.localPosition = new Vector3(0.90f, 1.4f, 0f);
            _buy.localRotation = Quaternion.Euler(0f, 18f, 0f);
            _buy.localScale = Vector3.one * PanelScale;
            UiKit.Text(_root.transform, new Vector3(0f, 2.27f, 0f), 1.8f, 0.15f, 0.9f, "<b>KOBOLD TRAVELER</b>", TextAlignmentOptions.Center);
            UiKit.Text(_root.transform, new Vector3(0f, 2.14f, 0f), 1.8f, 0.06f, 0.4f, "<color=#B0B0B0>shinies from the dungeon for tokens</color>", TextAlignmentOptions.Center);
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
            UiKit.Text(_sell, new Vector3(left, top - 0.05f, 0f), PanelWidth - 0.08f, 0.06f, HeadSize,
                $"<b>SELL</b>   {inv.Items.Count} item(s)   <color=#F5C542>{inv.Gold} tokens</color>", fit: true);

            var y0 = top - 0.19f;
            var start = _sellPage * perPage;
            // Right side, from the panel edge: SELL button, padlock, the price (centred), then the text.
            var sellX = PanelWidth * 0.5f - 0.04f - BtnW * 0.5f;
            var lockX = sellX - BtnW * 0.6f - 0.07f;
            var priceX = lockX - 0.07f - PriceW * 0.5f;
            var textStart = left + UiKit.TextX;
            var textEnd = priceX - PriceW * 0.5f - 0.02f;
            var nameW = (textEnd - textStart) * 0.54f;     // name on the left, one stat per line on the right
            var statX = textStart + nameW + 0.02f;
            var statW = textEnd - statX;
            for (var i = 0; i < RowsPerPage - 1 && start + i < items.Count; i++)
            {
                var item = items[start + i];
                var row = new GameObject($"SellRow_{i}"); row.transform.SetParent(_sell, false);
                row.transform.localPosition = new Vector3(0f, y0 - i * RowHeight, 0f);
                Stripe(row.transform, i, StripeSell);
                UiKit.Preview(row.transform, new Vector3(left + 0.055f, 0f, -0.03f), item, 0.1f);
                UiKit.TypeTag(row.transform, new Vector3(left + UiKit.TagX, 0f, 0f), item);
                var slot = inv.EquippedSlotOf(item);
                UiKit.Text(row.transform, new Vector3(textStart, 0f, 0f), nameW, 0.09f, TitleSize, item.ColoredName, fit: true, lines: 2);
                UiKit.Text(row.transform, new Vector3(statX, 0f, 0f), statW, 0.13f, 0.26f, StatList(item), fit: true, lines: 4);
                PriceText(row.transform, priceX, $"<color=#F5C542>{SellPrice(item)} tk</color>");
                var captured = item;
                UiKit.LockIcon(row.transform, new Vector3(lockX, 0f, 0f), item.Locked, () => ToggleLock(captured));
                // State text sits where the SELL button would be.
                if (item.Locked)
                    UiKit.Text(row.transform, new Vector3(sellX, 0f, 0f), 0.15f, 0.05f, 0.3f, "<color=#B0B0B0>locked</color>", TextAlignmentOptions.Center, fit: true);
                else if (inv.InUse(item))
                    UiKit.Text(row.transform, new Vector3(sellX, 0f, 0f), 0.15f, 0.05f, 0.3f,
                        slot >= 0 ? "<color=#F5C542>equipped</color>" : "<color=#C9A86A>worn</color>", TextAlignmentOptions.Center, fit: true);
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
            var caption = new System.Text.StringBuilder("<b>SELL ALL</b> <color=#B0B0B0>of a kind — never locked, equipped or worn:</color>");
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
            UiKit.Text(_sell, new Vector3(left, bottom + 0.245f, 0f), PanelWidth - 0.08f, 0.045f, 0.32f, caption.ToString(), fit: true);
            // A bigger bag, the kobold's token sink, on the row above the pager.
            var bagY = bottom + 0.09f;
            if (inv.BagLevel < BagManager.BagUpgrades.Length)
            {
                var next = BagManager.BagUpgrades[inv.BagLevel];
                var price = (int)Math.Round(next.price * ModConfig.ShopPriceMultiplier.Value);
                var bagBtnX = PanelWidth * 0.5f - 0.04f - BtnW * 0.5f;
                UiKit.Text(_sell, new Vector3(left, bagY, 0f), WidthBefore(bagBtnX, left), 0.05f, 0.36f,
                    $"<color=#B0B0B0>bag {inv.TotalWeight:0.#} / {BagManager.Capacity:0} wt   ·   {next.name} (+{next.bonus:0} wt)  <color=#F5C542>{price} tokens</color></color>", fit: true);
                UiKit.Button(_sell, new Vector3(bagBtnX, bagY, 0f), "BUY", () => { BagManager.BuyBagUpgrade(); }, BtnScale, enabled: inv.Gold >= price);
            }
            else
                UiKit.Text(_sell, new Vector3(left, bagY, 0f), PanelWidth - 0.08f, 0.05f, 0.36f, $"<color=#B0B0B0>bag {inv.TotalWeight:0.#} / {BagManager.Capacity:0} wt   ·   {BagManager.BagUpgrades[^1].name}, the biggest there is</color>", fit: true);
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
            UiKit.Text(_buy, new Vector3(left, top - 0.05f, 0f), WidthBefore(restockX, left), 0.06f, HeadSize,
                $"<b>WEAPONS</b>   {Tokens(inv)}   <color=#B0B0B0>{stock.Count} in stock · new in {minutesLeft} min</color>", fit: true);
            UiKit.Button(_buy, new Vector3(restockX, top - 0.05f, 0f), $"RESTOCK {Shop.RestockPrice(inv)} tk", () => { if (Shop.Restock()) Rebuild(); }, BtnScale);

            var y0 = top - 0.27f;
            var buyX = PanelWidth * 0.5f - 0.04f - BtnW * 0.5f;
            var priceX = buyX - BtnW * 0.6f - 0.02f - PriceW * 0.5f;
            var textStart = left + UiKit.TextX;
            var textW = priceX - PriceW * 0.5f - 0.02f - textStart;
            for (var i = 0; i < RowsPerPage && i < stock.Count; i++)
            {
                var item = stock[i];
                var row = new GameObject($"BuyRow_{i}"); row.transform.SetParent(_buy, false);
                row.transform.localPosition = new Vector3(0f, y0 - i * RowHeight, 0f);
                Stripe(row.transform, i, StripeBuy);
                UiKit.Preview(row.transform, new Vector3(left + 0.055f, 0f, -0.03f), item, 0.1f);
                UiKit.TypeTag(row.transform, new Vector3(left + UiKit.TagX, 0f, 0f), item);
                string stats = "";
                try { stats = UiKit.StatsLine(WeaponCodec.ToModule(item).GetStatsText()); } catch { }
                var afford = inv.Gold >= item.Value;
                var priceColor = afford ? "#F5C542" : "#E06060";
                UiKit.Text(row.transform, new Vector3(textStart, 0.03f, 0f), textW, 0.05f, TitleSize, item.ColoredName, fit: true);
                UiKit.Text(row.transform, new Vector3(textStart, -0.03f, 0f), textW, 0.045f, SubSize, stats, fit: true);
                PriceText(row.transform, priceX, $"<color={priceColor}>{item.Value} tk</color>");
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
            var tabScale = BtnScale * 0.9f;
            var w = Mathf.Max(BtnW * 0.9f, 0.22f);
            var gap = w + 0.1f;
            var x = left + w * 0.5f;
            // A gold bar under the open tab and a rule under the row, so the current page is never in doubt.
            UiKit.Bar(_buy, new Vector3(left + w * 0.5f + _buyMode * gap, y - 0.045f, 0.004f), w + 0.04f, 0.005f, new Color(0.96f, 0.77f, 0.26f, 1f));
            UiKit.Bar(_buy, new Vector3(0f, y - 0.058f, 0.004f), PanelWidth - 0.04f, 0.004f, new Color(0.25f, 0.3f, 0.4f, 1f));
            UiKit.Button(_buy, new Vector3(x, y, 0f), _buyMode == 0 ? "• WEAPONS" : "WEAPONS", () => { _buyMode = 0; BuildBuy(); }, tabScale); x += gap;
            UiKit.Button(_buy, new Vector3(x, y, 0f), _buyMode == 1 ? "• TONICS" : "TONICS", () => { _buyMode = 1; BuildBuy(); }, tabScale); x += gap;
            UiKit.Button(_buy, new Vector3(x, y, 0f), _buyMode == 2 ? "• ENCHANT" : "ENCHANT", () => { _buyMode = 2; _enchantTarget = null; _enchantHelp = false; _enchantKind = -1; BuildBuy(); }, tabScale); x += gap;
            UiKit.Button(_buy, new Vector3(x, y, 0f), _buyMode == 3 ? "• ARMOR" : "ARMOR", () => { _buyMode = 3; BuildBuy(); }, tabScale);
        }

        private static void BuildTonics(float top, float left)
        {
            var inv = BagManager.Inventory;
            var offered = Buffs.Offered();
            UiKit.Text(_buy, new Vector3(left, top - 0.05f, 0f), PanelWidth - 0.08f, 0.06f, HeadSize,
                $"<b>TONICS</b>   {Tokens(inv)}   <color=#B0B0B0>good for one excursion · {offered.Count} brew(s)</color>", fit: true);
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
                Stripe(row.transform, i, StripeBuy);
                UiKit.Text(row.transform, new Vector3(left, 0.03f, 0f), textW, 0.05f, TitleSize, $"<color=#7FD8FF>{def.Name}</color>", fit: true);
                UiKit.Text(row.transform, new Vector3(left, -0.03f, 0f), textW, 0.045f, SubSize, $"<color=#B0B0B0>{def.Flavor} · {Buffs.Tiers(def)}</color>", fit: true);
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
            if (_enchantKind < 0) { BuildEnchantMenu(top, left); return; }
            if (_enchantKind == 1) { BuildEnchantArmor(top, left, bx); return; }
            if (_enchantHelp) { BuildEnchantHelp(top, left, bx); return; }
            var headerW = WidthBefore(bx, left);
            if (_enchantTarget != null)
                UiKit.Button(_buy, new Vector3(bx, top - 0.05f, 0f), "BACK", () => { _enchantTarget = null; BuildBuy(); }, BtnScale);
            else
            {
                UiKit.Button(_buy, new Vector3(bx, top - 0.05f, 0f), "HELP", () => { _enchantHelp = true; _helpPage = 0; BuildBuy(); }, BtnScale);
                var menuX = bx - BtnW - 0.04f;
                UiKit.Button(_buy, new Vector3(menuX, top - 0.05f, 0f), "MENU", () => { _enchantKind = -1; BuildBuy(); }, BtnScale);
                headerW = WidthBefore(menuX, left);
            }
            if (!Enchanting.Ready)
            {
                UiKit.Text(_buy, new Vector3(left, top - 0.05f, 0f), headerW, 0.06f, HeadSize, "<b>ENCHANT</b>   <color=#B0B0B0>unavailable</color>", fit: true);
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
                UiKit.Text(_buy, new Vector3(left, top - 0.05f, 0f), headerW, 0.06f, HeadSize, $"<b>ENCHANT</b>   {Tokens(inv)}   <color=#B0B0B0>pick a weapon</color>", fit: true);
                var y0 = top - 0.27f;
                var start = _enchantPage * RowsPerPage;
                var textX = left + UiKit.TextX;
                var textW = WidthBefore(bx, textX);
                for (var i = 0; i < RowsPerPage && start + i < weapons.Count; i++)
                {
                    var item = weapons[start + i];
                    Enchanting.ReadRolledPerks(item);
                    var row = new GameObject($"EnchRow_{i}"); row.transform.SetParent(_buy, false);
                    row.transform.localPosition = new Vector3(0f, y0 - i * RowHeight, 0f);
                    Stripe(row.transform, i, StripeBuy);
                    UiKit.Preview(row.transform, new Vector3(left + 0.055f, 0f, -0.03f), item, 0.1f);
                    UiKit.TypeTag(row.transform, new Vector3(left + UiKit.TagX, 0f, 0f), item);
                    var slot = inv.EquippedSlotOf(item);
                    var equipped = slot >= 0 ? $"   <color=#F5C542>equipped: {Loadout.SlotNames[slot]}</color>" : "";
                    var price = Enchanting.Price(item);
                    var afford = inv.Gold >= price;
                    UiKit.Text(row.transform, new Vector3(textX, 0.03f, 0f), textW, 0.05f, TitleSize, $"{item.ColoredName}{equipped}", fit: true);
                    UiKit.Text(row.transform, new Vector3(textX, -0.03f, 0f), textW, 0.045f, SubSize,
                        $"<color=#B0B0B0>slots {Enchanting.UsedSlots(item)}/{Enchanting.Slots(item.WeaponClass)}   element {(item.DamageType < 0 ? "none" : Enchanting.Elements[Math.Min(2, item.DamageType)])}   <color={(afford ? "#F5C542" : "#E06060")}>{price} tokens</color> per enchantment</color>", fit: true);
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
            UiKit.Text(_buy, new Vector3(left, top - 0.05f, 0f), headerW, 0.06f, HeadSize, $"<b>ENCHANT</b>   {target.ColoredName}{(tslot >= 0 ? $"   <color=#F5C542>equipped: {Loadout.SlotNames[tslot]}</color>" : "")}", fit: true);
            var tprice = Enchanting.Price(target);
            UiKit.Text(_buy, new Vector3(left, top - 0.22f, 0f), PanelWidth - 0.08f, 0.05f, 0.34f,
                $"<color=#B0B0B0>has: {Enchanting.PerkName(target.PerkA)} {Enchanting.PerkName(target.PerkB)} {Enchanting.PerkName(target.PerkC)}   element {(target.DamageType < 0 ? "none" : Enchanting.Elements[Math.Min(2, target.DamageType)])}   " +
                $"each costs <color={(inv.Gold >= tprice ? "#F5C542" : "#E06060")}>{tprice} tokens</color> · you have {inv.Gold}{(tslot >= 0 ? " · stays in your hand" : "")}</color>", fit: true);
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

        /// <summary>ENCHANT's first page: a weapon or your armor? Two cards, one word each.</summary>
        private static void BuildEnchantMenu(float top, float left)
        {
            var inv = BagManager.Inventory;
            UiKit.Text(_buy, new Vector3(left, top - 0.05f, 0f), PanelWidth - 0.08f, 0.06f, HeadSize,
                $"<b>ENCHANT</b>   {Tokens(inv)}", fit: true);
            var cardW = (PanelWidth - 0.14f) / 2f;
            var cardH = 0.6f;
            var cy = top - 0.65f;
            var cards = new[]
            {
                (x: -cardW * 0.5f - 0.03f, title: "WEAPON", color: "#7FD8FF", line: "Add Perks and Elements", kind: 0),
                (x: cardW * 0.5f + 0.03f, title: "ARMOR", color: "#F5C542", line: "Raise Stats", kind: 1),
            };
            foreach (var c in cards)
            {
                UiKit.Bar(_buy, new Vector3(c.x, cy, 0.006f), cardW, cardH, StripeBuy);
                UiKit.Bar(_buy, new Vector3(c.x, cy + cardH * 0.5f - 0.006f, 0.004f), cardW, 0.006f, ColorFromHex(c.color));
                UiKit.Text(_buy, new Vector3(c.x, cy + 0.15f, 0f), cardW - 0.06f, 0.07f, 0.7f, $"<color={c.color}><b>{c.title}</b></color>", TextAlignmentOptions.Center, fit: true);
                UiKit.Text(_buy, new Vector3(c.x, cy + 0.03f, 0f), cardW - 0.06f, 0.05f, 0.4f, $"<color=#D0D0D0>{c.line}</color>", TextAlignmentOptions.Center, fit: true);
                var kind = c.kind;
                UiKit.Button(_buy, new Vector3(c.x, cy - 0.17f, 0f), c.title, () => { _enchantKind = kind; _enchantTarget = null; _enchantHelp = false; BuildBuy(); }, BtnScale * 1.2f);
            }
        }

        private static Color ColorFromHex(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.white;

        /// <summary>
        /// ENCHANT / ARMOR: the three slots, then the piece worn in the open one (name, rarity,
        /// sell price) and a card per stat: current, next and max as a percentage, the rarity's
        /// range as a slim bar with "5/13 Upgrades", the price beside an up-arrow button that raises
        /// that one stat by 0.01. The sell price never changes.
        /// </summary>
        private static void BuildEnchantArmor(float top, float left, float bx)
        {
            var inv = BagManager.Inventory;
            UiKit.Button(_buy, new Vector3(bx, top - 0.05f, 0f), "MENU", () => { _enchantKind = -1; BuildBuy(); }, BtnScale);
            UiKit.Text(_buy, new Vector3(left, top - 0.05f, 0f), WidthBefore(bx, left), 0.06f, HeadSize,
                $"<b>ENCHANT</b> <color=#F5C542>ARMOR</color>   {Tokens(inv)}", fit: true);

            // ---- the three slots
            _upSlot = Math.Max(0, Math.Min(2, _upSlot));
            var colW = (PanelWidth - 0.08f) / 3f;
            for (var s = 0; s < 3; s++)
            {
                var slot = s;
                var cx = left + colW * (s + 0.5f);
                UiKit.Button(_buy, new Vector3(cx, top - 0.26f, 0f), Armor.SlotNames[s].ToUpperInvariant(), () => { _upSlot = slot; BuildBuy(); }, BtnScale * 0.9f);
                if (s == _upSlot) UiKit.Bar(_buy, new Vector3(cx, top - 0.30f, 0.004f), BtnW * 1.2f, 0.005f, new Color(0.96f, 0.77f, 0.26f, 1f));
            }
            UiKit.Bar(_buy, new Vector3(0f, top - 0.325f, 0.004f), PanelWidth - 0.04f, 0.004f, new Color(0.25f, 0.3f, 0.4f, 1f));

            var piece = Armor.Worn(_upSlot);
            if (piece == null)
            {
                UiKit.Text(_buy, new Vector3(0f, top - 0.62f, 0f), 1.2f, 0.06f, 0.4f,
                    $"Nothing worn on your {Armor.SlotNames[_upSlot].ToLowerInvariant()}.", TextAlignmentOptions.Center, fit: true);
                return;
            }

            // ---- the piece: name, rarity, sell price
            var infoW = PanelWidth - 0.08f;
            UiKit.Text(_buy, new Vector3(left, top - 0.385f, 0f), infoW * 0.62f, 0.05f, 0.42f, piece.ColoredName, fit: true);
            UiKit.Text(_buy, new Vector3(left + infoW * 0.88f, top - 0.385f, 0f), infoW * 0.24f, 0.05f, 0.4f,
                $"<color={Armor.ClassColor(piece.WeaponClass)}>{LootTables.ClassName(piece.WeaponClass)}</color>", TextAlignmentOptions.Center, fit: true);
            UiKit.Bar(_buy, new Vector3(0f, top - 0.43f, 0.004f), PanelWidth - 0.04f, 0.004f, new Color(0.25f, 0.3f, 0.4f, 1f));

            // ---- one card per stat
            var stats = Armor.Decode(piece.ArmorStats);
            var cardH = 0.25f;
            var cardW = PanelWidth - 0.04f;
            var price = Armor.UpgradePrice(piece);
            var afford = inv.Gold >= price;
            var btnX = PanelWidth * 0.5f - 0.04f - BtnW * 0.5f;
            var priceX = btnX - BtnW * 0.5f - 0.11f;                // centred beside the arrow
            var innerL = left + 0.02f;
            var innerW = priceX - 0.11f - innerL;                  // everything left of the price
            var (lo, hi) = Armor.Band(piece.WeaponClass);
            var totalSteps = Mathf.RoundToInt((hi - lo) / Armor.UpgradeStep);
            for (var i = 0; i < stats.Count && i < 2; i++)
            {
                var (st, m) = stats[i];
                var d = Buffs.Find(st);
                var invert = d != null && d.Invert;
                var label = d == null ? st : string.IsNullOrEmpty(d.Short) ? d.Flavor : d.Short;
                if (label.Length > 0) label = char.ToUpperInvariant(label[0]) + label.Substring(1);
                var atMax = Armor.AtMax(piece, m);
                var next = Armor.NextMult(piece, m);
                var cy = top - 0.57f - i * (cardH + 0.025f);
                string Pct(float v) => invert ? $"-{(1f - 1f / v) * 100f:0.#}%" : $"+{(v - 1f) * 100f:0}%";
                UiKit.Bar(_buy, new Vector3(0f, cy, 0.006f), cardW, cardH, StripeBuy);

                UiKit.Text(_buy, new Vector3(innerL, cy + 0.093f, 0f), innerW, 0.05f, 0.44f, $"<b>{label}</b>", fit: true);
                // the three values sit close together, centred in the space left of the price
                var midX = innerL + innerW * 0.5f;
                const float third = 0.25f;
                float Col(int n) => midX + (n - 1) * third;
                UiKit.Text(_buy, new Vector3(Col(0), cy + 0.04f, 0f), third, 0.035f, 0.28f, "<color=#C9A86A>CURRENT</color>", TextAlignmentOptions.Center, fit: true);
                UiKit.Text(_buy, new Vector3(Col(1), cy + 0.04f, 0f), third, 0.035f, 0.28f, "<color=#5BD75B>NEXT</color>", TextAlignmentOptions.Center, fit: true);
                UiKit.Text(_buy, new Vector3(Col(2), cy + 0.04f, 0f), third, 0.035f, 0.28f, "<color=#F5C542>MAX</color>", TextAlignmentOptions.Center, fit: true);
                UiKit.Text(_buy, new Vector3(Col(0), cy - 0.005f, 0f), third, 0.05f, 0.5f, Pct(m), TextAlignmentOptions.Center, fit: true);
                UiKit.Text(_buy, new Vector3(Col(1), cy - 0.005f, 0f), third, 0.05f, 0.5f, atMax ? "<color=#B0B0B0>-</color>" : $"<color=#5BD75B>{Pct(next)}</color>", TextAlignmentOptions.Center, fit: true);
                UiKit.Text(_buy, new Vector3(Col(2), cy - 0.005f, 0f), third, 0.05f, 0.5f, $"<color=#F5C542>{Pct(hi)}</color>", TextAlignmentOptions.Center, fit: true);

                // the rarity's range, slim and centred under the values: gold up to now, a green sliver for the next step
                var done = Mathf.Clamp(Mathf.RoundToInt((m - lo) / Armor.UpgradeStep), 0, totalSteps);
                var barY = cy - 0.06f;
                var barW = third * 3f;
                var barL = midX - barW * 0.5f;
                var span = Mathf.Max(0.001f, hi - lo);
                var nowF = Mathf.Clamp01((m - lo) / span);
                var nextF = Mathf.Clamp01((next - lo) / span);
                UiKit.Bar(_buy, new Vector3(midX, barY, -0.004f), barW, 0.012f, new Color(0.14f, 0.17f, 0.24f, 1f));
                if (nowF > 0.001f) UiKit.Bar(_buy, new Vector3(barL + barW * nowF * 0.5f, barY, -0.01f), barW * nowF, 0.012f, new Color(0.96f, 0.77f, 0.26f, 1f));
                if (nextF > nowF + 0.001f) UiKit.Bar(_buy, new Vector3(barL + barW * (nowF + nextF) * 0.5f, barY, -0.01f), barW * (nextF - nowF), 0.012f, new Color(0.36f, 0.84f, 0.36f, 1f));
                UiKit.Text(_buy, new Vector3(midX, cy - 0.095f, 0f), barW, 0.03f, 0.28f, $"<color=#B0B0B0>{done}/{totalSteps} Upgrades</color>", TextAlignmentOptions.Center, fit: true);

                // the action: price centred beside an up-arrow button
                if (atMax)
                    UiKit.Text(_buy, new Vector3((priceX + btnX) * 0.5f, cy, 0f), 0.3f, 0.05f, 0.44f, "<color=#5BD75B><b>MAXED</b></color>", TextAlignmentOptions.Center, fit: true);
                else
                {
                    var idx = i;
                    UiKit.Text(_buy, new Vector3(priceX, cy, 0f), 0.24f, 0.05f, 0.42f,
                        $"<color={(afford ? "#F5C542" : "#E06060")}>{price} tk</color>", TextAlignmentOptions.Center, fit: true);
                    if (afford) UiKit.HitBox(_buy, new Vector3(btnX, cy, 0f), 0.16f, 0.16f, () => { Armor.Upgrade(piece, idx); });
                    UpArrow(new Vector3(btnX, cy, -0.03f), afford ? new Color(0.36f, 0.84f, 0.36f, 1f) : new Color(0.45f, 0.45f, 0.5f, 1f));
                }
            }
            if (stats.Count == 0)
                UiKit.Text(_buy, new Vector3(0f, top - 0.7f, 0f), 1.2f, 0.06f, 0.4f, "This piece has no stats to upgrade.", TextAlignmentOptions.Center);
        }

        /// <summary>An up arrow drawn from bars (a stepped head and a stem), so it does not depend on the game's font having the glyph.</summary>
        private static void UpArrow(Vector3 c, Color color)
        {
            float[] widths = { 0.018f, 0.045f, 0.072f, 0.099f };
            for (var r = 0; r < widths.Length; r++)
                UiKit.Bar(_buy, c + new Vector3(0f, 0.034f - r * 0.0135f, 0f), widths[r], 0.0135f, color);
            UiKit.Bar(_buy, c + new Vector3(0f, -0.033f, 0f), 0.027f, 0.039f, color);
        }

        /// <summary>
        /// The table's HELP page (report 2026-09-12: nothing said what the perks do): every
        /// enchantment with the game's own name and description for it, and the weapon types
        /// that can carry it. Paged; BACK returns to the weapon list.
        /// </summary>
        private static void BuildEnchantHelp(float top, float left, float bx)
        {
            UiKit.Button(_buy, new Vector3(bx, top - 0.05f, 0f), "BACK", () => { _enchantHelp = false; BuildBuy(); }, BtnScale);
            UiKit.Text(_buy, new Vector3(left, top - 0.05f, 0f), WidthBefore(bx, left), 0.06f, HeadSize, "<b>ENCHANT</b>   <color=#B0B0B0>what the enchantments do</color>", fit: true);
            var docs = Enchanting.Documentation();
            const int perPage = 9;
            var pages = Math.Max(1, (docs.Count + perPage - 1) / perPage);
            _helpPage = Math.Max(0, Math.Min(_helpPage, pages - 1));
            UiKit.Text(_buy, new Vector3(left, top - 0.215f, 0f), PanelWidth - 0.08f, 0.045f, 0.32f,
                $"<color=#B0B0B0>Common weapons hold 1 perk, Unique and Rare 2, Legendary 3, plus one element. The game's own words for each:</color>", fit: true);
            var y0 = top - 0.29f;
            var start = _helpPage * perPage;
            for (var i = 0; i < perPage && start + i < docs.Count; i++)
            {
                var d = docs[start + i];
                var y = y0 - i * 0.09f;
                if (i % 2 == 0) UiKit.Bar(_buy, new Vector3(0f, y - 0.002f, 0.006f), PanelWidth - 0.03f, 0.086f, StripeBuy);
                UiKit.Text(_buy, new Vector3(left, y + 0.022f, 0f), PanelWidth - 0.08f, 0.045f, 0.36f,
                    $"<color=#7FD8FF>{d.Title}</color>   <size=80%><color=#B0B0B0>{d.Types}</color></size>", fit: true);
                UiKit.Text(_buy, new Vector3(left, y - 0.025f, 0f), PanelWidth - 0.08f, 0.04f, 0.3f, $"<color=#D0D0D0>{d.Description}</color>", fit: true);
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
        /// Armor has no vanilla screen, so the kobold is where it is worn. A table (report
        /// 2026-10-08). One compact row for the three slots (HEAD / CHEST / LEGS, each with the
        /// piece worn there). Below, three columns, one stat per row: WEARING, IN THE BAG and
        /// IF YOU SWAP (Diablo style: "Leap -1.15" in red, "Melee taken +1.21" in green). TAKE OFF
        /// sits under the worn piece, WEAR (and the &lt; &gt; pager) under the bag piece.
        /// </summary>
        private static void BuildArmor(float top, float left)
        {
            var inv = BagManager.Inventory;
            _armorSlot = Math.Max(0, Math.Min(2, _armorSlot));
            UiKit.Text(_buy, new Vector3(left, top - 0.05f, 0f), PanelWidth - 0.08f, 0.06f, HeadSize,
                "<b>ARMOR</b>   <color=#B0B0B0>what you wear, and what a swap changes</color>", fit: true);

            // ---- the three slots, one compact row: tab (with spare count) and the worn piece under it
            var colW = (PanelWidth - 0.08f) / 3f;
            for (var s = 0; s < 3; s++)
            {
                var slot = s;
                var cx = left + colW * (s + 0.5f);
                var w = Armor.Worn(s);
                var spare = 0; foreach (var i in inv.Items) if (i.IsArmor && i.WornSlot < 0 && i.ArmorSlot == s) spare++;
                var label = Armor.SlotNames[s].ToUpperInvariant() + (spare > 0 ? $" ({spare})" : "");
                UiKit.Button(_buy, new Vector3(cx, top - 0.26f, 0f), label, () => { _armorSlot = slot; _armorPage = 0; BuildBuy(); }, BtnScale * 0.9f);
                if (s == _armorSlot) UiKit.Bar(_buy, new Vector3(cx, top - 0.30f, 0.004f), BtnW * 1.2f, 0.005f, new Color(0.96f, 0.77f, 0.26f, 1f));
                UiKit.Text(_buy, new Vector3(cx, top - 0.34f, 0f), colW - 0.04f, 0.05f, 0.34f,
                    w == null ? "<color=#B0B0B0>nothing worn</color>" : w.ColoredName, TextAlignmentOptions.Center, fit: true);
            }
            UiKit.Bar(_buy, new Vector3(0f, top - 0.385f, 0.004f), PanelWidth - 0.04f, 0.004f, new Color(0.25f, 0.3f, 0.4f, 1f));

            // ---- the open slot
            var worn = Armor.Worn(_armorSlot);
            var pieces = new List<LootItem>();
            foreach (var i in inv.Items) if (i.IsArmor && i.WornSlot < 0 && i.ArmorSlot == _armorSlot) pieces.Add(i);
            pieces.Sort((a, b) => b.WeaponClass != a.WeaponClass ? b.WeaponClass.CompareTo(a.WeaponClass) : b.Value.CompareTo(a.Value));
            _armorPage = pieces.Count == 0 ? 0 : Math.Max(0, Math.Min(_armorPage, pieces.Count - 1));
            var cand = pieces.Count == 0 ? null : pieces[_armorPage];
            var slotName = Armor.SlotNames[_armorSlot].ToLowerInvariant();
            var rows = Armor.CompareRows(cand, worn);
            var better = 0; var worse = 0;
            foreach (var r in rows) { if (r.Verdict > 0) better++; else if (r.Verdict < 0) worse++; }

            const float colWidth = 0.44f;
            float[] cx3 = { left + colW * 0.5f, left + colW * 1.5f, left + colW * 2.5f };
            var hy = top - 0.43f;       // captions
            var ny = top - 0.505f;      // names, two lines
            var by = top - 0.595f;      // buttons
            UiKit.Text(_buy, new Vector3(cx3[0], hy, 0f), colWidth, 0.04f, 0.3f, "<color=#C9A86A>WEARING</color>", TextAlignmentOptions.Center, fit: true);
            UiKit.Text(_buy, new Vector3(cx3[1], hy, 0f), colWidth, 0.04f, 0.3f,
                cand == null ? "<color=#7FD8FF>IN THE BAG</color>" : $"<color=#7FD8FF>IN THE BAG</color> <color=#B0B0B0>{_armorPage + 1} / {pieces.Count}</color>", TextAlignmentOptions.Center, fit: true);
            UiKit.Text(_buy, new Vector3(cx3[2], hy, 0f), colWidth, 0.04f, 0.3f, "<color=#F5C542>IF YOU SWAP</color>", TextAlignmentOptions.Center, fit: true);
            UiKit.Text(_buy, new Vector3(cx3[0], ny, 0f), colWidth, 0.08f, 0.38f,
                worn == null ? $"<color=#B0B0B0>no {slotName} armor worn</color>" : worn.ColoredName, TextAlignmentOptions.Center, fit: true, lines: 2);
            UiKit.Text(_buy, new Vector3(cx3[1], ny, 0f), colWidth, 0.08f, 0.38f,
                cand == null ? $"<color=#B0B0B0>no spare {slotName} armor</color>" : $"{cand.ColoredName}{(cand.Locked ? "  <color=#F5C542>locked</color>" : "")}", TextAlignmentOptions.Center, fit: true, lines: 2);
            string verdict;
            if (cand == null) verdict = "<color=#B0B0B0>nothing to compare</color>";
            else if (worn == null) verdict = $"<color=#5BD75B>adds {rows.Count} stat(s)</color>";
            else verdict = $"<color=#5BD75B>{better} better</color>\n<color=#E06060>{worse} worse</color>";
            UiKit.Text(_buy, new Vector3(cx3[2], ny, 0f), colWidth, 0.08f, 0.38f, verdict, TextAlignmentOptions.Center, fit: true, lines: 2);

            // Buttons belong to the piece above them.
            if (worn != null)
            {
                var capturedWorn = worn;
                UiKit.Button(_buy, new Vector3(cx3[0], by, 0f), "TAKE OFF", () => { Armor.Remove(capturedWorn); BuildBuy(); }, BtnScale);
            }
            if (cand != null)
            {
                var capturedCand = cand;
                // WEAR and SELL sit side by side: slightly smaller buttons with a clear gap (the glow edges touched and the pointer flickered between them, report 2026-10-08).
                var pairScale = BtnScale * 0.8f;
                var pairOff = BtnW * 0.8f * 0.5f + 0.04f;
                UiKit.Button(_buy, new Vector3(cx3[1] - pairOff, by, 0f), "WEAR", () => { Armor.Wear(capturedCand); BuildBuy(); }, pairScale);
                // Sell the piece you just compared, right here; a locked one has to be unlocked on the SELL page first.
                UiKit.Button(_buy, new Vector3(cx3[1] + pairOff, by, 0f), "SELL", () => { Sell(capturedCand); }, pairScale, enabled: !cand.Locked);
                UiKit.Text(_buy, new Vector3(cx3[2], by, 0f), colWidth, 0.05f, 0.34f,
                    cand.Locked ? "<color=#B0B0B0>locked, cannot sell</color>" : $"<color=#B0B0B0>sells for</color> <color=#F5C542>{SellPrice(cand)} tk</color>", TextAlignmentOptions.Center, fit: true);
                if (pieces.Count > 1)
                {
                    var n = pieces.Count;
                    UiKit.Button(_buy, new Vector3(cx3[1] - 0.2f, hy, 0f), "<", () => { _armorPage = (_armorPage + n - 1) % n; BuildBuy(); }, BtnScale * 0.55f);
                    UiKit.Button(_buy, new Vector3(cx3[1] + 0.2f, hy, 0f), ">", () => { _armorPage = (_armorPage + 1) % n; BuildBuy(); }, BtnScale * 0.55f);
                }
            }
            UiKit.Bar(_buy, new Vector3(0f, top - 0.655f, 0.004f), PanelWidth - 0.04f, 0.004f, new Color(0.25f, 0.3f, 0.4f, 1f));

            // ---- one stat per row
            var y0 = top - 0.72f;
            const float pitch = 0.085f;
            for (var r = 0; r < rows.Count && r < 6; r++)
            {
                var row = rows[r];
                var y = y0 - r * pitch;
                if (r % 2 == 0) UiKit.Bar(_buy, new Vector3(0f, y, 0.006f), PanelWidth - 0.03f, pitch - 0.004f, StripeBuy);
                UiKit.Text(_buy, new Vector3(cx3[0], y, 0f), colWidth, 0.05f, 0.4f, row.WornText, TextAlignmentOptions.Center, fit: true);
                UiKit.Text(_buy, new Vector3(cx3[1], y, 0f), colWidth, 0.05f, 0.4f, row.NewText, TextAlignmentOptions.Center, fit: true);
                UiKit.Text(_buy, new Vector3(cx3[2], y, 0f), colWidth, 0.05f, 0.4f, row.Net, TextAlignmentOptions.Center, fit: true);
            }
            if (rows.Count == 0)
                UiKit.Text(_buy, new Vector3(0f, y0 - pitch, 0f), 1.1f, 0.06f, 0.4f, $"Nothing worn and nothing spare for {slotName} armor.", TextAlignmentOptions.Center);
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
            Rebuild();
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
            ReconLog.Line($"sold {live.Name} for {price} -> tokens {inv.Gold}");
            Rebuild();
            BagPanel.Refresh();
        }


    }
}
