using System;
using MelonLoader;
using LootOverhaul;
using LootOverhaul.Gate;
using LootOverhaul.Loot;
using LootOverhaul.Net;
using LootOverhaul.Recon;

[assembly: MelonInfo(typeof(Core), "LootOverhaul", "0.9.11", "dan")]
[assembly: MelonGame("Othergate LLC", "Dungeons of Eternity")]

namespace LootOverhaul
{
    /// <summary>
    /// LootOverhaul: Diablo-style weapon drops, a weight-limited loot bag, and a lobby booth
    /// to sell and equip from it. See docs/LOOT-OVERHAUL.md for the investigation and the
    /// design decisions this build follows.
    ///
    /// v0.2 is milestone **L1, the core loop**: the master rolls a drop on every real kill,
    /// spawns the weapon through the game's own networked path, tags it (rarity beam,
    /// toast) for every modded client, and picking a tagged weapon up bags it instead of
    /// wielding it, arbitrated by the master. The bag is a local JSON. The 0.1 recon hooks,
    /// watchdog and probes are still here behind `ReconEnabled`.
    /// </summary>
    public class Core : MelonMod
    {
        public const string Version = "0.9.12";

        public static Core Instance { get; private set; }
        public static MelonLogger.Instance Log => Instance.LoggerInstance;

        private ModRoster _roster;
        private ModHandshake _handshake;
        private float _templateCaptureAt = -1f;

        public override void OnInitializeMelon()
        {
            Instance = this;
            ModConfig.Load();
            try { MelonPreferences.Save(); }
            catch (Exception e) { LoggerInstance.Warning($"Could not write MelonPreferences.cfg: {e.Message}"); }

            LoggerInstance.Msg($"LootOverhaul {Version} — drops, junk, bag, the kobold traveler (sell + shop), loot in the fabricator. Recon hooks {(ModConfig.ReconEnabled.Value ? "on" : "off")}.");
            LoggerInstance.Msg($"Data folder: {ModPaths.Root}; recon transcripts in {ModPaths.ReconDir}");

            SelfCheck.LogSelfHash(LoggerInstance);

            // One read-only prefix on Photon's inbound dispatch. CustomAvatars installs the same
            // kind of prefix from its own Harmony instance; Harmony chains them.
            PhotonHook.Install(HarmonyInstance);
            ModNet.Init();

            _roster = new ModRoster();
            _handshake = new ModHandshake(_roster);
            ModGate.ActiveChanged += OnGateChanged;

            Hooks.Init(HarmonyInstance);
            SceneExit.Install();
            LootNet.Init(_roster);
            DropRoller.Install();
            BagPickup.Install();
            FabricatorBridge.Install();
            Buffs.Install();

            if (ModConfig.ReconEnabled.Value)
            {
                EventTally.Install();
                if (ModConfig.ProfileWatchEnabled.Value) ProfileWatch.Install();
                GameplayHooks.Install();
            }
            Hooks.Report();
            LoggerInstance.Msg("Hotkeys: [ = open/close the bag panel (or the VR gesture, see BagGesture), ] = drop the last bagged item, = (equals) = place the booth where you stand (lobby), - (minus) = junk prefab probe, ; (semicolon) = Resources census.");
            if (ModConfig.ReconEnabled.Value)
            {
                LoggerInstance.Msg("Recon hotkeys: Insert = generator survey, Delete = spawn a test weapon (private room), Backslash (\\) = lobby survey + marker cubes, Scroll Lock = cloned button + pointer test.");
            }
        }

