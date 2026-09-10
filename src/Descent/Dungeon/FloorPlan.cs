using System;
using System.Collections;
using Descent.Recon;
using Descent.Run;
using Il2Cpp;
using Realm = Il2CppOthergate.Biome.Realm;
using UnityEngine;
using Interop = Descent.Recon.Interop;

namespace Descent.Dungeon
{
    /// <summary>
    /// Turns a <see cref="FloorSpec"/> into the game's own mission object,
    /// <c>DungeonScanner.Dungeon</c>, the thing every launch path takes. The constructor rolls
    /// the hazards deterministically from seed and hazard level
    /// (<c>GameManager.GenerateDeterministicHazards</c>), the name comes from the game's
    /// generator, and the layout pass — the same one the lobby scanner runs to draw its
    /// hologram — fills rooms, XP and gold bonus and <c>isValid</c>.
    /// </summary>
    public static class FloorPlan
    {
        public const string DungeonScene = "dungeon_builder_v1";

        public static DungeonScanner.Dungeon Build(FloorSpec spec)
        {
            var (ids, name) = Naming.ForFloor(spec.Seed, spec.Realm);
            // isRandom = true: "random" here means "generated from a seed" as opposed to the prebuilt
            // tutorial and sandbox scenes; InitializeLevel only calls the builder for a random
            // mission (2026-09-07 session: false gave a black screen and no generation).
            var dungeon = new DungeonScanner.Dungeon(
                (DungeonScanner.ScannerRealm)spec.Realm, true, spec.Seed, ids, name, DungeonScene,
                GameMode.DungeonRaid, (Difficulty)spec.Difficulty, (GameManager.HazardLevel)spec.HazardLevel);
            try { dungeon.customRealm = (Realm)spec.Realm; } catch { }
            if (spec.Boss)
            {
                try
                {
                    dungeon.hazards ??= new Il2CppSystem.Collections.Generic.List<GameManager.HazardModifier>();
                    var has = false;
                    for (var i = 0; i < dungeon.hazards.Count; i++) if (dungeon.hazards[i] == GameManager.HazardModifier.BossBattle) has = true;
                    if (!has) dungeon.hazards.Add(GameManager.HazardModifier.BossBattle);
                }
                catch (Exception e) { Core.Log.Warning($"Could not add the boss hazard: {e.GetType().Name}: {e.Message}"); }
            }
            // The scanner marks a mission valid after its own layout pass; the builder never touches
            // the flag (2026-09-07 session: rooms, XP and gold filled in, isValid still false).
            try { dungeon.isValid = true; } catch { }
            ReconLog.Line($"floor plan: {spec.Describe()} -> `{name}` hazards [{DescribeHazards(dungeon)}]");
            return dungeon;
        }

        public static string DescribeHazards(DungeonScanner.Dungeon d)
        {
            try
            {
                if (d.hazards == null || d.hazards.Count == 0) return "none";
                var parts = new string[d.hazards.Count];
                for (var i = 0; i < d.hazards.Count; i++) parts[i] = d.hazards[i].ToString();
                return string.Join(", ", parts);
            }
            catch { return "?"; }
        }

        public static string Describe(DungeonScanner.Dungeon d)
        {
            if (d == null) return "<null>";
            try
            {
                var rooms = -1; try { rooms = d.rooms?.Count ?? -1; } catch { }
                int xp = -1, gold = -1; try { xp = d.XPBonus.Value; gold = d.GoldBonus.Value; } catch { }
                return $"`{d.name}` seed {d.seed} random {d.isRandom} realm {d.GetDungeonRealm()} mode {d.gameMode} diff {d.difficulty} hazardLevel {d.hazardLevel} hazards [{DescribeHazards(d)}] rooms {rooms} xp {xp} gold {gold} valid {d.isValid} scene {d.sceneName}";
            }
            catch (Exception e) { return $"<describe failed: {e.GetType().Name}>"; }
        }

        /// <summary>The builder in whichever scene we are in, or null.</summary>
        public static DungeonBuilder Builder()
        {
            try { var b = GameManager.Get_DungeonBuilder; if (Interop.Alive(b)) return b; } catch { }
            try { var b = DungeonBuilder.Instance; if (Interop.Alive(b)) return b; } catch { }
            return null;
        }

        /// <summary>
        /// The game's layout-only pass on <paramref name="dungeon"/>. Lobby only: in the dungeon
        /// scene the same builder owns the live rooms, so a second pass there is not worth the
        /// risk. Yields until the dungeon is valid, the builder reports failure, or the timeout.
        /// </summary>
        public static IEnumerator Validate(DungeonScanner.Dungeon dungeon, FloorSpec spec, Action<bool> done, float timeoutSeconds = 10f)
        {
            var builder = Builder();
            if (builder == null) { Core.Log.Warning("Validate: no DungeonBuilder in this scene."); done?.Invoke(true); yield break; }
            var started = Time.unscaledTime;
            var ok = false;
            var launched = false;
            try
            {
                builder.enableLogging = ModConfig.BuilderLogging.Value;
                InitBuilderHook.Pending = spec;
                var routine = builder.GenerateLayout(dungeon, ModConfig.BuilderLogging.Value);
                builder.StartCoroutine(routine);
                launched = true;
            }
            catch (Exception e) { Core.Log.Warning($"Validate: GenerateLayout threw {e.GetType().Name}: {e.Message}"); }
            if (!launched) { InitBuilderHook.Pending = null; done?.Invoke(true); yield break; }

            while (Time.unscaledTime - started < timeoutSeconds)
            {
                yield return null;
                int rooms = 0, xp = 0;
                try { rooms = dungeon.rooms?.Count ?? 0; xp = dungeon.XPBonus.Value; } catch { }
                // Success is rooms on the mission object (the builder writes them, plus XP and gold, at the
                // end of its pass); a failed pass leaves the list empty and the routine ends.
                if (rooms > 0 && xp > 0) { ok = true; break; }
                if (Time.unscaledTime - started > 3f && rooms == 0 && BuilderIdle(builder)) break;
            }
            InitBuilderHook.Pending = null;
            ReconLog.Line($"validate: {spec.Describe()} -> {(ok ? "OK" : "FAILED")} in {Time.unscaledTime - started:0.0}s: {Describe(dungeon)}");
            done?.Invoke(ok);
        }

        private static bool BuilderIdle(DungeonBuilder b)
        {
            try { return b.buildRoutine == null; } catch { return false; }
        }
    }
}
