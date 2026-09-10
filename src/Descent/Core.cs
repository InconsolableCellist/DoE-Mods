using System;
using MelonLoader;
using Descent;
using Descent.Dungeon;
using Descent.Gate;
using Descent.Hub;
using Descent.Net;
using Descent.Recon;

[assembly: MelonInfo(typeof(Core), "Descent", "0.1.0", "dan")]
[assembly: MelonGame("Othergate LLC", "Dungeons of Eternity")]

namespace Descent
{
    /// <summary>
    /// Descent: a sixteen-floor dungeon built out of the game's own generator. A run is a
    /// seed; each floor is a vanilla-generated dungeon launched through the game's own
    /// mission path, harder than the last; the exit teleporter goes down instead of home;
    /// XP and gold are banked every floor through the game's own end-of-mission code; a wipe
    /// or a quit keeps the floor, and the hub board resumes it. See docs/DUNGEON-DESCENT.md.
    ///
    /// 0.1.0 is the first build, untested in a headset: everything is behind the private-room
    /// gate, every step writes to the recon transcript, and the dev hotkeys let one session
    /// walk several floors without clearing them.
    /// </summary>
    public class Core : MelonMod
    {
        public const string Version = "0.1.0";

        public static Core Instance { get; private set; }
        public static MelonLogger.Instance Log => Instance.LoggerInstance;

        private ModRoster _roster;
        private ModHandshake _handshake;
        private float _boardAt = -1f;
        private string _scene = "";

        public override void OnInitializeMelon()
        {
            Instance = this;
            ModConfig.Load();
            try { MelonPreferences.Save(); }
            catch (Exception e) { LoggerInstance.Warning($"Could not write MelonPreferences.cfg: {e.Message}"); }

            LoggerInstance.Msg($"Descent {Version} — {ModConfig.FloorsPerRun.Value} floors, tier {ModConfig.StartTier.Value} rising every {ModConfig.TierRampEvery.Value}, rewards {(ModConfig.BankRewardsPerFloor.Value ? "banked per floor" : "on surfacing")}, launch mode {ModConfig.LaunchMode.Value}{(ModConfig.ObserveOnly.Value ? ", OBSERVE ONLY" : "")}.");
            LoggerInstance.Msg($"Data folder: {ModPaths.Root}; runs in {ModPaths.RunsFile}; recon transcripts in {ModPaths.ReconDir}");
            if (!ModConfig.Enabled.Value) { LoggerInstance.Msg("Enabled=false: doing nothing."); return; }

            SelfCheck.LogSelfHash(LoggerInstance);

            PhotonHook.Install(HarmonyInstance);
            ModNet.Init();
            _roster = new ModRoster();
            _handshake = new ModHandshake(_roster);
            _roster.PeerJoined += RunSync.OnPeerJoined;
            ModGate.ActiveChanged += OnGateChanged;

            Hooks.Init(HarmonyInstance);
            if (ModConfig.UnityLogTap.Value && ModConfig.ReconEnabled.Value) UnityLogTap.Install();
            if (ModConfig.NativeThrowTrace.Value && ModConfig.ReconEnabled.Value) NativeThrowTrace.Install();
            RunSync.Init();
            Launcher.Init();
            Descender.Install();
            InitBuilderHook.Install();
            Hooks.Report();

            LoggerInstance.Msg("Hotkeys (game window focused): Backspace = lobby: new descent now / dungeon: force the exit (descend); End = lobby: resume the latest run / dungeon: surface (forfeit, run kept); Slash (/) = place the board where you stand.");
        }

        public override void OnUpdate()
        {
            if (!ModConfig.Enabled.Value) return;
            var dt = UnityEngine.Time.unscaledDeltaTime;
            _roster.Tick(dt);
            ModGate.Evaluate(_roster);
            ModNet.Pump();
            Launcher.Tick();
            Board.Tick();
            if (_scene == FloorPlan.DungeonScene) FloorWatch.Tick();
            if (_boardAt > 0f && UnityEngine.Time.unscaledTime >= _boardAt)
            {
                _boardAt = -1f;
                try { UiKit.CaptureTemplates(); Board.ShowIfLobby(Il2Cpp.GameManager.LOBBY_SCENE); }
                catch (Exception e) { LoggerInstance.Warning($"Board build threw: {e.GetType().Name}: {e.Message}"); }
            }

            if (!ModConfig.HotkeysEnabled.Value) return;
            try
            {
                if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.Backspace))
                {
                    if (Il2Cpp.GameManager.IsLobbyScene) Launcher.StartNew();
                    else if (_scene == FloorPlan.DungeonScene) Descender.ForceDescend();
                }
                else if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.End))
                {
                    if (Il2Cpp.GameManager.IsLobbyScene)
                    {
                        var runs = Run.RunStore.Resumable();
                        if (runs.Count == 0) Rewards.Toast("No run to resume.");
                        else Launcher.Resume(runs[0]);
                    }
                    else if (_scene == FloorPlan.DungeonScene) Descender.ForceSurface();
                }
                else if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.Slash)) Board.PlaceHere();
            }
            catch (Exception e) { LoggerInstance.Warning($"Hotkey threw: {e.GetType().Name}: {e.Message}"); }
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            if (!ModConfig.Enabled.Value) return;
            _scene = sceneName;
            Descender.Reset();
            Board.Hide();
            if (ModConfig.ReconEnabled.Value)
            {
                ReconLog.Section($"Scene initialized: {sceneName} (#{buildIndex})");
                ReconLog.TryKeyValue("gate", () => $"active={ModGate.Active} ({ModGate.Reason})");
                ReconLog.TryKeyValue("host", () => Il2CppPhoton.Pun.PhotonNetwork.IsMasterClient);
                ReconLog.TryKeyValue("run", () => RunSync.Active ? RunSync.Floor.Describe() : "(none)");
            }
            if (sceneName == FloorPlan.DungeonScene)
            {
                RunSync.OnDungeonScene();
                FloorWatch.OnDungeonScene();
            }
            else
            {
                FloorWatch.OnOtherScene();
                if (sceneName == Il2Cpp.GameManager.LOBBY_SCENE || sceneName == Il2Cpp.GameManager.MEGALOBBY_SCENE || sceneName == Il2Cpp.GameManager.MAINMENU_SCENE)
                    RunSync.OnLobbyScene();
            }
            _boardAt = sceneName == Il2Cpp.GameManager.LOBBY_SCENE ? UnityEngine.Time.unscaledTime + 3f : -1f;
        }

        public override void OnApplicationQuit()
        {
            ModGate.ForceInert("application quitting");
            if (ModConfig.ReconEnabled.Value)
            {
                ReconLog.Section("Descent counters");
                ReconLog.Line($"- descents: {Descender.Descents}, suppressed ReturnToLobby calls: {Descender.Suppressed}, floors banked: {Rewards.Banked} (last: {Rewards.LastSummary})");
                ReconLog.Line($"- InitBuilder length overrides: {InitBuilderHook.Applied}; net {ModNet.Stats()}; unity log: {UnityLogTap.Summary()}; native throws: {NativeThrowTrace.Summary()}");
                ReconLog.Close();
            }
        }

        private void OnGateChanged(bool active)
        {
            if (!active) { Board.Hide(); if (Launcher.Armed) Launcher.Cancel(local: false); }
            else if (Il2Cpp.GameManager.IsLobbyScene) _boardAt = UnityEngine.Time.unscaledTime + 1f;
        }
    }
}
