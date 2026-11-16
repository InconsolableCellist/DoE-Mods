using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppTMPro;
using LootOverhaul.Gate;
using LootOverhaul.Recon;
using MelonLoader;
using UnityEngine;
using Interop = LootOverhaul.Recon.Interop;
using S = LootOverhaul.Loot.UiStyle;

namespace LootOverhaul.Loot
{
    /// <summary>
    /// The Kobold Traveler, a stall in the lobby: a sell counter (bag items for the game's own
    /// gold, see <see cref="Wallet"/>) and a shop for tonics, enchanting, armor and ring settings.
    /// Since 0.15.0 it sells no weapons (the game's vendor does). Equipping is at the game's own
    /// fabricator (see <see cref="FabricatorBridge"/>).
    /// Placed from config (press = in the lobby to move it to where you stand), built once,
    /// kept across scene loads, shown only in the lobby with the gate open.
    /// 0.13.0: every page is built from the shared components in <see cref="UiKit"/> and the
    /// sizes, colours and grid in <see cref="UiStyle"/>, so all of them look and read alike.
    /// </summary>
    public static class Booth
    {
        private const int RowsPerPage = 6;
        private const int SellRows = 5;
        private const int HelpRows = 8;
        private const float PanelScale = 1.1f;
        /// <summary>
        /// Height of the panels' centres over the booth's floor. 0.13.2: 1.55 m (was 1.4). Since 0.13.0
        /// made the panels 1.26 m tall the bottom row and the pager sat behind the counter.
        /// </summary>
        private const float PanelY = 1.55f;
        private const float L = S.Left, R = S.Right;

        private static GameObject _root;
        private static Transform _sell, _buy;
        private static int _sellPage, _tonicPage, _enchantPage, _helpPage, _armorPage;
        private static int _buyMode;            // 0 tonics, 1 enchant, 2 armor, 3 rings
        private static int _armorSlot;          // ARMOR tab: which slot is being compared (0 head, 1 chest, 2 legs)
        private static bool _enchantHelp;       // ENCHANT tab: the "what do they do" page
        private static string _enchantTarget;   // bag item id being enchanted ("stock-…" for an armory weapon), or null for the list
        private static bool _enchantStock;      // ENCHANT weapon list: the armory's stock weapons instead of the bag's
        private static int _enchantKind = -1;   // ENCHANT tab: -1 the menu, 0 weapon, 1 armor
        private static int _upSlot;             // armor upgrade page: which slot (0 head, 1 chest, 2 legs)

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
            _sell.localPosition = new Vector3(-0.90f, PanelY, 0f);
            _sell.localRotation = Quaternion.Euler(0f, -18f, 0f);
            _sell.localScale = Vector3.one * PanelScale;
            _buy.localPosition = new Vector3(0.90f, PanelY, 0f);
            _buy.localRotation = Quaternion.Euler(0f, 18f, 0f);
            _buy.localScale = Vector3.one * PanelScale;
            UiKit.Text(_root.transform, new Vector3(0f, PanelY + 0.87f, 0f), 1.8f, 0.15f, S.Sign, UiKit.Paint(S.TextHex, "<b>KOBOLD TRAVELER</b>"), TextAlignmentOptions.Center);
            UiKit.Text(_root.transform, new Vector3(0f, PanelY + 0.74f, 0f), 1.8f, 0.06f, S.Row, UiKit.Grey("shinies from the dungeon for gold"), TextAlignmentOptions.Center);
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

        /// <summary>A fresh panel: everything under it destroyed, the one backdrop (with its laser catcher) put back.</summary>
        private static void Clear(Transform panel)
        {
            UiKit.DestroyChildren(panel);
            UiKit.Backdrop(panel, new Vector3(0f, 0f, 0.01f), S.PanelWidth, S.PanelHeight, S.Panel);
        }

        /// <summary>An item's second line: the game's stats for a weapon, the effect for a tonic, the stats for armor, the tier for junk.</summary>
        internal static string StatsOf(LootItem item)
        {
            if (item.IsWeapon) { try { return UiKit.StatsLine(WeaponCodec.ToModule(item).GetStatsText()); } catch { return ""; } }
            if (item.IsBuff) return Buffs.Effect(item.BuffStat, item.BuffMult);
            if (item.IsArmor) return Armor.DescribeStatsShort(item).Replace(", ", " · ");
            return LootTables.JunkTierName(item.WeaponClass);
        }

        /// <summary>A text line with a price on the right and up to one button: the toolbar row of a page, lined up with the row columns.</summary>
        private static void ToolRow(Transform panel, float y, string text, int? price, bool affordable, string label, Action onPressed,
                                    S.Variant variant = S.Variant.Secondary, S.State state = S.State.Normal, bool lockCol = false)
        {
            var c = UiKit.Columns(label == null ? 0 : 1, lockCol, price.HasValue);
            if (!string.IsNullOrEmpty(text)) UiKit.Label(panel, L, y, c.TextEnd - L, 0.085f, S.Body, text, S.Text2, lines: 2);
            if (price.HasValue) UiKit.Price(panel, c.Price, y, price.Value, affordable);
            if (label != null) UiKit.ActionBtn(panel, c.Act[0], y, label, onPressed, variant, state);
        }

        // ---- sell counter -------------------------------------------------------------------

