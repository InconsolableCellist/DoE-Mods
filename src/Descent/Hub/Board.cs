using System;
using System.Collections.Generic;
using Descent.Dungeon;
using Descent.Gate;
using Descent.Recon;
using Descent.Run;
using Il2Cpp;
using Il2CppTMPro;
using MelonLoader;
using UnityEngine;
using Interop = Descent.Recon.Interop;

namespace Descent.Hub
{
    /// <summary>
    /// The board in the hub: the descents this machine knows, with RESUME / RESTART on each
    /// and NEW DESCENT at the top, plus the countdown and a CANCEL while one is armed.
    /// Placed from config (press / in the lobby to move it to where you stand), built once,
    /// kept across scene loads, shown only in the lobby with the gate open. Later this becomes
    /// a doorway with a staircase; the buttons stay the same.
    /// </summary>
    public static class Board
    {
        private const int Rows = 5;
        private const float RowHeight = 0.13f;
        private const float PanelWidth = 1.35f;
        private const float BtnScale = 0.34f;
        private static float BtnW => UiKit.ButtonSize.x * BtnScale;

        private static GameObject _root;
        private static Transform _panel;
        private static TextMeshPro _status;
        private static float _nextStatus;

        /// <summary>Beside LootOverhaul's kobold (its built-in spot is (42.075, -1.930, 16.224), yaw 1.536): 2.6 m along the kobold's right.</summary>
        public static readonly Vector3 DefaultPosition = new Vector3(42.075f + 2.6f, -1.930f, 16.224f);
        public const float DefaultYaw = 1.536f;

        public static bool IsShown => Interop.Alive(_root) && _root.activeSelf;

        public static void ShowIfLobby(string sceneName)
        {
            if (sceneName != GameManager.LOBBY_SCENE || !ModGate.Active) { Hide(); return; }
            UiKit.CaptureTemplates();
            if (!UiKit.Ready) { Core.Log.Warning("Board: UI templates not ready; will not build this time."); return; }
            EnsureRoot();
            PlaceFromConfig();
            _root.SetActive(true);
            Rebuild();
        }

        public static void Hide()
        {
            if (Interop.Alive(_root)) _root.SetActive(false);
        }

        public static void Refresh()
        {
            if (IsShown) Rebuild();
        }

        public static void Tick()
        {
            if (!IsShown || Time.unscaledTime < _nextStatus) return;
            _nextStatus = Time.unscaledTime + 0.5f;
            try { if (Interop.Alive(_status)) _status.text = StatusLine(); } catch { }
        }

        /// <summary>Hotkey: put the board 1.5 m in front of where you stand, facing you, and remember it.</summary>
        public static void PlaceHere()
        {
            try
            {
                var local = AvatarPlayer.LocalAvatar;
                if (!Interop.Alive(local)) { Rewards.Toast("No avatar."); return; }
                var head = local.Head;
                var fwd = head.forward; fwd.y = 0f; fwd.Normalize();
                var floorY = local.transform.position.y;
                var pos = head.position + fwd * 1.5f;
                pos.y = floorY;
                var yaw = Quaternion.LookRotation(-fwd, Vector3.up).eulerAngles.y;
                ModConfig.BoardX.Value = pos.x; ModConfig.BoardY.Value = pos.y; ModConfig.BoardZ.Value = pos.z; ModConfig.BoardYaw.Value = yaw;
                ModConfig.BoardPlaced.Value = true;
                MelonPreferences.Save();
                Rewards.Toast("Board moved.");
                if (GameManager.IsLobbyScene) ShowIfLobby(GameManager.LOBBY_SCENE);
            }
            catch (Exception e) { Core.Log.Warning($"PlaceHere failed: {e.GetType().Name}: {e.Message}"); }
        }

        private static void EnsureRoot()
        {
            if (Interop.Alive(_root)) return;
            _root = new GameObject("Descent_Board");
            UnityEngine.Object.DontDestroyOnLoad(_root);
            var panel = new GameObject("Panel"); panel.transform.SetParent(_root.transform, false);
            _panel = panel.transform;
            _panel.localPosition = new Vector3(0f, 1.35f, 0f);
            UiKit.Text(_root.transform, new Vector3(0f, 2.12f, 0f), 1.6f, 0.15f, 0.9f, "<b>THE DESCENT</b>", TextAlignmentOptions.Center);
            UiKit.Text(_root.transform, new Vector3(0f, 2.0f, 0f), 1.6f, 0.06f, 0.36f, "<color=#9A9A9A>sixteen floors down, and the way back up</color>", TextAlignmentOptions.Center);
        }

        private static void PlaceFromConfig()
        {
            var placed = ModConfig.BoardPlaced.Value;
            var pos = placed ? new Vector3(ModConfig.BoardX.Value, ModConfig.BoardY.Value, ModConfig.BoardZ.Value) : DefaultPosition;
            var yaw = placed ? ModConfig.BoardYaw.Value : DefaultYaw;
            _root.transform.position = pos;
            _root.transform.rotation = Quaternion.Euler(0f, yaw + 180f, 0f);
        }

