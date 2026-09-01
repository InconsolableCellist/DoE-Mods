using System;
using System.Collections.Generic;
using CustomAvatars.Recon;
using UnityEngine;
using Il2Cpp;
using Interop = CustomAvatars.Recon.Interop;

namespace CustomAvatars.Avatars
{
    /// <summary>
    /// Makes you the size of your avatar.
    ///
    /// Everything until now has resized the MODEL to fit the player: the swap measures how far
    /// your head is off the floor and stretches the avatar to reach it, so a 1.2 m character
    /// ends up a 1.75 m character wearing their face. This does the opposite — it resizes the
    /// PLAYER, so a small avatar is genuinely small and the world is genuinely bigger.
    ///
    /// One transform does all of it. `VR Controller` is a scene root and the parent of every
    /// piece of you the game owns:
    ///
    /// <code>
    /// VR Controller
    ///   Head Collider, Body Collider     the two capsules you are hit through
    ///   Fist Left, Fist Right            what your empty hands hit with
    ///   FPS-Arms-Model                   the first-person arms
    ///   Holsters/…                       and every weapon parked in them
    ///   OpenVR Rig (SteamVR)(Clone)/[CameraRig]/{Camera, Controller (left|right)}
    /// </code>
    ///
    /// A uniform scale there is the standard Unity world-scale trick: the tracked local offsets
    /// of the HMD and controllers are multiplied by it, so your eye height, your stereo
    /// separation and your reach all shrink together and the world reads as bigger rather than
    /// as a camera that dropped. Colliders come along because Unity derives capsule dimensions
    /// from lossyScale — which is the fairness answer to "isn't a small player unhittable":
    /// they ARE a smaller target, and that is the deal. The game agrees with itself about this:
    /// hits on you are resolved locally, by `VRPlayerDamage.ProcessMuscleCollision` and friends,
    /// against these very capsules. Nothing elsewhere gets a second opinion.
    ///
    /// SteamVR trackers follow for free — <see cref="Fbt.TrackerReader"/> maps poses with
    /// <c>rig.TransformPoint</c>, which carries scale — and so does the avatar, because the
    /// swap's own height calibration measures where your head ended up.
    ///
    /// What does NOT follow, and is handled here: the camera's near clip plane, and locomotion
    /// speed, which stays in metres per second no matter how big you are.
    /// </summary>
    public class PlayerScaler
    {
        private readonly AvatarSwapManager _swaps;
        private readonly AvatarLibrary _library;

        private Transform _rig;
        private Vector3 _rigRestScale = Vector3.one;
        private string _rigPath = "-";

        private Camera _camera;
        private float _baseNearClip;

        private VRPlayerControl _control;
        private float _baseMoveSpeed, _baseJumpHeight;
        private bool _haveLocomotionBase;

        private float _applied = 1f;
        private float _eyeHeight;          // your real eye height in metres, scale divided out
        private float _nextDriftLogAt;
        private float _nextPropRootScanAt;
        private int _driftTrips;
        private bool _dead;                // resolution threw; complain once, then stay quiet

        /// <summary>Props that were in our hands, and the local scale they had when we took them.</summary>
        private readonly Dictionary<int, (Prop prop, Vector3 scale)> _held =
            new Dictionary<int, (Prop, Vector3)>();
        private readonly List<PropRoot> _propRoots = new List<PropRoot>();
        private readonly List<int> _scratch = new List<int>();

        public PlayerScaler(AvatarSwapManager swaps, AvatarLibrary library)
        {
            _swaps = swaps;
            _library = library;
        }

        /// <summary>What the rig is scaled by right now. 1 when the feature is off.</summary>
        public float Applied => _applied;

        /// <summary>Your own eye height in metres, as measured. 0 until we've seen you stand.</summary>
        public float EyeHeight => _eyeHeight;

        public string Describe()
        {
            if (!ModConfig.HeightScalingEnabled.Value) return "off";
            if (!Interop.Alive(_rig)) return "waiting for the rig";
            var eyes = _eyeHeight > 0f ? $"{_eyeHeight * _applied:0.00} m" : "?";
            var from = ModConfig.HeightFromAvatar.Value ? "avatar" : "setting";
            return $"x{_applied:0.00} — {eyes} to the eyes (from {from})";
        }