        public override void OnUpdate()
        {
            var dt = UnityEngine.Time.unscaledDeltaTime;
            _roster.Tick(dt);
            ModGate.Evaluate(_roster);
            ModNet.Pump();
            LootRegistry.Tick();
            BagPickup.Tick();
            Claims.Tick();
            BagGesture.Tick();
            BagPanel.Tick();
            Buffs.Tick();
            SceneExit.Tick();
            if (_templateCaptureAt > 0f && UnityEngine.Time.unscaledTime >= _templateCaptureAt)
            {
                _templateCaptureAt = -1f;
                try { UiKit.CaptureTemplates(); Booth.ShowIfLobby(Il2Cpp.GameManager.LOBBY_SCENE); if (ModConfig.ReconEnabled.Value) Buffs.Snapshot("lobby"); Enchanting.SelfTest(); }
                catch (Exception e) { LoggerInstance.Warning($"Template capture / booth threw: {e.GetType().Name}: {e.Message}"); }
            }

            if (!ModConfig.HotkeysEnabled.Value) return;
            try
            {
                if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.LeftBracket)) BagPanel.Toggle();
                else if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.RightBracket)) BagManager.DropLast();
                else if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.Equals)) Booth.PlaceHere();
                else if (ModConfig.ReconEnabled.Value && UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.Minus)) GeneratorProbe.JunkProbe();
                else if (ModConfig.ReconEnabled.Value && UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.Semicolon)) GeneratorProbe.ResourceCensus();
                if (!ModConfig.ReconEnabled.Value) return;
                if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.Insert)) GeneratorProbe.Survey();
                else if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.Delete)) GeneratorProbe.SpawnTest();
                else if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.Backslash)) LobbyProbe.Survey();
                else if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.ScrollLock)) LobbyProbe.ButtonTest();
            }
            catch (Exception e) { LoggerInstance.Warning($"Hotkey probe threw: {e.GetType().Name}: {e.Message}"); }
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            // The pooled loot bodies outlive the scene; whatever the pre-load hook did not
            // catch is destroyed here, while the room is still the same one.
            LootRegistry.Clear($"scene changed to {sceneName}", destroyOwned: true);
            Unlocks.Invalidate();
            if (sceneName == Il2Cpp.GameManager.LOBBY_SCENE || sceneName == Il2Cpp.GameManager.MAINMENU_SCENE) Buffs.ClearAll($"entered {sceneName}");
            BagPanel.Hide();
            Booth.Hide();
            _templateCaptureAt = sceneName == Il2Cpp.GameManager.LOBBY_SCENE ? UnityEngine.Time.unscaledTime + 3f : -1f;
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
            LootRegistry.Clear("application quitting", destroyOwned: true);
            ModGate.ForceInert("application quitting");
            if (ModConfig.ReconEnabled.Value)
            {
                ReconLog.Section("Loot loop counters");
                ReconLog.Line($"- kills rolled on this master: {DropRoller.RollsSeen}, weapon drops: {DropRoller.Dropped}, junk drops: {DropRoller.JunkDropped}, pickups turned into claims: {BagPickup.Cancelled}");
                ReconLog.Line($"- loadout: {Loadout.Describe()}");
                ReconLog.Line($"- fabricator bridge: {FabricatorBridge.Describe()}");
                ReconLog.Line($"- shop: {Shop.Bought} bought, {Shop.Restocks} restock(s)");
                ReconLog.Line($"- buffs active at quit: {Buffs.DescribeActive()}; worn: {Armor.DescribeWorn()}");
                ReconLog.Line($"- enchanting: {Enchanting.SelfTestReport}");
                Buffs.Snapshot("quit");
                EventTally.Report("quit");
                ProfileWatch.Report();
                ReconLog.Close();
            }
        }

        private void OnGateChanged(bool active)
        {
            // The drop roll and the pickup prefix each check ModGate.Active on every call, so
            // there is nothing to arm. Disarming means forgetting the floor loot: in a room
            // that just went vanilla, those weapons are ordinary weapons now.
            if (!active) { LootRegistry.Clear("gate closed", destroyOwned: true); Booth.Hide(); }
            else if (Il2Cpp.GameManager.IsLobbyScene) _templateCaptureAt = UnityEngine.Time.unscaledTime + 1f;
        }
    }
}