        private static void BuildSell()
        {
            if (!Interop.Alive(_sell)) return;
            Clear(_sell);
            var inv = BagManager.Inventory;
            var items = new List<LootItem>(inv.Items);
            items.Sort((a, b) => b.Value.CompareTo(a.Value));
            var pages = Math.Max(1, (items.Count + SellRows - 1) / SellRows);
            _sellPage = Math.Max(0, Math.Min(_sellPage, pages - 1));
            UiKit.Header(_sell, "SELL", $"{UiKit.Plural(inv.Items.Count, "item")}, most valuable first", Wallet.Balance);

            // SELL ALL, top: junk, then weapons and armor by rarity up to Rare (a Legendary is sold
            // one at a time, on purpose). Locked, equipped and worn items are never swept. Green
            // buttons under a SELL ALL heading, the total each one pays under it.
            UiKit.Label(_sell, L, S.StripY, 0.34f, 0.06f, S.Row, "<b>SELL ALL</b>", S.Text);
            UiKit.Label(_sell, L, S.ToolY, 0.34f, 0.08f, S.Body, "never locked, equipped or worn", S.Text2, lines: 2);
            var groups = new[] { (label: "JUNK", cls: -1), (label: "COMMON", cls: 0), (label: "UNIQUE", cls: 1), (label: "RARE", cls: 2) };
            // 0.13.2: compact buttons (they were full action buttons stretched to a quarter of the
            // panel), with the total centred under each.
            var x0 = L + 0.37f;
            var cell = (R - x0 - S.Gap * 3f) / 4f;
            var gw = Mathf.Min(cell, 0.19f);
            for (var g = 0; g < groups.Length; g++)
            {
                var (n, value) = SweepTotal(inv, groups[g].cls);
                var x = x0 + cell * 0.5f + g * (cell + S.Gap);
                var cls = groups[g].cls;
                UiKit.Btn(_sell, new Vector3(x, S.StripY, 0f), new Vector2(gw, S.Compact.y), n > 0 ? $"{n} {groups[g].label}" : groups[g].label,
                    () => SellAll(cls), S.Variant.Sell, n > 0 ? S.State.Normal : S.State.Disabled, S.Body);
                if (n > 0) UiKit.CoinNumberCentered(_sell, x, S.ToolY, cell, value, S.Gold);
                else UiKit.Label(_sell, x - cell * 0.5f, S.ToolY, cell, 0.05f, S.Body, "none", S.Text2, TextAlignmentOptions.Center);
            }
            UiKit.Rule(_sell, S.RuleToolY);

            var cols = UiKit.Columns(1, lockCol: true);
            var start = _sellPage * SellRows;
            for (var i = 0; i < SellRows && start + i < items.Count; i++)
            {
                var item = items[start + i];
                var row = UiKit.NewRow(_sell, $"SellRow_{i}", S.Row0Y - i * S.Pitch, i);
                var slot = inv.EquippedSlotOf(item);
                var inUse = inv.InUse(item);
                // 0.13.2: no LOCKED word, the closed padlock beside the price says it.
                var chip = item.IsBound ? "ARMORY" : inUse ? (slot >= 0 ? "EQUIPPED" : "WORN") : null;
                UiKit.ItemText(row, cols.TextEnd, item, chip);
                UiKit.Price(row, cols.Price, 0f, SellPrice(item));
                var captured = item;
                UiKit.LockToggle(row, new Vector3(cols.Lock, 0f, 0f), item.Locked, () => ToggleLock(captured));
                UiKit.ActionBtn(row, cols.Act[0], 0f, "SELL", () => Sell(captured), S.Variant.Primary,
                    item.Locked || inUse ? S.State.Disabled : S.State.Normal);
            }
            if (items.Count == 0) UiKit.Empty(_sell, "Your bag is empty.");

            // A bigger bag, as a row lined up with the price and button columns.
            var yb = S.Row0Y - SellRows * S.Pitch + 0.02f;
            UiKit.Rule(_sell, yb + 0.04f);
            var bagY = yb - 0.035f;
            if (inv.BagLevel < BagManager.BagUpgrades.Length)
            {
                var next = BagManager.BagUpgrades[inv.BagLevel];
                var price = (int)Math.Round(next.price * ModConfig.ShopPriceMultiplier.Value);
                var ok = Wallet.Balance >= price;
                ToolRow(_sell, bagY, $"Bag {inv.TotalWeight:0.#} / {BagManager.Capacity:0} wt.   Next: {next.name}, +{next.bonus:0} wt", price, ok,
                    "BUY", () => { BagManager.BuyBagUpgrade(); }, S.Variant.Primary, ok ? S.State.Normal : S.State.Disabled, lockCol: true);
            }
            else
                ToolRow(_sell, bagY, $"Bag {inv.TotalWeight:0.#} / {BagManager.Capacity:0} wt.   {BagManager.BagUpgrades[^1].name}, the biggest there is", null, true, null, null);
            UiKit.Pager(_sell, _sellPage, pages, () => { _sellPage--; BuildSell(); }, () => { _sellPage++; BuildSell(); });
        }

        // ---- the shop --------------------------------------------------------------------------

        /// <summary>0.15.0: no WEAPONS tab. Weapons come from the game's own vendor; the kobold enchants them.</summary>
        private static readonly string[] TabNames = { "TONICS", "ENCHANT", "ARMOR", "RINGS" };

        private static void BuildBuy()
        {
            if (!Interop.Alive(_buy)) return;
            Clear(_buy);
            UiKit.Tabs(_buy, L, R, S.StripY, TabNames, _buyMode, i =>
            {
                _buyMode = i;
                if (i == 1) { _enchantTarget = null; _enchantHelp = false; _enchantKind = -1; }
                BuildBuy();
            });
            UiKit.Rule(_buy, S.RuleStripY);
            switch (_buyMode)
            {
                case 1: BuildEnchant(); break;
                case 2: BuildArmor(); break;
                case 3: BuildConfig(); break;
                default: BuildTonics(); break;
            }
        }

        // ---- RINGS tab: CustomAvatars' ring settings, for your own avatar ---------------------

        private static int _ringPage;   // 0 your left ring, 1 your right ring, 2 friends' rings
        private static bool _ringLeft => _ringPage == 0;

