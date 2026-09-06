using System;
using MelonLoader;
using VisualCues;
using VisualCues.Controls;
using VisualCues.Cues;
using VisualCues.Hud;
using VisualCues.Net;

[assembly: MelonInfo(typeof(Core), "VisualCues", "0.1.3", "dan")]
[assembly: MelonGame("Othergate LLC", "Dungeons of Eternity")]

namespace VisualCues
{
    /// <summary>
    /// VisualCues: visual stand-ins for two things a deaf player cannot hear.
    ///
    ///  - The call: click a stick in and every player running the mod gets an arrow to you
    ///    with your name and distance (one Photon custom event, code 160).
    ///  - The noise cue: an arrow toward every nearby enemy that makes a sound outside your
    ///    view, from the enemy's own animation events and the game's positional audio.
    ///
    /// No gate, no roster, no handshake. The mod works alone in any room; the call needs the
    /// mod on both ends, the noise cue is entirely local. Nothing is written to the profile.
    /// </summary>
    public class Core : MelonMod
    {
        public const string Version = "0.1.3";

        public static Core Instance { get; private set; }
        public static MelonLogger.Instance Log => Instance.LoggerInstance;

        public override void OnInitializeMelon()
        {
            Instance = this;
            ModConfig.Load();
            try { MelonPreferences.Save(); }
            catch (Exception e) { LoggerInstance.Warning($"Could not write MelonPreferences.cfg: {e.Message}"); }

            LoggerInstance.Msg($"VisualCues {Version} — stick-click call (event {CueNet.CodeSummon}), unseen-enemy noise markers. Logs in {ModPaths.LogDir}");

            PhotonHook.Install(HarmonyInstance);
            CueNet.Init();
            SummonCue.Init();

            Hooks.Init(HarmonyInstance);
            NoiseCue.Install();
            Hooks.Report();

            LoggerInstance.Msg("Hotkeys (window focused): ' (quote) = send a call, . (period) = mark the nearest enemy, , (comma) = reload settings.");
        }

        public override void OnUpdate()
        {
            CueNet.Pump();
            if (ModConfig.Enabled.Value && ModConfig.SummonEnabled.Value) StickPress.Tick();
            SummonCue.Tick();
            NoiseCue.Tick();

            if (!ModConfig.HotkeysEnabled.Value) return;
            try
            {
                if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.Quote)) SummonCue.Fire("key");
                else if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.Period)) NoiseCue.Test();
                else if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.Comma))
                {
                    MelonPreferences.Load();
                    LoggerInstance.Msg("Settings reloaded from MelonPreferences.cfg.");
                    CueHud.Flash("Settings reloaded", UnityEngine.Color.white, 1.2f);
                }
            }
            catch (Exception e) { LoggerInstance.Warning($"Hotkey threw: {e.GetType().Name}: {e.Message}"); }
        }

        public override void OnLateUpdate()
        {
            CueHud.LateTick();
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            SummonCue.Clear($"scene changed to {sceneName}");
            NoiseCue.Clear($"scene changed to {sceneName}");
            CueHud.Reset();
        }

        public override void OnApplicationQuit()
        {
            CueLog.Headline($"Quit. {SummonCue.Describe()}; {NoiseCue.Describe()}; net {CueNet.Stats()}; HUD {CueHud.Describe()}");
            CueLog.Close();
        }
    }
}
