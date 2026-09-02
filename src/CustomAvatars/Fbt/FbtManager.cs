using System;
using System.Collections.Generic;
using CustomAvatars.Avatars;
using CustomAvatars.Gate;
using UnityEngine;
using Il2Cpp;
using Interop = CustomAvatars.Recon.Interop;

namespace CustomAvatars.Fbt
{
    /// <summary>
    /// Full-body tracking, assembled: <see cref="TrackerReader"/> supplies puck poses,
    /// <see cref="FbtCalibrator"/> turns a T-pose and a squeeze into per-tracker offsets,
    /// <see cref="FbtRig"/> feeds the game's own VRIK, <see cref="TrackerVisuals"/> shows the
    /// pucks while binding, and <see cref="TrackerSync"/> carries the result to peers.
    ///
    /// Owned by <see cref="Core"/>, not <see cref="AvatarSwapper"/>, on purpose: what gets
    /// wired is the GAME's rig, which outlives any avatar swap — so one watchdog here handles
    /// the avatar coming on and off, scene changes replacing the player, and death, without
    /// threading FBT teardown through the swapper's already-load-bearing Revert.
    ///
    /// Frame order: <c>Update</c> runs from Core.OnUpdate — before every MonoBehaviour
    /// LateUpdate, so target transforms are in place when FinalIK solves this frame.
    /// <c>LateTick</c> runs from Core.OnLateUpdate, after the solve, and streams the result.
    /// </summary>
    public class FbtManager
    {
        private const float RespawnSettleSeconds = 0.5f;

        private readonly AvatarSwapManager _swaps;
        private readonly TrackerReader _reader;
        private readonly FbtCalibrator _calibrator = new FbtCalibrator();
        private readonly TrackerVisuals _visuals = new TrackerVisuals();
        private readonly TrackerSync _sync;

        // Hip, LeftFoot, RightFoot — indexed by (int)TrackerRole.
        private static readonly TrackerRole[] Roles =
            { TrackerRole.Hip, TrackerRole.LeftFoot, TrackerRole.RightFoot };

        private List<CalibratedTracker> _calibration;
        private float _bodyScale = 1f;
        private float _calibratedAtSize = 1f;   // PlayerSize when the offsets were captured
        private readonly Dictionary<string, TrackerRole> _roleOf = new Dictionary<string, TrackerRole>();

        // ---- local rig ----
        private readonly FbtRig _localRig = new FbtRig();
        private readonly GameObject[] _proxies = new GameObject[3];   // follow the raw trackers
        private readonly Transform[] _targets = new Transform[3];     // children carrying the offsets
        private bool _wasAlive = true;
        private float _wireAllowedAt;
        private float _flashUntil;

        // Tracker blips, aggregated. Logging each transition put two lines in the log per
        // second for a whole session; one summarising line every few seconds carries the same
        // information, plus the WHY (tracking result / garbage values / absent), which is what
        // actually distinguishes an occluded puck from a broken read path.
        private int _blipCount;
        private string _blipDetail = "";
        private float _blipNextLogAt;

        // ---- remote rigs ----
        private class RemoteRig
        {
            public readonly FbtRig Rig = new FbtRig();
            public readonly Transform[] Targets = new Transform[3];
            public readonly Vector3[] SmoothedPos = new Vector3[3];
            public readonly Quaternion[] SmoothedRot = new Quaternion[3];
            public bool Smoothing;
        }
        private readonly Dictionary<int, RemoteRig> _remoteRigs = new Dictionary<int, RemoteRig>();

        public bool Enabled { get; private set; }

        public FbtManager(AvatarSwapManager swaps, ModRoster roster, TrackerReader reader)
        {
            _swaps = swaps;
            _reader = reader;
            _sync = new TrackerSync(roster);
            _calibrator.Locked += OnCalibrationLocked;
            ModGate.ActiveChanged += active => { if (!active) RestoreRemotes("gate closed"); };

            // Calibrated last session and left it on: come back up without a keypress. The
            // trackers may not exist yet this early; Update keeps trying quietly.
            Enabled = ModConfig.FbtEnabled.Value;
            if (Enabled) Core.Log.Msg("FBT: enabled from settings — will engage once trackers and body are up.");
        }

        // ---- controls (F10 / F11) ---------------------------------------------------------

