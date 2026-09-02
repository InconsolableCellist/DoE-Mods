using System;
using System.Collections.Generic;
using System.Reflection;
using Il2Cpp;

namespace LootOverhaul.Recon
{
    /// <summary>
    /// The PlayFab write watchdog. Logging prefixes on every <c>PlayerProfile</c> method that
    /// mutates the profile. Two jobs: (1) show what vanilla writes and when, so the mod's
    /// "read-only" boundary is drawn around real behaviour rather than guesses; (2) prove
    /// whether any of our probes — the generator survey above all — cause a write. A call
    /// that lands while <see cref="Probe"/> is set is flagged loudly.
    ///
    /// Nothing here alters the call: the prefix returns void and touches no argument.
    /// </summary>
    public static class ProfileWatch
    {
        private static readonly string[] Writers =
        {
            "SetData", "IncrementData", "SetCharacterData", "IncrementCharacterData",
            "SetLoadoutData", "SetPerkLoadoutData", "SetCurrentLoadout", "SetCurrentPerkLoadout",
            "AddUncraftedWeapon", "RemoveUncraftedWeapon", "AddUnlockedWeapon", "RemoveUnlockedWeapon",
            "UnlockWeapon", "UnlockCosmetic", "UnlockCosmetics", "SetCosmeticLoadout", "SetPerk",
            "IncreasePerkLevel", "SetLevel", "IncrementPromotions", "UnlockAchievement", "UpdateAchievement",
            "IncrementAchievementData", "SavePlayerProfile",
        };

        private static readonly Dictionary<string, int> Counts = new Dictionary<string, int>();
        private static readonly List<string> DuringProbe = new List<string>();

        /// <summary>Set while a mod probe runs; any write seen meanwhile is attributed to it.</summary>
        public static string Probe;

        public static void Install()
        {
            var prefix = Hooks.Of(typeof(ProfileWatch), nameof(Prefix));
            foreach (var name in Writers) Hooks.Patch(typeof(PlayerProfile), name, prefix, null);
        }

        private static void Prefix(MethodBase __originalMethod, object[] __args)
        {
            try
            {
                var name = __originalMethod.Name;
                Counts.TryGetValue(name, out var n);
                Counts[name] = ++n;

                var args = DescribeArgs(__args);
                if (Probe != null)
                {
                    var line = $"!!! PlayerProfile.{name}({args}) DURING PROBE `{Probe}`";
                    DuringProbe.Add(line);
                    ReconLog.Headline(line);
                    return;
                }

                // Vanilla traffic: first 30 of each, then every 50th, so playtime ticks
                // can't drown the interesting ones.
                if (n <= 30 || n % 50 == 0)
                    ReconLog.Line($"profile write: PlayerProfile.{name}({args}) #{n}");
            }
            catch { }
        }

        private static string DescribeArgs(object[] args)
        {
            if (args == null || args.Length == 0) return "";
            var parts = new string[args.Length];
            for (var i = 0; i < args.Length; i++)
            {
                var a = args[i];
                string s;
                try
                {
                    if (a == null) s = "null";
                    else if (a is WeaponModule wm) s = $"WeaponModule<{Interop.OneLine(wm.GetDisplayName(false))}>";
                    else s = a.ToString();
                }
                catch { s = "?"; }
                if (s.Length > 80) s = s.Substring(0, 77) + "...";
                parts[i] = s;
            }
            return string.Join(", ", parts);
        }

        public static void Report()
        {
            ReconLog.Section("PlayFab write watchdog — summary");
            if (Counts.Count == 0) ReconLog.Line("- no PlayerProfile writer was called this session");
            foreach (var kv in Counts) ReconLog.Line($"- PlayerProfile.{kv.Key}: {kv.Value} call(s)");
            ReconLog.Line();
            if (DuringProbe.Count == 0)
                ReconLog.Headline("VERDICT: no profile write occurred during any mod probe.");
            else
            {
                ReconLog.Headline($"VERDICT: {DuringProbe.Count} profile write(s) occurred DURING mod probes — see below.");
                foreach (var l in DuringProbe) ReconLog.Line($"- {l}");
            }
        }
    }
}
