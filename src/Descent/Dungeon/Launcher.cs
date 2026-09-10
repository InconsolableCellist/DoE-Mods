using System;
using System.Collections;
using Descent.Gate;
using Descent.Recon;
using Descent.Run;
using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppPhoton.Pun;
using MelonLoader;
using UnityEngine;

namespace Descent.Dungeon
{
    /// <summary>
    /// Getting the party from wherever it is into a floor. Two callers: the hub board (arm a
    /// countdown, then launch floor N) and the descend hand-off (launch floor N+1 from inside
    /// floor N). The load itself is the game's own launch coroutine,
    /// <c>GameManager.LoadDungeon</c>, run on every client — it dissolves the avatar, sets
    /// the load screen, and on the host writes the room seed and calls
    /// <c>PhotonNetwork.LoadLevel</c>, which pulls the rest of the room along. If the step
    /// callback cannot be converted to an Il2Cpp delegate, the mod's own copy of those steps
    /// runs instead (<see cref="ManualLaunch"/>).
    /// </summary>
    public static class Launcher
    {
        private static RunState _armed;
        private static float _armedAt;
        private static int _lastTick = -1;
        private static bool _validating;
        private static object _countdown;

        public static bool Armed => _armed != null;
        public static bool Validating => _validating;
        public static string Status { get; private set; } = "";

        public static void Init()
        {
            RunSync.RunEvent += OnRunEvent;
        }

        // ---- hub: new / resume / restart ----------------------------------------------------

        /// <summary>A new run: name it, seed it, validate every floor's layout, then arm the countdown.</summary>
        public static void StartNew()
        {
            var seed = new System.Random().Next(1, int.MaxValue);
            var run = RunRecord.Create(seed, Naming.ForRun(seed), ModConfig.FloorsPerRun.Value, ModConfig.FloorsPerBand.Value);
            RunStore.Upsert(run);
            ReconLog.Headline($"New run: {run.Describe()}");
            Rewards.Toast($"A new descent: <b>{run.Name}</b>");
            Hub.Board.Refresh();
            BeginLaunch(run);
        }

        public static void Resume(RunRecord run)
        {
            if (run == null) return;
            if (run.IsFinished) { Rewards.Toast($"{run.Name} is finished. Restart it to go again."); return; }
            ReconLog.Headline($"Resume: {run.Describe()}");
            BeginLaunch(run);
        }

        public static void Restart(RunRecord run)
        {
            if (run == null) return;
            run.FloorIndex = 0;
            run.Completed = false;
            run.BankedThrough = -1;
            run.Touch();
            RunStore.Upsert(run);
            ReconLog.Headline($"Restart from floor 1: {run.Describe()}");
            Hub.Board.Refresh();
            BeginLaunch(run);
        }

        private static void BeginLaunch(RunRecord run)
        {
            if (!ModGate.Active) { Rewards.Toast($"Descent is inert: {ModGate.Reason}"); return; }
            if (Armed || _validating) { Rewards.Toast("Already counting down."); return; }
            if (!GameManager.IsLobbyScene) { Rewards.Toast("Start from the hub."); return; }
            var floor = run.Floor(run.FloorIndex);
            if (ModConfig.ValidateFloors.Value) { MelonCoroutines.Start(ValidateThenArm(run, floor)); Hub.Board.Refresh(); }
            else Arm(new RunState { Op = "arm", Run = run, Floor = floor, Seconds = ModConfig.CountdownSeconds.Value }, local: true);
        }

        /// <summary>Layout-check the next floor (stepping its seed on failure), and on a fresh run the rest of the floors too.</summary>
        private static IEnumerator ValidateThenArm(RunRecord run, FloorSpec floor)
        {
            _validating = true;
            Status = "checking the floor...";
            var ok = false;
            var attempts = 0;
            while (attempts < 3)
            {
                var spec = run.Floor(run.FloorIndex);
                var d = FloorPlan.Build(spec);
                var result = false; var finished = false;
                yield return MelonCoroutines.Start(FloorPlan.Validate(d, spec, r => { result = r; finished = true; }));
                while (!finished) yield return null;
                if (result) { ok = true; break; }
                attempts++;
                run.SeedBumps ??= new System.Collections.Generic.Dictionary<int, int>();
                run.SeedBumps[run.FloorIndex] = (run.SeedBumps.TryGetValue(run.FloorIndex, out var b) ? b : 0) + 1;
                ReconLog.Headline($"Floor {spec.Number} failed to lay out on seed {spec.Seed}; bumping (attempt {attempts}).");
            }
            _validating = false;
            Status = "";
            if (!ok) { Rewards.Toast("The builder refused every seed for this floor; check the log."); Hub.Board.Refresh(); yield break; }
            RunStore.Upsert(run);
            Arm(new RunState { Op = "arm", Run = run, Floor = run.Floor(run.FloorIndex), Seconds = ModConfig.CountdownSeconds.Value }, local: true);
        }

