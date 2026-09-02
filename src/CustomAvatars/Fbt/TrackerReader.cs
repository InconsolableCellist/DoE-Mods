using System;
using System.Collections.Generic;
using CustomAvatars.Recon;
using UnityEngine;
using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppValve.VR;
using Interop = CustomAvatars.Recon.Interop;

namespace CustomAvatars.Fbt
{
    /// <summary>
    /// Reads SteamVR generic trackers (hip/feet pucks) straight from OpenVR.
    ///
    /// The game itself never looks at trackers — its action manifest has no tracker poses and
    /// its input layer only knows the HMD and two controllers — but the full Valve wrapper is
    /// compiled into the build and the OpenVR session is already up, so
    /// <c>OpenVR.System.GetDeviceToAbsoluteTrackingPose</c> is free to call. Poses come back in
    /// tracking space; <c>XRRig.Transform</c> (the game's own <c>[CameraRig]</c> singleton) maps
    /// them into the world the same way the game maps its HMD.
    ///
    /// The pose array is read RAW, at the element stride il2cpp itself reports, not through
    /// the interop struct indexer. Field-tested reason: indexer reads came back as garbage —
    /// tracking results like -1164770763, positions of 2.5e9 metres — classic stride-mismatch
    /// corruption that got worse with device index, which is why the HMD looked fine while
    /// the trackers (indices 3+) exploded. The native side fills the buffer correctly; we
    /// just read the bytes where OpenVR actually put them and run Valve's own matrix→Unity
    /// conversion on them.
    ///
    /// Two copies of the Valve wrapper exist in the build: this one (<c>Il2CppValve.VR</c>,
    /// from Unity.XR.OpenVR + SteamVR) which is live, and a stale Oculus-bundled copy under
    /// <c>Il2CppOVR</c> which must not be touched — hence this being the only file with a
    /// <c>using Il2CppValve.VR</c>.
    /// </summary>
    public class TrackerReader
    {
        public class Device
        {
            public uint Index;
            public ETrackedDeviceClass Class;
            public string Serial;
            public string ControllerType;
            public bool PoseValid;
            /// <summary>Flagged valid by OpenVR but the numbers weren't numbers.</summary>
            public bool GarbagePose;
            public ETrackingResult Result;
            public Vector3 LocalPos, WorldPos;
            public Quaternion LocalRot = Quaternion.identity, WorldRot = Quaternion.identity;

            public override string ToString() =>
                $"#{Index} {Class} serial={Serial ?? "?"} type={ControllerType ?? "?"} " +
                (PoseValid ? $"world={WorldPos:F3}"
                           : $"pose INVALID ({Result}{(GarbagePose ? ", garbage values" : "")})");
        }

        /// <summary>
        /// TrackedDevicePose_t exactly as native OpenVR lays it out: 3x4 row-major matrix,
        /// two velocity vectors, a result enum, two one-byte bools. 0x4E of data, 0x50 stride.
        /// </summary>
        private const int NativePoseStrideFallback = 0x50;
        private const int OffsetResult = 0x48;
        private const int OffsetPoseIsValid = 0x4C;

        private CVRSystem _system;
        private Il2CppStructArray<TrackedDevicePose_t> _poses;
        private int _stride;
        private readonly List<Device> _trackers = new List<Device>();

        // Serials and controller types are native string reads; they can't change while a
        // device stays connected, so read them once per index and remember.
        private readonly Dictionary<uint, (string serial, string type)> _props =
            new Dictionary<uint, (string, string)>();

        /// <summary>Generic trackers only, freshest poll. Controllers/HMD are dump-only.</summary>
        public IReadOnlyList<Device> Trackers => _trackers;

        private CVRSystem System
        {
            get
            {
                if (_system != null) return _system;
                try { _system = OpenVR.System; } catch { }
                return _system;
            }
        }

        /// <summary>
        /// The true native element stride of the pose array, asked of il2cpp itself rather
        /// than assumed — the assumption (the interop indexer's) is what corrupted every read.
        /// </summary>
        private int Stride()
        {
            if (_stride > 0) return _stride;
            try
            {
                var arrayClass = IL2CPP.il2cpp_object_get_class(_poses.Pointer);
                var reported = (int)IL2CPP.il2cpp_array_element_size(arrayClass);
                // Sanity-band it: a wild value here means the API call went wrong, and the
                // known layout is a better bet than garbage.
                if (reported >= 0x4E && reported <= 0x60) _stride = reported;
            }
            catch { }
            if (_stride <= 0) _stride = NativePoseStrideFallback;
            Core.Log.Msg($"FBT: pose array element stride 0x{_stride:X} " +
                         $"({(_stride == NativePoseStrideFallback ? "matches" : "DIFFERS FROM")} the expected 0x50).");
            return _stride;
        }

