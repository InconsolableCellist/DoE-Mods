using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppTMPro;
using LootOverhaul.Recon;
using UnityEngine;
using Interop = LootOverhaul.Recon.Interop;
using S = LootOverhaul.Loot.UiStyle;

namespace LootOverhaul.Loot
{
    /// <summary>
    /// Milestone L2: the bag as a world-space panel in front of the player. Rows show the
    /// weapon's own generated mesh, its rarity-coloured name and stats line, what the kobold
    /// pays for it and what it weighs, with a Drop button per row. Sort by newest, value, weight or rarity; page
    /// through. Rebuilt from scratch whenever it is shown or changed — a bag is small and
    /// this keeps the code honest.
    /// </summary>
    public static class BagPanel
    {
        private enum Sort { Newest, Value, Weight, Rarity }

        private const int RowsPerPage = 6;

        private static GameObject _root;
        private static Transform _content;
        private static Sort _sort = Sort.Newest;
        private static int _page;
        private static bool _placedThisOpen;

        public static bool IsOpen => Interop.Alive(_root) && _root.activeSelf;

        public static void Toggle()
        {
            if (IsOpen) Hide(); else Show();
        }

        public static void Show()
        {
            UiKit.CaptureTemplates();
            if (!UiKit.Ready)
            {
                BagManager.Toast("Visit the lobby once before opening the bag.");
                BagManager.SummaryToast();
                return;
            }
            EnsureRoot();
            Place();
            _root.SetActive(true);
            Rebuild();
            BagManager.DumpIfChanged();
        }

        public static void Hide()
        {
            if (Interop.Alive(_root)) _root.SetActive(false);
        }

        /// <summary>Walk away from the open bag and it closes (BagAutoCloseMeters).</summary>
        public static void Tick()
        {
            if (!IsOpen) return;
            var limit = ModConfig.Fixed.BagAutoCloseMeters;
            if (limit <= 0f) return;
            try
            {
                var local = AvatarPlayer.LocalAvatar;
                if (!Interop.Alive(local)) return;
                var head = local.Head.position;
                var panel = _root.transform.position;
                var d = head - panel; d.y = 0f;
                if (d.sqrMagnitude > limit * limit) Hide();
            }
            catch { }
        }

        /// <summary>Called by the bag whenever its contents change, so an open panel stays true.</summary>
        public static void Refresh()
        {
            if (IsOpen) Rebuild();
        }

        private static void EnsureRoot()
        {
            if (Interop.Alive(_root)) return;
            _root = new GameObject("LootOverhaul_BagPanel");
            UnityEngine.Object.DontDestroyOnLoad(_root);
            var content = new GameObject("Content");
            content.transform.SetParent(_root.transform, false);
            _content = content.transform;
        }

        private static void Place()
        {
            try
            {
                var local = AvatarPlayer.LocalAvatar;
                if (!Interop.Alive(local)) return;
                var head = local.Head;
                var fwd = head.forward; fwd.y = 0f; fwd.Normalize();
                var pos = head.position + fwd * 0.85f + Vector3.down * 0.15f;
                _root.transform.position = pos;
                _root.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
                _root.transform.localScale = Vector3.one * 1.1f;
            }
            catch (Exception e) { Core.Log.Warning($"Bag panel placement failed: {e.GetType().Name}"); }
        }

        private static List<LootItem> Sorted(LootInventory inv)
        {
            var list = new List<LootItem>(inv.Items);
            switch (_sort)
            {
                case Sort.Value: list.Sort((a, b) => b.Value.CompareTo(a.Value)); break;
                case Sort.Weight: list.Sort((a, b) => b.Weight.CompareTo(a.Weight)); break;
                case Sort.Rarity: list.Sort((a, b) => b.WeaponClass != a.WeaponClass ? b.WeaponClass.CompareTo(a.WeaponClass) : b.Value.CompareTo(a.Value)); break;
                default: list.Sort((a, b) => b.FoundAt.CompareTo(a.FoundAt)); break;
            }
            return list;
        }

