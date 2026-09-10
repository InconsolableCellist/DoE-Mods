using System;
using Descent.Recon;
using Descent.Run;
using Il2Cpp;
using Realm = Il2CppOthergate.Biome.Realm;

namespace Descent.Dungeon
{
    /// <summary>
    /// A mission object carries realm, mode, difficulty and hazards but no length; the builder
    /// takes that from its own <c>buildForLength</c>. <c>DungeonBuilder.InitBuilder</c> is where
    /// the two meet (its callers into <c>GetMainPath</c>/<c>GetGenSettings</c> are inlined, so
    /// this is the one seam), and this prefix is where a floor's length is applied — and where
    /// a later build can lengthen or widen floors by editing the layout asset. Only the client
    /// that generates (the host) ever runs it for a live floor; the lobby validation pass runs
    /// it on whoever validates.
    /// </summary>
    public static class InitBuilderHook
    {
        /// <summary>The floor the next InitBuilder call belongs to, set around a validation pass.</summary>
        public static FloorSpec Pending;

        public static int Applied { get; private set; }

        public static void Install()
        {
            Hooks.Patch(typeof(DungeonBuilder), "InitBuilder",
                Hooks.Of(typeof(InitBuilderHook), nameof(InitBuilder_Prefix)), null, paramCount: 7);
        }

        private static FloorSpec Current() => Pending ?? RunSync.Floor;

        private static void InitBuilder_Prefix(DungeonBuilder __instance, GameMode _gameMode, Realm _realm, Difficulty _difficulty,
                                               ref MissionLength _missionLength, int _randomSeed)
        {
            try
            {
                if (!ModConfig.Enabled.Value) return;
                var spec = Current();
                if (spec == null) return;
                if (_gameMode != GameMode.DungeonRaid || _randomSeed != spec.Seed)
                {
                    ReconLog.Line($"InitBuilder for another dungeon (mode {_gameMode}, seed {_randomSeed}); floor spec seed {spec.Seed} not applied.");
                    return;
                }
                var was = _missionLength;
                _missionLength = (MissionLength)spec.Length;
                Applied++;
                ReconLog.Line($"InitBuilder: floor {spec.Number} realm {_realm} diff {_difficulty} seed {_randomSeed}: length {was} -> {_missionLength}");
            }
            catch (Exception e) { Core.Log.Warning($"InitBuilder prefix threw: {e.GetType().Name}: {e.Message}"); }
        }
    }
}