        public void Toggle()
        {
            if (Enabled)
            {
                Enabled = false;
                SavePreference();
                _calibrator.Cancel("FBT switched off");
                _visuals.Hide();
                if (_localRig.Wired) _localRig.Restore("F10 off");
                Core.Log.Msg("*** FBT off.");
                FbtAudio.Off();
                return;
            }

            _reader.Poll();
            var valid = CountValidTrackers();
            if (valid < 3)
            {
                Core.Log.Warning($"*** FBT needs 3 trackers with valid poses; SteamVR shows {valid}. " +
                                 "Full recon dump follows — check tracker power and visibility.");
                FbtAudio.Error();
                _reader.DumpNow();
                return;
            }

            // The controllers ride the exact same read-and-convert path as the trackers, and
            // the game independently knows where the hands are — so this one comparison
            // vouches for every tracker pose. If it's off, engaging FBT would wire that error
            // straight into the skeleton; refuse and put the evidence in the log instead.
            var check = _reader.SpaceCheck(out var worstMiss);
            Core.Log.Msg($"FBT {check}");
            if (worstMiss > 0.5f * Avatars.PlayerSize.Applied)
            {
                Core.Log.Warning("*** FBT: the tracker read path disagrees with the game by " +
                                 $"{worstMiss * 100f:0.0} cm — refusing to engage. Full dump follows.");
                FbtAudio.Error();
                _reader.DumpNow();
                return;
            }

            Enabled = true;
            SavePreference();
            if (!_swaps.SelfActive)
                Core.Log.Msg("FBT: on — engages when your avatar is (F4).");

            _calibration ??= FbtCalibrator.TryLoadPersisted(_reader, out _bodyScale, out _calibratedAtSize);
            if (_calibration != null)
            {
                RebuildRoleMap();
                _flashUntil = Time.unscaledTime + 2f;   // show the pucks briefly: bound and where
                Core.Log.Msg($"*** FBT on — calibration restored for {_calibration.Count} tracker(s). " +
                             "T-pose any time to recalibrate.");
                FbtAudio.On();
            }
            else
            {
                _calibrator.Arm("F10 with no stored calibration");
            }
        }

        public void StartCalibration()
        {
            if (_calibrator.State == FbtCalibrator.CalState.Armed)
            {
                _calibrator.Cancel("F11 again");
                return;
            }
            if (!Enabled)
            {
                Toggle();
                // Toggle may have armed already (no stored calibration) or refused (no trackers).
                if (!Enabled || _calibrator.State == FbtCalibrator.CalState.Armed) return;
            }
            _calibrator.Arm("F11");
        }

        private void SavePreference()
        {
            ModConfig.FbtEnabled.Value = Enabled;
            try { MelonLoader.MelonPreferences.Save(); } catch { }
        }

        // ---- per-frame, before the solvers -------------------------------------------------

        public void Update(float dt)
        {
            var calibrating = _calibrator.State == FbtCalibrator.CalState.Armed;
            var haveRemote = _sync.Remote.Count > 0;
            if (!Enabled && !calibrating && !haveRemote) { HideVisuals(); return; }

            if (Enabled || calibrating) _reader.Poll();

            _calibrator.Tick(_reader, dt, tposeEntryAllowed: Enabled && _swaps.SelfActive);

            UpdateVisuals();
            UpdateLocal();
            UpdateRemotes(dt);
        }

        private void UpdateVisuals()
        {
            var wanted = _calibrator.State == FbtCalibrator.CalState.Armed
                         || (Enabled && Time.unscaledTime < _flashUntil);
            if (wanted)
            {
                _visuals.Show();
                _visuals.Update(_reader, _roleOf, PuckLayer());
            }
            else HideVisuals();
        }

        private void HideVisuals()
        {
            if (_visuals.Shown) _visuals.Hide();
        }