        /// <summary>
        /// The tracking universe the runtime is actually using. The SteamVR plugin records it in
        /// its settings asset; if that probe fails, Standing is what a room-scale PCVR session
        /// uses in practice.
        /// </summary>
        private ETrackingUniverseOrigin Universe()
        {
            try
            {
                var settings = SteamVR_Settings.instance;
                if (Interop.Alive(settings)) return settings.trackingSpace;
            }
            catch { }
            return ETrackingUniverseOrigin.TrackingUniverseStanding;
        }

        /// <summary>
        /// The headset's height above the tracking floor, in real metres, straight from
        /// OpenVR — no Unity transform anywhere in the chain, so nothing the game or the mod
        /// does to the rig can change it. The reference the size diagnostics compare against.
        /// </summary>
        public bool TryHmdHeight(out float metres)
        {
            metres = 0f;
            var sys = System;
            if (sys == null) return false;
            try
            {
                _poses ??= new Il2CppStructArray<TrackedDevicePose_t>((int)OpenVR.k_unMaxTrackedDeviceCount);
                sys.GetDeviceToAbsoluteTrackingPose(Universe(), 0f, _poses);
                var hmd = ReadDevice(sys, null, OpenVR.k_unTrackedDeviceIndex_Hmd);
                if (!hmd.PoseValid) return false;
                metres = hmd.LocalPos.y;
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// Refresh <see cref="Trackers"/>. Returns false when OpenVR or the rig isn't up —
        /// callers treat that the same as zero trackers.
        /// </summary>
        public bool Poll()
        {
            _trackers.Clear();

            var sys = System;
            if (sys == null) return false;

            Transform rig = null;
            try { rig = XRRig.Transform; } catch { }

            try
            {
                _poses ??= new Il2CppStructArray<TrackedDevicePose_t>((int)OpenVR.k_unMaxTrackedDeviceCount);
                sys.GetDeviceToAbsoluteTrackingPose(Universe(), 0f, _poses);

                for (uint i = 0; i < OpenVR.k_unMaxTrackedDeviceCount; i++)
                {
                    if (sys.GetTrackedDeviceClass(i) != ETrackedDeviceClass.GenericTracker) continue;
                    _trackers.Add(ReadDevice(sys, rig, i));
                }
                // No rig yet (menus, headset waking): devices are listed but their world poses
                // are meaningless, so callers treat this poll as not-usable.
                return Interop.Alive(rig);
            }
            catch (Exception e)
            {
                // A dead CVRSystem proxy (headset restart) throws here; drop it so the next
                // poll re-fetches a live one instead of failing forever.
                _system = null;
                Core.Log.Warning($"Tracker poll failed: {e.GetType().Name}: {e.Message}");
                return false;
            }
        }

        private unsafe Device ReadDevice(CVRSystem sys, Transform rig, uint i)
        {
            var device = new Device { Index = i, Class = sys.GetTrackedDeviceClass(i) };

            if (!_props.TryGetValue(i, out var props))
            {
                props = (StringProp(sys, i, ETrackedDeviceProperty.Prop_SerialNumber_String),
                         StringProp(sys, i, ETrackedDeviceProperty.Prop_ControllerType_String));
                if (props.serial != null) _props[i] = props;
            }
            device.Serial = props.serial;
            device.ControllerType = props.type;

            // Raw bytes at il2cpp's element stride. The 64-bit il2cpp array header is four
            // pointers (klass, monitor, bounds, length); element 0 starts right after it.
            var element = (byte*)_poses.Pointer + 4 * IntPtr.Size + (int)i * Stride();
            var m = (float*)element;

            device.Result = (ETrackingResult)(*(int*)(element + OffsetResult));
            device.PoseValid = *(element + OffsetPoseIsValid) != 0;
            if (!device.PoseValid) return device;

            // Valve's own HmdMatrix34_t→Unity conversion, inlined: right-handed row-major
            // 3x4 to left-handed position and quaternion (the z-flip is baked into the signs).
            device.LocalPos = new Vector3(m[3], m[7], -m[11]);
            device.LocalRot = RotationOf(m);

            // "Valid" from OpenVR is not a promise the numbers are numbers, and a NaN written
            // into a bone outlives its cause. Nothing non-finite leaves this class as valid.
            if (!FbtMath.Finite(device.LocalPos) || !FbtMath.Finite(device.LocalRot))
            {
                device.PoseValid = false;
                device.GarbagePose = true;
                return device;
            }

            if (Interop.Alive(rig))
            {
                device.WorldPos = rig.TransformPoint(device.LocalPos);
                device.WorldRot = rig.rotation * device.LocalRot;
            }
            else
            {
                device.WorldPos = device.LocalPos;
                device.WorldRot = device.LocalRot;
            }
            return device;
        }

        private static unsafe Quaternion RotationOf(float* m)
        {
            // Degenerate rotation part (a zeroed pose): identity, like Valve's IsRotationValid.
            if (m[2] == 0f && m[6] == 0f && m[10] == 0f) return Quaternion.identity;

            var w = Mathf.Sqrt(Mathf.Max(0f, 1f + m[0] + m[5] + m[10])) / 2f;
            var x = Mathf.Sqrt(Mathf.Max(0f, 1f + m[0] - m[5] - m[10])) / 2f;
            var y = Mathf.Sqrt(Mathf.Max(0f, 1f - m[0] + m[5] - m[10])) / 2f;
            var z = Mathf.Sqrt(Mathf.Max(0f, 1f - m[0] - m[5] + m[10])) / 2f;
            x = CopySign(x, -(m[9] - m[6]));
            y = CopySign(y, -(m[2] - m[8]));
            z = CopySign(z, m[4] - m[1]);
            return new Quaternion(x, y, z, w);
        }

        private static float CopySign(float size, float sign) =>
            sign > 0f == size > 0f ? size : -size;

        private static string StringProp(CVRSystem sys, uint index, ETrackedDeviceProperty prop)
        {
            try
            {
                var error = ETrackedPropertyError.TrackedProp_Success;
                var buffer = new Il2CppSystem.Text.StringBuilder(64);
                var needed = sys.GetStringTrackedDeviceProperty(index, prop, buffer, 64, ref error);

                if (error == ETrackedPropertyError.TrackedProp_BufferTooSmall && needed > 0)
                {
                    buffer = new Il2CppSystem.Text.StringBuilder((int)needed);
                    sys.GetStringTrackedDeviceProperty(index, prop, buffer, needed, ref error);
                }

                return error == ETrackedPropertyError.TrackedProp_Success ? buffer.ToString() : null;
            }
            catch { return null; }
        }

        /// <summary>
        /// The end-to-end proof that our reads and our space conversion are sane: the
        /// controllers travel the exact same path as the trackers, and the game separately
        /// knows where the hands are. Centimetres apart = trustworthy. Anything more and FBT
        /// must refuse to engage, because the trackers would be exactly as wrong.
        /// Returns a printable line; <paramref name="worstMiss"/> is -1 when nothing compared.
        /// </summary>
        public string SpaceCheck(out float worstMiss)
        {
            worstMiss = -1f;
            var sys = System;
            if (sys == null) return "space check: OpenVR not reachable";

            Transform rig = null;
            try { rig = XRRig.Transform; } catch { }
            if (!Interop.Alive(rig)) return "space check: no XR rig yet";

            AvatarPlayer player = null;
            try { player = AvatarPlayer.LocalAvatar; } catch { }
            if (!Interop.Alive(player)) return "space check: no local player yet";

            try
            {
                var parts = new List<string>();
                for (uint i = 0; i < OpenVR.k_unMaxTrackedDeviceCount; i++)
                {
                    if (sys.GetTrackedDeviceClass(i) != ETrackedDeviceClass.Controller) continue;
                    var device = ReadDevice(sys, rig, i);
                    if (!device.PoseValid) continue;

                    var role = sys.GetControllerRoleForTrackedDeviceIndex(i);
                    Transform gameHand = null;
                    if (role == ETrackedControllerRole.LeftHand) gameHand = player.LeftHand;
                    else if (role == ETrackedControllerRole.RightHand) gameHand = player.RightHand;
                    if (!Interop.Alive(gameHand)) continue;

                    var miss = Vector3.Distance(device.WorldPos, gameHand.position);
                    worstMiss = Mathf.Max(worstMiss, miss);
                    parts.Add($"{(role == ETrackedControllerRole.LeftHand ? "L" : "R")} {miss * 100f:0.0}cm");
                }
                return parts.Count == 0 ? "space check: no controllers to compare"
                                        : $"space check (controller vs game hand): {string.Join(", ", parts)}";
            }
            catch (Exception e) { return $"space check failed: {e.Message}"; }
        }

        /// <summary>
        /// One recon pass over everything full-body tracking depends on: which trackers exist
        /// and where they are, whether the read path and space conversion agree with the
        /// game's own hands, and what state the local VRIK and grounder are in before we ever
        /// touch them. Run it standing somewhere interesting, then grep the transcript.
        /// </summary>
        public void DumpNow()
        {
            ReconLog.Section("SteamVR trackers (FBT recon)");

            var sys = System;
            ReconLog.KeyValue("OpenVR.System", sys == null ? "null — OpenVR not reachable" : "ok");
            if (sys == null) return;

            ReconLog.TryKeyValue("tracking universe", () => Universe());
            ReconLog.TryKeyValue("XRRig.Transform", () =>
            {
                var rig = XRRig.Transform;
                return Interop.Alive(rig) ? $"{rig.name} pos={rig.position:F3} rot={rig.rotation.eulerAngles:F1}" : "null";
            });

            var polled = Poll();
            ReconLog.KeyValue("poll", polled ? "ok" : "FAILED — see console");
            ReconLog.KeyValue("pose stride", $"0x{Stride():X}");

            // Every tracked device, not just trackers: the HMD row sanity-checks the space
            // conversion even with zero pucks on, and a mislabeled device shows up here.
            ReconLog.Line();
            ReconLog.Line("All tracked devices:");
            Transform rigT = null;
            try { rigT = XRRig.Transform; } catch { }
            try
            {
                for (uint i = 0; i < OpenVR.k_unMaxTrackedDeviceCount; i++)
                {
                    var cls = sys.GetTrackedDeviceClass(i);
                    if (cls == ETrackedDeviceClass.Invalid) continue;
                    var device = ReadDevice(sys, rigT, i);
                    var role = cls == ETrackedDeviceClass.Controller
                        ? $" role={sys.GetControllerRoleForTrackedDeviceIndex(i)}" : "";
                    ReconLog.Line($"- {device}{role}");
                }
            }
            catch (Exception e) { ReconLog.Error("device enumeration", e); }

            ReconLog.Line();
            ReconLog.Line($"- {SpaceCheck(out _)}");

            DumpIkState();

            var summary = $"FBT recon: {_trackers.Count} generic tracker(s)";
            foreach (var t in _trackers) summary += $"\n    {t}";
            Core.Log.Msg(summary + $"\n    full dump: {ReconLog.CurrentFile}");
        }

        /// <summary>
        /// What FBT wires into, exactly as the game left it. Captured before we ever modify
        /// anything so "what did it look like untouched" is always in a transcript.
        /// </summary>
        private void DumpIkState()
        {
            ReconLog.Line();
            ReconLog.Try("local VRIK / grounder state", () =>
            {
                var player = AvatarPlayer.LocalAvatar;
                if (!Interop.Alive(player)) { ReconLog.Line("- no local AvatarPlayer"); return; }

                var fullBody = player.FullBody;
                if (!Interop.Alive(fullBody)) { ReconLog.Line("- FullBody is null"); return; }

                ReconLog.TryKeyValue("fullBody.ikEnabled", () => fullBody.ikEnabled);

                var ik = fullBody.ik;
                if (!Interop.Alive(ik)) { ReconLog.Line("- fullBody.ik is null"); return; }

                ReconLog.TryKeyValue("VRIK.enabled", () => ik.enabled);
                var solver = ik.solver;
                ReconLog.TryKeyValue("solver.LOD", () => solver.LOD);
                ReconLog.TryKeyValue("solver.plantFeet", () => solver.plantFeet);
                ReconLog.TryKeyValue("solver.locomotion.weight", () => solver.locomotion.weight);
                ReconLog.TryKeyValue("spine.headTarget", () => Name(solver.spine.headTarget));
                ReconLog.TryKeyValue("spine.pelvisTarget", () => Name(solver.spine.pelvisTarget));
                ReconLog.TryKeyValue("spine.pelvisPositionWeight", () => solver.spine.pelvisPositionWeight);
                ReconLog.TryKeyValue("leftLeg.target", () => Name(solver.leftLeg.target));
                ReconLog.TryKeyValue("rightLeg.target", () => Name(solver.rightLeg.target));
                ReconLog.TryKeyValue("leftLeg.positionWeight", () => solver.leftLeg.positionWeight);
                ReconLog.TryKeyValue("grounderPrefab", () => Name(fullBody.grounderPrefab));

                ReconLog.Try("scene GrounderIK instances", () =>
                {
                    var grounders = UnityEngine.Object.FindObjectsOfType<Il2CppRootMotion.FinalIK.GrounderIK>();
                    if (grounders == null || grounders.Length == 0) { ReconLog.Line("- no GrounderIK in scene"); return; }
                    foreach (var g in grounders)
                        ReconLog.Line($"- GrounderIK '{g.gameObject.name}' weight={g.weight:0.00} legs={g.legs?.Length ?? 0}");
                });
            });
        }

        private static string Name(UnityEngine.Object o) => Interop.Alive(o) ? o.name : "null";
    }
}
