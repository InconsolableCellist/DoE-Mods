using System;
using MelonLoader;
using CustomAvatars;
using CustomAvatars.Avatars;
using CustomAvatars.Gate;
using CustomAvatars.Net;
using CustomAvatars.Recon;

[assembly: MelonInfo(typeof(Core), "CustomAvatars", "0.42.6", "dan")]
[assembly: MelonGame("Othergate LLC", "Dungeons of Eternity")]

namespace CustomAvatars
{
    /// <summary>
    /// Phase 0 + 1 build.
    ///
    /// Phase 0: observes the game and writes a transcript to
    /// <c>UserData/CustomAvatars/recon/</c>. Phase 1: the handshake and gating layer —
    /// <see cref="ModRoster"/> discovers modded peers via Photon player properties,
    /// <see cref="ModGate"/> is the master switch every future feature consults, and
    /// <see cref="ModNet"/> is the only place bytes can leave this process.
    ///
    /// Still no gameplay patches. The only Harmony patch in the build is a read-only prefix
    /// on Photon's inbound dispatch.
    /// </summary>
    public class Core : MelonMod
    {
        public const string Version = "0.42.6";

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
        private Face.VrcftBridge _face;
        private Face.FaceSync _faceSync;
        private Fbt.TrackerReader _trackers;
        private Fbt.FbtManager _fbt;
        private PlayerSize _size;
        private bool _envDumped;
        private float _hotkeyCooldown;

        public override void OnInitializeMelon()
        {
            Instance = this;
            ModConfig.Load();
            // MelonLoader only writes MelonPreferences.cfg when something saves it, which
            // otherwise means on quit. Save once now so a fresh install has a complete,
            // readable settings file to edit before the first clean exit rather than after it.
            try { MelonPreferences.Save(); }
            catch (Exception e) { LoggerInstance.Warning($"Could not write MelonPreferences.cfg: {e.Message}"); }

            LoggerInstance.Msg($"CustomAvatars {Version} — Phase 0 recon + Phase 1 gating + Phase 2 avatar loading and swap.");
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
            // Your size. Idle at AvatarSize 1; otherwise scales the play space and the game's
            // body together, and the avatar is re-fitted to wherever your head ends up.
            _trackers = new Fbt.TrackerReader();
            _size = new PlayerSize(_trackers);
            _size.Changed += size => _swaps.OnSelfSizeChanged(size);
            if (Math.Abs(PlayerSize.Wanted() - 1f) > 0.0005f)
                LoggerInstance.Msg($"Size: AvatarSize is {PlayerSize.Wanted():0.00} — you will be that size once you have spawned. Home puts it back to 1.");
            _avatarSync = new AvatarSync(_swaps, _avatarLibrary, _roster);
            _handSync = new HandSync(_swaps, _roster);
            _holograms = new HologramSwapper(_avatarLibrary, _swaps);
            _fbt = new Fbt.FbtManager(_swaps, _roster, _trackers);

            if (ModConfig.FaceOscEnabled.Value)
            {
                _face = new Face.VrcftBridge();
                _face.Start();
                _faceSync = new Face.FaceSync(_swaps, _roster);
            }
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

            // Size first: this frame's tracker poses, IK targets and avatar fit must all see
            // the play space at the size settled on, not last frame's.
            _size?.Tick();

            // Before the game's LateUpdate, where FinalIK solves: tracker targets set here are
            // where this frame's legs land. After Pump, so a peer's poses land the same frame.
            _fbt?.Update(dt);

            // Hotkeys are not a recon feature. They used to sit behind this check, which meant
            // turning the dumps off silently took F4 with it.
            PollHotkeys();

            if (!ModConfig.ReconEnabled.Value) return;

            _avatars.Tick();
            _photon.Tick();
        }

        /// <summary>Latest face-tracking values, or null when the OSC bridge isn't running.</summary>
        public Face.FaceState FaceState => _face?.State;

