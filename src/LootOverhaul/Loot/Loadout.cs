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
            return string.Join(" | ", parts) + $" (immediate applies {_applied}×)";
        }
    }
}
