using System;
using MelonLoader;
using LootOverhaul;
using LootOverhaul.Gate;
using LootOverhaul.Net;
using LootOverhaul.Recon;

[assembly: MelonInfo(typeof(Core), "LootOverhaul", "0.1.0", "dan")]
[assembly: MelonGame("Othergate LLC", "Dungeons of Eternity")]

namespace LootOverhaul
{
    /// <summary>
    /// LootOverhaul: Diablo-style weapon drops, a weight-limited loot bag, and a lobby booth
    /// to sell and equip from it. See docs/LOOT-OVERHAUL.md for the investigation and the
    /// design decisions this build follows.
    ///
    /// v0.1 is the **recon build**: gate, roster and transport under this mod's own identity
    /// (`lo.*` player properties, event block 150–159), read-only logging hooks on the
    /// gameplay methods the design will build on, a PlayFab write watchdog, and four hotkey
    /// probes that answer the "verify first" list. No gameplay changes.
    /// </summary>
    public class Core : MelonMod
    {
        public const string Version = "0.1.0";

        public static Core Instance { get; private set; }
        public static MelonLogger.Instance Log => Instance.LoggerInstance;

        private ModRoster _roster;
        private ModHandshake _handshake;

        public override void OnInitializeMelon()
        {
            Instance = this;
            ModConfig.Load();
            try { MelonPreferences.Save(); }
            catch (Exception e) { LoggerInstance.Warning($"Could not write MelonPreferences.cfg: {e.Message}"); }

            LoggerInstance.Msg($"LootOverhaul {Version} — recon build. No gameplay changes; read-only hooks and hotkey probes only.");
            LoggerInstance.Msg($"Data folder: {ModPaths.Root}; recon transcripts in {ModPaths.ReconDir}");

            SelfCheck.LogSelfHash(LoggerInstance);

            // One read-only prefix on Photon's inbound dispatch. CustomAvatars installs the same
            // kind of prefix from its own Harmony instance; Harmony chains them.
            PhotonHook.Install(HarmonyInstance);
            ModNet.Init();

            _roster = new ModRoster();
            _handshake = new ModHandshake(_roster);
            ModGate.ActiveChanged += OnGateChanged;

            if (ModConfig.ReconEnabled.Value)
            {
                Hooks.Init(HarmonyInstance);
                EventTally.Install();
                if (ModConfig.ProfileWatchEnabled.Value) ProfileWatch.Install();
                GameplayHooks.Install();
                Hooks.Report();
                LoggerInstance.Msg("Recon hotkeys: Insert = generator survey, Delete = spawn a test weapon (private room), Backslash (\\) = lobby survey + marker cubes, Scroll Lock = cloned button + pointer test.");
            }
        }

        public override void OnUpdate()
        {
            var dt = UnityEngine.Time.unscaledDeltaTime;
            _roster.Tick(dt);
            ModGate.Evaluate(_roster);
            ModNet.Pump();
            CoinRepair.Tick();

            if (!ModConfig.ReconEnabled.Value || !ModConfig.HotkeysEnabled.Value) return;
            try
            {
                if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.Insert)) GeneratorProbe.Survey();
                else if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.Delete)) GeneratorProbe.SpawnTest();
                else if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.Backslash)) LobbyProbe.Survey();
                else if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.ScrollLock)) LobbyProbe.ButtonTest();
            }
            catch (Exception e) { LoggerInstance.Warning($"Hotkey probe threw: {e.GetType().Name}: {e.Message}"); }
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            CoinRepair.OnScene(sceneName);
            if (!ModConfig.ReconEnabled.Value) return;
            ReconLog.Section($"Scene initialized: {sceneName} (#{buildIndex})");
            ReconLog.TryKeyValue("GameManager.IsLobbyScene", () => Il2Cpp.GameManager.IsLobbyScene);
            ReconLog.TryKeyValue("gate", () => $"active={ModGate.Active} ({ModGate.Reason})");
            LobbyProbe.OnSceneChanged(sceneName);
            GeneratorProbe.CheckSpawned(sceneName);
            EventTally.Report($"entering {sceneName}");
        }

        public override void OnApplicationQuit()
        {
            ModGate.ForceInert("application quitting");
            if (ModConfig.ReconEnabled.Value)
            {
                EventTally.Report("quit");
                ProfileWatch.Report();
                ReconLog.Close();
            }
        }

        private void OnGateChanged(bool active)
        {
            // Features arm and disarm here once they exist: drop rolls on the master, the
            // loot-tag table, the booth in the lobby. Nothing to do in the recon build.
        }
    }
}
