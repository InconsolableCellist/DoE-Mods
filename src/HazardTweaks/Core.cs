using System;
using MelonLoader;
using HazardTweaks;

[assembly: MelonInfo(typeof(Core), "HazardTweaks", "0.1.1", "Andorius13")]
[assembly: MelonGame("Othergate LLC", "Dungeons of Eternity")]

namespace HazardTweaks
{
    /// <summary>
    /// HazardTweaks: removes chosen dungeon hazards (by default every one that takes your own
    /// weapons away) and multiplies the XP/gold bonus every remaining hazard gives.
    ///
    /// Two hooks and one table. A dungeon's hazards come from
    /// <c>GameManager.GenerateDeterministicHazards</c> (seeded, so every player rolls the same
    /// list) and are switched on by <c>GameManager.InitHazardsForLevel</c>; both get the list
    /// filtered. The bonus percentages live in the <c>HazardData</c> asset's per-tier table,
    /// which is scaled in memory, so the mission board, the per-hazard "+X% Bonus" lines and
    /// the end-of-run total all read the same scaled numbers.
    ///
    /// Everything is behind <see cref="Gate"/>: offline, or a private lobby where every player
    /// advertises the same version and DLL hash; there the host's settings apply to everyone. Anything else is vanilla.
    /// </summary>
    public class Core : MelonMod
    {
        public const string Version = "0.1.1";

        public static Core Instance { get; private set; }
        public static MelonLogger.Instance Log => Instance.LoggerInstance;

        public override void OnInitializeMelon()
        {
            Instance = this;
            ModConfig.Load();
            try { MelonPreferences.Save(); }
            catch (Exception e) { LoggerInstance.Warning($"Could not write MelonPreferences.cfg: {e.Message}"); }

            LoggerInstance.Msg($"HazardTweaks {Version}: {Hazards.Describe()}");
            if (!ModConfig.Enabled.Value) { LoggerInstance.Msg("Enabled=false: doing nothing."); return; }

            Hazards.LogNames();
            Gate.ComputeDllHash();
            Hooks.Init(HarmonyInstance);
            Hazards.Install();
            Hooks.Report();
            LoggerInstance.Msg("F9 (game window focused) reloads the [HazardTweaks] settings; they apply to the next mission board.");
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            if (!ModConfig.Enabled.Value) return;
            Bonus.Sync("scene " + sceneName);
        }

        public override void OnUpdate()
        {
            if (!ModConfig.Enabled.Value) return;
            try { Gate.Tick(UnityEngine.Time.unscaledDeltaTime); }
            catch (Exception e) { LoggerInstance.Warning($"Lobby check: {e.GetType().Name}: {e.Message}"); }
            if (!Hotkeys.Pressed(UnityEngine.InputSystem.Key.F9)) return;
            try
            {
                ModConfig.Main.LoadFromFile(false);
                Hazards.ResetParse();
                LoggerInstance.Msg($"Settings reloaded: {Hazards.Describe()}");
                Bonus.Sync("reload");
            }
            catch (Exception e) { LoggerInstance.Warning($"Reload failed: {e.Message}"); }
        }
    }
}
