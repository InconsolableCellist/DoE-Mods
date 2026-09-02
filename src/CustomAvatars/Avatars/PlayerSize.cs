using System;
using System.Collections.Generic;
using CustomAvatars.Recon;
using UnityEngine;
using Il2Cpp;
using Interop = CustomAvatars.Recon.Interop;

namespace CustomAvatars.Avatars
{
    /// <summary>
    /// Lets you be a different size: your avatar scales up or down, and you scale with it.
    ///
    /// One number, <c>AvatarSize</c>. At 1 this class does nothing at all — it holds no
    /// reference, writes no transform, and the game runs exactly as it does without the mod.
    /// Any other value is applied, uniformly, to the two scene roots that ARE you:
    /// <c>VR Controller</c> (the play space — camera, hands, colliders, first-person arms,
    /// holsters) and <c>Model_&lt;nick&gt;</c> (the body the game solves and networks).
    /// Everything else derives from those two. Tracked offsets are multiplied by the play
    /// space's scale, so your eyes drop and your reach shortens together and the world reads
    /// as bigger; the hit capsules take their size from lossyScale, so a small player is a
    /// small target; the avatar's own one-shot fit measures your head where it now is, in
    /// world space, after this scale is already on, so it needs no arithmetic of its own.
    ///
    /// Why this is not the v0.33–v0.35 feature, which was removed for "fighting the game":
    /// those runs' logs were re-read for v0.40. Every one of the 751 "the game reset the rig
    /// scale" events happened while <c>Model_&lt;nick&gt;</c> did not exist — before the first
    /// swap, and at application quit — because the drift check counted a missing body as
    /// drift. With the body alive there were zero resets in seven minutes of play, and the
    /// game has no per-frame height code (<c>OpenVRRig.CalibrateHeight</c> runs on recentre;
    /// neither it nor <c>VRControllerBase</c> has an Update). The runaway shrink was a
    /// measurement bug: eye height was read from a camera that does not sit under the scaled
    /// rig, divided by the scale, kept as a running maximum, and fed back into the scale.
    ///
    /// So the rules here are the opposite of a fight:
    /// <list type="bullet">
    /// <item>Nothing is measured. The size is the setting.</item>
    /// <item>A missing body is nothing to do, not drift.</item>
    /// <item>If the scale ever does change under us, it is re-applied and COUNTED; past a
    /// threshold the feature switches itself off for the session and says so, rather than
    /// writing every frame forever.</item>
    /// <item>Off is exactly vanilla: rest scales written back once, every reference dropped.</item>
    /// </list>
    /// </summary>
    public sealed class PlayerSize
    {
        public const float Minimum = 0.3f;
        public const float Maximum = 3f;
        public const float Step = 0.05f;

        private static PlayerSize _instance;

        /// <summary>The play-space scale in force right now; 1 whenever the feature is idle.</summary>
        public static float Applied => _instance?._applied ?? 1f;

        /// <summary>Raised after a size has been written to the rig, including back to 1.</summary>
        public event Action<float> Changed;

        private Transform _rig;
        private Vector3 _rigRest = Vector3.one;
        private string _rigPath = "-";
        private Transform _body;
        private Vector3 _bodyRest = Vector3.one;
        private Camera _camera;
        private float _baseNearClip;
        private VRPlayerControl _control;
        private float _baseMoveSpeed, _baseJumpHeight;
        private bool _haveLocomotionBase;

        private bool _engaged;
        private float _applied = 1f;
        private bool _fused;               // gave up for the session; see Trip
        private bool _dead;                // threw; complain once, then stay quiet

        // Someone else writing a scale we hold. Counted in a short window so a real fight
        // trips the fuse, while a one-off (a pose switch, a respawn) is just a log line.
        private int _tripsInWindow, _tripsTotal;
        private float _windowStart, _nextTripLogAt;
        private const int FuseTrips = 120;
        private const float FuseWindow = 5f;

        // Held props: world scale on pickup, put back on drop if the game changed it. Unity
        // preserves world scale when the game parents a weapon into the scaled hand, but a
        // v0.35 log showed the game resetting a held sword's local scale, so a dropped weapon
        // could otherwise stay shrunk on the floor.
        private readonly List<PropRoot> _propRoots = new List<PropRoot>();
        private readonly Dictionary<int, (Prop prop, Vector3 world)> _held =
            new Dictionary<int, (Prop, Vector3)>();
        private readonly HashSet<int> _scratch = new HashSet<int>();
        private float _nextPropScanAt;

