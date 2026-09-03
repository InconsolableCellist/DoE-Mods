using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppPhoton.Pun;
using LootOverhaul.Gate;
using LootOverhaul.Recon;
using UnityEngine;
using Interop = LootOverhaul.Recon.Interop;

namespace LootOverhaul.Loot
{
    /// <summary>
    /// The three battle slots (design decision 5). The booth picks a weapon for a slot from
    /// the vanilla armory or the bag; the choice lives in the local JSON only. After every
    /// holster fill (`Holster.InitHolsterContents`, which the recon showed runs for all six
    /// holsters, then the respawn pair, then the loadout pickups) the mod waits a moment and
    /// swaps the chosen weapons in through <c>AvatarPlayer.ResetWeapon</c> +
    /// <c>AssignWeapon</c>, the same calls the fabricator makes after its own PlayFab save,
    /// minus the save. The watchdog is armed around the call so any write shows up loudly.
    /// Hazard-modifier weapons win: if the game replaced the loadout, we leave it alone.
    /// </summary>
    public static class Loadout
    {
        public const int Left = 0, Right = 1, Back = 2;
        public static readonly string[] SlotNames = { "Left hip", "Right hip", "Back" };

        // Holster.PropSetting acceptance, read from the lobby survey 2026-09-02.
        private static readonly int[] HipTypes = { LootTables.Axe, LootTables.Hammer, LootTables.Sword, LootTables.Dagger, 400 /* SmallAxe */, 10 /* Blunt */ };
        private static readonly int[] BackTypes = { LootTables.Bow, LootTables.Crossbow, LootTables.Staff, 17 /* KineticStaff */, LootTables.Spear, LootTables.Longsword, LootTables.LongAxe, LootTables.Shield };

        private static float _applyAt = -1f;
        private static int _applied, _lastFillFrame;

        public static bool Accepts(int slot, int propType) =>
            Array.IndexOf(slot == Back ? BackTypes : HipTypes, propType) >= 0;

        public static PlayerData.LoadoutValues SlotValue(int slot) => slot switch
        {
            Left => PlayerData.LoadoutValues.WeaponL,
            Right => PlayerData.LoadoutValues.WeaponR,
            _ => PlayerData.LoadoutValues.WeaponB,
        };

        public static void Install()
        {
            Hooks.Patch(typeof(Holster), "InitHolsterContents", null, Hooks.Of(typeof(Loadout), nameof(OnHolsterFilled)));
        }

        private static void OnHolsterFilled(Holster __instance, bool __0)
        {
            // Six holsters fill in the same frame; schedule once, a moment after the last.
            if (Time.frameCount == _lastFillFrame) return;
            _lastFillFrame = Time.frameCount;
            _applyAt = Time.unscaledTime + 1.5f;
        }

        /// <summary>Called from the booth after a choice, and by Tick after a holster fill.</summary>
        public static void ApplySoon() => _applyAt = Time.unscaledTime + 0.1f;

        public static void Tick()
        {
            if (_applyAt < 0f || Time.unscaledTime < _applyAt) return;
            _applyAt = -1f;
            Apply("holster fill");
        }

        public static void Apply(string why)
        {
            if (!ModConfig.Enabled.Value || !ModConfig.LoadoutEnabled.Value || !ModGate.Active) return;
            var inv = BagManager.Inventory;
            var any = false;
            foreach (var e in inv.Loadout) if (e != null) any = true;
            if (!any) return;

            try
            {
                var hazards = Holster.hazardWeapons;
                if (hazards != null && hazards.Count > 0) { ReconLog.Line($"loadout: {hazards.Count} hazard weapon(s) active — leaving the game's loadout alone ({why})"); return; }
            }
            catch { }

            var local = AvatarPlayer.LocalAvatar;
            if (!Interop.Alive(local)) return;

            ProfileWatch.Probe = "loadout apply";
            try
            {
                for (var slot = 0; slot < 3; slot++)
                {
                    var item = inv.Loadout[slot];
                    if (item == null) continue;
                    try
                    {
                        var wm = WeaponCodec.ToModule(item);
                        var lv = SlotValue(slot);
                        local.ResetWeapon(lv);
                        var spawned = new Il2CppSystem.Collections.Generic.List<Prop>();
                        local.AssignWeapon(lv, wm, spawned);
                        _applied++;
                        ReconLog.Line($"loadout: {SlotNames[slot]} <- {item.Name} [{item.Source}] ({why}); AssignWeapon spawned {spawned.Count} prop(s)");
                    }
                    catch (Exception e) { Core.Log.Warning($"Loadout apply for {SlotNames[slot]} failed: {e.GetType().Name}: {e.Message}"); }
                }
            }
            finally { ProfileWatch.Probe = null; }
        }

        /// <summary>Set or clear a slot. A bag item is marked equipped; an armory item is copied.</summary>
        public static void Set(int slot, LootItem item)
        {
            var inv = BagManager.Inventory;
            var old = inv.Loadout[slot];
            if (old != null && old.Source == "loot")
            {
                var bagOld = inv.Find(old.Id);
                if (bagOld != null) bagOld.EquippedSlot = -1;
            }
            if (item == null)
            {
                inv.Loadout[slot] = null;
                inv.Save();
                BagManager.Toast($"{SlotNames[slot]}: back to your vanilla weapon on the next spawn.");
                return;
            }
            // One item, one slot.
            for (var s = 0; s < 3; s++)
                if (s != slot && inv.Loadout[s] != null && inv.Loadout[s].Id == item.Id) inv.Loadout[s] = null;
            if (item.Source == "loot")
            {
                var bagItem = inv.Find(item.Id);
                if (bagItem != null) bagItem.EquippedSlot = slot;
            }
            inv.Loadout[slot] = item;
            inv.Save();
            BagManager.Toast($"{SlotNames[slot]}: {item.ColoredName}");
            ApplySoon();
        }

        /// <summary>The vanilla armory as item records, read-only from the profile.</summary>
        public static List<LootItem> ArmoryItems()
        {
            var list = new List<LootItem>();
            try
            {
                var unlocked = PlayerProfile.GetUnlockedWeapons();
                for (var i = 0; i < unlocked.Count; i++)
                {
                    try
                    {
                        var item = WeaponCodec.FromModule(unlocked[i]);
                        item.Source = "armory";
                        item.Id = "armory-" + (item.WeaponGuid ?? item.RandomSeed.ToString());
                        list.Add(item);
                    }
                    catch { }
                }
            }
            catch (Exception e) { Core.Log.Warning($"Armory read failed: {e.GetType().Name}: {e.Message}"); }
            return list;
        }

        public static string Describe()
        {
            var inv = BagManager.Inventory;
            var parts = new List<string>();
            for (var s = 0; s < 3; s++) parts.Add($"{SlotNames[s]}: {(inv.Loadout[s] == null ? "vanilla" : inv.Loadout[s].Name)}");
            return string.Join(" | ", parts) + $" (applied {_applied}×)";
        }
    }
}
