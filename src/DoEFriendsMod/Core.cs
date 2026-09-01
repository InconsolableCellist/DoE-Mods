using System;
using MelonLoader;
using DoEFriendsMod;
using DoEFriendsMod.Avatars;
using DoEFriendsMod.Gate;
using DoEFriendsMod.Net;
using DoEFriendsMod.Recon;

[assembly: MelonInfo(typeof(Core), "DoEFriendsMod", "0.14.0", "dan")]
[assembly: MelonGame("Othergate LLC", "Dungeons of Eternity")]

namespace DoEFriendsMod
{
    /// <summary>
    /// Phase 0 + 1 build.
    ///
    /// Phase 0: observes the game and writes a transcript to
    /// <c>UserData/DoEFriendsMod/recon/</c>. Phase 1: the handshake and gating layer —
    /// <see cref="ModRoster"/> discovers modded peers via Photon player properties,
    /// <see cref="ModGate"/> is the master switch every future feature consults, and
    /// <see cref="ModNet"/> is the only place bytes can leave this process.
    ///
    /// Still no gameplay patches. The only Harmony patch in the build is a read-only prefix
    /// on Photon's inbound dispatch.
    /// </summary>
    public class Core : MelonMod
    {
        public const string Version = "0.14.0";

        public static Core Instance { get; private set; }
        public static MelonLogger.Instance Log => Instance.LoggerInstance;

        private AvatarWatcher _avatars;
        private PhotonRecon _photon;
        private ModRoster _roster;
        private ModHandshake _handshake;
        private AvatarLibrary _avatarLibrary;
        private AvatarPreview _preview;
        private AvatarSwapManager _swaps;
        private AvatarSync _avatarSync;
        private HandSync _handSync;
        private HologramSwapper _holograms;
        private bool _envDumped;
        private float _hotkeyCooldown;

        public override void OnInitializeMelon()
        {
            Instance = this;
            ModConfig.Load();

            LoggerInstance.Msg($"DoEFriendsMod {Version} — Phase 0 recon + Phase 1 gating + Phase 2 avatar loading and swap.");
            LoggerInstance.Msg($"Recon output: {ReconLog.OutputDir}");
            LoggerInstance.Msg("This build makes no gameplay changes and sends no network traffic.");

            SelfCheck.LogSelfHash(LoggerInstance);
            LoggerInstance.Msg($"Swap settings: {AvatarSwapper.DescribeSettings()}");

            // One read-only prefix on Photon's inbound dispatch, shared by every listener.
            PhotonHook.Install(HarmonyInstance);
            ModNet.Init();

            _avatars = new AvatarWatcher();
            _photon = new PhotonRecon();
            _photon.InstallEventSniffer();

            _roster = new ModRoster();
            _handshake = new ModHandshake(_roster);
            ModGate.ActiveChanged += OnGateChanged;

            _avatarLibrary = new AvatarLibrary();
            _avatarLibrary.Rescan();
            _preview = new AvatarPreview(_avatarLibrary);
            _swaps = new AvatarSwapManager(_avatarLibrary);
            _avatarSync = new AvatarSync(_swaps, _avatarLibrary, _roster);
            _handSync = new HandSync(_swaps, _roster);
            _holograms = new HologramSwapper(_avatarLibrary, _swaps);
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            LoggerInstance.Msg($"Scene initialized: [{buildIndex}] {sceneName}");

            // The XR stack is only fully up once a real scene is running, so the
            // environment dump waits for the first one rather than firing at melon init.
            if (!_envDumped)
            {
                _envDumped = true;
                EnvironmentRecon.DumpOnce();
            }

            _avatars.Reset();
        }

        public override void OnUpdate()
        {
            var dt = UnityEngine.Time.unscaledDeltaTime;

            // Gating runs unconditionally — it is what keeps the mod inert, so it must not
            // be switchable off from a config file the way the recon dumps are.
            _roster.Tick(dt);
            ModGate.Evaluate(_roster);
            ModNet.Pump();

            if (!ModConfig.ReconEnabled.Value) return;

            _avatars.Tick();
            _photon.Tick();
            PollHotkeys();
        }

        /// <summary>One-line swap state for the overlay.</summary>
        public string SwapSummary => _swaps?.Describe() ?? "-";

        public override void OnGUI()
        {
            try { Overlay.Draw(); }
            catch { /* a GUI exception every frame would bury the log */ }
        }

        public override void OnLateUpdate()
        {
            var dt = UnityEngine.Time.deltaTime;
            _preview?.LateUpdate(dt);
            // After the game's own IK has solved this frame; ours runs on top of the pose it left.
            _swaps?.Tick(dt);
            _handSync?.Tick(UnityEngine.Time.unscaledTime);
            _holograms?.Tick(UnityEngine.Time.unscaledTime);
        }

        private void OnGateChanged(bool active)
        {
            LoggerInstance.Msg(active
                ? $"Features may now arm. Roster: {_roster.Describe()}"
                : $"Features must disarm now. {ModNet.Stats()}");
        }


