using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Descent.Run
{
    /// <summary>
    /// One descent: a seed, a name, and where the party got to. Everything about a floor is
    /// derived from the seed and the floor index (<see cref="Floor"/>), so a run is fully
    /// described by this record and any copy of it — on any player's machine — can resume it.
    /// </summary>
    public class RunRecord
    {
        public int Version = 1;
        public string Id;
        public string Name;
        public int Seed;
        public int Floors = 16;
        /// <summary>Realm enum values (0–3) in the order the bands come, shuffled by the seed at creation.</summary>
        public List<int> RealmOrder = new List<int>();
        /// <summary>The floor to play next, 0-based. Dying on a floor leaves it here; descending advances it.</summary>
        public int FloorIndex;
        /// <summary>Highest floor index reached, for the board.</summary>
        public int DeepestFloor;
        /// <summary>Highest floor index whose rewards were banked (-1 none).</summary>
        public int BankedThrough = -1;
        /// <summary>Per-floor seed bumps when the layout pass failed on the natural seed. Key = floor index.</summary>
        public Dictionary<int, int> SeedBumps = new Dictionary<int, int>();
        public bool Completed;
        public string CreatedAt;
        public string UpdatedAt;
        /// <summary>Reserved for quest runs (retrieve an artifact, kill a named boss). Unused in 0.1.</summary>
        public string Objective;

        [JsonIgnore] public bool IsFinished => Completed || FloorIndex >= Floors;
        [JsonIgnore] public int LastFloorIndex => Floors - 1;

        public static RunRecord Create(int seed, string name, int floors, int floorsPerBand)
        {
            var run = new RunRecord
            {
                Id = Guid.NewGuid().ToString("N").Substring(0, 12),
                Seed = seed,
                Name = name,
                Floors = Math.Max(1, floors),
                CreatedAt = DateTime.UtcNow.ToString("o"),
                UpdatedAt = DateTime.UtcNow.ToString("o"),
            };
            // A seed-shuffled permutation of the four released realms; bands repeat it if the run is longer than 4 bands.
            var realms = new List<int> { 0, 1, 2, 3 };
            var rng = new Random(seed ^ 0x5EED);
            for (var i = realms.Count - 1; i > 0; i--) { var j = rng.Next(i + 1); (realms[i], realms[j]) = (realms[j], realms[i]); }
            run.RealmOrder = realms;
            return run;
        }

        public void Touch() => UpdatedAt = DateTime.UtcNow.ToString("o");

        /// <summary>Everything the launcher needs for one floor, derived — never stored.</summary>
        public FloorSpec Floor(int index)
        {
            index = Math.Max(0, Math.Min(index, Floors - 1));
            var bump = SeedBumps != null && SeedBumps.TryGetValue(index, out var b) ? b : 0;
            var spec = new FloorSpec
            {
                RunId = Id,
                RunName = Name,
                Index = index,
                Floors = Floors,
                Seed = FloorSeed(Seed, index, bump),
                Realm = RealmFor(index),
                Tier = Ramp.TierFor(index),
                Difficulty = Ramp.DifficultyFor(index),
                Length = Ramp.LengthFor(index),
                HazardLevel = Ramp.HazardLevelFor(index),
                IsLast = index == Floors - 1,
            };
            spec.Boss = spec.IsLast && ModConfig.FinalFloorBoss.Value;
            return spec;
        }

        public int RealmFor(int index)
        {
            if (RealmOrder == null || RealmOrder.Count == 0) return index % 4;
            var perBand = Math.Max(1, ModConfig.FloorsPerBand.Value);
            return RealmOrder[(index / perBand) % RealmOrder.Count];
        }

        /// <summary>A stable, well-mixed int from the run seed and floor. Positive, non-zero, so it survives the game's seed fields.</summary>
        public static int FloorSeed(int runSeed, int floor, int bump)
        {
            unchecked
            {
                uint h = (uint)runSeed * 2654435761u;
                h ^= (uint)(floor + 1) * 2246822519u;
                h ^= h >> 15; h *= 0x85EBCA6Bu; h ^= h >> 13; h *= 0xC2B2AE35u; h ^= h >> 16;
                var s = (int)(h & 0x7FFFFFFF) + bump;
                if (s <= 0) s = 1;
                return s;
            }
        }

        public string Describe() =>
            $"`{Name}` seed {Seed}: floor {FloorIndex + 1}/{Floors} next, deepest {DeepestFloor + 1}, banked through {BankedThrough + 1}, " +
            $"realms {string.Join(">", RealmOrder ?? new List<int>())}{(Completed ? ", COMPLETED" : "")}";
    }

    /// <summary>What one floor is. Sent to peers as JSON so every client sets the same tier and knows what it is playing.</summary>
    public class FloorSpec
    {
        public string RunId;
        public string RunName;
        public int Index;
        public int Floors;
        public int Seed;
        /// <summary>Il2Cpp.Realm value 0–3.</summary>
        public int Realm;
        /// <summary>TierOverride 0–6 (the scanner calls these Tier 1–7).</summary>
        public int Tier;
        /// <summary>Il2Cpp.Difficulty: 100 Easy, 200 Medium, 300 Hard, 400 Nightmare.</summary>
        public int Difficulty;
        /// <summary>Il2Cpp.MissionLength: 100 Short, 200 Medium, 300 Long.</summary>
        public int Length;
        /// <summary>GameManager.HazardLevel 0–3.</summary>
        public int HazardLevel;
        public bool IsLast;
        public bool Boss;

        [JsonIgnore] public int Number => Index + 1;
        [JsonIgnore] public string RealmName => Ramp.RealmName(Realm);

        public string Describe() =>
            $"floor {Number}/{Floors} — {RealmName}, tier {Tier + 1}, {Ramp.DifficultyName(Difficulty)}, {Ramp.LengthName(Length)}, hazard L{HazardLevel}{(Boss ? ", BOSS" : "")}, seed {Seed}";
    }

    /// <summary>The difficulty curve, as a table over the floor index. Tune here, not in the launcher.</summary>
    public static class Ramp
    {
        public static int TierFor(int index)
        {
            var start = Math.Max(1, Math.Min(7, ModConfig.StartTier.Value)) - 1;
            var every = Math.Max(1, ModConfig.TierRampEvery.Value);
            return Math.Min(6, start + index / every);
        }

        public static int DifficultyFor(int index) => index < 4 ? 100 : index < 8 ? 200 : index < 12 ? 300 : 400;

        public static int LengthFor(int index) => index < 4 ? 100 : index < 10 ? 200 : 300;

        public static int HazardLevelFor(int index) => index < 4 ? 0 : index < 9 ? 1 : index < 13 ? 2 : 3;

        public static string RealmName(int realm) => realm switch
        {
            0 => "Underworld", 1 => "Sandstorm", 2 => "Vilehalls", 3 => "Lava Forge",
            4 => "Frostbound", 5 => "Stormgrave", _ => $"realm {realm}"
        };

        public static string DifficultyName(int d) => d switch { 100 => "easy", 200 => "medium", 300 => "hard", 400 => "nightmare", _ => d.ToString() };
        public static string LengthName(int l) => l switch { 100 => "short", 200 => "medium", 300 => "long", _ => l.ToString() };
    }
}