        private static void BuildConfig()
        {
            var inv = BagManager.Inventory;
            UiKit.Header(_buy, "RINGS", "saved at once, friends see yours as you do", Wallet.Balance);
            if (!RingSettings.Available)
            {
                UiKit.Rule(_buy, S.RuleToolY);
                UiKit.Empty(_buy, "Needs CustomAvatars 0.42.14 or later.");
                UiKit.Label(_buy, -0.6f, S.Row0Y - 2f * S.Pitch, 1.2f, 0.06f, S.Body, "Ring settings appear here once it is installed.", S.Text2, TextAlignmentOptions.Center);
                return;
            }
            var side = _ringLeft;
            var friends = _ringPage == 2;
            UiKit.Tabs(_buy, L, L + 0.78f, S.ToolY, new[] { "LEFT RING", "RIGHT RING", "FRIENDS" }, _ringPage, i => { _ringPage = i; BuildBuy(); });
            UiKit.CompactBtn(_buy, R - S.Compact.x * 0.5f, S.ToolY, "RESET",
                () => { if (friends) RingSettings.ResetOthers(); else RingSettings.Reset(side); BuildBuy(); }, S.Variant.Danger);
            UiKit.Rule(_buy, S.RuleToolY);

            // -- - + ++ on the right of each row; values in one column.
            var bw = S.Stepper.x;
            var x4 = R - bw * 0.5f;
            var x3 = x4 - bw - S.Gap; var x2 = x3 - bw - S.Gap; var x1 = x2 - bw - S.Gap;
            var lx = L + 0.02f;
            var vx = L + 0.47f;
            var helpW = x1 - bw * 0.5f - 0.03f - lx;
            var y0 = S.RuleToolY - 0.01f - S.Compact_Pitch * 0.5f;

            if (friends)
            {
                // Everyone else's rings: shown or not, and one size on top of how they tuned them.
                var others = RingSettings.OthersVisible;
                var f0 = UiKit.NewRow(_buy, "CfgRow_others", y0, 0, S.Compact_Pitch);
                UiKit.Label(f0, lx, 0.022f, 0.44f, 0.055f, S.Row, "<b>SHOW FRIENDS' RINGS</b>", S.Text);
                UiKit.Label(f0, vx, 0.022f, 0.24f, 0.055f, S.Row, $"<b>{(others ? "ON" : "OFF")}</b>", others ? S.Green : S.Red);
                UiKit.Label(f0, lx, -0.028f, helpW, 0.045f, S.Body, "as each friend set them up", S.Text2);
                UiKit.Btn(f0, new Vector3((x3 + x4) * 0.5f, 0f, 0f), new Vector2(bw * 2f + S.Gap, S.Stepper.y), others ? "TURN OFF" : "TURN ON",
                    () => { RingSettings.OthersVisible = !others; BuildBuy(); }, S.Variant.Secondary);

                var os = RingSettings.OthersSize;
                var f1 = UiKit.NewRow(_buy, "CfgRow_othersSize", y0 - S.Compact_Pitch, 1, S.Compact_Pitch);
                UiKit.Label(f1, lx, 0.022f, 0.44f, 0.055f, S.Row, $"<b>{os.Label}</b>", S.Text);
                if (!RingSettings.OthersSizeAvailable)
                {
                    UiKit.Label(f1, lx, -0.028f, 1.0f, 0.045f, S.Body, "needs CustomAvatars 0.42.22 or later", S.Text2);
                    return;
                }
                UiKit.Label(f1, vx, 0.022f, 0.24f, 0.055f, S.Row, $"<b>{string.Format(os.Format, RingSettings.GetOthersSize())}</b>", S.Text);
                UiKit.Label(f1, lx, -0.028f, helpW, 0.045f, S.Body, os.Help, S.Text2);
                UiKit.Stepper(f1, new Vector3(x1, 0f, 0f), "--", () => { RingSettings.NudgeOthersSize(-os.BigStep); BuildBuy(); });
                UiKit.Stepper(f1, new Vector3(x2, 0f, 0f), "-", () => { RingSettings.NudgeOthersSize(-os.Step); BuildBuy(); });
                UiKit.Stepper(f1, new Vector3(x3, 0f, 0f), "+", () => { RingSettings.NudgeOthersSize(os.Step); BuildBuy(); });
                UiKit.Stepper(f1, new Vector3(x4, 0f, 0f), "++", () => { RingSettings.NudgeOthersSize(os.BigStep); BuildBuy(); });
                return;
            }

            // Shown or not: off until the player turns it on.
            var on = RingSettings.Visible(side);
            var row0 = UiKit.NewRow(_buy, "CfgRow_visible", y0, 0, S.Compact_Pitch);
            UiKit.Label(row0, lx, 0.022f, 0.44f, 0.055f, S.Row, $"<b>SHOW {(side ? "LEFT" : "RIGHT")} RING</b>", S.Text);
            UiKit.Label(row0, vx, 0.022f, 0.24f, 0.055f, S.Row, $"<b>{(on ? "ON" : "OFF")}</b>", on ? S.Green : S.Red);
            UiKit.Label(row0, lx, -0.028f, helpW, 0.045f, S.Body, "off until you turn it on", S.Text2);
            UiKit.Btn(row0, new Vector3((x3 + x4) * 0.5f, 0f, 0f), new Vector2(bw * 2f + S.Gap, S.Stepper.y), on ? "TURN OFF" : "TURN ON",
                () => { RingSettings.SetVisible(side, !on); BuildBuy(); }, S.Variant.Secondary);

            for (var i = 0; i < RingSettings.All.Length; i++)
            {
                var s = RingSettings.All[i];
                var row = UiKit.NewRow(_buy, $"CfgRow_{s.Key}", y0 - (i + 1) * S.Compact_Pitch, i + 1, S.Compact_Pitch);
                UiKit.Label(row, lx, 0.022f, 0.44f, 0.055f, S.Row, $"<b>{s.Label}</b>", S.Text);
                UiKit.Label(row, vx, 0.022f, 0.24f, 0.055f, S.Row, $"<b>{RingSettings.Show(side, s)}</b>", S.Text);
                UiKit.Label(row, lx, -0.028f, helpW, 0.045f, S.Body, s.Help, S.Text2);
                var captured = s;
                UiKit.Stepper(row, new Vector3(x1, 0f, 0f), "--", () => { RingSettings.Nudge(side, captured, -captured.BigStep); BuildBuy(); });
                UiKit.Stepper(row, new Vector3(x2, 0f, 0f), "-", () => { RingSettings.Nudge(side, captured, -captured.Step); BuildBuy(); });
                UiKit.Stepper(row, new Vector3(x3, 0f, 0f), "+", () => { RingSettings.Nudge(side, captured, captured.Step); BuildBuy(); });
                UiKit.Stepper(row, new Vector3(x4, 0f, 0f), "++", () => { RingSettings.Nudge(side, captured, captured.BigStep); BuildBuy(); });
            }
        }

        // ---- TONICS -----------------------------------------------------------------------------

        private static void BuildTonics()
        {
            var inv = BagManager.Inventory;
            var offered = Buffs.Offered();
            var prepared = inv.PreparedTonics;
            UiKit.Header(_buy, "TONICS", "drunk when your next run starts", Wallet.Balance);
            var summary = prepared.Count == 0
                ? "One per kind."
                : UiKit.Paint(S.GreenHex, $"{UiKit.Plural(prepared.Count, "tonic")} prepared, {UiKit.Money(PreparedTonics.TotalRefund())} back on revert");
            const float wideW = 0.26f;
            UiKit.Label(_buy, L, S.ToolY, R - wideW - 0.03f - L, 0.05f, S.Body, summary, S.Text2);
            UiKit.Btn(_buy, new Vector3(R - wideW * 0.5f, S.ToolY, 0f), new Vector2(wideW, S.Action.y), "REVERT ALL",
                () => { if (PreparedTonics.RevertAll()) Rebuild(); }, S.Variant.Danger, prepared.Count > 0 ? S.State.Normal : S.State.Disabled);
            UiKit.Rule(_buy, S.RuleToolY);
            if (offered.Count == 0) { UiKit.Empty(_buy, "Unlock exosuit perks to buy tonics for them."); return; }

            var pages = Math.Max(1, (offered.Count + RowsPerPage - 1) / RowsPerPage);
            _tonicPage = Math.Max(0, Math.Min(_tonicPage, pages - 1));
            var start = _tonicPage * RowsPerPage;
            // Three tier buttons on the right with the price under each; REVERT left of them on a prepared row.
            const float tw = 0.16f, th = 0.07f, by = 0.018f, py = -0.045f;
            var tierX = new[] { R - 0.44f, R - 0.26f, R - 0.08f };   // Minor, Major, Grand
            var revertX = R - 0.62f;
            for (var i = 0; i < RowsPerPage && start + i < offered.Count; i++)
            {
                var def = offered[start + i];
                var prep = PreparedTonics.Get(def.Stat);
                var row = UiKit.NewRow(_buy, $"TonicRow_{i}", S.Row0Y - i * S.Pitch, i, S.Pitch, prep != null ? S.StripeOk : (Color?)null);
                UiKit.TonicIcon(row, new Vector3(S.IconX, 0f, -0.03f));
                // Every row here is a tonic, so the page skips the type tag and gives the name that room.
                // 0.13.2: the text runs up to the REVERT column only on a prepared row (it stopped
                // there on every row, and the effect was cut), and the effect and the three tier
                // values are a line each instead of one line wrapped wherever it ran out.
                var textEnd = (prep != null ? revertX : tierX[0]) - tw * 0.5f - 0.03f;
                UiKit.RowText(row, textEnd, UiKit.Paint(S.TonicHex, def.Name), $"{def.Flavor}\n{Buffs.Tiers(def)}", null, S.Text2, L + 0.09f);
                var captured = def;
                for (var t = 0; t < 3; t++)
                {
                    var tier = t;
                    var x = tierX[t];
                    var tierName = Buffs.TierNames[tier].ToUpperInvariant();
                    if (prep != null && prep.Tier == tier)
                    {
                        UiKit.Btn(row, new Vector3(x, by, 0f), new Vector2(tw, th), tierName, null, S.Variant.Primary, S.State.Selected);
                        UiKit.Label(row, x - 0.09f, py, 0.18f, 0.045f, S.Body, "<b>PREPARED</b>", S.Green, TextAlignmentOptions.Center);
                    }
                    else if (prep != null)
                        UiKit.Btn(row, new Vector3(x, by, 0f), new Vector2(tw, th), tierName, null, S.Variant.Primary, S.State.Disabled);
                    else
                    {
                        var price = Shop.TonicPrice(def, tier);
                        var ok = Wallet.Balance >= price;
                        UiKit.Btn(row, new Vector3(x, by, 0f), new Vector2(tw, th), tierName, () => { if (PreparedTonics.Prepare(captured, tier)) Rebuild(); },
                            S.Variant.Primary, ok ? S.State.Normal : S.State.Disabled);
                        UiKit.CoinNumberCentered(row, x, py, tw + 0.02f, price, ok ? S.Gold : S.Red);
                    }
                }
                if (prep != null)
                {
                    var stat = def.Stat;
                    UiKit.Btn(row, new Vector3(revertX, by, 0f), new Vector2(tw, th), "REVERT", () => { if (PreparedTonics.Revert(stat)) Rebuild(); }, S.Variant.Danger);
                    UiKit.CoinNumberCentered(row, revertX, py, tw + 0.02f, prep.Refund, S.Gold);
                }
            }
            UiKit.Pager(_buy, _tonicPage, pages, () => { _tonicPage--; BuildBuy(); }, () => { _tonicPage++; BuildBuy(); });
        }