        public PlayerSize() { _instance = this; }

        public bool Engaged => _engaged;
        public bool Fused => _fused;

        /// <summary>What the setting asks for, sanitised.</summary>
        public static float Wanted()
        {
            var v = ModConfig.AvatarSize.Value;
            if (!float.IsFinite(v) || v <= 0f) return 1f;
            return Mathf.Clamp(v, Minimum, Maximum);
        }

        public string Describe()
        {
            if (_fused) return "OFF for this session — the game fought for the scale (see log)";
            var wanted = Wanted();
            if (!_engaged) return Mathf.Abs(wanted - 1f) < 0.0005f ? "x1.00 (vanilla)" : $"x{wanted:0.00} — waiting for the rig";
            return $"x{_applied:0.00}" + (Mathf.Abs(wanted - _applied) > 0.0005f ? $" → x{wanted:0.00}" : "");
        }

        /// <summary>
        /// Every frame from OnUpdate, before full-body tracking and the swap read anything:
        /// this frame's tracker poses, IK targets and avatar fit must see the size settled on.
        /// </summary>
        public void Tick()
        {
            if (_dead) return;
            try
            {
                var wanted = Wanted();
                if (!_engaged)
                {
                    // The vanilla path: no reference held, nothing written, no opinion.
                    if (Mathf.Abs(wanted - 1f) < 0.0005f || _fused) return;
                    if (!Engage()) return;
                }

                if (Mathf.Abs(wanted - _applied) > 0.0005f) Apply(wanted);
                else Maintain();
                if (!_engaged) return;   // Maintain can trip the fuse

                WatchProps();

                if (Mathf.Abs(_applied - 1f) < 0.0005f) Release("back to vanilla size");
            }
            catch (Exception e)
            {
                _dead = true;
                Core.Log.Error($"Size: threw and is off for this session: {e}");
                try { Release("the size code failed"); } catch { }
            }
        }

        /// <summary>PageUp / PageDown: one step bigger or smaller, written to the settings file.</summary>
        public void Nudge(int steps)
        {
            if (_fused)
            {
                Core.Log.Warning("Size: off for this session — the game fought for the scale. Restart to try again.");
                return;
            }
            var next = Mathf.Clamp(Mathf.Round((Wanted() + steps * Step) / Step) * Step, Minimum, Maximum);
            Set(next, steps > 0 ? "PageUp" : "PageDown");
        }

        /// <summary>Home: back to vanilla. Not a suspend — the setting itself goes to 1.</summary>
        public void ResetToVanilla() => Set(1f, "Home");

        private static void Set(float size, string why)
        {
            ModConfig.AvatarSize.Value = size;
            // Written now, not on quit: F3 re-reads the file, and would otherwise put back
            // whatever was there before the keys were pressed.
            try { MelonLoader.MelonPreferences.Save(); } catch { }
            Core.Log.Msg(Mathf.Abs(size - 1f) < 0.0005f
                ? $"Size: AvatarSize 1.00 — vanilla ({why})."
                : $"Size: AvatarSize now {size:0.00} ({why}).");
        }

        public void Shutdown()
        {
            if (_engaged) { try { Release("application quitting"); } catch { } }
        }

        // ---- engage / apply / release ---------------------------------------------------

        /// <summary>
        /// Finds the rig by walking up from the game's own camera rig singleton rather than
        /// by name: <c>XRRig.Transform</c> is <c>[CameraRig]</c>, and the scene root above it
        /// is the object that owns your colliders, arms and holsters.
        /// </summary>
        private bool Engage()
        {
            Transform cameraRig = null;
            try { cameraRig = XRRig.Transform; } catch { }
            if (!Interop.Alive(cameraRig)) return false;

            var root = cameraRig;
            var guard = 0;
            while (Interop.Alive(root.parent) && guard++ < 32) root = root.parent;
            if (!Interop.Alive(root)) return false;

            _rig = root;
            _rigRest = SaneRest(root.localScale);
            _rigPath = Interop.ScenePath(root);
            _applied = 1f;
            _body = null;

            try { _camera = XRRig.Camera; } catch { _camera = null; }
            _baseNearClip = Interop.Alive(_camera) ? _camera.nearClipPlane : 0f;

            ResolveLocomotion();
            ResolvePropRoots();

            _engaged = true;
            _tripsInWindow = 0;
            _windowStart = Time.unscaledTime;

            Core.Log.Msg($"Size: holding `{_rigPath}` — rest scale {Interop.Vec(_rigRest)}, " +
                         $"camera `{Interop.Name(_camera)}` near clip {_baseNearClip:0.###} m, " +
                         $"{_propRoots.Count} hand(s) that can hold things" +
                         (_haveLocomotionBase ? ", locomotion reachable" : ", locomotion NOT reachable"));
            return true;
        }