        /// <summary>
        /// Everything VRCFaceTracking has actually sent, with current values.
        ///
        /// This is the only way to tell "the avatar has no shape for that" apart from "the
        /// tracking never produced it" apart from "we're reading the wrong parameter name".
        /// VRCFaceTracking only transmits a parameter when its value CHANGES, so a shape the
        /// hardware doesn't track never appears here at all — the list is what moved, not what
        /// exists.
        /// </summary>
        private void DumpFaceParameters()
        {
            var state = _face?.State;
            if (state == null) { LoggerInstance.Warning("Face OSC is not running."); return; }

            var snapshot = state.Snapshot();
            ReconLog.Section($"Face OSC parameters ({snapshot.Count} seen)");
            ReconLog.KeyValue("messages received", state.MessageCount);
            ReconLog.KeyValue("last message", $"{state.SecondsSinceLastMessage:0.0}s ago");
            ReconLog.Line();
            ReconLog.Line("```");
            foreach (var kv in snapshot) ReconLog.Line($"{kv.Value,7:0.000}  {kv.Key}");
            ReconLog.Line("```");

            LoggerInstance.Msg($"Face OSC: {snapshot.Count} parameter(s) written to {ReconLog.CurrentFile}");
            foreach (var kv in snapshot) LoggerInstance.Msg($"    {kv.Value,7:0.000}  {kv.Key}");
        }

        /// <summary>One-line swap state for the overlay.</summary>
        public string SwapSummary => _swaps?.Describe() ?? "-";

        /// <summary>One-line FBT state for the overlay.</summary>
        public string FbtSummary => _fbt?.Describe() ?? "-";

        /// <summary>One-line size state for the overlay.</summary>
        public string SizeSummary => _size?.Describe() ?? "-";

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
            _fbt?.LateTick(UnityEngine.Time.unscaledTime);
            _holograms?.Tick(UnityEngine.Time.unscaledTime, dt);

            _face?.Tick(UnityEngine.Time.unscaledTime);
            _faceSync?.Tick(UnityEngine.Time.unscaledTime);
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
                    DumpFaceParameters();
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
                else if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F6))
                {
                    _hotkeyCooldown = 0.5f;
                    _preview.Toggle();
                }
                else if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F4))
                {
                    _hotkeyCooldown = 0.5f;
                    // Peers are told by AvatarSwapManager.SelfAvatarChanged, which also covers
                    // taking the avatar off and putting it back on after a scene change.
                    _swaps.ToggleSelf();
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
                else if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F10))
                {
                    // Refusing (fewer than 3 trackers) still writes the full recon dump, so
                    // F10 with no pucks on doubles as the tracker diagnostics key.
                    _hotkeyCooldown = 0.5f;
                    _fbt.Toggle();
                }
                else if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F11))
                {
                    _hotkeyCooldown = 0.5f;
                    _fbt.StartCalibration();
                }
                else if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.PageUp))
                {
                    // Short cooldown: these are held-and-tapped keys, not one-shot dumps.
                    _hotkeyCooldown = 0.12f;
                    _size.Nudge(+1);
                }
                else if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.PageDown))
                {
                    _hotkeyCooldown = 0.12f;
                    _size.Nudge(-1);
                }
                else if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.Home))
                {
                    _hotkeyCooldown = 0.5f;
                    _size.ResetToVanilla();
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
                    LoggerInstance.Msg($"Preferences reloaded — AvatarSize {PlayerSize.Wanted():0.00}, " +
                                       $"SizeMoveSpeedBlend {ModConfig.SizeMoveSpeedBlend.Value:0.00}");
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
            _fbt?.Shutdown();
            _size?.Shutdown();
            _preview?.Despawn("application quitting");
            _face?.Dispose();
            _holograms?.RevertAll("application quitting");
            _swaps?.RevertAll("application quitting");
            ModGate.ForceInert("application quitting");
            ReconLog.Close();
        }
    }
}