        // ---- ENCHANT ----------------------------------------------------------------------------

        private static void BuildEnchant()
        {
            if (_enchantKind < 0) { BuildEnchantMenu(); return; }
            if (_enchantKind == 1) { BuildEnchantArmor(); return; }
            if (_enchantHelp) { BuildEnchantHelp(); return; }
            var inv = BagManager.Inventory;
            if (!Enchanting.Ready)
            {
                UiKit.Header(_buy, "ENCHANT", "unavailable", Wallet.Balance);
                ToolRow(_buy, S.ToolY, null, null, true, "BACK", () => { _enchantKind = -1; BuildBuy(); });
                UiKit.Rule(_buy, S.RuleToolY);
                UiKit.Empty(_buy, "Enchanting is unavailable in this game version.");
                return;
            }
            var target = _enchantTarget == null ? null : inv.Find(_enchantTarget);
            List<Enchanting.StockEntry> stockList = null;
            if (target == null && _enchantTarget != null && _enchantTarget.StartsWith("stock-"))
            {
                stockList = Enchanting.StockWeapons();
                foreach (var e in stockList) if (e.Item.Id == _enchantTarget) target = e.Item;
            }
            if (target == null) BuildEnchantList(stockList);
            else BuildEnchantTarget(target);
        }

        /// <summary>ENCHANT, pick a weapon: every bag weapon (equipped ones included), or with ARMORY the armory's stock weapons.</summary>
        private static void BuildEnchantList(List<Enchanting.StockEntry> stockList)
        {
            var inv = BagManager.Inventory;
            UiKit.Header(_buy, "ENCHANT", _enchantStock ? "pick an armory weapon" : "pick a weapon", Wallet.Balance);
            UiKit.Tabs(_buy, L, L + 0.5f, S.ToolY, new[] { "BAG", "ARMORY" }, _enchantStock ? 1 : 0, i => { _enchantStock = i == 1; _enchantPage = 0; BuildBuy(); });
            UiKit.ActionBtn(_buy, R - S.Action.x * 1.5f - S.Gap, S.ToolY, "HELP", () => { _enchantHelp = true; _helpPage = 0; BuildBuy(); }, S.Variant.Secondary);
            UiKit.ActionBtn(_buy, R - S.Action.x * 0.5f, S.ToolY, "BACK", () => { _enchantKind = -1; BuildBuy(); }, S.Variant.Secondary);
            UiKit.Rule(_buy, S.RuleToolY);

            // ARMORY: the first enchantment moves a stock weapon into the bag.
            var weapons = new List<LootItem>();
            var stockSlots = new Dictionary<string, int>();
            if (_enchantStock)
                foreach (var e in stockList ?? Enchanting.StockWeapons()) { weapons.Add(e.Item); stockSlots[e.Item.Id] = e.Slot; }
            else foreach (var w in inv.Items) if (w.IsWeapon) weapons.Add(w);
            weapons.Sort((a, b) => b.WeaponClass != a.WeaponClass ? b.WeaponClass.CompareTo(a.WeaponClass) : b.Value.CompareTo(a.Value));
            var pages = Math.Max(1, (weapons.Count + RowsPerPage - 1) / RowsPerPage);
            _enchantPage = Math.Max(0, Math.Min(_enchantPage, pages - 1));
            var start = _enchantPage * RowsPerPage;
            var cols = UiKit.Columns(1);
            for (var i = 0; i < RowsPerPage && start + i < weapons.Count; i++)
            {
                var item = weapons[start + i];
                if (!_enchantStock) Enchanting.ReadRolledPerks(item);
                var row = UiKit.NewRow(_buy, $"EnchRow_{i}", S.Row0Y - i * S.Pitch, i);
                UiKit.Icon(row, new Vector3(S.IconX, 0f, -0.03f), item);
                var slot = _enchantStock ? (stockSlots.TryGetValue(item.Id, out var ss) ? ss : -1) : inv.EquippedSlotOf(item);
                var chip = slot >= 0 ? "EQUIPPED" : !_enchantStock && item.IsBound ? "ARMORY" : null;
                UiKit.TypeTag(row, new Vector3(S.TagX, 0f, 0f), item, chip);
                var price = Enchanting.Price(item);
                UiKit.RowText(row, cols.TextEnd, UiKit.NameOf(item),
                    $"slots {Math.Min(Enchanting.UsedSlots(item), Enchanting.Slots(item.WeaponClass))}/{Enchanting.Slots(item.WeaponClass)} · {(item.DamageType >= 0 ? Enchanting.ElementName(item.DamageType).ToLowerInvariant() : "no element")}",
                    null, S.Text2);
                UiKit.Price(row, cols.Price, 0f, price, Wallet.Balance >= price);
                var captured = item;
                UiKit.ActionBtn(row, cols.Act[0], 0f, "SELECT", () => { _enchantTarget = captured.Id; BuildBuy(); });
            }
            if (weapons.Count == 0) UiKit.Empty(_buy, _enchantStock ? "No armory weapons can be enchanted." : "No weapons in your bag.");
            UiKit.Pager(_buy, _enchantPage, pages, () => { _enchantPage--; BuildBuy(); }, () => { _enchantPage++; BuildBuy(); });
        }