        private void Apply(float size)
        {
            if (!Interop.Alive(_rig)) { _engaged = false; return; }

            SetRigScale(_rigRest * size);
            _applied = size;
            ApplyBody(sizeChanged: true);
            ApplyNearClip();
            ApplyLocomotion();

            Core.Log.Msg($"*** Size: x{size:0.00} — play space and body scaled" +
                         (Interop.Alive(_body) ? "" : " (no body yet; it is sized when it appears)") + ".");
            LogPlayerCapsules();

            try { Changed?.Invoke(size); }
            catch (Exception e) { Core.Log.Warning($"Size change handler threw: {e.Message}"); }
        }

        /// <summary>
        /// The steady state: the body may have been replaced (a respawn hands over a fresh
        /// one at the game's rest scale), and the rig's scale is ours unless something else
        /// wrote it — which is counted, not silently absorbed.
        /// </summary>
        private void Maintain()
        {
            if (!Interop.Alive(_rig))
            {
                // The rig object itself went away (scene teardown). Put back what is still
                // there (near clip, speeds, props), drop everything, and start over next frame
                // from a fresh rest scale — the setting is unchanged, so it re-applies.
                Release("the rig object is gone; re-resolving");
                return;
            }

            var want = _rigRest * _applied;
            if ((_rig.localScale - want).sqrMagnitude > 1e-6f)
            {
                Trip("play space", _rig.localScale);
                if (!_engaged) return;
                SetRigScale(want);
            }

            ApplyBody(sizeChanged: false);
        }

        private void Release(string why)
        {
            try { if (Interop.Alive(_rig)) SetRigScale(_rigRest); } catch { }
            try { if (Interop.Alive(_body)) _body.localScale = _bodyRest; } catch { }

            _applied = 1f;
            ApplyNearClip();
            ApplyLocomotion();
            RestoreAllProps(why);

            var path = _rigPath;
            _rig = null;
            _body = null;
            _camera = null;
            _control = null;
            _haveLocomotionBase = false;
            _propRoots.Clear();
            _rigRest = Vector3.one;
            _bodyRest = Vector3.one;
            _rigPath = "-";
            _engaged = false;

            Core.Log.Msg($"Size: x1.00 — `{path}` and your body are back at the scale the game had them ({why}).");
            try { Changed?.Invoke(1f); }
            catch (Exception e) { Core.Log.Warning($"Size change handler threw: {e.Message}"); }
        }

        private void Trip(string what, Vector3 found)
        {
            var now = Time.unscaledTime;
            if (now - _windowStart > FuseWindow) { _windowStart = now; _tripsInWindow = 0; }
            _tripsInWindow++;
            _tripsTotal++;

            if (now >= _nextTripLogAt)
            {
                _nextTripLogAt = now + 5f;
                Core.Log.Warning($"Size: something else wrote the {what} scale ({Interop.Vec(found)}); " +
                                 $"re-applied x{_applied:0.00}. {_tripsTotal} time(s) this session.");
            }

            if (_tripsInWindow >= FuseTrips)
            {
                Core.Log.Error($"*** Size: the {what} scale was rewritten {_tripsInWindow} times in " +
                               $"{FuseWindow:0} s — the game is fighting for it. Giving up for this session " +
                               "so nothing is written every frame; you are vanilla size. Please report this line.");
                _fused = true;
                Release("the game fought for the scale");
            }
        }

        // ---- the two roots ----------------------------------------------------------------