        // ---- countdown ----------------------------------------------------------------------

        public static void Arm(RunState state, bool local)
        {
            _armed = state;
            _armedAt = Time.unscaledTime;
            _lastTick = -1;
            Status = $"descending in {state.Seconds:0}s";
            ReconLog.Headline($"Armed: {state.Floor.Describe()} in {state.Seconds:0}s ({(local ? "by us" : "by a peer")}).");
            if (local) RunSync.Send(new RunState { Op = "arm", Run = state.Run, Floor = state.Floor, Seconds = state.Seconds });
            Rewards.Toast($"<b>{state.Run.Name}</b> — floor {state.Floor.Number} ({state.Floor.RealmName}, tier {state.Floor.Tier + 1}) in {state.Seconds:0} seconds. Anyone can cancel at the board.");
            Hub.Board.Refresh();
        }

        public static void Cancel(bool local)
        {
            if (!Armed) return;
            ReconLog.Headline($"Countdown cancelled ({(local ? "by us" : "by a peer")}).");
            _armed = null;
            Status = "";
            if (local) RunSync.Send(new RunState { Op = "cancel" });
            Rewards.Toast("Descent cancelled.");
            Hub.Board.Refresh();
        }

        public static void Tick()
        {
            if (!Armed) return;
            var left = _armed.Seconds - (Time.unscaledTime - _armedAt);
            var tick = (int)Math.Ceiling(left);
            if (tick != _lastTick && tick > 0 && tick <= 5) { _lastTick = tick; Rewards.Toast($"{tick}..."); }
            Status = $"descending in {Math.Max(0, tick)}s";
            if (left > 0f) return;
            var state = _armed;
            _armed = null;
            Status = "";
            if (!PhotonNetwork.IsMasterClient)
            {
                // The host's own countdown launches; ours only ran the toasts. If nothing happens, the host's mod is not armed.
                ReconLog.Line("Countdown reached zero on a non-host client; waiting for the host's launch.");
                return;
            }
            Launch(state);
        }

        // ---- the load -------------------------------------------------------------------------

        /// <summary>Host: floor N from the hub. Writes the room property, tells peers, loads.</summary>
        public static void Launch(RunState state)
        {
            if (!PhotonNetwork.IsMasterClient) { Core.Log.Warning("Launch called on a non-host client; ignored."); return; }
            var run = state.Run; var floor = state.Floor;
            run.DeepestFloor = Math.Max(run.DeepestFloor, floor.Index);
            run.Touch();
            RunStore.Upsert(run);
            RunSync.Set(run, floor, "launch");
            RunSync.WriteRoomProperty(run, floor);
            RunSync.Send(new RunState { Op = "launch", Run = run, Floor = floor });
            Execute(floor, "hub launch");
        }

        /// <summary>Every client: set the tier, hand the mission to the game, run its launch coroutine.</summary>
        public static void Execute(FloorSpec floor, string why)
        {
            var dungeon = FloorPlan.Build(floor);
            try { GameManager.DifficultyTier = (TierOverride)floor.Tier; } catch (Exception e) { Core.Log.Warning($"DifficultyTier set failed: {e.GetType().Name}"); }
            try { GameManager.CurrentDungeon = dungeon; } catch (Exception e) { Core.Log.Warning($"CurrentDungeon set failed: {e.GetType().Name}"); }
            try { GameManager.ResetHazards(); } catch { }
            // The same scene is loaded again on a descent; the game's "SCENE Initialized twice" guard
            // keys on these. Vanilla clears them somewhere inline on the way to the lobby; we never go there.
            if (!GameManager.IsLobbyScene)
            {
                try { ReconLog.Line($"LevelInitialized {GameManager.LevelInitialized} / LastLevelInitialized `{GameManager.LastLevelInitialized}` -> reset for the same-scene reload"); GameManager.LevelInitialized = false; GameManager.LastLevelInitialized = ""; }
                catch (Exception e) { ReconLog.Line($"LevelInitialized reset threw {e.GetType().Name}"); }
            }
            ReconLog.Headline($"Launching {floor.Describe()} ({why}); tier {GameManager.DifficultyTier}; host {PhotonNetwork.IsMasterClient}; dungeon {FloorPlan.Describe(dungeon)}");

            var mode = (ModConfig.LaunchMode.Value ?? "vanilla").Trim().ToLowerInvariant();
            if (mode != "manual")
            {
                try
                {
                    Il2CppSystem.Action<DungeonScanner.TeleportStep> cb =
                        DelegateSupport.ConvertDelegate<Il2CppSystem.Action<DungeonScanner.TeleportStep>>(
                            (Action<DungeonScanner.TeleportStep>)OnTeleportStep);
                    GameManager.LoadDungeon(cb, dungeon, ModConfig.DissolveSeconds.Value);
                    ReconLog.Line("GameManager.LoadDungeon called (vanilla launch coroutine).");
                    return;
                }
                catch (Exception e)
                {
                    Core.Log.Warning($"Vanilla launch path failed ({e.GetType().Name}: {e.Message}); using the manual steps.");
                }
            }
            MelonCoroutines.Start(ManualLaunch(dungeon, floor));
        }