        /// <summary>
        /// Called every frame from OnUpdate, BEFORE full-body tracking and the swap read
        /// anything: this frame's tracker poses, IK targets and avatar calibration all have to
        /// see the size we've settled on, not the one from last frame.
        /// </summary>
        public void Tick()
        {
            if (_dead) return;
            try
            {
                if (!ResolveRig()) return;

                MeasureEyeHeight();

                var wanted = WantedScale();
                if (Mathf.Abs(wanted - _applied) > 0.001f)
                {
                    Apply(wanted, WhyChanged(wanted));
                }
                else if (Drifted())
                {
                    // Something in the game put the rig back to full size — reparenting between
                    // the standing and seated poses is the known candidate. Just do it again,
                    // and say so at most once a second so a fight is visible without flooding.
                    _driftTrips++;
                    Apply(_applied, null);
                    if (Time.unscaledTime >= _nextDriftLogAt)
                    {
                        _nextDriftLogAt = Time.unscaledTime + 1f;
                        Core.Log.Msg($"Height: the game reset the rig scale, re-applied x{_applied:0.000} " +
                                     $"({_driftTrips} time(s) so far).");
                    }
                }

                RestoreDroppedProps();
            }
            catch (Exception e)
            {
                _dead = true;
                Core.Log.Error($"Height scaling threw and is now off for this session: {e}");
                try { Apply(1f, "scaling failed"); } catch { }
            }
        }

        /// <summary>PageUp / PageDown. Trims your height live, in headset, without the config file.</summary>
        public void Nudge(float delta)
        {
            // Writes the setting rather than a private field, so what you dialled in is what the
            // overlay shows, what F3 prints, and what MelonPreferences.cfg keeps for next time.
            ModConfig.HeightScalingEnabled.Value = true;
            ModConfig.HeightScale.Value = Mathf.Clamp(ModConfig.HeightScale.Value + delta, MinScale, MaxScale);
            Core.Log.Msg($"Height: HeightScale now {ModConfig.HeightScale.Value:0.00}" +
                         (ModConfig.HeightFromAvatar.Value ? " (on top of the avatar's own height)" : ""));
        }

        /// <summary>F12. Back to the size the game shipped you at, without changing your settings.</summary>
        public void Reset()
        {
            ModConfig.HeightScalingEnabled.Value = false;
            ModConfig.HeightScale.Value = 1f;
            Core.Log.Msg("Height: back to vanilla size (HeightScalingEnabled = false).");
        }

        // ---- resolution -----------------------------------------------------------------

        private static float MinScale => Mathf.Clamp(ModConfig.HeightMinScale.Value, 0.05f, 1f);
        private static float MaxScale => Mathf.Clamp(ModConfig.HeightMaxScale.Value, 1f, 10f);

        /// <summary>
        /// Finds the rig by walking up from the game's own camera rig singleton rather than by
        /// name: `XRRig.Transform` is `[CameraRig]`, and the scene root above it is the object
        /// that owns your colliders, arms and holsters. Nothing here depends on the spelling of
        /// "VR Controller", which is one game update away from being something else.
        /// </summary>
        private bool ResolveRig()
        {
            if (Interop.Alive(_rig)) return true;

            Transform cameraRig = null;
            try { cameraRig = XRRig.Transform; } catch { }
            if (!Interop.Alive(cameraRig)) return false;

            var root = cameraRig;
            var guard = 0;
            while (Interop.Alive(root.parent) && guard++ < 32) root = root.parent;

            // A rig we can't scale is worse than no feature: half-applied scale would leave the
            // camera in one size and the colliders in another.
            if (!Interop.Alive(root)) return false;

            _rig = root;
            _rigRestScale = root.localScale;
            _rigPath = Interop.ScenePath(root);
            _applied = 1f;

            if (_rigRestScale.x <= 0.0001f) _rigRestScale = Vector3.one;

            try { _camera = XRRig.Camera; } catch { _camera = null; }
            if (Interop.Alive(_camera)) _baseNearClip = _camera.nearClipPlane;

            ResolveLocomotion();
            ResolvePropRoots();

            Core.Log.Msg($"Height: rig resolved — `{_rigPath}`, rest scale {Interop.Vec(_rigRestScale)}, " +
                         $"camera `{Interop.Name(_camera)}` near clip {_baseNearClip:0.###} m, " +
                         $"{_propRoots.Count} hand(s) that can hold things" +
                         (_control != null ? ", locomotion reachable" : ", locomotion NOT reachable"));
            return true;
        }

        private void ResolveLocomotion()
        {
            _control = null;
            _haveLocomotionBase = false;
            try
            {
                _control = _rig.GetComponent<VRPlayerControl>();
                if (_control == null) _control = _rig.GetComponentInChildren<VRPlayerControl>(true);
                // The rig is a scene root, so if the movement component lives on a root of its
                // own there is nowhere above it to look. There is exactly one local player.
                if (_control == null) _control = UnityEngine.Object.FindObjectOfType<VRPlayerControl>();
                if (_control == null) return;

                _baseMoveSpeed = _control.firstPersonMoveSpeed;
                _baseJumpHeight = _control.jumpHeight;
                _haveLocomotionBase = true;
            }
            catch (Exception e)
            {
                // Not fatal: the scale still applies, you just move at vanilla speed.
                Core.Log.Warning($"Height: locomotion speeds not reachable ({e.GetType().Name}), " +
                                 "HeightMoveSpeedBlend will do nothing.");
                _control = null;
            }
        }