        /// <summary>Scale the rig about its origin and put your head back over where it was.</summary>
        private void SetRigScale(Vector3 scale)
        {
            // Your world position is the rig root plus your tracked offset inside the play
            // space, and scaling multiplies that offset — so resizing while standing a metre
            // off-centre would slide you through the room. Anchor on the head, horizontally:
            // your eyes moving vertically is the entire point.
            var anchor = HeadAnchor();
            var before = anchor != null ? anchor.position : Vector3.zero;

            _rig.localScale = scale;

            if (anchor == null) return;
            var drift = anchor.position - before;
            drift.y = 0f;
            if (drift.sqrMagnitude > 1e-8f) _rig.position -= drift;
        }

        /// <summary>
        /// Something tracked, under the rig. The game's own head transform first; the XR
        /// centre-eye anchor as the fallback. Not the camera: the v0.35 logs showed
        /// `FirstPersonCamera` does not move with the rig's scale, which is precisely what
        /// made its eye-height reading run away.
        /// </summary>
        private Transform HeadAnchor()
        {
            Transform t = null;
            try
            {
                var player = AvatarPlayer.LocalAvatar;
                if (Interop.Alive(player)) t = player.Head;
            }
            catch { t = null; }
            if (Interop.Alive(t) && IsUnderRig(t)) return t;

            try { t = XRRig.CenterEyeAnchor; } catch { t = null; }
            return Interop.Alive(t) && IsUnderRig(t) ? t : null;
        }

        private bool IsUnderRig(Transform t)
        {
            var guard = 0;
            for (var p = t; Interop.Alive(p) && guard++ < 32; p = p.parent)
                if (p.Pointer == _rig.Pointer) return true;
            return false;
        }

        /// <summary>
        /// `Model_&lt;nick&gt;` is its own scene root, not a child of the rig. It carries the hit
        /// capsules the game resolves damage against, its VRIK reaches for your hand targets,
        /// and it is what a peer's client animates — so it must be the same size as you.
        /// </summary>
        private void ApplyBody(bool sizeChanged)
        {
            Transform body = null;
            try
            {
                var player = AvatarPlayer.LocalAvatar;
                if (Interop.Alive(player))
                {
                    var full = player.FullBody;
                    if (Interop.Alive(full)) body = full.transform;
                }
            }
            catch { body = null; }

            // No body is nothing to do. This exact case was what the old drift check counted
            // as "the game reset the scale", 751 times.
            if (!Interop.Alive(body)) { _body = null; return; }

            var fresh = !Interop.Alive(_body) || _body.Pointer != body.Pointer;
            if (fresh)
            {
                _body = body;
                _bodyRest = SaneRest(body.localScale);
                Core.Log.Msg($"Size: body `{Interop.ScenePath(body)}` at rest scale {Interop.Vec(_bodyRest)}" +
                             (Mathf.Abs(_applied - 1f) > 0.0005f ? $", sizing it x{_applied:0.00}" : ""));
            }

            var want = _bodyRest * _applied;
            if ((body.localScale - want).sqrMagnitude <= 1e-6f) return;
            if (!fresh && !sizeChanged)
            {
                Trip("body", body.localScale);
                if (!_engaged) return;
            }
            try { body.localScale = want; } catch { }
        }

        private static Vector3 SaneRest(Vector3 v)
        {
            if (!float.IsFinite(v.x) || !float.IsFinite(v.y) || !float.IsFinite(v.z)) return Vector3.one;
            if (v.x <= 1e-4f || v.y <= 1e-4f || v.z <= 1e-4f) return Vector3.one;
            return v;
        }

        // ---- what does not scale by itself --------------------------------------------------

        private void ApplyNearClip()
        {
            if (!Interop.Alive(_camera) || _baseNearClip <= 0f) return;
            try
            {
                // Near clip is in world metres and doesn't know how big you are, so at half
                // size everything crosses it twice as close to your face — including the
                // weapon in your hand.
                _camera.nearClipPlane = Mathf.Max(0.005f, _baseNearClip * _applied);
            }
            catch { }
        }

        private void ResolveLocomotion()
        {
            _control = null;
            _haveLocomotionBase = false;
            try
            {
                _control = _rig.GetComponent<VRPlayerControl>();
                if (_control == null) _control = _rig.GetComponentInChildren<VRPlayerControl>(true);
                if (_control == null) _control = UnityEngine.Object.FindObjectOfType<VRPlayerControl>();
                if (_control == null) return;

                _baseMoveSpeed = _control.firstPersonMoveSpeed;
                _baseJumpHeight = _control.jumpHeight;
                _haveLocomotionBase = true;
            }
            catch (Exception e)
            {
                // Not fatal: the size still applies, you just move at vanilla speed.
                Core.Log.Warning($"Size: locomotion speeds not reachable ({e.GetType().Name}); " +
                                 "SizeMoveSpeedBlend will do nothing.");
                _control = null;
            }
        }