        private static void OnTeleportStep(DungeonScanner.TeleportStep step)
        {
            ReconLog.Line($"teleport step: {step}");
        }

        /// <summary>The steps of <c>GameManager.TeleportToDungeonLevel</c>, from its disassembly, without the callback.</summary>
        private static IEnumerator ManualLaunch(DungeonScanner.Dungeon dungeon, FloorSpec floor)
        {
            ReconLog.Line("manual launch: lock, load screen, dissolve, respawn-on-load, seed, fade, prepare, LoadLevel");
            try { NetManager.LockRoom(); } catch (Exception e) { ReconLog.Line($"LockRoom threw {e.GetType().Name}"); }
            try { GameManager.SetLoadScreenMode(GameMode.DungeonRaid, LoadingScreenMode.DungeonLoad); } catch (Exception e) { ReconLog.Line($"SetLoadScreenMode threw {e.GetType().Name}"); }
            try { AvatarPlayer.LocalAvatar?.SetEndMissionMode(false); } catch { }
            try { GameEvents.InitiatePlayerDissolve?.Invoke(ModConfig.DissolveSeconds.Value); } catch (Exception e) { ReconLog.Line($"dissolve threw {e.GetType().Name}"); }
            yield return new WaitForSeconds(ModConfig.DissolveSeconds.Value);
            try { PlayerSpawner.SetRespawnAvatarOnLevelLoad(FloorPlan.DungeonScene); } catch (Exception e) { ReconLog.Line($"SetRespawnAvatarOnLevelLoad threw {e.GetType().Name}"); }
            if (PhotonNetwork.IsMasterClient)
            {
                try { NetManager.SetRandomRoomSeed(dungeon.seed, "quest_raid", dungeon.name); } catch (Exception e) { ReconLog.Line($"SetRandomRoomSeed threw {e.GetType().Name}"); }
            }
            try { FXCameraFade.Instance?.FadeOut(true); } catch (Exception e) { ReconLog.Line($"FadeOut threw {e.GetType().Name}"); }
            yield return new WaitForSeconds(0.5f);
            try { GameManager.Instance?.PrepareForSceneChange(); } catch (Exception e) { ReconLog.Line($"PrepareForSceneChange threw {e.GetType().Name}"); }
            yield return new WaitForSeconds(0.25f);
            if (PhotonNetwork.IsMasterClient)
            {
                ReconLog.Line($"PhotonNetwork.LoadLevel({FloorPlan.DungeonScene})");
                try { PhotonNetwork.LoadLevel(FloorPlan.DungeonScene); } catch (Exception e) { Core.Log.Error($"LoadLevel threw {e.GetType().Name}: {e.Message}"); }
            }
        }

        // ---- peers ------------------------------------------------------------------------------

        private static void OnRunEvent(int sender, RunState state)
        {
            switch (state.Op)
            {
                case "arm":
                    if (state.Run == null || state.Floor == null) return;
                    Arm(state, local: false);
                    break;
                case "cancel":
                    Cancel(local: false);
                    break;
                case "launch":
                    if (state.Run == null || state.Floor == null) return;
                    _armed = null; Status = "";
                    RunSync.Set(RunStore.Upsert(state.Run, onlyIfNewer: true), state.Floor, $"launch from actor {sender}");
                    Execute(state.Floor, $"launch from actor {sender}");
                    break;
                case "descend":
                    if (state.Run == null || state.Floor == null) return;
                    Descender.OnPeerDescend(sender, state);
                    break;
                case "state":
                    if (state.Floor != null && GameManager.IsLobbyScene == false && !RunSync.Active)
                        RunSync.Set(RunStore.Upsert(state.Run, onlyIfNewer: true), state.Floor, $"state from actor {sender}");
                    break;
            }
        }
    }
}