        /// <summary>Element colours on the enchant page: fire, ice, poison (poison is the game's own green).</summary>
        private static readonly string[] ElementHex = { "#FF8A3D", "#9CC8FF", "#30CB18" };

        /// <summary>
        /// The weapon's second line on the enchant page: "22 damage · Fire · Absorb · Elite Slayer".
        /// A third perk from before 0.13.2 is shown grey with "no effect" (the game keeps two).
        /// </summary>
        private static string WeaponDetail(LootItem w)
        {
            var parts = new List<string>();
            try
            {
                var m = System.Text.RegularExpressions.Regex.Match(StatsOf(w), @"\d+");
                if (m.Success) parts.Add($"{UiKit.Paint(S.TextHex, $"<b>{m.Value}</b>")} damage");
            }
            catch { }
            if (w.DamageType >= 0)
                parts.Add(UiKit.Paint(ElementHex[Math.Min(ElementHex.Length - 1, w.DamageType)], Enchanting.ElementName(w.DamageType)));
            var perks = 0;
            foreach (var p in new[] { w.PerkA, w.PerkB, w.PerkC })
            {
                if (p <= 0) continue;
                perks++;
                parts.Add(Enchanting.IsInactive(w, p) ? $"{Enchanting.PerkName(p)} (no effect)" : UiKit.Paint(S.TextHex, Enchanting.PerkName(p)));
            }
            if (perks == 0) parts.Add("no perks");
            if (w.Boomerang) parts.Add(UiKit.Paint(S.TextHex, Boomerang.Name));
            return string.Join(" · ", parts);
        }

        /// <summary>A heading line with the price of each change on the right: "ADD … each (coin) 220".</summary>
        private static void Section(float y, string title, string note, int price, bool affordable)
        {
            UiKit.Text(_buy, new Vector3(L, y, 0f), 0.9f, 0.05f, S.Body, $"{UiKit.Paint(S.TextHex, $"<b>{title}</b>")}   {UiKit.Grey(note)}", fit: true);
            UiKit.Label(_buy, R - 0.30f, y, 0.12f, 0.045f, S.Body, "each", S.Text2, TextAlignmentOptions.Right);
            UiKit.CoinNumber(_buy, R, y, 0.17f, price, affordable ? S.Gold : S.Red);
        }

        /// <summary>
        /// ENCHANT, one weapon: the weapon with all its stats on top, then ADD (perks and elements),
        /// BOOMERANG where the weapon can take it, and TAKE OFF (de-enchanting).
        /// </summary>
        private static void BuildEnchantTarget(LootItem target)
        {
            var inv = BagManager.Inventory;
            var preview = inv.Find(target.Id) == null;   // a stock weapon not bound yet
            if (!preview) Enchanting.ReadRolledPerks(target);
            var tslot = preview ? Enchanting.StockSlot(target.StockGuid) : inv.EquippedSlotOf(target);
            UiKit.Header(_buy, "ENCHANT", "add or take off perks and elements", Wallet.Balance);
            var note = tslot >= 0 ? $"Equipped on {Loadout.SlotNames[tslot].ToLowerInvariant()}: it stays in your hand." : "";
            if (target.IsBound) note += (note.Length > 0 ? "   " : "") + "An armory weapon.";
            ToolRow(_buy, S.ToolY, note, null, true, "BACK", () => { _enchantTarget = null; BuildBuy(); });
            UiKit.Rule(_buy, S.RuleToolY);

            // The weapon itself, laid out like the armor page (0.13.2): its name in the rarity colour
            // with rarity and type in grey after it, then damage, element and each perk by the
            // mod's name, instead of the game's own stats text with its own colours.
            var row = UiKit.NewRow(_buy, "EnchTarget", S.Row0Y, 0);
            UiKit.Icon(row, new Vector3(S.IconX, 0f, -0.03f), target);
            UiKit.TypeTag(row, new Vector3(S.TagX, 0f, 0f), target);
            UiKit.RowText(row, R - S.Gap, $"{UiKit.NameOf(target)}   {UiKit.Grey($"{LootTables.ClassName(target.WeaponClass)} {LootTables.TypeName(target.PropType).ToLowerInvariant()}")}",
                WeaponDetail(target), tslot >= 0 ? "EQUIPPED" : target.IsBound ? "ARMORY" : null, S.Text2);

            var gw = (S.PanelWidth - 0.08f - S.Gap * 3f) / 4f;
            float GridX(int i) => L + gw * 0.5f + (i % 4) * (gw + S.Gap);
            var y = S.Row0Y - 0.105f;

            var tprice = Enchanting.Price(target);
            var ok = Wallet.Balance >= tprice;
            Section(y, "ADD", $"one perk or element, slots {Math.Min(Enchanting.UsedSlots(target), Enchanting.Slots(target.WeaponClass))} / {Enchanting.Slots(target.WeaponClass)}", tprice, ok);
            y -= 0.08f;
            var options = Enchanting.Options(target);
            for (var i = 0; i < options.Count && i < 16; i++)
            {
                var (label, perkId, element) = options[i];
                var pid = perkId; var el = element;
                UiKit.Btn(_buy, new Vector3(GridX(i), y - (i / 4) * 0.10f, 0f), new Vector2(gw, S.Action.y), label.ToUpperInvariant(),
                    () => { var r = Enchanting.Enchant(target, pid, el); if (r != null) _enchantTarget = r.Id; Rebuild(); },
                    S.Variant.Primary, ok ? S.State.Normal : S.State.Disabled);
            }
            if (options.Count == 0)
            {
                UiKit.Label(_buy, L, y, 1.0f, 0.05f, S.Body, "Nothing more can be added to this weapon.", S.Text2);
                y -= 0.10f;
            }
            else y -= ((Math.Min(options.Count, 16) + 3) / 4) * 0.10f + 0.005f;

            // Boomerang (throwing weapons, or a sword with Spire): a flag, priced on its own.
            if (Boomerang.CanTake(target) && !target.Boomerang)
            {
                var bprice = Enchanting.BoomerangPrice(target);
                var bok = Wallet.Balance >= bprice;
                UiKit.Btn(_buy, new Vector3(GridX(0), y, 0f), new Vector2(gw, S.Action.y), Boomerang.Name.ToUpperInvariant(),
                    () => { var r = Enchanting.SetBoomerang(target, true); if (r != null) _enchantTarget = r.Id; Rebuild(); },
                    S.Variant.Primary, bok ? S.State.Normal : S.State.Disabled);
                UiKit.Label(_buy, GridX(1) - gw * 0.5f, y, 0.62f, 0.05f, S.Body, "flies back to your hand when thrown", S.Text2);
                UiKit.CoinNumber(_buy, R, y, 0.17f, bprice, bok ? S.Gold : S.Red);
                y -= 0.095f;
            }
            else if (target.Boomerang)
            {
                UiKit.Label(_buy, L, y, S.PanelWidth - 0.08f, 0.05f, S.Body,
                    $"{UiKit.Paint(S.TextHex, $"<b>{Boomerang.Name.ToUpperInvariant()}</b>")}   " +
                    (Boomerang.CanTake(target) ? "flies back to your hand when thrown" : "inactive, this weapon cannot be thrown"), S.Text2);
                y -= 0.095f;
            }

            // De-enchanting: take a perk or the element off. A stock weapon taken back to what it
            // had goes back to the armory. Red, like every undo.
            var removable = Enchanting.Removable(target);
            if (removable.Count > 0)
            {
                var dprice = Enchanting.DisenchantPrice(target);
                var dok = Wallet.Balance >= dprice;
                Section(y, "TAKE OFF", target.IsBound ? "back to how it was returns it to your armory" : "one perk or the element", dprice, dok);
                y -= 0.08f;
                string refundNote = null;
                for (var i = 0; i < removable.Count; i++)
                {
                    var (label, perkId, element) = removable[i];
                    var pid = perkId; var el = element;
                    // 0.13.2: a third perk from before never worked; taking it off is free and pays back.
                    var inactive = Enchanting.IsInactive(target, perkId);
                    if (inactive) refundNote = $"{label} never worked, the game keeps two perks. Taking it off is free.";
                    UiKit.Btn(_buy, new Vector3(GridX(i), y - (i / 4) * 0.10f, 0f), new Vector2(gw, S.Action.y), label.ToUpperInvariant(),
                        () => { var r = Enchanting.Disenchant(target, pid, el); if (r != null) _enchantTarget = r.Id; Rebuild(); },
                        inactive ? S.Variant.Sell : S.Variant.Danger, inactive || dok ? S.State.Normal : S.State.Disabled);
                }
                if (refundNote != null)
                    UiKit.Label(_buy, L, y - ((removable.Count + 3) / 4) * 0.10f + 0.02f, S.PanelWidth - 0.08f, 0.05f, S.Body, refundNote, S.Green);
            }
        }

