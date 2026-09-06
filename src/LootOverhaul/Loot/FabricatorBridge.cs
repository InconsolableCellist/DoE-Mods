using System;
using System.Collections.Generic;
using Il2Cpp;
using LootOverhaul.Gate;
using LootOverhaul.Recon;
using UnityEngine;
using Interop = LootOverhaul.Recon.Interop;

namespace LootOverhaul.Loot
{
    /// <summary>
    /// Bag weapons inside the game's own fabricator (design decision 6, the "version 2
    /// merge", pulled forward because the booth's own picker was the wrong shape).
    ///
    /// Five read-side patches make loot look owned to the game, and three write-side
    /// patches catch what the game would then try to save:
    ///
    /// - <c>PlayerProfile.GetUnlockedWeapons</c> returns a COPY of the armory with the bag
    ///   appended, so the gear list shows them. Never the live list: the game may mutate
    ///   what it gets back, and a copy would swallow a real unlock.
    /// - <c>GetWeaponModule(guid)</c> and <c>GetLoadoutData(slot)</c> resolve a bag weapon
    ///   that the local file says is equipped, so the holster fill spawns it natively — no
    ///   re-apply, no hazard-list confusion.
    /// - <c>WeaponModule.GetDisplayName</c> marks bag weapons with a prefix.
    /// - <c>SetLoadoutData</c> with a bag GUID is turned into a local-file write;
    ///   <c>AddUnlockedWeapon</c> / <c>RemoveUnlockedWeapon</c> on a bag weapon are turned
    ///   into bag operations. The originals never run for our GUIDs.
    ///
    /// While any profile mutator or the profile save is running, injection is suspended:
    /// inside those calls the game sees exactly its own data.
    /// </summary>
    public static class FabricatorBridge
    {
        public const string Marker = "[LOOT] ";

        private static int _suspend;
        private static readonly Dictionary<string, WeaponModule> ModuleCache = new Dictionary<string, WeaponModule>();
        public static int Injected, Resolved, LoadoutWrites, Blocked;

        private static bool Active => _suspend == 0 && ModConfig.Enabled.Value && ModConfig.LoadoutEnabled.Value && ModGate.Active;

        public static void Install()
        {
            var t = typeof(FabricatorBridge);
            // Suspend injection inside every profile mutator and the save.
            foreach (var name in new[] { "SavePlayerProfile", "AddUncraftedWeapon", "RemoveUncraftedWeapon", "UnlockWeapon", "OwnsVendorWeapon", "SetData", "SetCharacterData", "IncrementCharacterData" })
                Hooks.Patch(typeof(PlayerProfile), name, Hooks.Of(t, nameof(Suspend)), Hooks.Of(t, nameof(Resume)));
            Hooks.Patch(typeof(PlayerProfile), "AddUnlockedWeapon", Hooks.Of(t, nameof(AddUnlocked_Prefix)), Hooks.Of(t, nameof(Resume)));
            Hooks.Patch(typeof(PlayerProfile), "RemoveUnlockedWeapon", Hooks.Of(t, nameof(RemoveUnlocked_Prefix)), Hooks.Of(t, nameof(Resume)));
            Hooks.Patch(typeof(PlayerProfile), "SetLoadoutData", Hooks.Of(t, nameof(SetLoadout_Prefix)), Hooks.Of(t, nameof(Resume)));

            Hooks.Patch(typeof(PlayerProfile), "GetUnlockedWeapons", null, Hooks.Of(t, nameof(GetUnlocked_Postfix)));
            Hooks.Patch(typeof(PlayerProfile), "GetWeaponModule", null, Hooks.Of(t, nameof(GetWeaponModule_Postfix)));
            Hooks.Patch(typeof(PlayerProfile), "GetLoadoutData", null, Hooks.Of(t, nameof(GetLoadoutData_Postfix)));
            Hooks.Patch(typeof(WeaponModule), "GetDisplayName", null, Hooks.Of(t, nameof(DisplayName_Postfix)));
            // The pedestal fills its thumbnails through SetCustomItemType (Fabricator.UpdateItemButtons,
            // read from the assembly 2026-09-04), not SetCustomModule; both are tagged.
            Hooks.Patch(typeof(ModuleButton), "SetCustomModule", null, Hooks.Of(t, nameof(ModuleButton_Postfix)));
            Hooks.Patch(typeof(ModuleButton), "SetCustomItemType", null, Hooks.Of(t, nameof(ModuleButtonType_Postfix)));
            // The trash can at the pedestal: the game removes the weapon and credits its salvage
            // value as real coins (EV_TrashModule -> IncrementCharacterData(Coins, salvage)). For a
            // bag weapon the removal is ours and the coins must not happen.
            Hooks.Patch(typeof(Fabricator), "EV_TrashModule", Hooks.Of(t, nameof(Trash_Prefix)), Hooks.Of(t, nameof(Trash_Postfix)));
            Hooks.Patch(typeof(PlayerProfile), "IncrementCharacterData", Hooks.Of(t, nameof(Increment_Prefix)), null);
        }

