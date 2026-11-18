using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Il2Cpp;
using HazardList = Il2CppSystem.Collections.Generic.List<Il2Cpp.GameManager.HazardModifier>;

namespace HazardTweaks
{
    /// <summary>
    /// Which hazards are dropped, and the two hooks that drop them.
    ///
    /// <c>GenerateDeterministicHazards</c> is where the scanner (and anything else building a
    /// mission) gets a dungeon's list, so filtering its result keeps the board, the bonus total
    /// and the run in agreement. <c>InitHazardsForLevel</c> is where a run switches the hazards
    /// on; filtering there too catches a list that came from somewhere else, such as a host
    /// without the mod.
    /// </summary>
    public static class Hazards
    {
        // Weapon-limiting hazards by name, so one the game adds later (CrossbowsOnly, SpearsOnly ...)
        // is caught without a new build. Vanilla 2026-11: BowsOnly, AxesOnly, SwordAndShield,
        // DaggersOnly, FireballStaffs, RandomWeapons.
        private static readonly Regex WeaponHazard =
            new Regex(@"Only$|^SwordAndShield$|^FireballStaffs$|^RandomWeapons", RegexOptions.IgnoreCase);

        private static HashSet<int> _removed;

        public static void Install()
        {
            Hooks.Patch(Hooks.Of(typeof(GameManager), nameof(GameManager.GenerateDeterministicHazards)),
                null, Hooks.Of(typeof(Hazards), nameof(GeneratePostfix)), "GameManager.GenerateDeterministicHazards");
            Hooks.Patch(Hooks.Of(typeof(GameManager), nameof(GameManager.InitHazardsForLevel)),
                Hooks.Of(typeof(Hazards), nameof(InitPrefix)), null, "GameManager.InitHazardsForLevel");
        }

        /// <summary>The lobby check at the moment a list is rolled or switched on; logs why when it says no.</summary>
        public static bool ActiveLogged(string where)
        {
            var on = Gate.Evaluate(out var why);
            if (!on && ModConfig.VerboseLogging.Value) Core.Log.Msg($"{where}: inert ({why}), hazards left vanilla.");
            return on;
        }

        public static void ResetParse() => _removed = null;

        /// <summary>This player's own removed set, parsed from the settings once per (re)load.</summary>
        public static HashSet<int> LocalRemoved()
        {
            if (_removed != null) return _removed;
            var set = new HashSet<int>();
            var extra = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in (ModConfig.AlsoRemove.Value ?? "").Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                extra.Add(s.Trim());

            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (GameManager.HazardModifier h in Enum.GetValues(typeof(GameManager.HazardModifier)))
            {
                var name = h.ToString();
                known.Add(name);
                if (h == GameManager.HazardModifier.Default || h == GameManager.HazardModifier.NoHazard) continue;
                if ((ModConfig.RemoveWeaponRestrictions.Value && WeaponHazard.IsMatch(name)) || extra.Contains(name))
                    set.Add((int)h);
            }
            foreach (var e in extra)
                if (!known.Contains(e)) Core.Log.Warning($"AlsoRemove: no hazard called '{e}' (see the list at startup).");
            _removed = set;
            return set;
        }

        /// <summary>A removed set from hazard names (the host's <c>ht.rm</c>); unknown names are skipped.</summary>
        public static HashSet<int> ParseNames(string names)
        {
            var set = new HashSet<int>();
            foreach (var s in (names ?? "").Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                if (Enum.TryParse<GameManager.HazardModifier>(s.Trim(), true, out var h)
                    && h != GameManager.HazardModifier.Default && h != GameManager.HazardModifier.NoHazard)
                    set.Add((int)h);
            return set;
        }

        public static string Describe()
        {
            var names = new List<string>();
            try { foreach (var v in LocalRemoved()) names.Add(((GameManager.HazardModifier)v).ToString()); }
            catch { names.Add("?"); }
            return $"removing [{(names.Count == 0 ? "nothing" : string.Join(", ", names))}], hazard bonus x{ModConfig.BonusMultiplier.Value:0.##}; " +
                   "offline, or in private lobbies where everyone runs this build (the host's settings apply there)";
        }

        public static void LogNames()
        {
            try { Core.Log.Msg("Hazard names: " + string.Join(", ", Enum.GetNames(typeof(GameManager.HazardModifier)))); }
            catch (Exception e) { Core.Log.Warning("Could not list hazard names: " + e.Message); }
        }

        /// <summary>Removes the configured hazards from <paramref name="list"/> in place. Returns what was removed.</summary>
        private static string Filter(HazardList list)
        {
            if (list == null || list.Count == 0) return null;
            var drop = Gate.Effective?.Removed;
            if (drop == null || drop.Count == 0) return null;
            List<string> gone = null;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var h = list[i];
                if (!drop.Contains((int)h)) continue;
                (gone ??= new List<string>()).Add(h.ToString());
                list.RemoveAt(i);
            }
            return gone == null ? null : string.Join(", ", gone);
        }

        private static void GeneratePostfix(int __0, HazardList __result)
        {
            try
            {
                Bonus.Sync(null);
                if (!ActiveLogged($"Dungeon seed {__0}")) return;
                var gone = Filter(__result);
                if (gone != null && ModConfig.VerboseLogging.Value)
                    Core.Log.Msg($"Dungeon seed {__0}: removed {gone}; left {Names(__result)}.");
            }
            catch (Exception e) { Core.Log.Warning($"GenerateDeterministicHazards postfix: {e.GetType().Name}: {e.Message}"); }
        }

        private static void InitPrefix(HazardList __0)
        {
            try
            {
                Bonus.Sync(null);
                if (!ActiveLogged("Run start")) return;
                var gone = Filter(__0);
                if (gone != null && ModConfig.VerboseLogging.Value)
                    Core.Log.Msg($"Run start: removed {gone}; active hazards {Names(__0)}.");
            }
            catch (Exception e) { Core.Log.Warning($"InitHazardsForLevel prefix: {e.GetType().Name}: {e.Message}"); }
        }

        private static string Names(HazardList list)
        {
            if (list == null || list.Count == 0) return "none";
            var n = new List<string>();
            for (int i = 0; i < list.Count; i++) n.Add(list[i].ToString());
            return string.Join(", ", n);
        }
    }
}