        private void UpdateLocal()
        {
            // While calibration is armed, the rig must be back in the game's hands: capturing
            // offsets against a rig that is still solving toward the PREVIOUS calibration's
            // targets bakes the old error into the new one (field-tested: a recalibration's
            // offsets jumped from 9 cm to 29 cm because the legs were standing on the old
            // targets when measured). Idle rig in, clean capture out.
            if (_calibrator.State == FbtCalibrator.CalState.Armed)
            {
                if (_localRig.Wired) _localRig.Restore("calibrating against the idle rig");
                return;
            }

            if (!Enabled || _calibration == null || !_swaps.SelfActive)
            {
                if (_localRig.Wired) _localRig.Restore(!Enabled ? "FBT off" :
                    _calibration == null ? "awaiting calibration" : "avatar off");
                return;
            }

            AvatarPlayer player = null;
            try { player = AvatarPlayer.LocalAvatar; } catch { }
            if (!Interop.Alive(player))
            {
                if (_localRig.Wired) _localRig.Restore("no local player");
                return;
            }

            // Death: hand the body back for the ragdoll, take it again shortly after respawn
            // — shortly, because the swapper rebuilds its pose reference on the respawn frame
            // and the rig deserves one settled frame before targets start pulling on it.
            var alive = true;
            try { alive = player.IsAlive; } catch { }
            if (alive != _wasAlive)
            {
                _wasAlive = alive;
                if (!alive && _localRig.Wired) _localRig.Restore("died — the ragdoll owns the body");
                if (alive) _wireAllowedAt = Time.unscaledTime + RespawnSettleSeconds;
            }
            if (!alive) return;

            // Scene change replaced the player object out from under us: drop the dead
            // references and rewire against the new body. Offsets persist, so it's silent.
            if (_localRig.Wired && !_localRig.Alive) _localRig.Restore("player object replaced");

            if (!_localRig.Wired)
            {
                if (Time.unscaledTime < _wireAllowedAt) return;
                CharacterPrefab fullBody = null;
                try { fullBody = player.FullBody; } catch { }
                if (!Interop.Alive(fullBody)) return;   // body not built yet; next frame

                EnsureProxies();
                if (!_localRig.Wire(fullBody, isLocal: true, _targets[0], _targets[1], _targets[2], _bodyScale))
                    return;
            }

            DriveProxies();
            _localRig.AssertPerFrame();

            if (ModConfig.FbtDebug.Value)
                Core.Log.Msg($"FBT: hip={_targets[0].position:F3} L={_targets[1].position:F3} R={_targets[2].position:F3}");
        }

        /// <summary>Move each proxy onto its tracker; the offset children become the targets.</summary>
        private void DriveProxies()
        {
            foreach (var c in _calibration)
            {
                TrackerReader.Device device = null;
                foreach (var t in _reader.Trackers)
                    if (t.Serial == c.Serial) { device = t; break; }

                // Occluded or dropped: the proxy keeps its last pose, which reads as a frozen
                // foot rather than a leg snapping to origin.
                if (device == null)
                {
                    _blipCount++;
                    _blipDetail = $"{c.Role} {c.Serial}: absent from the poll";
                    continue;
                }
                if (!device.PoseValid)
                {
                    _blipCount++;
                    _blipDetail = $"{c.Role} {c.Serial}: {device.Result}" +
                                  (device.GarbagePose ? " with non-finite values (read path suspect)" : "");
                    continue;
                }

                var proxy = _proxies[(int)c.Role];
                if (Interop.Alive(proxy))
                {
                    proxy.transform.SetPositionAndRotation(device.WorldPos, device.WorldRot);
                    // The tracker→bone offset under this proxy is world metres captured at
                    // one size; at another size the same strap sits proportionally closer.
                    var ratio = Avatars.PlayerSize.Applied / Mathf.Max(0.05f, _calibratedAtSize);
                    if (Mathf.Abs(proxy.transform.localScale.x - ratio) > 1e-4f)
                        proxy.transform.localScale = Vector3.one * ratio;
                }
            }

            if (_blipCount > 0 && Time.unscaledTime >= _blipNextLogAt)
            {
                Core.Log.Warning($"FBT: {_blipCount} tracker blip(s) in the last few seconds — " +
                                 $"holding last poses through them. Latest: {_blipDetail}");
                _blipNextLogAt = Time.unscaledTime + 5f;
                _blipCount = 0;
            }
        }

        private void EnsureProxies()
        {
            for (var i = 0; i < 3; i++)
            {
                if (Interop.Alive(_proxies[i])) continue;
                var proxy = new GameObject($"DFM_Tracker_{Roles[i]}");
                UnityEngine.Object.DontDestroyOnLoad(proxy);
                var target = new GameObject($"DFM_TrackerTarget_{Roles[i]}");
                target.transform.SetParent(proxy.transform, false);
                _proxies[i] = proxy;
                _targets[i] = target.transform;
            }
            ApplyOffsets();
        }

