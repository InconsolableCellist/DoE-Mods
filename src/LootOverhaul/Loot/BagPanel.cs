using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppTMPro;
using LootOverhaul.Recon;
using UnityEngine;
using Interop = LootOverhaul.Recon.Interop;

namespace LootOverhaul.Loot
{
    /// <summary>
    /// Milestone L2: the bag as a world-space panel in front of the player. Rows show the
    /// weapon's own generated mesh, its rarity-coloured name and stats line, weight and
    /// value, with a Drop button per row. Sort by newest, value, weight or rarity; page
    /// through. Rebuilt from scratch whenever it is shown or changed — a bag is small and
    /// this keeps the code honest.
    /// </summary>
    public static class BagPanel
    {
        private enum Sort { Newest, Value, Weight, Rarity }

        private const int RowsPerPage = 6;
        private const float RowHeight = 0.11f;
        private const float Width = 1.05f;

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
                BagManager.Toast("Bag panel needs the lobby once to borrow the game's buttons and font.");
                BagManager.SummaryToast();
                return;
            }
            EnsureRoot();
            Place();
            _root.SetActive(true);
            Rebuild();
            BagManager.DumpToTranscript();
        }

        public static void Hide()
        {
            if (Interop.Alive(_root)) _root.SetActive(false);
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

        private static void Rebuild()
        {
            if (!Interop.Alive(_content)) return;
            UiKit.DestroyChildren(_content);
            var inv = BagManager.Inventory;
            var items = Sorted(inv);
            var pages = Math.Max(1, (items.Count + RowsPerPage - 1) / RowsPerPage);
            if (_page >= pages) _page = pages - 1;
            if (_page < 0) _page = 0;

            var height = 0.32f + RowsPerPage * RowHeight;
            UiKit.Backdrop(_content, new Vector3(0f, 0f, 0.01f), Width, height, new Color(0.05f, 0.05f, 0.08f, 1f));

            var top = height * 0.5f;
            UiKit.Text(_content, new Vector3(-Width * 0.5f + 0.03f, top - 0.06f, 0f), Width - 0.06f, 0.08f, 1.6f,
                $"<b>Bag</b>   {inv.Items.Count} item(s)   {inv.TotalWeight:0.#} / {ModConfig.BagWeightCapacity.Value:0} wt   {inv.Gold} gold   sort: {_sort}");

            // Sort + close buttons along the second line.
            var by = top - 0.16f;
            UiKit.Button(_content, new Vector3(-0.40f, by, 0f), "NEW", () => { _sort = Sort.Newest; _page = 0; Rebuild(); }, 0.55f);
            UiKit.Button(_content, new Vector3(-0.22f, by, 0f), "VALUE", () => { _sort = Sort.Value; _page = 0; Rebuild(); }, 0.55f);
            UiKit.Button(_content, new Vector3(-0.04f, by, 0f), "WEIGHT", () => { _sort = Sort.Weight; _page = 0; Rebuild(); }, 0.55f);
            UiKit.Button(_content, new Vector3(0.14f, by, 0f), "RARITY", () => { _sort = Sort.Rarity; _page = 0; Rebuild(); }, 0.55f);
            UiKit.Button(_content, new Vector3(0.42f, by, 0f), "CLOSE", Hide, 0.55f);

            var y0 = top - 0.28f;
            var start = _page * RowsPerPage;
            for (var i = 0; i < RowsPerPage && start + i < items.Count; i++)
            {
                var item = items[start + i];
                var y = y0 - i * RowHeight;
                var row = new GameObject($"Row_{i}");
                row.transform.SetParent(_content, false);
                row.transform.localPosition = new Vector3(0f, y, 0f);

                UiKit.WeaponPreview(row.transform, new Vector3(-0.45f, 0f, -0.02f), item, 0.12f);

                string stats = "";
                try { stats = Interop.OneLine(WeaponCodec.ToModule(item).GetStatsText()); } catch { }
                UiKit.Text(row.transform, new Vector3(-0.36f, 0.022f, 0f), 0.70f, 0.05f, 1.25f,
                    $"{item.ColoredName}   <size=75%>{LootTables.TypeName(item.PropType)} t{item.WeaponTier + 1}</size>");
                UiKit.Text(row.transform, new Vector3(-0.36f, -0.026f, 0f), 0.70f, 0.045f, 0.95f,
                    $"<size=90%>{stats}</size>   wt {item.Weight:0.#}   value {item.Value}");

                var captured = item;
                if (item.EquippedSlot >= 0)
                    UiKit.Text(row.transform, new Vector3(0.36f, 0f, 0f), 0.16f, 0.05f, 0.9f, $"<color=#F5C542>{Loadout.SlotNames[item.EquippedSlot]}</color>");
                else
                    UiKit.Button(row.transform, new Vector3(0.44f, 0f, 0f), "DROP", () => BagManager.Drop(captured), 0.5f);
            }

            var bottom = -top + 0.06f;
            UiKit.Text(_content, new Vector3(-0.08f, bottom, 0f), 0.3f, 0.06f, 1.1f, $"page {_page + 1} / {pages}", TextAlignmentOptions.Center);
            if (pages > 1)
            {
                UiKit.Button(_content, new Vector3(-0.30f, bottom, 0f), "<", () => { _page = Math.Max(0, _page - 1); Rebuild(); }, 0.45f);
                UiKit.Button(_content, new Vector3(0.14f, bottom, 0f), ">", () => { _page = Math.Min(pages - 1, _page + 1); Rebuild(); }, 0.45f);
            }
            if (items.Count == 0)
                UiKit.Text(_content, new Vector3(-0.3f, y0 - RowHeight, 0f), 0.6f, 0.06f, 1.2f, "Empty. Go kill something.", TextAlignmentOptions.Center);
        }
    }
}