        /// <summary>ENCHANT's first page: a weapon or your armor? Two cards.</summary>
        private static void BuildEnchantMenu()
        {
            var inv = BagManager.Inventory;
            UiKit.Header(_buy, "ENCHANT", "perks for weapons, stats for armor", Wallet.Balance);
            UiKit.Rule(_buy, S.RuleToolY);
            var cardW = (S.PanelWidth - 0.12f) / 2f;
            const float cardH = 0.8f;
            var cy = S.RuleToolY - 0.03f - cardH * 0.5f;
            var cards = new[]
            {
                (x: -cardW * 0.5f - 0.02f, title: "WEAPON", line: "Add perks and elements, or take them off", kind: 0),
                (x: cardW * 0.5f + 0.02f, title: "ARMOR", line: "Raise a stat, one step at a time", kind: 1),
            };
            foreach (var c in cards)
            {
                UiKit.Bar(_buy, new Vector3(c.x, cy, 0.006f), cardW, cardH, S.Card);
                UiKit.Bar(_buy, new Vector3(c.x, cy + cardH * 0.5f - 0.004f, 0.004f), cardW, 0.008f, S.Accent);
                UiKit.Label(_buy, c.x - (cardW - 0.06f) * 0.5f, cy + 0.12f, cardW - 0.06f, 0.07f, S.Title, $"<b>{c.title}</b>", S.Text, TextAlignmentOptions.Center);
                UiKit.Label(_buy, c.x - (cardW - 0.08f) * 0.5f, cy, cardW - 0.08f, 0.1f, S.Body, c.line, S.Text2, TextAlignmentOptions.Center, lines: 2);
                var kind = c.kind;
                UiKit.ActionBtn(_buy, c.x, cy - 0.2f, "OPEN", () => { _enchantKind = kind; _enchantTarget = null; _enchantHelp = false; BuildBuy(); });
            }
        }