        private void ApplyLocomotion()
        {
            if (_control == null || !_haveLocomotionBase) return;
            try
            {
                // Stick movement is metres per second in WORLD units, so a half-size player
                // covers the dungeon at twice their own body's pace. Scaling it all the way
                // back makes them genuinely slower than the party, which is a different
                // problem, so this is a blend and it defaults to leaving the game alone.
                var blend = Mathf.Clamp01(ModConfig.SizeMoveSpeedBlend.Value);
                var factor = Mathf.Lerp(1f, _applied, blend);
                _control.firstPersonMoveSpeed = _baseMoveSpeed * factor;
                _control.jumpHeight = _baseJumpHeight * factor;
            }
            catch { }
        }

        private void LogPlayerCapsules()
        {
            try
            {
                foreach (var capsule in _rig.GetComponentsInChildren<CapsuleCollider>(true))
                {
                    if (capsule == null) continue;
                    var t = capsule.transform;
                    // Only the player's own capsules, which sit directly under the rig; the
                    // finger and weapon colliders below them are noise here.
                    if (!Interop.Alive(t.parent) || t.parent.Pointer != _rig.Pointer) continue;
                    var s = t.lossyScale.x;
                    Core.Log.Msg($"    hitbox `{capsule.name}`: radius {capsule.radius * s:0.000} m, " +
                                 $"height {capsule.height * s:0.000} m");
                }
            }
            catch { }
        }

        // ---- props -----------------------------------------------------------------------------

        private void ResolvePropRoots()
        {
            _propRoots.Clear();
            try
            {
                foreach (var root in _rig.GetComponentsInChildren<PropRoot>(true))
                    if (root != null) _propRoots.Add(root);
            }
            catch (Exception e) { Core.Log.Warning($"Size: hand prop roots not found: {e.Message}"); }
        }

        private void WatchProps()
        {
            if (_propRoots.Count == 0)
            {
                // The hands are built with the rig but not necessarily before we first look.
                if (Time.unscaledTime < _nextPropScanAt) return;
                _nextPropScanAt = Time.unscaledTime + 5f;
                ResolvePropRoots();
                if (_propRoots.Count == 0) return;
            }

            _scratch.Clear();
            foreach (var root in _propRoots)
            {
                if (!Interop.Alive(root)) continue;
                Prop prop = null;
                try { prop = root.prop; } catch { }
                if (!Interop.Alive(prop)) continue;

                var id = prop.GetInstanceID();
                _scratch.Add(id);
                if (_held.ContainsKey(id)) continue;
                _held[id] = (prop, prop.transform.lossyScale);
            }

            if (_held.Count == 0) return;
            List<int> dropped = null;
            foreach (var kv in _held)
                if (!_scratch.Contains(kv.Key)) (dropped ??= new List<int>()).Add(kv.Key);
            if (dropped == null) return;

            foreach (var id in dropped)
            {
                var entry = _held[id];
                _held.Remove(id);
                RestoreProp(entry.prop, entry.world, "left your hands");
            }
        }

        private void RestoreAllProps(string why)
        {
            if (_held.Count == 0) return;
            foreach (var kv in _held) RestoreProp(kv.Value.prop, kv.Value.world, why);
            _held.Clear();
        }

        private static void RestoreProp(Prop prop, Vector3 world, string why)
        {
            if (!Interop.Alive(prop)) return;
            try
            {
                var t = prop.transform;
                var now = t.lossyScale;
                if ((now - world).sqrMagnitude < 1e-6f) return;
                if (now.x <= 1e-4f || now.y <= 1e-4f || now.z <= 1e-4f) return;
                var local = t.localScale;
                var fixedLocal = new Vector3(local.x * world.x / now.x, local.y * world.y / now.y, local.z * world.z / now.z);
                Core.Log.Msg($"Size: `{Interop.Name(prop)}` {why} at world scale {Interop.Vec(now)}; " +
                             $"put back to {Interop.Vec(world)}.");
                t.localScale = fixedLocal;
            }
            catch { }
        }
    }
}