        private static int _trashingBagWeapon;
        public static int CoinsBlocked;

        private static void Trash_Prefix(Fabricator __instance)
        {
            try
            {
                BaseModule sel = null;
                try { sel = __instance.selectedModule; } catch { }
                var wm = sel == null ? null : sel.TryCast<WeaponModule>();
                if (wm != null && ModGate.Active && FindByGuid(GuidOf(wm)) != null)
                {
                    _trashingBagWeapon++;
                    ReconLog.Line($"fabricator: trashing bag weapon {Interop.OneLine(wm.GetDisplayName(false))}; salvage coins will be refused");
                }
            }
            catch (Exception e) { Core.Log.Warning($"Trash prefix failed: {e.GetType().Name}: {e.Message}"); }
        }

        private static void Trash_Postfix()
        {
            if (_trashingBagWeapon > 0) _trashingBagWeapon--;
        }

        private static bool Increment_Prefix(PlayerData.CharacterValues __0, int __1, ref bool __result)
        {
            if (_trashingBagWeapon <= 0 || __0 != PlayerData.CharacterValues.Coins) return true;
            CoinsBlocked++;
            ReconLog.Line($"fabricator: refused IncrementCharacterData(Coins, {__1}) for a trashed bag weapon");
            BagManager.Toast("Trashed loot pays nothing here. The kobold buys shinies.");
            __result = true;
            return false;
        }

        private static void Suspend() => _suspend++;
        private static void Resume() { if (_suspend > 0) _suspend--; }

        public static string Norm(string guid) => string.IsNullOrEmpty(guid) ? "" : guid.Replace("-", "").Trim().ToLowerInvariant();
        public static string SaveString(LootItem item) => "#" + Norm(item.WeaponGuid);

        private static LootItem FindByGuid(string guidNorm)
        {
            if (string.IsNullOrEmpty(guidNorm)) return null;
            foreach (var i in BagManager.Inventory.Items)
                if (i.IsWeapon && Norm(i.WeaponGuid) == guidNorm) return i;
            return null;
        }

        /// <summary>Is this module one of ours (a bag weapon injected into the game's lists)?</summary>
        public static bool IsBagWeapon(WeaponModule wm)
        {
            try { return wm != null && FindByGuid(GuidOf(wm)) != null; } catch { return false; }
        }

        private static string GuidOf(WeaponModule wm)
        {
            try { return Norm(wm.GetGuid().ToString()); } catch { return ""; }
        }

        private static WeaponModule ModuleFor(LootItem item)
        {
            var key = Norm(item.WeaponGuid);
            if (ModuleCache.TryGetValue(key, out var wm) && wm != null) return wm;
            wm = WeaponCodec.ToModule(item);
            ModuleCache[key] = wm;
            return wm;
        }

        // ---- reads ----------------------------------------------------------------------------