        /// <summary>
        /// ENCHANT / ARMOR: the three slots, then the piece worn in the open one (name, rarity,
        /// sell price) and a card per stat: current, next and max as a percentage, the rarity's
        /// range as a slim bar with "5 / 13 upgrades done", the price beside an UPGRADE button that
        /// raises that one stat by 0.01. The sell price never changes.
        /// </summary>
        private static void BuildEnchantArmor()
        {
            var inv = BagManager.Inventory;
            UiKit.Header(_buy, "ENCHANT", "armor: raise one stat at a time", Wallet.Balance);
            _upSlot = Math.Max(0, Math.Min(2, _upSlot));
            var slotLabels = new string[3];
            for (var s = 0; s < 3; s++) slotLabels[s] = Armor.SlotNames[s].ToUpperInvariant();
            UiKit.Tabs(_buy, L, L + 0.72f, S.ToolY, slotLabels, _upSlot, i => { _upSlot = i; BuildBuy(); });
            UiKit.ActionBtn(_buy, R - S.Action.x * 0.5f, S.ToolY, "BACK", () => { _enchantKind = -1; BuildBuy(); }, S.Variant.Secondary);
            UiKit.Rule(_buy, S.RuleToolY);

            var piece = Armor.Worn(_upSlot);
            if (piece == null) { UiKit.Empty(_buy, $"Nothing worn on your {Armor.SlotNames[_upSlot].ToLowerInvariant()}."); return; }

            var cols = UiKit.Columns(1);
            var py = S.Row0Y + 0.01f;
            // 0.13.2: the name runs up to "sells for" (it stopped at 0.66 m and cut "Legendary head armor").
            UiKit.Text(_buy, new Vector3(L, py, 0f), cols.Price - S.PriceWidth * 0.5f - 0.32f + 0.10f - L, 0.055f, S.Row,
                $"{UiKit.NameOf(piece)}   {UiKit.Grey($"{LootTables.ClassName(piece.WeaponClass)} {Armor.SlotNames[_upSlot].ToLowerInvariant()} armor")}", fit: true);
            UiKit.Label(_buy, cols.Price - S.PriceWidth * 0.5f - 0.32f, py, 0.3f, 0.05f, S.Body, "sells for", S.Text2, TextAlignmentOptions.Right);
            UiKit.Price(_buy, cols.Price, py, SellPrice(piece));

            var stats = Armor.Decode(piece.ArmorStats);
            const float cardH = 0.33f;
            float[] vx = { L + 0.15f, L + 0.43f, L + 0.71f };
            for (var i = 0; i < stats.Count && i < 2; i++)
            {
                var (st, m) = stats[i];
                var d = Buffs.Find(st);
                var lo = Armor.Band(piece.WeaponClass, st).lo;
                var cur = Armor.MultToPct(st, m);
                // 0.13.2: a piece rolled under the older, higher caps can be above today's maximum
                // (CURRENT +28%, MAX +25%); MAX then shows what it has.
                var hi = Mathf.Max(Armor.MaxPct(piece, st), cur);
                var totalSteps = Mathf.Max(0, hi - lo);
                var label = d == null ? st : string.IsNullOrEmpty(d.Short) ? d.Flavor : d.Short;
                if (label.Length > 0) label = char.ToUpperInvariant(label[0]) + label.Substring(1);
                var atMax = Armor.AtMax(piece, st, m);
                var next = Armor.MultToPct(st, Armor.NextMult(piece, st, m));
                var price = Armor.UpgradePrice(piece);
                var afford = Wallet.Balance >= price;
                var cy = py - 0.07f - cardH * 0.5f - i * (cardH + 0.03f);
                string Pct(int v) => Armor.PctText(st, v);
                UiKit.Bar(_buy, new Vector3(0f, cy, 0.006f), S.PanelWidth - 0.03f, cardH, S.Card);
                UiKit.Label(_buy, L + 0.02f, cy + 0.12f, 0.6f, 0.055f, S.Row, $"<b>{label}</b>", S.Text);
                var caps = new[] { "CURRENT", "NEXT", "MAX" };
                for (var k = 0; k < 3; k++) UiKit.Label(_buy, vx[k] - 0.13f, cy + 0.05f, 0.26f, 0.045f, S.Body, caps[k], S.Text2, TextAlignmentOptions.Center);
                UiKit.Label(_buy, vx[0] - 0.13f, cy - 0.005f, 0.26f, 0.055f, S.Row, $"<b>{Pct(cur)}</b>", S.Text, TextAlignmentOptions.Center);
                if (atMax) UiKit.Label(_buy, vx[1] - 0.13f, cy - 0.005f, 0.26f, 0.055f, S.Row, "<b>-</b>", S.Text2, TextAlignmentOptions.Center);
                else UiKit.Label(_buy, vx[1] - 0.13f, cy - 0.005f, 0.26f, 0.055f, S.Row, $"<b>{Pct(next)}</b>", S.Green, TextAlignmentOptions.Center);
                UiKit.Label(_buy, vx[2] - 0.13f, cy - 0.005f, 0.26f, 0.055f, S.Row, $"<b>{Pct(hi)}</b>", S.Text, TextAlignmentOptions.Center);

                // the rarity's range: light up to now, a green sliver for the next step
                var done = Mathf.Clamp(cur - lo, 0, totalSteps);
                const float barW = 0.82f;
                var barL = L + 0.02f;
                var by = cy - 0.07f;
                var span = (float)Mathf.Max(1, hi - lo);
                var nowF = Mathf.Clamp01((cur - lo) / span);
                var nextF = atMax ? nowF : Mathf.Clamp01((next - lo) / span);
                UiKit.Bar(_buy, new Vector3(barL + barW * 0.5f, by, -0.002f), barW, 0.016f, S.Track);
                if (nowF > 0.001f) UiKit.Bar(_buy, new Vector3(barL + barW * nowF * 0.5f, by, -0.004f), barW * nowF, 0.016f, S.Fill);
                if (nextF > nowF + 0.001f) UiKit.Bar(_buy, new Vector3(barL + barW * (nowF + nextF) * 0.5f, by, -0.004f), barW * (nextF - nowF), 0.016f, S.Green);
                UiKit.Label(_buy, L + 0.02f, cy - 0.115f, 0.6f, 0.045f, S.Body, $"{done} / {totalSteps} upgrades done", S.Text2);

                if (atMax)
                {
                    UiKit.Label(_buy, cols.Price - S.PriceWidth * 0.5f, cy, S.PriceWidth, 0.045f, S.Body, "<b>MAXED</b>", S.Green, TextAlignmentOptions.Center);
                    UiKit.ActionBtn(_buy, cols.Act[0], cy, "UPGRADE", null, S.Variant.Primary, S.State.Disabled);
                }
                else
                {
                    var idx = i;
                    UiKit.Price(_buy, cols.Price, cy, price, afford);
                    UiKit.ActionBtn(_buy, cols.Act[0], cy, "UPGRADE", () => { Armor.Upgrade(piece, idx); }, S.Variant.Primary, afford ? S.State.Normal : S.State.Disabled);
                }
            }
            if (stats.Count == 0) UiKit.Empty(_buy, "This piece has no stats to upgrade.");
        }

        /// <summary>
        /// The table's HELP page: every enchantment with the game's own name and description for
        /// it, and the weapon types that can carry it. Paged; BACK returns to the weapon list.
        /// </summary>
        private static void BuildEnchantHelp()
        {
            var inv = BagManager.Inventory;
            UiKit.Header(_buy, "ENCHANT", "what the enchantments do", Wallet.Balance);
            ToolRow(_buy, S.ToolY, "Common weapons hold 1 perk, Unique, Rare and Legendary 2, plus one element. The game's own words for each:",
                null, true, "BACK", () => { _enchantHelp = false; BuildBuy(); });
            UiKit.Rule(_buy, S.RuleToolY);
            var docs = Enchanting.Documentation();
            var pages = Math.Max(1, (docs.Count + HelpRows - 1) / HelpRows);
            _helpPage = Math.Max(0, Math.Min(_helpPage, pages - 1));
            var y0 = S.RuleToolY - 0.01f - S.Compact_Pitch * 0.5f;
            var start = _helpPage * HelpRows;
            for (var i = 0; i < HelpRows && start + i < docs.Count; i++)
            {
                var d = docs[start + i];
                var row = UiKit.NewRow(_buy, $"HelpRow_{i}", y0 - i * S.Compact_Pitch, i, S.Compact_Pitch);
                UiKit.Label(row, L, 0.024f, S.PanelWidth - 0.08f, 0.045f, S.Body, $"<b>{d.Title}</b>   {UiKit.Grey(d.Types)}", S.Text);
                UiKit.Label(row, L, -0.024f, S.PanelWidth - 0.08f, 0.045f, S.Body, d.Description, S.Text2);
            }
            UiKit.Pager(_buy, _helpPage, pages, () => { _helpPage--; BuildBuy(); }, () => { _helpPage++; BuildBuy(); });
        }

        // ---- ARMOR ------------------------------------------------------------------------------