        /// <summary>
        /// 0.13.0: the same grid as the stall, from the shared components. Header with the token
        /// balance, sort tabs and CLOSE, a status line, six rows, the pager. DROP is always the
        /// small red button on the right; the item's own action (DRINK, WEAR, TAKE OFF) sits left of it.
        /// </summary>
        private static void Rebuild()
        {
            if (!Interop.Alive(_content)) return;
            UiKit.DestroyChildren(_content);
            var inv = BagManager.Inventory;
            var items = Sorted(inv);
            var pages = Math.Max(1, (items.Count + RowsPerPage - 1) / RowsPerPage);
            if (_page >= pages) _page = pages - 1;
            if (_page < 0) _page = 0;

            const float L = S.Left, R = S.Right;
            UiKit.Backdrop(_content, new Vector3(0f, 0f, 0.01f), S.PanelWidth, S.PanelHeight, S.Panel);
            UiKit.Header(_content, "BAG", $"{UiKit.Plural(inv.Items.Count, "item")}, {inv.TotalWeight:0.#} / {BagManager.Capacity:0} wt", inv.Gold);

            // Sort tabs and CLOSE.
            UiKit.Label(_content, L, S.StripY, 0.12f, 0.05f, S.Body, "SORT", S.Text2);
            var closeX = R - S.Compact.x * 0.5f;
            UiKit.Tabs(_content, L + 0.13f, closeX - S.Compact.x * 0.5f - 0.03f, S.StripY, new[] { "NEW", "VALUE", "WEIGHT", "RARITY" }, (int)_sort,
                i => { _sort = (Sort)i; _page = 0; Rebuild(); });
            UiKit.CompactBtn(_content, closeX, S.StripY, "CLOSE", Hide);
            UiKit.Rule(_content, S.RuleStripY);

            // What you wear and how many tonics run, in one short line (0.13.2: it listed every worn
            // piece's full name and every tonic's effect, two lines of small text). The ARMOR page
            // at the kobold has the details.
            var worn = 0;
            for (var s = 0; s < 3; s++) if (Armor.Worn(s) != null) worn++;
            var tonics = Buffs.ActiveCount;
            var status = (worn == 0 ? UiKit.Grey("No armor worn") : $"{UiKit.Grey("Wearing")} {worn} / 3 {UiKit.Grey("armor")}")
                       + UiKit.Grey("   ·   ")
                       + (tonics == 0 ? UiKit.Grey("no tonics this run") : $"{UiKit.Paint(S.TonicHex, UiKit.Plural(tonics, "tonic"))} {UiKit.Grey("this run")}");
            UiKit.Text(_content, new Vector3(L, S.ToolY, 0f), S.PanelWidth - 0.08f, 0.05f, S.Body, UiKit.Paint(S.TextHex, status), fit: true);
            UiKit.Rule(_content, S.RuleToolY);

            var dropX = R - S.Compact.x * 0.5f;
            var mainX = dropX - S.Compact.x * 0.5f - S.Gap - S.Action.x * 0.5f;
            var priceX = mainX - S.Action.x * 0.5f - S.Gap - S.PriceWidth * 0.5f;
            var textEnd = priceX - S.PriceWidth * 0.5f - S.Gap;
            var dropSize = new Vector2(S.Compact.x, S.Action.y);
            var start = _page * RowsPerPage;
            for (var i = 0; i < RowsPerPage && start + i < items.Count; i++)
            {
                var item = items[start + i];
                var row = UiKit.NewRow(_content, $"Row_{i}", S.Row0Y - i * S.Pitch, i);
                UiKit.Icon(row, new Vector3(S.IconX, 0f, -0.03f), item);
                UiKit.TypeTag(row, new Vector3(S.TagX, 0f, 0f), item);
                var slot = inv.EquippedSlotOf(item);
                var captured = item;

                string main = null; Action mainAct = null; var mainVar = S.Variant.Primary;
                var dropState = S.State.Normal;
                string chip = null;
                if (item.IsBuff) { main = "DRINK"; mainAct = () => Buffs.Drink(captured); }
                else if (item.IsArmor)
                {
                    if (item.WornSlot >= 0) { main = "TAKE OFF"; mainAct = () => Armor.Remove(captured); mainVar = S.Variant.Secondary; chip = "WORN"; dropState = S.State.Disabled; }
                    else { main = "WEAR"; mainAct = () => Armor.Wear(captured); }
                }
                else if (slot >= 0) { chip = "EQUIPPED"; dropState = S.State.Disabled; }
                // 0.13.2: no LOCKED word; DROP is the only thing a lock blocks here, and it says so when pressed.
                if (item.IsBound) chip = "ARMORY";

                UiKit.Btn(row, new Vector3(dropX, 0f, 0f), dropSize, "DROP", () => BagManager.Drop(captured), S.Variant.Danger, dropState);
                if (main != null) UiKit.ActionBtn(row, mainX, 0f, main, mainAct, mainVar);
                // What the kobold pays and what it weighs, on every row (asked for 2026-09-12).
                UiKit.Price(row, priceX, 0f, Booth.SellPrice(item), true, item.Weight);
                // A status word goes in the free button slot when there is one, else in front of the stats.
                if (chip != null && main == null) { UiKit.Chip(row, mainX - S.Action.x * 0.5f + 0.02f, 0f, chip); chip = null; }
                UiKit.RowText(row, textEnd, UiKit.NameOf(item), Booth.StatsOf(item), chip, S.Text2, statsLines: 1);
            }
            if (items.Count == 0) UiKit.Empty(_content, "Your bag is empty.");
            UiKit.Pager(_content, _page, pages, () => { _page = Math.Max(0, _page - 1); Rebuild(); }, () => { _page = Math.Min(pages - 1, _page + 1); Rebuild(); });
        }
    }
}