        /// <summary>
        /// Desktop keys for on-demand dumps. In VR the game window still receives them when
        /// focused, which is enough for a "do that again now that I'm standing somewhere
        /// interesting" workflow. Legacy Input throws outright under the new Input System;
        /// if that happens we log once and stop asking.
        /// </summary>
        private bool _hotkeysDead;

        private void PollHotkeys()
        {
            if (_hotkeysDead || !ModConfig.HotkeysEnabled.Value) return;
            if (_hotkeyCooldown > 0f) { _hotkeyCooldown -= UnityEngine.Time.unscaledDeltaTime; return; }

            try
            {
                if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F7))
                {
                    _hotkeyCooldown = 0.5f;
                    EnvironmentRecon.DumpOnce(force: true);
                }
                else if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F8))
                {
                    _hotkeyCooldown = 0.5f;
                    _avatars.DumpEverythingNow();
                }
                else if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F9))
                {
                    _hotkeyCooldown = 0.5f;
                    _photon.DumpRoomNow();
                }
                else if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F10))
                {
                    // Direct toggles for the two diagnostic switches. Editing the .cfg and
                    // pressing F3 should do the same thing, but when a setting appears to have
                    // no effect you cannot tell whether the code ignored it or the file never
                    // reached the game — this removes that question.
                    _hotkeyCooldown = 0.5f;
                    ModConfig.SwapHideVanillaMesh.Value = !ModConfig.SwapHideVanillaMesh.Value;
                    LoggerInstance.Msg($"*** SwapHideVanillaMesh is now {ModConfig.SwapHideVanillaMesh.Value} " +
                                       "(false = your vanilla body is drawn too)");
                }
                else if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F11))
                {
                    _hotkeyCooldown = 0.5f;
                    ModConfig.SwapHideFpsArms.Value = !ModConfig.SwapHideFpsArms.Value;
                    LoggerInstance.Msg($"*** SwapHideFpsArms is now {ModConfig.SwapHideFpsArms.Value} " +
                                       "(false = the vanilla first-person arms are drawn too)");
                }
                else if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F12))
                {
                    _hotkeyCooldown = 0.5f;
                    var toIk = !string.Equals(ModConfig.SwapArmSource.Value, "IKTargets",
                                              System.StringComparison.OrdinalIgnoreCase);
                    ModConfig.SwapArmSource.Value = toIk ? "IKTargets" : "Retarget";
                    LoggerInstance.Msg($"*** SwapArmSource is now {ModConfig.SwapArmSource.Value} " +
                                       "— press F4 twice to rebuild the avatar with it.");
                }
                else if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F6))
                {
                    _hotkeyCooldown = 0.5f;
                    _preview.Toggle();
                }
                else if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F4))
                {
                    _hotkeyCooldown = 0.5f;
                    _swaps.ToggleSelf();
                    // Tell peers either way — taking the avatar off has to reach them too, or
                    // they keep seeing a model you're no longer wearing.
                    _avatarSync.Broadcast("F4");
                }
                else if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F5))
                {
                    _hotkeyCooldown = 0.5f;
                    _avatarLibrary.Rescan();
                }
                else if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F1))
                {
                    _hotkeyCooldown = 0.5f;
                    Overlay.Visible = !Overlay.Visible;
                }
                else if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F2))
                {
                    _hotkeyCooldown = 0.5f;
                    var next = _avatarLibrary.CycleSelection();
                    LoggerInstance.Msg(next == null
                        ? "No avatars installed to choose from."
                        : $"*** Avatar selected: {next} — press F4 twice to put it on.");
                }
                else if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F3))
                {
                    // Re-read MelonPreferences.cfg from disk. The spring constants are read
                    // per frame, so edit-save-F3 retunes a spawned avatar with no respawn.
                    _hotkeyCooldown = 0.5f;
                    MelonPreferences.Load();
                    // Print everything that matters, not just the springs. A setting left over
                    // from an earlier debugging session is invisible otherwise, and the symptom
                    // it causes looks like a bug in the code rather than a value in a file.
                    LoggerInstance.Msg($"Preferences reloaded — springs: stiffness x{ModConfig.SpringStiffnessScale.Value}, " +
                                       $"gravity x{ModConfig.SpringGravityScale.Value}, drag {ModConfig.SpringDragBase.Value}, " +
                                       $"colliders {ModConfig.SpringCollidersEnabled.Value}");
                    LoggerInstance.Msg($"Preferences reloaded — swap: {AvatarSwapper.DescribeSettings()}");
                    if (!ModConfig.SwapUseVrik.Value)
                        LoggerInstance.Warning("*** SwapUseVrik is FALSE — swapped avatars will T-pose. " +
                                               "That is a diagnostic setting; set it back to true.");
                }
            }
            catch (Exception e)
            {
                _hotkeysDead = true;
                LoggerInstance.Warning($"Legacy Input unavailable, hotkeys disabled: {e.Message}");
            }
        }

        public override void OnApplicationQuit()
        {
            _preview?.Despawn("application quitting");
            _holograms?.RevertAll("application quitting");
            _swaps?.RevertAll("application quitting");
            ModGate.ForceInert("application quitting");
            ReconLog.Close();
        }
    }
}
