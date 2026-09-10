using System;
using System.Collections.Generic;
using Realm = Il2CppOthergate.Biome.Realm;

namespace Descent.Run
{
    /// <summary>
    /// A run's name. The game's own generator (<c>GameManager.nameGenerator</c>, realm-flavoured
    /// word lists) when it is loaded; the mod's short lists otherwise, so the board never shows
    /// a bare number.
    /// </summary>
    public static class Naming
    {
        private static readonly string[] Adjectives =
        {
            "Forsaken", "Sunken", "Hollow", "Ashen", "Silent", "Buried", "Weeping", "Blackened",
            "Forgotten", "Howling", "Drowned", "Shattered", "Gilded", "Nameless", "Rotting", "Frozen",
        };

        private static readonly string[] Nouns =
        {
            "Depths", "Chambers", "Catacombs", "Delve", "Well", "Vaults", "Warrens", "Pit",
            "Undercroft", "Halls", "Reach", "Abyss", "Descent", "Barrow", "Sepulchre", "Maw",
        };

        /// <summary>Name IDs plus name from the game's generator, or null IDs and a mod name.</summary>
        public static (Il2CppSystem.Collections.Generic.List<int> ids, string name) ForFloor(int seed, int realm)
        {
            try
            {
                var gen = Il2Cpp.GameManager.nameGenerator;
                if (gen != null)
                {
                    string name;
                    var ids = gen.Generate(seed, (Realm)realm, Il2Cpp.GameMode.DungeonRaid, out name);
                    if (ids != null && !string.IsNullOrEmpty(name)) return (ids, name);
                }
            }
            catch (Exception e) { Core.Log.Warning($"Game name generator unavailable ({e.GetType().Name}); using the mod's names."); }
            return (new Il2CppSystem.Collections.Generic.List<int>(), Fallback(seed));
        }

        public static string Fallback(int seed)
        {
            var rng = new Random(seed);
            return $"The {Adjectives[rng.Next(Adjectives.Length)]} {Nouns[rng.Next(Nouns.Length)]}";
        }

        /// <summary>The run's own name: the mod's lists, so it reads as a place rather than as floor 1's dungeon name.</summary>
        public static string ForRun(int seed) => Fallback(seed ^ 0x2F1);
    }
}
