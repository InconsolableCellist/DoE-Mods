using System;
using MelonLoader;
using PartyHealth;
using PartyHealth.Hud;
using PartyHealth.Peers;

[assembly: MelonInfo(typeof(Core), "PartyHealth", "0.1.1", "Foxipso")]
[assembly: MelonGame("Othergate LLC", "Dungeons of Eternity")]

namespace PartyHealth
{
    /// <summary>
    /// PartyHealth: a small health bar over each friend's head in a networked game.
    ///
    /// Read-only. The game already tells every client what happens to every player's health
    /// (its own RPCs, see <see cref="PeerHealth"/>); this mod listens to those, polls the remote
    /// copy of each avatar as a second source, and draws. No gate, no roster, nothing sent:
    /// it works with friends who run nothing at all, and nothing is written to the profile.
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

            LoggerInstance.Msg($"PartyHealth {Version} — a health bar over each friend's head. Logs in {ModPaths.LogDir}");

            Hooks.Init(HarmonyInstance);
            PeerHealth.Install();
            Hooks.Report();

            LoggerInstance.Msg("Hotkeys (window focused): H = hide/show bars, J = demo bar in front of you, K = reload settings.");
        }

        public override void OnUpdate()
        {
            PeerHealth.Tick();

            if (!ModConfig.HotkeysEnabled.Value) return;
            try
            {
                if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.H))
                {
                    HealthBars.Hidden = !HealthBars.Hidden;
                    LoggerInstance.Msg(HealthBars.Hidden ? "Bars hidden (H again to show)." : "Bars shown.");
                }
                else if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.J))
                {
                    if (HealthBars.TryEyes(out var pos, out var fwd))
                    {
                        fwd.y = 0f; if (fwd.sqrMagnitude < 0.001f) fwd = UnityEngine.Vector3.forward;
                        PeerHealth.StartDemo(pos + fwd.normalized * 1.5f - UnityEngine.Vector3.up * 0.28f);
                        LoggerInstance.Msg("Demo bar 1.5 m ahead: drains, goes DOWN, heals, fades.");
                    }
                    else LoggerInstance.Msg("No camera yet; the demo needs you in a scene.");
                }
                else if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.K))
                {
                    MelonPreferences.Load();
                    LoggerInstance.Msg("Settings reloaded from MelonPreferences.cfg.");
                }
            }
            catch (Exception e) { LoggerInstance.Warning($"Hotkey threw: {e.GetType().Name}: {e.Message}"); }
        }

        public override void OnLateUpdate()
        {
            HealthBars.LateTick();
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            PeerHealth.Clear($"scene changed to {sceneName}");
            HealthBars.Reset();
        }

        public override void OnApplicationQuit()
        {
            HealthLog.Headline($"Quit. {PeerHealth.Stats()}; bars: {HealthBars.Describe()}.");
            HealthLog.Close();
        }
    }
}
