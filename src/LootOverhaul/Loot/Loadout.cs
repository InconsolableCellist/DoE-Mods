using System;
using System.Collections.Generic;
using Il2Cpp;
using LootOverhaul.Gate;
using LootOverhaul.Recon;
using Interop = LootOverhaul.Recon.Interop;

namespace LootOverhaul.Loot
{
    /// <summary>
    /// The three battle slots (design decision 5), chosen at the game's own fabricator now
    /// that <see cref="FabricatorBridge"/> puts bag weapons in its gear list. The choice
    /// lives in the local JSON; the holster fill resolves it through the bridge, so nothing
    /// here has to run on spawn. <see cref="Apply"/> only gives immediate feedback right
    /// after choosing, through the same <c>AssignWeapon</c> the fabricator uses.
    /// </summary>
    public static class Loadout
    {
        public const int Left = 0, Right = 1, Back = 2;
        public static readonly string[] SlotNames = { "Left hip", "Right hip", "Back" };
        private static int _applied;
        private static bool _filledWhileInert;
        private static float _reapplyAt = -1f;
        public static int Reapplied;

        /// <summary>
        /// The holster fills during the scene load, and on the first lobby of a session that is
        /// before the gate has opened (the roster needs the room's properties to echo: lobby at
        /// 19:24:30.16, gate at 19:24:30.83 in the 2026-09-09 log). With the gate shut the
        /// bridge hands the game its vanilla loadout, so the loot weapons showed on the
        /// hologram but were not in the hands until re-equipped. A fill seen with the gate shut
        /// is remembered, and the loot slots are re-applied a moment after the gate opens.
        /// </summary>
        public static void Install()
        {
            Hooks.Patch(typeof(Holster), "InitHolsterContents", null, Hooks.Of(typeof(Loadout), nameof(HolsterFilled)));
        }

        private static void HolsterFilled()
        {
            if (ModGate.Active || _filledWhileInert) return;
            if (!HasLootSlots()) return;
            _filledWhileInert = true;
            ReconLog.Line("loadout: holster filled with the gate shut; loot slots will be re-applied when it opens");
        }

        public static void OnGateChanged(bool active)
        {
            if (!active) return;
            if (!_filledWhileInert) return;
            _filledWhileInert = false;
            _reapplyAt = UnityEngine.Time.unscaledTime + 1.5f;
        }

        public static void Tick()
        {
            if (_reapplyAt < 0f || UnityEngine.Time.unscaledTime < _reapplyAt) return;
            _reapplyAt = -1f;
            ReapplyAll("gate opened after the holster fill");
        }

        private static bool HasLootSlots()
        {
            try
            {
                var inv = BagManager.Inventory;
                for (var s = 0; s < 3; s++) if (inv.Loadout[s] != null && inv.Loadout[s].Source == "loot") return true;
            }
            catch { }
            return false;
        }

        /// <summary>Put every loot weapon the local file says is equipped into its slot now.</summary>
        public static void ReapplyAll(string why)
        {
            if (!ModConfig.Enabled.Value || !ModConfig.LoadoutEnabled.Value || !ModGate.Active) return;
            var inv = BagManager.Inventory;
            var n = 0;
            for (var s = 0; s < 3; s++)
            {
                var item = inv.Loadout[s];
                if (item == null || item.Source != "loot") continue;
                if (inv.Find(item.Id) == null) { inv.Loadout[s] = null; continue; }   // sold or dropped since
                Apply(s, item);
                n++;
            }
            if (n > 0) { Reapplied += n; ReconLog.Line($"loadout: re-applied {n} loot slot(s) ({why})"); }
        }

        public static PlayerData.LoadoutValues SlotValue(int slot) => slot switch
        {
            Left => PlayerData.LoadoutValues.WeaponL,
            Right => PlayerData.LoadoutValues.WeaponR,
            _ => PlayerData.LoadoutValues.WeaponB,
        };

        public static int SlotIndex(PlayerData.LoadoutValues lv) => lv switch
        {
            PlayerData.LoadoutValues.WeaponL => Left,
            PlayerData.LoadoutValues.WeaponR => Right,
            PlayerData.LoadoutValues.WeaponB => Back,
            _ => -1,
        };

        /// <summary>Set or clear a slot. A bag item is marked equipped so it cannot be sold or dropped.</summary>
        public static void Set(int slot, LootItem item, bool apply)
        {
            var inv = BagManager.Inventory;
            var old = inv.Loadout[slot];
            if (old != null)
            {
                var bagOld = inv.Find(old.Id);
                if (bagOld != null) bagOld.EquippedSlot = -1;
            }
            if (item == null)
            {
                inv.Loadout[slot] = null;
                inv.Save();
                BagPanel.Refresh(); Booth.Refresh();
                return;
            }
            for (var s = 0; s < 3; s++)
                if (s != slot && inv.Loadout[s] != null && inv.Loadout[s].Id == item.Id) inv.Loadout[s] = null;
            var bagItem = inv.Find(item.Id);
            if (bagItem != null) bagItem.EquippedSlot = slot;
            inv.Loadout[slot] = item;
            inv.Save();
            BagManager.Toast($"{SlotNames[slot]}: {item.ColoredName}");
            BagPanel.Refresh(); Booth.Refresh();
            if (apply) Apply(slot, item);
        }

        private static void Apply(int slot, LootItem item)
        {
            if (!ModGate.Active) return;
            var local = AvatarPlayer.LocalAvatar;
            if (!Interop.Alive(local)) return;
            ProfileWatch.Probe = "loadout apply";
            try
            {
                var wm = WeaponCodec.ToModule(item);
                var lv = SlotValue(slot);
                local.ResetWeapon(lv);
                var spawned = new Il2CppSystem.Collections.Generic.List<Prop>();
                local.AssignWeapon(lv, wm, spawned);
                _applied++;
                ReconLog.Line($"loadout: {SlotNames[slot]} <- {item.Name} (immediate); AssignWeapon spawned {spawned.Count} prop(s)");
            }
            catch (Exception e) { Core.Log.Warning($"Loadout apply for {SlotNames[slot]} failed: {e.GetType().Name}: {e.Message}"); }
            finally { ProfileWatch.Probe = null; }
        }

        public static string Describe()
        {
            var inv = BagManager.Inventory;
            var parts = new List<string>();
            for (var s = 0; s < 3; s++) parts.Add($"{SlotNames[s]}: {(inv.Loadout[s] == null ? "vanilla" : inv.Loadout[s].Name)}");
            return string.Join(" | ", parts) + $" (immediate applies {_applied}×, re-applied {Reapplied})";
        }
    }
}