        private void ResolvePropRoots()
        {
            _propRoots.Clear();
            try
            {
                foreach (var root in _rig.GetComponentsInChildren<PropRoot>(true))
                    if (root != null) _propRoots.Add(root);
            }
            catch (Exception e) { Core.Log.Warning($"Height: hand prop roots not found: {e.Message}"); }
        }

        // ---- how big ---------------------------------------------------------------------

        /// <summary>
        /// Your real eye height, in real metres, kept as the tallest plausible reading of the
        /// session. Divided by the scale we're applying, so it stays a measurement of YOU and
        /// can be re-read at any size — no "stand still for calibration" moment, and crouching
        /// can't shrink you a second time.
        /// </summary>
        private void MeasureEyeHeight()
        {
            if (ModConfig.HeightEyeHeightOverride.Value > 0.2f)
            {
                _eyeHeight = ModConfig.HeightEyeHeightOverride.Value;
                return;
            }

            try
            {
                var player = AvatarPlayer.LocalAvatar;
                if (!Interop.Alive(player)) return;

                var head = player.Head;
                if (!Interop.Alive(head)) head = player.IKTargetHead;
                if (!Interop.Alive(head)) return;

                var measured = (head.position.y - player.transform.position.y) / Mathf.Max(0.01f, _applied);

                // A person. Anything else is a ragdoll, a chair, a loading screen or a climb,
                // and none of those are how tall you are.
                if (measured < 0.9f || measured > 2.3f) return;
                if (measured > _eyeHeight) _eyeHeight = measured;
            }
            catch { }
        }

        /// <summary>The avatar's own eye height in metres, from the exporter's measurement.</summary>
        private float AvatarHeadHeight()
        {
            try
            {
                var name = _swaps?.SelfAvatarName;
                if (string.IsNullOrEmpty(name)) return 0f;
                var manifest = _library?.Get(name);
                var h = manifest?.rig?.headHeight ?? 0f;
                return h > 0.2f && h < 5f ? h : 0f;
            }
            catch { return 0f; }
        }

        private float WantedScale()
        {
            if (!ModConfig.HeightScalingEnabled.Value) return 1f;

            var scale = ModConfig.HeightScale.Value;

            if (ModConfig.HeightFromAvatar.Value)
            {
                // The whole point, in one line: be as tall as the character you're wearing.
                // Both halves are eye heights — the avatar's from the exporter, yours from the
                // headset — so the ratio is what your play space has to shrink by for the
                // avatar to stand on the floor at its own size with your eyes in its head.
                var avatar = AvatarHeadHeight();
                if (avatar > 0f && _eyeHeight > 0.5f) scale *= avatar / _eyeHeight;
            }

            if (!float.IsFinite(scale) || scale <= 0f) scale = 1f;
            return Mathf.Clamp(scale, MinScale, MaxScale);
        }

        private string WhyChanged(float wanted)
        {
            if (wanted >= 0.999f && wanted <= 1.001f) return "back to vanilla size";
            if (!ModConfig.HeightFromAvatar.Value) return "HeightScale";
            var avatar = AvatarHeadHeight();
            if (avatar <= 0f) return "HeightScale (no avatar measurement to use)";
            return $"avatar {avatar:0.00} m ÷ you {_eyeHeight:0.00} m";
        }

        // ---- applying --------------------------------------------------------------------

        private bool Drifted()
        {
            if (!Interop.Alive(_rig)) return false;
            var want = _rigRestScale.x * _applied;
            return Mathf.Abs(_rig.localScale.x - want) > 0.001f ||
                   Mathf.Abs(_rig.localScale.y - want) > 0.001f ||
                   Mathf.Abs(_rig.localScale.z - want) > 0.001f;
        }

        /// <param name="why">null for a silent re-application after drift.</param>
        private void Apply(float scale, string why)
        {
            if (!Interop.Alive(_rig)) return;

            // Your world position is the rig root PLUS your tracked offset inside the play
            // space, and scaling multiplies that offset — so resizing while standing a metre
            // off-centre would slide you half a metre sideways through the room. Put the rig
            // back under where you were standing. Horizontally only: your eyes moving is the
            // entire point of the exercise.
            var eyesBefore = Interop.Alive(_camera) ? _camera.transform.position : Vector3.zero;
            var recentre = Interop.Alive(_camera);

            _rig.localScale = _rigRestScale * scale;
            _applied = scale;

            if (recentre)
            {
                var drift = _camera.transform.position - eyesBefore;
                drift.y = 0f;
                if (drift.sqrMagnitude > 1e-6f) _rig.position -= drift;
            }

            ApplyNearClip();
            ApplyLocomotion();
            if (scale >= 0.999f && scale <= 1.001f) RestoreAllProps("back to vanilla size");

            if (why == null) return;

            var eyes = _eyeHeight > 0f ? $"{_eyeHeight * scale:0.00} m" : "unmeasured";
            Core.Log.Msg($"*** Height: x{scale:0.000} — {eyes} to the eyes ({why}).");
            LogHitboxes();
        }