        private static void GetUnlocked_Postfix(ref Il2CppSystem.Collections.Generic.List<WeaponModule> __result)
        {
            try
            {
                if (!Active) return;
                var inv = BagManager.Inventory;
                if (inv.Items.Count == 0) return;
                var copy = new Il2CppSystem.Collections.Generic.List<WeaponModule>();
                if (__result != null) for (var i = 0; i < __result.Count; i++) copy.Add(__result[i]);
                var n = 0;
                foreach (var item in inv.Items)
                {
                    if (!item.IsWeapon) continue;
                    try { copy.Add(ModuleFor(item)); n++; } catch { }
                }
                Injected = n;
                __result = copy;
            }
            catch (Exception e) { Core.Log.Warning($"GetUnlockedWeapons injection failed: {e.GetType().Name}: {e.Message}"); }
        }

        private static void GetWeaponModule_Postfix(Il2CppSystem.Guid __0, ref WeaponModule __result)
        {
            try
            {
                if (__result != null || !Active) return;
                var item = FindByGuid(Norm(__0.ToString()));
                if (item == null) return;
                __result = ModuleFor(item);
                Resolved++;
            }
            catch (Exception e) { Core.Log.Warning($"GetWeaponModule bridge failed: {e.GetType().Name}: {e.Message}"); }
        }

        private static void GetLoadoutData_Postfix(PlayerData.LoadoutValues __0, ref Il2CppSystem.Object __result)
        {
            try
            {
                if (!Active) return;
                var slot = Loadout.SlotIndex(__0);
                if (slot < 0) return;
                var item = BagManager.Inventory.Loadout[slot];
                if (item == null || item.Source != "loot") return;
                if (FindByGuid(Norm(item.WeaponGuid)) == null) return;   // sold or dropped since; fall back to vanilla
                __result = (Il2CppSystem.String)SaveString(item);
            }
            catch (Exception e) { Core.Log.Warning($"GetLoadoutData bridge failed: {e.GetType().Name}: {e.Message}"); }
        }

        private static void DisplayName_Postfix(WeaponModule __instance, ref string __result)
        {
            try
            {
                if (!Active || string.IsNullOrEmpty(__result) || __result.Contains(Marker)) return;
                if (FindByGuid(GuidOf(__instance)) == null) return;
                __result = Marker + __result;
            }
            catch { }
        }

        private static void ModuleButton_Postfix(ModuleButton __instance, BaseModule __0) => TagButton(__instance, __0);
        private static void ModuleButtonType_Postfix(ModuleButton __instance, BaseModule __1) => TagButton(__instance, __1);

        private static bool _tagLogged;

        /// <summary>
        /// A small gold "LOOT" tag over the pedestal's thumbnail for a bag weapon. Placed on
        /// the thumbnail's own icon bounds (the earlier version used the fabricate button's
        /// width and drew off the tile, 2026-09-04) and kept at world scale under the tile.
        /// </summary>
        private static void TagButton(ModuleButton __instance, BaseModule module)
        {
            try
            {
                if (!Interop.Alive(__instance)) return;
                var wm = module == null ? null : module.TryCast<WeaponModule>();
                var ours = wm != null && Active && FindByGuid(GuidOf(wm)) != null;
                var t = __instance.transform.Find("LootTag");
                if (!ours) { if (t != null) t.gameObject.SetActive(false); return; }
                if (t != null) { t.gameObject.SetActive(true); return; }

                // The tile's visual bounds: its icon renderer, else every renderer under it.
                Bounds b; var have = false;
                try
                {
                    var icon = __instance.icon;
                    if (Interop.Alive(icon)) { b = icon.bounds; have = true; } else b = new Bounds(__instance.transform.position, Vector3.zero);
                }
                catch { b = new Bounds(__instance.transform.position, Vector3.zero); }
                if (!have)
                {
                    foreach (var r in __instance.GetComponentsInChildren<Renderer>())
                    {
                        if (!Interop.Alive(r)) continue;
                        if (!have) { b = r.bounds; have = true; } else b.Encapsulate(r.bounds);
                    }
                }
                var tile = have ? b : new Bounds(__instance.transform.position, new Vector3(0.1f, 0.1f, 0.01f));
                var h = Mathf.Clamp(tile.size.y, 0.03f, 0.3f);

                var tag = new GameObject("LootTag");
                tag.transform.SetParent(__instance.transform, false);
                // World scale one under a scaled tile, so the text size below is in metres.
                var ls = __instance.transform.lossyScale;
                tag.transform.localScale = new Vector3(1f / Mathf.Max(0.001f, ls.x), 1f / Mathf.Max(0.001f, ls.y), 1f / Mathf.Max(0.001f, ls.z));
                tag.transform.rotation = __instance.transform.rotation;
                // Top-left corner of the tile, a hair toward the viewer.
                var right = __instance.transform.right; var up = __instance.transform.up; var fwd = __instance.transform.forward;
                tag.transform.position = tile.center - right * (tile.extents.x - 0.004f) + up * (tile.extents.y - 0.004f) - fwd * 0.004f;
                UiKit.Text(tag.transform, Vector3.zero, h * 1.5f, h * 0.3f, h * 2.2f, "<color=#F5C542><b>LOOT</b></color>");
                if (!_tagLogged) { _tagLogged = true; ReconLog.Line($"pedestal tag placed on tile {tile.size.x:0.###}×{tile.size.y:0.###} m (icon {(have ? "found" : "missing")}), tile scale {ls.x:0.###}"); }
            }
            catch (Exception e) { Core.Log.Warning($"ModuleButton tag failed: {e.GetType().Name}: {e.Message}"); }
        }