        /// <summary>Bake the calibration into the target children's local pose.</summary>
        private void ApplyOffsets()
        {
            if (_calibration == null) return;
            foreach (var c in _calibration)
            {
                var target = _targets[(int)c.Role];
                if (!Interop.Alive(target)) continue;
                target.localPosition = c.OffsetPos;
                target.localRotation = c.OffsetRot;
            }
        }

        private void OnCalibrationLocked(List<CalibratedTracker> calibrated)
        {
            _calibration = calibrated;
            _bodyScale = _calibrator.LastBodyScale;
            _calibratedAtSize = _calibrator.LastCalibratedAtSize;
            RebuildRoleMap();
            EnsureProxies();          // creates or re-offsets, whichever applies
            HideVisuals();            // "pucks disappear" is the lock-in confirmation
            if (!Enabled) { Enabled = true; SavePreference(); }
            // A live rig keeps stale offsets until rewired; cheapest correct move is rewire.
            if (_localRig.Wired) _localRig.Restore("recalibrated");
        }

        private void RebuildRoleMap()
        {
            _roleOf.Clear();
            if (_calibration == null) return;
            foreach (var c in _calibration) _roleOf[c.Serial] = c.Role;
        }

        private int PuckLayer()
        {
            try
            {
                var player = AvatarPlayer.LocalAvatar;
                if (Interop.Alive(player))
                {
                    var fullBody = player.FullBody;
                    if (Interop.Alive(fullBody)) return fullBody.gameObject.layer;
                }
            }
            catch { }
            return 0;
        }

        private int CountValidTrackers()
        {
            var n = 0;
            foreach (var t in _reader.Trackers) if (t.PoseValid) n++;
            return n;
        }

        // ---- remote rigs -------------------------------------------------------------------

        private void UpdateRemotes(float dt)
        {
            if (_sync.Remote.Count == 0) return;

            List<int> drop = null;
            foreach (var kv in _sync.Remote)
            {
                var actor = kv.Key;
                var fresh = Time.unscaledTime - kv.Value.ArrivedAt < Mathf.Max(0.2f, ModConfig.FbtStaleSeconds.Value);

                var player = fresh ? AvatarSwapManager.FindPlayer(actor) : null;
                var alive = false;
                if (Interop.Alive(player)) { try { alive = player.IsAlive; } catch { } }

                if (!fresh || !alive)
                {
                    // Stream stopped, they left, or they're a ragdoll right now: give the legs
                    // back to the game's own animation rather than freezing mid-stride.
                    if (_remoteRigs.TryGetValue(actor, out var idle))
                    {
                        ReleaseRemote(actor, idle, fresh ? "player gone or dead" : "stream went quiet");
                        (drop ??= new List<int>()).Add(actor);
                    }
                    if (!fresh && !Interop.Alive(player)) _sync.Forget(actor);
                    continue;
                }

                if (!_remoteRigs.TryGetValue(actor, out var remote))
                    _remoteRigs[actor] = remote = new RemoteRig();

                if (remote.Rig.Wired && !remote.Rig.Alive) remote.Rig.Restore("peer's player object replaced");
                if (!remote.Rig.Wired)
                {
                    CharacterPrefab fullBody = null;
                    try { fullBody = player.FullBody; } catch { }
                    if (!Interop.Alive(fullBody)) continue;

                    for (var i = 0; i < 3; i++)
                    {
                        if (Interop.Alive(remote.Targets[i])) continue;
                        var target = new GameObject($"DFM_TrackerTarget_A{actor}_{Roles[i]}");
                        UnityEngine.Object.DontDestroyOnLoad(target);
                        remote.Targets[i] = target.transform;
                    }
                    remote.Smoothing = false;
                    if (!remote.Rig.Wire(fullBody, isLocal: false,
                                         remote.Targets[0], remote.Targets[1], remote.Targets[2]))
                        continue;
                }

                ApplyRemotePoses(remote, player, kv.Value.Poses, dt);
                remote.Rig.AssertPerFrame();
            }

            if (drop != null) foreach (var actor in drop) _remoteRigs.Remove(actor);
        }

