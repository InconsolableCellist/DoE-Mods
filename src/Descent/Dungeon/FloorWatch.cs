using System;
using Descent.Recon;
using Il2Cpp;
using Il2CppTMPro;
using UnityEngine;
using Interop = Descent.Recon.Interop;

namespace Descent.Dungeon
{
    /// <summary>
    /// In the dungeon scene: watch the generation land, write what the game actually built
    /// (rooms, hazards, tier, end state) to the transcript, and relabel the exit pads.
    /// Everything here is read-only except the pad text.
    /// </summary>
    public static class FloorWatch
    {
        private static float _nextPoll;
        private static bool _generationLogged;
        private static bool _padsLabelled;
        private static float _sceneStart;
        private static string _lastEndState = "";
        private static float _stateAt = -1f;

        public static void OnDungeonScene()
        {
            _generationLogged = false;
            _padsLabelled = false;
            _sceneStart = Time.unscaledTime;
            _nextPoll = Time.unscaledTime + 1f;
            try { _lastEndState = GameManager.MissionEndState.ToString(); } catch { _lastEndState = "?"; }
            ReconLog.Line($"dungeon scene start: end state {_lastEndState}, end-mission mode {SafeEndMode()}, tier {SafeTier()}, CurrentDungeon {FloorPlan.Describe(SafeDungeon())}");
            ReconLog.Line($"- state: {SpawnState()}");
            // MissionEndState reads Success at the start of a fresh dungeon (2026-09-07 session), so
            // Success is the game's "nothing has gone wrong yet" state and must be left alone. Only the
            // end-mission mode flag is cleared if it survived the hand-off.
            if (RunSync.Active && !ModConfig.ObserveOnly.Value)
            {
                try
                {
                    if (GameManager.IsEndMissionMode)
                    {
                        ReconLog.Line("resetting stale IsEndMissionMode for the new floor");
                        GameManager.IsEndMissionMode = false;
                    }
                }
                catch (Exception e) { ReconLog.Line($"end-mode reset threw {e.GetType().Name}: {e.Message}"); }
            }
        }

        public static void OnOtherScene()
        {
            _generationLogged = true;
            _padsLabelled = true;
        }

        public static void Tick()
        {
            if (Time.unscaledTime < _nextPoll) return;
            _nextPoll = Time.unscaledTime + 2f;
            try
            {
                var end = "?";
                try { end = GameManager.MissionEndState.ToString(); } catch { }
                if (end != _lastEndState) { ReconLog.Line($"end state {_lastEndState} -> {end}"); _lastEndState = end; }

                if (!_generationLogged)
                {
                    var d = SafeDungeon();
                    var rooms = 0;
                    try { rooms = SceneOcclusion.RoomList?.Length ?? 0; } catch { }
                    if (d != null && rooms > 0)
                    {
                        _generationLogged = true;
                        var tier = SafeTier();
                        var enemyTier = "?"; try { enemyTier = CombatRunner.EnemyTier.ToString(); } catch { }
                        ReconLog.Section($"Floor generated: {rooms} room(s) in scene after {Time.unscaledTime - _sceneStart:0.0}s");
                        ReconLog.Line($"- dungeon: {FloorPlan.Describe(d)}");
                        ReconLog.Line($"- DifficultyTier {tier}, CombatRunner.EnemyTier {enemyTier}, InitBuilder overrides so far {InitBuilderHook.Applied}");
                        ReconLog.Line($"- run: {(RunSync.Active ? RunSync.Floor.Describe() : "(no run)")}");
                        ReconLog.Line($"- hazards live: any {GameManager.Hazard_Any} swarms {GameManager.Hazard_CreatureSwarms} boss {GameManager.Hazard_BossBattles} miniboss {GameManager.Hazard_MiniBossBattles} dmg150 {GameManager.Hazard_150PctDamage} dmg200 {GameManager.Hazard_200PctDamage}");
                        ReconLog.Line($"- state: {SpawnState()}");
                        _stateAt = Time.unscaledTime + 8f;
                        try
                        {
                            var names = new System.Collections.Generic.List<string>();
                            for (var i = 0; i < d.rooms.Count && i < 64; i++) { var r = d.rooms[i]; names.Add($"{r.roomIndex}:{r.def?.prefabName}@d{r.roomDepth}{(r.IsMainPath ? "*" : "")}"); }
                            ReconLog.Line($"- rooms: {string.Join(" ", names)}");
                        }
                        catch (Exception e) { ReconLog.Line($"- rooms: unreadable ({e.GetType().Name})"); }
                    }
                    else if (Time.unscaledTime - _sceneStart > 90f)
                    {
                        _generationLogged = true;
                        ReconLog.Line($"no generated dungeon seen after 90 s: CurrentDungeon {FloorPlan.Describe(d)}, rooms {rooms}");
                    }
                }

                if (_stateAt > 0f && Time.unscaledTime >= _stateAt)
                {
                    _stateAt = -1f;
                    ReconLog.Line($"- state 8 s later: {SpawnState()}");
                }
                if (!_padsLabelled && ModConfig.RelabelExit.Value && RunSync.Active) LabelPads();
            }
            catch (Exception e) { Core.Log.Warning($"FloorWatch tick threw: {e.GetType().Name}: {e.Message}"); }
        }

