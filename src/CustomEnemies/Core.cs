using System;
using MelonLoader;
using CustomEnemies;
using CustomEnemies.Skins;
using UnityEngine.InputSystem;

[assembly: MelonInfo(typeof(Core), "CustomEnemies", "0.1.0", "Foxipso")]
[assembly: MelonGame("Othergate LLC", "Dungeons of Eternity")]

namespace CustomEnemies
{
    /// <summary>
    /// CustomEnemies: draws a different model over an enemy, on this client only
    /// (docs/CUSTOM-ENEMIES-PLAN.md). The enemy itself is untouched: its transforms, colliders,
    /// stats, AI and network traffic stay vanilla, and only its renderers are switched to
    /// ShadowsOnly, which keeps it animating. No gate and no Photon traffic, so it works in
    /// public lobbies and next to unmodded players.
    ///
    /// Phase 1a: the mechanism. A placeholder skin built from primitives at runtime (no bundle,
    /// no model on disk) rides the enemies named in <c>PlaceholderOn</c> (Death by default),
    /// following the vanilla root, with a head that follows the vanilla head bone.
    /// </summary>
    public class Core : MelonMod
    {
        public const string Version = "0.1.0";

        public static Core Instance { get; private set; }
        public static MelonLogger.Instance Log => Instance.LoggerInstance;

        public override void OnInitializeMelon()
        {
            Instance = this;
            ModConfig.Load();
            try { MelonPreferences.Save(); }
            catch (Exception e) { LoggerInstance.Warning($"Could not write MelonPreferences.cfg: {e.Message}"); }

            LoggerInstance.Msg($"CustomEnemies {Version}: placeholder skin on {Describe()}.");
            if (!ModConfig.Enabled.Value) { LoggerInstance.Msg("Enabled=false: doing nothing."); return; }

            Hooks.Init(HarmonyInstance);
            SkinManager.Install();
            Hooks.Report();
            LoggerInstance.Msg("Keys (game window focused): Numpad1 = placeholder on/off for the nearest enemy, Numpad2 = all skins off/on, Numpad3 = reload [CustomEnemies] settings.");
        }

        private static string Describe()
        {
            var s = ModConfig.PlaceholderOn.Value;
            return string.IsNullOrWhiteSpace(s) ? "no enemy (PlaceholderOn is empty; Numpad1 still works)" : s.Trim();
        }

        public override void OnUpdate()
        {
            if (!ModConfig.Enabled.Value) return;
            try { SkinManager.Tick(UnityEngine.Time.unscaledDeltaTime); }
            catch (Exception e) { SkinManager.LogOnce("tick", e); }
            PollHotkeys();
        }

        public override void OnLateUpdate()
        {
            if (!ModConfig.Enabled.Value) return;
            try { SkinManager.LateTick(); }
            catch (Exception e) { SkinManager.LogOnce("late tick", e); }
        }

        public override void OnSceneWasUnloaded(int buildIndex, string sceneName)
        {
            if (!ModConfig.Enabled.Value) return;
            try { SkinManager.Purge("scene " + sceneName + " unloaded"); }
            catch (Exception e) { SkinManager.LogOnce("purge", e); }
        }

        private void PollHotkeys()
        {
            try
            {
                if (Hotkeys.Pressed(Key.Numpad1)) SkinManager.ToggleNearest();
                else if (Hotkeys.Pressed(Key.Numpad2)) SkinManager.ToggleAll();
                else if (Hotkeys.Pressed(Key.Numpad3))
                {
                    ModConfig.Main.LoadFromFile(false);
                    SkinManager.Reloaded();
                    LoggerInstance.Msg($"Settings reloaded: placeholder skin on {Describe()}.");
                }
            }
            catch (Exception e) { LoggerInstance.Warning($"Hotkey failed: {e.GetType().Name}: {e.Message}"); }
        }
    }
}