        /// <summary>
        /// Stream poses are relative to the sender's play-space root; the receiving copy of
        /// that root is the same vanilla-synced transform, so composing through it lands the
        /// targets on their body wherever our client has smoothed it to. Codebase convention
        /// for streams: latest value wins, exponential smoothing at apply time.
        /// </summary>
        private void ApplyRemotePoses(RemoteRig remote, AvatarPlayer player, TrackerSync.PoseSet poses, float dt)
        {
            Transform root = null;
            try { root = player.transform; } catch { }
            if (!Interop.Alive(root)) return;

            var worldPos = new[]
            {
                root.TransformPoint(poses.HipPos),
                root.TransformPoint(poses.LeftPos),
                root.TransformPoint(poses.RightPos),
            };
            var worldRot = new[]
            {
                root.rotation * poses.HipRot,
                root.rotation * poses.LeftRot,
                root.rotation * poses.RightRot,
            };

            var k = 1f - Mathf.Exp(-Mathf.Max(0.01f, ModConfig.FbtRemoteSmoothing.Value) * dt * 60f);
            for (var i = 0; i < 3; i++)
            {
                if (!Interop.Alive(remote.Targets[i])) continue;
                if (!remote.Smoothing)
                {
                    remote.SmoothedPos[i] = worldPos[i];
                    remote.SmoothedRot[i] = worldRot[i];
                }
                else
                {
                    remote.SmoothedPos[i] = Vector3.Lerp(remote.SmoothedPos[i], worldPos[i], k);
                    remote.SmoothedRot[i] = Quaternion.Slerp(remote.SmoothedRot[i], worldRot[i], k);
                }
                remote.Targets[i].SetPositionAndRotation(remote.SmoothedPos[i], remote.SmoothedRot[i]);
            }
            remote.Smoothing = true;
        }

        private void ReleaseRemote(int actor, RemoteRig remote, string why)
        {
            remote.Rig.Restore($"actor {actor}: {why}");
            foreach (var t in remote.Targets)
                if (Interop.Alive(t)) UnityEngine.Object.Destroy(t.gameObject);
        }

        private void RestoreRemotes(string why)
        {
            foreach (var kv in _remoteRigs) ReleaseRemote(kv.Key, kv.Value, why);
            _remoteRigs.Clear();
        }

        // ---- per-frame, after the solvers --------------------------------------------------

        public void LateTick(float unscaledTime)
        {
            TrackerSync.PoseSet? local = null;
            if (Enabled && _localRig.Wired)
            {
                try
                {
                    var player = AvatarPlayer.LocalAvatar;
                    if (Interop.Alive(player))
                    {
                        var root = player.transform;
                        var inverse = Quaternion.Inverse(root.rotation);
                        local = new TrackerSync.PoseSet
                        {
                            HipPos = root.InverseTransformPoint(_targets[0].position),
                            HipRot = inverse * _targets[0].rotation,
                            LeftPos = root.InverseTransformPoint(_targets[1].position),
                            LeftRot = inverse * _targets[1].rotation,
                            RightPos = root.InverseTransformPoint(_targets[2].position),
                            RightRot = inverse * _targets[2].rotation,
                        };
                    }
                }
                catch { }
            }
            _sync.Tick(unscaledTime, local);
        }

        // ---- lifecycle ---------------------------------------------------------------------

        /// <summary>
        /// One line for the overlay. Always leads with an unambiguous ON or OFF — "on,
        /// waiting" and "off" read identically at a glance, and a glance is all the desktop
        /// panel gets.
        /// </summary>
        public string Describe()
        {
            if (_calibrator.State == FbtCalibrator.CalState.Armed)
                return "ON — CALIBRATING: T-pose, then squeeze both triggers";
            if (!Enabled) return "OFF — press F10 to turn on";
            if (_calibration == null) return "ON — not calibrated yet: press F11 or hold a T-pose";
            if (!_localRig.Wired) return "ON — waiting for your body (wear the avatar, F4)";
            var peers = _remoteRigs.Count > 0 ? $" + {_remoteRigs.Count} peer(s)" : "";
            return $"ON — tracking hip + feet{peers}";
        }

        public void Shutdown()
        {
            if (_localRig.Wired) _localRig.Restore("application quitting");
            RestoreRemotes("application quitting");
            _visuals.DestroyAll();
            foreach (var proxy in _proxies)
                if (Interop.Alive(proxy)) UnityEngine.Object.Destroy(proxy);
        }
    }
}