        /// <summary>The scene-start and post-generation snapshot: what the spawn and the load screen depend on.</summary>
        private static string SpawnState()
        {
            var sb = new System.Text.StringBuilder();
            try { sb.Append($"loadScreen {GameManager.LoadScreenMode} loading {GameManager.LevelIsLoading} levelInit {GameManager.LevelInitialized} endMode {GameManager.IsEndMissionMode} endState {GameManager.MissionEndState} enemyTier {CombatRunner.EnemyTier}"); } catch (Exception e) { sb.Append($"gm? {e.GetType().Name}"); }
            try { var a = AvatarPlayer.LocalAvatar; sb.Append(a == null ? " | no local avatar" : $" | avatar `{Interop.Name(a)}` endMission {a.InEndMissionMode} pos {Interop.Vec(a.transform.position)}"); } catch (Exception e) { sb.Append($" | avatar? {e.GetType().Name}"); }
            try { sb.Append($" | navmesh {SpawnArea.IsNavMeshValid} spawnAreas {(SpawnArea.AllSpawnAreas == null ? -1 : SpawnArea.AllSpawnAreas.Count)}"); } catch (Exception e) { sb.Append($" | spawner? {e.GetType().Name}"); }
            try { var gm = GameManager.Instance; var a = AvatarPlayer.LocalAvatar; sb.Append(gm == null || a == null ? " | stats n/a" : $" | my stats {(gm.FindPlayerStatDef(a) == null ? "MISSING" : "present")}"); } catch (Exception e) { sb.Append($" | stats? {e.GetType().Name}"); }
            try { sb.Append($" | AI in scene {UnityEngine.Object.FindObjectsOfType<Il2CppSauron.AI>().Length}"); } catch { }
            try { sb.Append($" | teleporters {UnityEngine.Object.FindObjectsOfType<Teleporter>().Length} pads {UnityEngine.Object.FindObjectsOfType<TeleporterPad>().Length}"); } catch { }
            try { var ls = UnityEngine.Object.FindObjectOfType<UILoadScreen>(); sb.Append(ls == null ? " | no load screen object" : $" | load screen `{Interop.ScenePath(ls.transform)}` active {ls.gameObject.activeInHierarchy} players {(ls.players == null ? -1 : ls.players.Count)}"); } catch (Exception e) { sb.Append($" | loadscreen? {e.GetType().Name}"); }
            return sb.ToString();
        }

        private static void LabelPads()
        {
            // Teleporter.Instance stayed null for a whole floor on 2026-09-07; find the exit by type.
            Teleporter tp = null;
            try { foreach (var t in UnityEngine.Object.FindObjectsOfType<Teleporter>()) if (Interop.Alive(t)) { tp = t; break; } } catch { }
            if (tp == null) return;
            var floor = RunSync.Floor;
            var label = floor.IsLast ? "SURFACE — THE DESCENT ENDS HERE" : $"DESCEND TO FLOOR {floor.Number + 1}";
            var going = floor.IsLast ? "Returning to Outpost" : $"Descending to floor {floor.Number + 1}";
            var n = 0;
            foreach (var pad in UnityEngine.Object.FindObjectsOfType<TeleporterPad>())
            {
                if (!Interop.Alive(pad)) continue;
                try
                {
                    pad.waitingStr = label;
                    pad.teleportingStr = going;
                    var t = pad.detailText;
                    if (Interop.Alive(t)) { t.text = label; n++; }
                }
                catch (Exception e) { ReconLog.Line($"pad label failed: {e.GetType().Name}: {e.Message}"); }
            }
            _padsLabelled = true;
            ReconLog.Line($"exit pads relabelled `{label}` on {n} pad(s); teleporter `{Interop.ScenePath(tp.transform)}` scene {tp.Get_SceneName}");
        }

        private static DungeonScanner.Dungeon SafeDungeon()
        {
            try { return GameManager.CurrentDungeon; } catch { return null; }
        }

        private static string SafeEndMode()
        {
            try { return GameManager.IsEndMissionMode.ToString(); } catch { return "?"; }
        }

        private static string SafeTier()
        {
            try { return GameManager.DifficultyTier.ToString(); } catch { return "?"; }
        }
    }
}