        // ---- writes ---------------------------------------------------------------------------

        private static bool SetLoadout_Prefix(PlayerData.LoadoutValues __0, string __1, ref bool __result)
        {
            _suspend++;
            try
            {
                if (!ModGate.Active || !ModConfig.Enabled.Value || !ModConfig.LoadoutEnabled.Value) return true;
                var slot = Loadout.SlotIndex(__0);
                if (slot < 0) return true;
                var data = __1 ?? "";
                var item = data.StartsWith("#") ? FindByGuid(Norm(data.Substring(1))) : null;
                if (item == null)
                {
                    // A vanilla weapon is going into this slot: clear any loot override and let the game save.
                    if (BagManager.Inventory.Loadout[slot] != null) Loadout.Set(slot, null, apply: false);
                    return true;
                }
                LoadoutWrites++;
                ReconLog.Line($"fabricator: {Loadout.SlotNames[slot]} <- {item.Name} (bag) — kept local, profile write skipped");
                Loadout.Set(slot, item, apply: true);
                __result = true;
                return false;
            }
            catch (Exception e) { Core.Log.Warning($"SetLoadoutData bridge failed: {e.GetType().Name}: {e.Message}"); return true; }
        }

        private static bool AddUnlocked_Prefix(WeaponModule __0, ref bool __result)
        {
            _suspend++;
            try
            {
                if (!ModGate.Active || FindByGuid(GuidOf(__0)) == null) return true;
                Blocked++;
                ReconLog.Line($"fabricator: AddUnlockedWeapon on a bag weapon blocked ({Interop.OneLine(__0.GetDisplayName(false))})");
                __result = true;
                return false;
            }
            catch { return true; }
        }

        private static bool RemoveUnlocked_Prefix(WeaponModule __0, ref bool __result)
        {
            _suspend++;
            try
            {
                if (!ModGate.Active) return true;
                var item = FindByGuid(GuidOf(__0));
                if (item == null) return true;
                Blocked++;
                if (item.Locked)
                {
                    BagManager.Toast($"{item.ColoredName} is locked. Unlock it at the kobold first.");
                    ReconLog.Line($"fabricator: trash refused, bag weapon {item.Name} is locked");
                    __result = false;
                    return false;
                }
                // Trash at the fabricator means trash: the item leaves the bag.
                BagManager.Inventory.Remove(item.Id);
                BagManager.Inventory.Save();
                ModuleCache.Remove(Norm(item.WeaponGuid));
                BagManager.Toast($"Trashed {item.ColoredName}");
                ReconLog.Line($"fabricator: trashed bag weapon {item.Name}");
                BagPanel.Refresh(); Booth.Refresh();
                __result = true;
                return false;
            }
            catch { return true; }
        }

        public static string Describe() => $"injected {Injected} into the gear list, resolved {Resolved} holster lookups, {LoadoutWrites} loadout write(s) kept local, {Blocked} armory write(s) blocked, {CoinsBlocked} salvage coin write(s) refused";
    }
}