        private static string StatusLine()
        {
            if (Launcher.Armed) return $"<color=#F5C542>{Launcher.Status}</color>";
            if (Launcher.Validating) return $"<color=#9A9A9A>{Launcher.Status}</color>";
            if (!ModGate.Active) return $"<color=#B04040>inert: {ModGate.Reason}</color>";
            return $"<color=#9A9A9A>{(Il2CppPhoton.Pun.PhotonNetwork.IsMasterClient ? "you are the host" : "the host loads the floor")} · rewards {(ModConfig.BankRewardsPerFloor.Value ? "banked every floor" : "on surfacing only")}</color>";
        }

        private static void Rebuild()
        {
            if (!Interop.Alive(_panel)) return;
            UiKit.DestroyChildren(_panel);
            var runs = RunStore.Recent(Rows);
            var height = 0.5f + Rows * RowHeight;
            UiKit.Backdrop(_panel, new Vector3(0f, 0f, 0.01f), PanelWidth, height, new Color(0.05f, 0.05f, 0.08f, 1f));
            var top = height * 0.5f;
            var left = -PanelWidth * 0.5f + 0.04f;
            var right = PanelWidth * 0.5f - 0.04f;

            UiKit.Text(_panel, new Vector3(left, top - 0.06f, 0f), 0.8f, 0.06f, 0.5f, $"<b>DESCENTS</b>   <color=#9A9A9A>{runs.Count} known</color>");
            if (Launcher.Armed)
                UiKit.Button(_panel, new Vector3(right - BtnW * 0.75f, top - 0.06f, 0f), "CANCEL", () => Launcher.Cancel(local: true), BtnScale * 1.5f);
            else
                UiKit.Button(_panel, new Vector3(right - BtnW * 0.75f, top - 0.06f, 0f), "NEW DESCENT", Launcher.StartNew, BtnScale * 1.5f, enabled: !Launcher.Validating);

            _status = UiKit.Text(_panel, new Vector3(left, top - 0.15f, 0f), PanelWidth - 0.08f, 0.05f, 0.3f, StatusLine());

            var y0 = top - 0.3f;
            var restartX = right - BtnW * 0.5f;
            var resumeX = restartX - BtnW - 0.04f;
            var textW = resumeX - BtnW * 0.5f - 0.03f - left;
            for (var i = 0; i < runs.Count; i++)
            {
                var run = runs[i];
                var row = new GameObject($"Run_{i}"); row.transform.SetParent(_panel, false);
                row.transform.localPosition = new Vector3(0f, y0 - i * RowHeight, 0f);
                var next = run.Floor(Math.Min(run.FloorIndex, run.LastFloorIndex));
                var state = run.Completed ? "<color=#7FD8FF>completed</color>"
                          : run.FloorIndex == 0 && run.DeepestFloor == 0 ? "<color=#9A9A9A>not started</color>"
                          : $"<color=#F5C542>floor {run.FloorIndex + 1}</color> of {run.Floors}";
                UiKit.Text(row.transform, new Vector3(left, 0.028f, 0f), textW, 0.05f, 0.38f, $"<b>{run.Name}</b>   {state}");
                UiKit.Text(row.transform, new Vector3(left, -0.03f, 0f), textW, 0.045f, 0.28f,
                    $"<color=#9A9A9A>next: {next.RealmName}, tier {next.Tier + 1}, {Ramp.DifficultyName(next.Difficulty)} · realms {DescribeOrder(run)} · seed {run.Seed}</color>");
                var captured = run;
                if (!run.IsFinished)
                    UiKit.Button(row.transform, new Vector3(resumeX, 0f, 0f), run.FloorIndex == 0 ? "ENTER" : "RESUME", () => Launcher.Resume(captured), BtnScale, enabled: !Launcher.Armed && !Launcher.Validating);
                UiKit.Button(row.transform, new Vector3(restartX, 0f, 0f), "RESTART", () => Launcher.Restart(captured), BtnScale, enabled: !Launcher.Armed && !Launcher.Validating);
            }
            if (runs.Count == 0)
                UiKit.Text(_panel, new Vector3(0f, y0 - RowHeight, 0f), 1.0f, 0.06f, 0.4f, "No descents yet. Press NEW DESCENT.", TextAlignmentOptions.Center);

            var bottom = -top + 0.07f;
            UiKit.Text(_panel, new Vector3(left, bottom, 0f), PanelWidth - 0.08f, 0.05f, 0.26f,
                $"<color=#9A9A9A>{ModConfig.FloorsPerRun.Value} floors · tier {ModConfig.StartTier.Value} rising every {ModConfig.TierRampEvery.Value} · a death keeps the floor · the exit goes down</color>");
        }

        private static string DescribeOrder(RunRecord run)
        {
            if (run.RealmOrder == null || run.RealmOrder.Count == 0) return "?";
            var parts = new List<string>();
            foreach (var r in run.RealmOrder) parts.Add(Ramp.RealmName(r).Split(' ')[0]);
            return string.Join(" > ", parts);
        }
    }
}
