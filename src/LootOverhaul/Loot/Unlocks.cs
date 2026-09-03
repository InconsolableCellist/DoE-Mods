using System;
using System.Collections.Generic;
using Il2Cpp;
using LootOverhaul.Recon;

namespace LootOverhaul.Loot
{
    /// <summary>
    /// Lock-step with the game's own gating (design decision, 2026-09-02): the mod only
    /// offers what the player has already reached in the base game. Weapon types come from
    /// the armory (a type you have never crafted never drops), staff styles from the staffs
    /// you own, buff stats from the perks you have unlocked. All reads, refreshed on each
    /// lobby visit and on demand; nothing here writes.
    /// </summary>
    public static class Unlocks
    {
        private static HashSet<int> _types;
        private static HashSet<int> _staffStyles;
        private static List<string> _perks;
        private static float _at = -1f;

        public static void Invalidate() => _at = -1f;

        private static void Refresh()
        {
            if (_at >= 0f && UnityEngine.Time.unscaledTime - _at < 30f) return;
            _at = UnityEngine.Time.unscaledTime;
            var types = new HashSet<int>();
            var styles = new HashSet<int>();
            try
            {
                var lists = new[] { PlayerProfile.GetUnlockedWeapons(), PlayerProfile.GetUncraftedWeapons() };
                foreach (var list in lists)
                {
                    if (list == null) continue;
                    for (var i = 0; i < list.Count; i++)
                    {
                        try
                        {
                            var wm = list[i];
                            if (FabricatorBridge.IsBagWeapon(wm)) continue;   // our own injected copies don't count as progress
                            var t = (int)wm.GetWeaponType();
                            types.Add(t);
                            if (t == LootTables.Staff) styles.Add((int)wm.GetWeaponStyle());
                        }
                        catch { }
                    }
                }
            }
            catch (Exception e) { Core.Log.Warning($"Unlocks: armory read failed ({e.GetType().Name})."); }
            // Loadout weapons count too (starter weapons are not always in the armory list).
            try
            {
                var loadouts = new[] { PlayerData.LoadoutValues.WeaponL, PlayerData.LoadoutValues.WeaponR, PlayerData.LoadoutValues.WeaponB, PlayerData.LoadoutValues.WeaponS };
                foreach (var lv in loadouts)
                {
                    var data = PlayerProfile.GetLoadoutData(lv)?.ToString();
                    if (string.IsNullOrEmpty(data) || !data.StartsWith("#")) continue;
                    var wm = PlayerProfile.GetWeaponModule(new Il2CppSystem.Guid(data.Substring(1)));
                    if (wm == null || FabricatorBridge.IsBagWeapon(wm)) continue;
                    var t = (int)wm.GetWeaponType();
                    types.Add(t);
                    if (t == LootTables.Staff) styles.Add((int)wm.GetWeaponStyle());
                }
            }
            catch { }
            if (types.Count == 0) { types.Add(LootTables.Sword); types.Add(LootTables.Axe); types.Add(LootTables.Dagger); }
            _types = types; _staffStyles = styles;

            try
            {
                var perks = PlayerProfile.GetUnlockedPerks();
                var list = new List<string>();
                if (perks != null) for (var i = 0; i < perks.Count; i++) list.Add(perks[i]);
                _perks = list;
            }
            catch { _perks = new List<string>(); }
        }

        /// <summary>Weapon types that may drop or be sold: the droppable set ∩ what the player has reached.</summary>
        public static int[] DroppableTypes()
        {
            Refresh();
            var list = new List<int>();
            foreach (var t in LootTables.DroppableTypes) if (_types.Contains(t)) list.Add(t);
            if (list.Count == 0) list.Add(LootTables.Sword);
            return list.ToArray();
        }

        /// <summary>Staff styles (spells) the player owns; -1 means "any" only if none are known.</summary>
        public static int PickStaffStyle(System.Random rng)
        {
            Refresh();
            if (_staffStyles == null || _staffStyles.Count == 0) return -1;
            var arr = new List<int>(_staffStyles);
            return arr[rng.Next(arr.Count)];
        }

        public static IReadOnlyList<string> UnlockedPerks() { Refresh(); return _perks; }

        /// <summary>Does the perk list contain a name matching this exosuit stat (e.g. "Chest_Armor" ~ "ExoChestArmor")?</summary>
        public static bool PerkUnlocked(string exosuitField)
        {
            Refresh();
            var key = Norm(exosuitField);
            foreach (var p in _perks) if (Norm(p).Contains(key)) return true;
            return false;
        }

        private static string Norm(string s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var c in s ?? "") if (char.IsLetter(c)) sb.Append(char.ToLowerInvariant(c));
            return sb.ToString();
        }

        public static string Describe()
        {
            Refresh();
            var t = new List<string>(); foreach (var x in DroppableTypes()) t.Add(LootTables.TypeName(x));
            return $"types [{string.Join(", ", t)}], staff styles [{string.Join(",", _staffStyles ?? new HashSet<int>())}], perks [{string.Join(", ", _perks ?? new List<string>())}]";
        }
    }
}
