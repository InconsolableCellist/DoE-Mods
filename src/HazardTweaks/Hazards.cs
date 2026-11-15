using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Il2Cpp;
using Il2CppPhoton.Pun;
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
        private static bool _loggedGate;

        public static void Install()
        {
            Hooks.Patch(Hooks.Of(typeof(GameManager), nameof(GameManager.GenerateDeterministicHazards)),
                null, Hooks.Of(typeof(Hazards), nameof(GeneratePostfix)), "GameManager.GenerateDeterministicHazards");
            Hooks.Patch(Hooks.Of(typeof(GameManager), nameof(GameManager.InitHazardsForLevel)),
                Hooks.Of(typeof(Hazards), nameof(InitPrefix)), null, "GameManager.InitHazardsForLevel");
        }

        /// <summary>Is the mod allowed to change anything right now.</summary>
        public static bool Active(out string why)
        {
            why = null;
            if (!ModConfig.Enabled.Value) { why = "disabled"; return false; }
            if (!ModConfig.PrivateLobbyOnly.Value) return true;
            try
            {
                if (!PhotonNetwork.InRoom) return true; // solo / offline
                var room = PhotonNetwork.CurrentRoom;
                if (ReferenceEquals(room, null)) return true;
                if (room.IsVisible) { why = "public lobby"; return false; }
                return true;
            }
            catch (Exception e) { why = "room check threw " + e.GetType().Name; return false; }
        }

        public static bool ActiveLogged()
        {
            var on = Active(out var why);
            if (!on && !_loggedGate) { _loggedGate = true; Core.Log.Msg($"Inert ({why}): hazards and bonuses stay vanilla."); }
            if (on) _loggedGate = false;
            return on;
        }

        public static void ResetParse() => _removed = null;

        private static HashSet<int> Removed()
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

        public static string Describe()
        {
            var names = new List<string>();
            try { foreach (var v in Removed()) names.Add(((GameManager.HazardModifier)v).ToString()); }
            catch { names.Add("?"); }
            return $"removing [{(names.Count == 0 ? "nothing" : string.Join(", ", names))}], hazard bonus x{ModConfig.BonusMultiplier.Value:0.##}, " +
                   (ModConfig.PrivateLobbyOnly.Value ? "private lobbies and solo only" : "any lobby");
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
            var drop = Removed();
            if (drop.Count == 0) return null;
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
                if (!ActiveLogged()) { Bonus.Sync(null); return; }
                Bonus.Sync(null);
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
                if (!ActiveLogged()) return;
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