        private void ApplyNearClip()
        {
            if (!Interop.Alive(_camera) || _baseNearClip <= 0f) return;
            try
            {
                // Near clip is in world metres and doesn't know how big you are, so at half
                // size everything crosses it twice as close to your face — including the weapon
                // in your hand.
                _camera.nearClipPlane = ModConfig.HeightScaleNearClip.Value
                    ? _baseNearClip * _applied
                    : _baseNearClip;
            }
            catch { }
        }

        private void ApplyLocomotion()
        {
            if (_control == null || !_haveLocomotionBase) return;
            try
            {
                // Stick movement is metres per second in WORLD units, so a half-size player
                // covers the dungeon at twice their own body's pace — fast and floaty. Scaling
                // it all the way back fixes the feel and makes them genuinely slower than the
                // party, which is a different problem, so this is a blend and it defaults to
                // leaving the game alone.
                var blend = Mathf.Clamp01(ModConfig.HeightMoveSpeedBlend.Value);
                var factor = Mathf.Lerp(1f, _applied, blend);
                _control.firstPersonMoveSpeed = _baseMoveSpeed * factor;
                _control.jumpHeight = _baseJumpHeight * factor;
            }
            catch { }
        }

        /// <summary>
        /// Prints what the change actually did to the two capsules you get hit through. This is
        /// the claim worth checking with your own eyes the first time, because everything about
        /// whether this is fair rests on it.
        /// </summary>
        private void LogHitboxes()
        {
            try
            {
                foreach (var capsule in _rig.GetComponentsInChildren<CapsuleCollider>(true))
                {
                    if (capsule == null) continue;
                    var t = capsule.transform;
                    // Unity scales a capsule's radius by the larger of the two off-axis scales
                    // and its height by the axis scale; ours is uniform, so one number does.
                    var s = t.lossyScale.x;
                    Core.Log.Msg($"    hitbox `{capsule.name}`: radius {capsule.radius * s:0.000} m, " +
                                 $"height {capsule.height * s:0.000} m");
                }
            }
            catch { }
        }

        // ---- props ------------------------------------------------------------------------

        /// <summary>
        /// Insurance against leaving half-size axes all over the dungeon.
        ///
        /// A held weapon is parented into the rig, so it scales with you — which is the
        /// intended look, a small fighter with a small sword. The danger is the moment it
        /// LEAVES: Unity preserves world scale across a reparent, so the prop's own localScale
        /// gets rewritten to keep it small, and it stays small forever — in a shared, networked,
        /// pooled object that a full-size friend can then pick up. So we remember the scale a
        /// prop had when it came into our hands, and put it back when it goes.
        ///
        /// If it turns out the game doesn't parent held props at all, this simply never fires.
        /// </summary>
        private void RestoreDroppedProps()
        {
            if (!ModConfig.HeightRestorePropScale.Value) return;

            if (_propRoots.Count == 0)
            {
                // The hands are built with the rig but not necessarily before we first look,
                // so an empty list means "not yet", not "never".
                if (Time.unscaledTime < _nextPropRootScanAt) return;
                _nextPropRootScanAt = Time.unscaledTime + 5f;
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

                _held[id] = (prop, prop.transform.localScale);
                if (_applied < 0.999f || _applied > 1.001f)
                    Core.Log.Msg($"Height: picked up `{Interop.Name(prop)}` — local scale " +
                                 $"{Interop.Vec(prop.transform.localScale)}, world " +
                                 $"{Interop.Vec(prop.transform.lossyScale)}, under " +
                                 $"`{Interop.ScenePath(prop.transform.parent)}`");
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
                RestoreProp(entry.prop, entry.scale, "left our hands");
            }
        }

        private void RestoreAllProps(string why)
        {
            if (_held.Count == 0) return;
            foreach (var kv in _held) RestoreProp(kv.Value.prop, kv.Value.scale, why);
            _held.Clear();
        }

        private void RestoreProp(Prop prop, Vector3 scale, string why)
        {
            if (!Interop.Alive(prop)) return;
            try
            {
                var t = prop.transform;
                if ((t.localScale - scale).sqrMagnitude < 1e-6f) return;
                Core.Log.Msg($"Height: `{Interop.Name(prop)}` {why} at scale {Interop.Vec(t.localScale)}; " +
                             $"put back to {Interop.Vec(scale)}.");
                t.localScale = scale;
            }
            catch { }
        }
    }
}