        /// <summary>
        /// Armor has no vanilla screen, so the kobold is where it is worn. Slot tabs (with the
        /// count of spare pieces), then three columns, one stat per row: WEARING, IN THE BAG and
        /// IF YOU SWAP (Diablo style: "-12" in red, "+7" in green). TAKE OFF sits under the worn
        /// piece, WEAR and SELL (and the &lt; &gt; arrows) under and beside the bag piece.
        /// </summary>
        private static void BuildArmor()
        {
            var inv = BagManager.Inventory;
            _armorSlot = Math.Max(0, Math.Min(2, _armorSlot));
            UiKit.Header(_buy, "ARMOR", "what you wear, and what a swap changes", Wallet.Balance);

            var colW = (S.PanelWidth - 0.08f) / 3f;
            var cw = colW - 0.04f;
            float[] cx = { L + colW * 0.5f, L + colW * 1.5f, L + colW * 2.5f };
            var tabLabels = new string[3];
            for (var s = 0; s < 3; s++)
            {
                var spare = 0; foreach (var i in inv.Items) if (i.IsArmor && i.WornSlot < 0 && i.ArmorSlot == s) spare++;
                tabLabels[s] = Armor.SlotNames[s].ToUpperInvariant() + (spare > 0 ? $" ({spare})" : "");
            }
            UiKit.Tabs(_buy, L, R, S.ToolY, tabLabels, _armorSlot, i => { _armorSlot = i; _armorPage = 0; BuildBuy(); });
            UiKit.Rule(_buy, S.RuleToolY);

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

            var hy = S.RuleToolY - 0.045f;   // captions
            var ny = hy - 0.095f;            // names, two lines
            var by = ny - 0.105f;            // buttons
            string[] caps = { "WEARING", cand == null ? "IN THE BAG" : $"IN THE BAG  {_armorPage + 1} / {pieces.Count}", "IF YOU SWAP" };
            for (var k = 0; k < 3; k++) UiKit.Label(_buy, cx[k] - cw * 0.5f, hy, cw, 0.045f, S.Body, $"<b>{caps[k]}</b>", S.Text2, TextAlignmentOptions.Center);
            UiKit.Text(_buy, new Vector3(cx[0], ny, 0f), cw, 0.1f, S.Row, worn == null ? UiKit.Grey($"no {slotName} armor worn") : UiKit.NameOf(worn), TextAlignmentOptions.Center, fit: true, lines: 2);
            var nameW = pieces.Count > 1 ? 0.25f : cw;
            UiKit.Text(_buy, new Vector3(cx[1], ny, 0f), nameW, 0.1f, S.Row,
                cand == null ? UiKit.Grey($"no spare {slotName} armor") : UiKit.NameOf(cand), TextAlignmentOptions.Center, fit: true, lines: 2);
            if (pieces.Count > 1)
            {
                var n = pieces.Count;
                var arrow = new Vector2(0.08f, S.Compact.y);
                UiKit.Btn(_buy, new Vector3(cx[1] - 0.175f, ny, 0f), arrow, "<", () => { _armorPage = (_armorPage + n - 1) % n; BuildBuy(); }, S.Variant.Secondary);
                UiKit.Btn(_buy, new Vector3(cx[1] + 0.175f, ny, 0f), arrow, ">", () => { _armorPage = (_armorPage + 1) % n; BuildBuy(); }, S.Variant.Secondary);
            }
            string verdict;
            if (cand == null) verdict = UiKit.Grey("nothing to compare");
            else if (worn == null) verdict = UiKit.Paint(S.GreenHex, $"adds {UiKit.Plural(rows.Count, "stat")}");
            else verdict = $"{UiKit.Paint(S.GreenHex, $"{better} better")}\n{UiKit.Paint(S.RedHex, $"{worse} worse")}";
            UiKit.Text(_buy, new Vector3(cx[2], ny, 0f), cw, 0.1f, S.Row, verdict, TextAlignmentOptions.Center, fit: true, lines: 2);

            // Buttons belong to the piece above them.
            if (worn != null)
            {
                var capturedWorn = worn;
                UiKit.ActionBtn(_buy, cx[0], by, "TAKE OFF", () => { Armor.Remove(capturedWorn); BuildBuy(); }, S.Variant.Secondary);
            }
            if (cand != null)
            {
                var capturedCand = cand;
                var pair = new Vector2(0.18f, S.Action.y);
                UiKit.Btn(_buy, new Vector3(cx[1] - 0.1f, by, 0f), pair, "WEAR", () => { Armor.Wear(capturedCand); BuildBuy(); });
                // Sell the piece you just compared, right here; a locked one has to be unlocked on the SELL page first.
                UiKit.Btn(_buy, new Vector3(cx[1] + 0.1f, by, 0f), pair, "SELL", () => { Sell(capturedCand); }, S.Variant.Primary, cand.Locked ? S.State.Disabled : S.State.Normal);
                if (cand.Locked)
                    UiKit.Label(_buy, cx[2] - cw * 0.5f, by, cw, 0.045f, S.Body, "locked, can't be sold", S.Text2, TextAlignmentOptions.Center);
                else
                {
                    UiKit.Label(_buy, cx[2] - 0.22f, by, 0.23f, 0.045f, S.Body, "sells for", S.Text2, TextAlignmentOptions.Right);
                    UiKit.Price(_buy, cx[2] + 0.11f, by, SellPrice(cand));
                }
            }
            var ry = by - 0.07f;
            UiKit.Rule(_buy, ry);

            // ---- one stat per row
            for (var r = 0; r < rows.Count && r < 6; r++)
            {
                var cmp = rows[r];
                var row = UiKit.NewRow(_buy, $"CmpRow_{r}", ry - 0.01f - S.Compact_Pitch * 0.5f - r * S.Compact_Pitch, r, S.Compact_Pitch);
                UiKit.Text(row, new Vector3(cx[0], 0f, 0f), cw, 0.055f, S.Row, UiKit.Paint(S.TextHex, cmp.WornText), TextAlignmentOptions.Center, fit: true);
                UiKit.Text(row, new Vector3(cx[1], 0f, 0f), cw, 0.055f, S.Row, UiKit.Paint(S.TextHex, cmp.NewText), TextAlignmentOptions.Center, fit: true);
                UiKit.Text(row, new Vector3(cx[2], 0f, 0f), cw, 0.055f, S.Row, cmp.Net, TextAlignmentOptions.Center, fit: true);
            }
            if (rows.Count == 0)
                UiKit.Label(_buy, -0.6f, ry - 0.1f, 1.2f, 0.06f, S.Row, $"Nothing worn and nothing spare for {slotName} armor.", S.Text2, TextAlignmentOptions.Center);
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
            inv.Save();
            Wallet.Pay(total, $"sold {n} {SweepName(cls)}");
            // 0.13.2: back to the first page. It kept the old page number (clamped), so after a sweep
            // the list could open on the last page with a single row.
            _sellPage = 0;
            ReconLog.Line($"sold {n} {SweepName(cls)} for {total} gold");
            Rebuild();
            BagPanel.Refresh();
        }

        /// <summary>A locked item cannot be sold, dropped or trashed until it is unlocked here.</summary>
        private static void ToggleLock(LootItem item)
        {
            var inv = BagManager.Inventory;
            var live = inv.Find(item.Id);
            if (live == null) { BagManager.Toast("Already gone."); Refresh(); return; }
            if (live.IsBound) { BagManager.Toast(Enchanting.LockedReason(live)); return; }
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
            if (live.Locked) { BagManager.Toast(live.IsBound ? Enchanting.LockedReason(live) : "Locked."); return; }
            var price = SellPrice(live);
            inv.Remove(live.Id);
            inv.Save();
            Wallet.Pay(price, $"sold {live.Name}");
            ReconLog.Line($"sold {live.Name} for {price} gold");
            Rebuild();
            BagPanel.Refresh();
        }


    }
}
