using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using Il2Cpp;
using Interop = CustomAvatars.Recon.Interop;

namespace CustomAvatars.Fbt
{
    public enum TrackerRole { Hip, LeftFoot, RightFoot }

    /// <summary>One tracker's binding: which body part, and where the bone sits relative to it.</summary>
    public class CalibratedTracker
    {
        public string Serial;
        public TrackerRole Role;
        public Vector3 OffsetPos;
        public Quaternion OffsetRot = Quaternion.identity;
    }

    /// <summary>
    /// The VRChat-style calibration flow: T-pose, watch your pucks appear, squeeze both
    /// triggers, done.
    ///
    /// Arming (showing pucks, listening for the squeeze) happens three ways — F11, F10 with no
    /// stored calibration, or just holding a T-pose in the headset for a moment, so
    /// recalibrating never needs the keyboard. Locking is always the double trigger squeeze:
    /// the T-pose alone can happen in play, but a deliberate second gesture on top of it can't.
    ///
    /// What locking captures, per tracker: which role it plays, and the constant offset from
    /// the tracker to the matching bone of the GAME's rig (not the custom avatar's — those
    /// bones never change, so one calibration works for every avatar and survives restarts,
    /// keyed by tracker serial). The idle rig approximates a person standing straight; whatever
    /// stance mismatch remains bakes into the offset, which is exactly the mounting offset the
    /// targets need anyway.
    /// </summary>
    public class FbtCalibrator
    {
        public enum CalState { Idle, Armed }

        private const float ArmedTimeoutSeconds = 60f;
        private const float TriggerPressPoint = 0.85f;
        private const float TriggerReleasePoint = 0.3f;

        public CalState State { get; private set; } = CalState.Idle;

        /// <summary>Fired at lock-in with the full role + offset set, already persisted.</summary>
        public event Action<List<CalibratedTracker>> Locked;

        private float _armedUntil;
        private float _triggerHeldFor;
        private float _tposeHeldFor;
        private float _validStreak;          // seconds all three trackers have tracked cleanly
        private float _noAutoArmBefore;      // grace after a lock, so it can't chain
        private bool _tposeMustDrop;         // the pose that armed (or locked) has to end first
        private bool _triggersMustRelease;   // hysteresis: no re-lock until both released
        private bool _steamVrActionsDead;    // fallback input path failed once; stop asking

        public string StatusLine => State == CalState.Armed
            ? "calibrating — T-pose and squeeze both triggers"
            : "idle";

        public void Arm(string how)
        {
            if (State == CalState.Armed) return;
            State = CalState.Armed;
            _armedUntil = Time.unscaledTime + ArmedTimeoutSeconds;
            _triggerHeldFor = 0f;
            _tposeHeldFor = 0f;
            _validStreak = 0f;
            _triggersMustRelease = true;   // a squeeze that predates arming shouldn't lock
            Core.Log.Msg($"*** FBT calibration armed ({how}). Stand naturally, T-pose, " +
                         "then squeeze both triggers for a second to lock in.");
            FbtAudio.Armed();
        }

        public void Cancel(string why)
        {
            if (State == CalState.Idle) return;
            State = CalState.Idle;
            _tposeMustDrop = true;   // still T-posing? That mustn't re-arm what was cancelled.
            Core.Log.Msg($"FBT calibration cancelled ({why}).");
            FbtAudio.Cancelled();
        }

        /// <summary>
        /// Run the gestures. Returns true while armed (the manager shows pucks then).
        /// <paramref name="tposeEntryAllowed"/> is the "FBT is on" condition — holding a
        /// T-pose only arms calibration when the feature itself is switched on.
        /// </summary>
        public void Tick(TrackerReader reader, float dt, bool tposeEntryAllowed)
        {
            var player = LocalPlayer();

            if (State == CalState.Idle)
            {
                if (tposeEntryAllowed && Time.unscaledTime >= _noAutoArmBefore &&
                    DetectTpose(player, dt))
                    Arm("T-pose held");
                return;
            }

            if (Time.unscaledTime > _armedUntil)
            {
                Cancel("nothing locked in for a minute");
                return;
            }

            // A lock is only accepted after the trackers have been clean for a moment. The
            // first field test locked during a validity blip and captured NaN offsets.
            var validNow = 0;
            foreach (var t in reader.Trackers)
                if (t.PoseValid && !string.IsNullOrEmpty(t.Serial)) validNow++;
            _validStreak = validNow >= 3 ? _validStreak + dt : 0f;

            ReadTriggers(out var left, out var right);

            if (_triggersMustRelease)
            {
                if (left < TriggerReleasePoint && right < TriggerReleasePoint) _triggersMustRelease = false;
                return;
            }

            if (left > TriggerPressPoint && right > TriggerPressPoint)
            {
                _triggerHeldFor += dt;
                if (_triggerHeldFor >= Mathf.Max(0.1f, ModConfig.FbtTriggerHoldSeconds.Value))
                {
                    _triggersMustRelease = true;
                    _triggerHeldFor = 0f;
                    TryLock(reader, player);
                }
            }
            else
            {
                _triggerHeldFor = 0f;
            }
        }

        private static AvatarPlayer LocalPlayer()
        {
            try
            {
                var p = AvatarPlayer.LocalAvatar;
                return Interop.Alive(p) ? p : null;
            }
            catch { return null; }
        }

        // ---- gestures -------------------------------------------------------------------

        /// <summary>
        /// Arms out sideways at shoulder height, held for a moment. Measured against the
        /// controller transforms, not the smoothed IK targets, so it reads the real pose.
        /// The thresholds scale with the player's own head height — no hardcoded human.
        /// </summary>
        private bool DetectTpose(AvatarPlayer player, float dt)
        {
            var posed = IsTposed(player);

            // The pose that armed (or locked) the last calibration has to END before it can
            // start another — without this latch, locking while still T-posed re-armed
            // calibration eleven milliseconds later.
            if (_tposeMustDrop)
            {
                if (!posed) _tposeMustDrop = false;
                _tposeHeldFor = 0f;
                return false;
            }

            _tposeHeldFor = posed ? _tposeHeldFor + dt : 0f;
            return _tposeHeldFor >= Mathf.Max(0.2f, ModConfig.FbtTposeHoldSeconds.Value);
        }

        /// <summary>Arms out level with the shoulders, standing. Shared with the pose re-bind.</summary>
        public static bool IsTposed(AvatarPlayer player)
        {
            var posed = false;
            try
            {
                if (Interop.Alive(player))
                {
                    var head = player.Head;
                    var lh = player.LeftHand;
                    var rh = player.RightHand;
                    if (Interop.Alive(head) && Interop.Alive(lh) && Interop.Alive(rh))
                    {
                        var headY = head.position.y;
                        var floorY = player.transform.position.y;
                        var headHeight = headY - floorY;

                        var shoulderY = headY - 0.18f;
                        var handsAtShoulders =
                            Mathf.Abs(lh.position.y - shoulderY) < 0.25f &&
                            Mathf.Abs(rh.position.y - shoulderY) < 0.25f;

                        var l = lh.position; l.y = 0;
                        var r = rh.position; r.y = 0;
                        var span = Vector3.Distance(l, r);

                        // Arms at your sides span well under half your height; a T-pose spans
                        // close to all of it. 0.8× eye height splits the two cleanly.
                        posed = headHeight > 0.8f && handsAtShoulders && span > headHeight * 0.8f;
                    }
                }
            }
            catch { }
            return posed;
        }

        /// <summary>
        /// Both analog triggers, from the game's own input singleton — with the SteamVR action
        /// read as a second opinion, because <c>XRInput</c> reports zero while a game menu is
        /// open and calibration must still be lockable there. Max of the two per hand.
        /// </summary>
        private void ReadTriggers(out float left, out float right)
        {
            left = right = 0f;
            try
            {
                var input = XRInput.Instance;
                if (input != null)
                {
                    left = Mathf.Clamp01(input.leftIndexTrigger);
                    right = Mathf.Clamp01(input.rightIndexTrigger);
                }
            }
            catch { }

            if (_steamVrActionsDead) return;
            try
            {
                var action = Il2CppValve.VR.SteamVR_Actions.othergate_IndexTrigger;
                if (action != null)
                {
                    left = Mathf.Max(left, action.GetAxis(Il2CppValve.VR.SteamVR_Input_Sources.LeftHand));
                    right = Mathf.Max(right, action.GetAxis(Il2CppValve.VR.SteamVR_Input_Sources.RightHand));
                }
            }
            catch { _steamVrActionsDead = true; }
        }

        // ---- locking --------------------------------------------------------------------

        private void TryLock(TrackerReader reader, AvatarPlayer player)
        {
            reader.Poll();
            var valid = new List<TrackerReader.Device>();
            foreach (var t in reader.Trackers)
                if (t.PoseValid && !string.IsNullOrEmpty(t.Serial)) valid.Add(t);

            if (valid.Count < 3)
            {
                Core.Log.Warning($"*** FBT: only {valid.Count} tracker(s) with a valid pose — " +
                                 "need hip + both feet. Still armed; check SteamVR and squeeze again.");
                FbtAudio.Error();
                return;
            }
            if (_validStreak < 0.5f)
            {
                Core.Log.Warning("*** FBT: tracker poses are blipping in and out — locking now would " +
                                 "capture garbage. Hold still a moment and squeeze again.");
                FbtAudio.Error();
                return;
            }
            if (!Interop.Alive(player))
            {
                Core.Log.Warning("*** FBT: no local player to calibrate against yet.");
                FbtAudio.Error();
                return;
            }

            var roles = AssignRoles(valid, player);
            if (roles == null) { FbtAudio.Error(); return; }   // AssignRoles already said why

            var calibrated = CaptureOffsets(roles, player);
            if (calibrated == null) { FbtAudio.Error(); return; }

            // The last line of defence: a NaN in a stored offset would poison every frame from
            // here on and outlive its cause — and a finite-but-absurd one (2.5 billion metres,
            // field-tested) is the same poison wearing a disguise. A tracker-to-bone mounting
            // offset is centimetres; anything past a metre means the data was wrong, not the
            // person. Refuse the lock, stay armed, say squeeze again.
            foreach (var c in calibrated)
            {
                if (!FbtMath.Finite(c.OffsetPos) || !FbtMath.Finite(c.OffsetRot))
                {
                    Core.Log.Warning($"*** FBT: the {c.Role} capture came out non-finite — a tracker " +
                                     "lied mid-read. Nothing saved; squeeze again.");
                    FbtAudio.Error();
                    return;
                }
                if (c.OffsetPos.magnitude > 1f)
                {
                    Core.Log.Warning($"*** FBT: the {c.Role} offset came out {c.OffsetPos.magnitude:0.00} m — " +
                                     "that is not a mounting offset, the tracker data is wrong. " +
                                     "Nothing saved; squeeze again.");
                    FbtAudio.Error();
                    return;
                }
            }

            Persist(calibrated);
            State = CalState.Idle;
            _tposeMustDrop = true;                               // still posing ≠ recalibrate
            _noAutoArmBefore = Time.unscaledTime + 5f;

            foreach (var c in calibrated)
                Core.Log.Msg($"*** FBT calibrated: {c.Role} = {c.Serial}, " +
                             $"offset {c.OffsetPos.magnitude * 100f:0.0}cm");
            FbtAudio.Locked();
            Locked?.Invoke(calibrated);
        }

        /// <summary>
        /// SteamVR role hints first (a puck assigned waist/foot in SteamVR settings names
        /// itself); geometry for the rest — the two lowest are feet, told apart by which side
        /// of you they're on, and the hip is whatever sits nearest half your height.
        /// </summary>
        private static Dictionary<TrackerRole, TrackerReader.Device> AssignRoles(
            List<TrackerReader.Device> valid, AvatarPlayer player)
        {
            var roles = new Dictionary<TrackerRole, TrackerReader.Device>();
            var unassigned = new List<TrackerReader.Device>();

            foreach (var t in valid)
            {
                var type = t.ControllerType ?? "";
                if (type.Contains("waist") && !roles.ContainsKey(TrackerRole.Hip)) roles[TrackerRole.Hip] = t;
                else if (type.Contains("left_foot") && !roles.ContainsKey(TrackerRole.LeftFoot)) roles[TrackerRole.LeftFoot] = t;
                else if (type.Contains("right_foot") && !roles.ContainsKey(TrackerRole.RightFoot)) roles[TrackerRole.RightFoot] = t;
                else unassigned.Add(t);
            }

            if (roles.Count < 3)
            {
                Transform head = null;
                try { head = player.Head; } catch { }
                if (!Interop.Alive(head))
                {
                    Core.Log.Warning("*** FBT: no head transform to orient the feet against.");
                    return null;
                }

                unassigned.Sort((a, b) => a.WorldPos.y.CompareTo(b.WorldPos.y));

                if (!roles.ContainsKey(TrackerRole.LeftFoot) || !roles.ContainsKey(TrackerRole.RightFoot))
                {
                    // The two lowest unassigned pucks are on your feet.
                    var feet = new List<TrackerReader.Device>();
                    while (feet.Count < 2 && unassigned.Count > 0)
                    {
                        feet.Add(unassigned[0]);
                        unassigned.RemoveAt(0);
                    }
                    if (feet.Count == 2)
                    {
                        // Same geometry-not-axes rule as the yaw mapping: mid-T-pose, the
                        // spread hands are the ground truth for which way is right.
                        var rightAxis = UserRight(player, head);

                        var headFlat = head.position; headFlat.y = 0;
                        var a = feet[0].WorldPos; a.y = 0;
                        var b = feet[1].WorldPos; b.y = 0;
                        var aIsRight = Vector3.Dot(a - headFlat, rightAxis) > Vector3.Dot(b - headFlat, rightAxis);

                        roles[TrackerRole.RightFoot] = aIsRight ? feet[0] : feet[1];
                        roles[TrackerRole.LeftFoot] = aIsRight ? feet[1] : feet[0];
                    }
                }

                if (!roles.ContainsKey(TrackerRole.Hip) && unassigned.Count > 0)
                {
                    // Nearest to half your eye height wins — that skips a chest tracker.
                    var floorY = player.transform.position.y;
                    var target = floorY + (head.position.y - floorY) * 0.55f;
                    unassigned.Sort((x, y) =>
                        Mathf.Abs(x.WorldPos.y - target).CompareTo(Mathf.Abs(y.WorldPos.y - target)));
                    roles[TrackerRole.Hip] = unassigned[0];
                }
            }

            if (roles.Count < 3)
            {
                Core.Log.Warning("*** FBT: could not assign hip + both feet from the trackers seen.");
                return null;
            }
            return roles;
        }

        /// <summary>
        /// Tracker-local offsets to where the game rig's bones WILL BE once VRIK is tracking
        /// you — not to where the rig happens to be standing right now.
        ///
        /// The distinction is the first field test's 165 cm "offset": the local rig is a
        /// display object parked near the play-space origin, while you and your trackers are
        /// wherever you physically are in the room. The moment FBT enables VRIK, the rig's
        /// head snaps to your real head — so the frame calibration must measure in is the
        /// rig's skeleton picked up and set down under your head, yaw-aligned to the way
        /// you're facing. Bone offsets from the rig's own head keep the rig's proportions,
        /// which is what its solver needs; your mounting offsets are what's left over, and
        /// that's exactly what we want to store.
        /// </summary>
        private List<CalibratedTracker> CaptureOffsets(
            Dictionary<TrackerRole, TrackerReader.Device> roles, AvatarPlayer player)
        {
            Animator animator = null;
            try { animator = player.RemoteAnimator; } catch { }
            if (!Interop.Alive(animator))
            {
                Core.Log.Warning("*** FBT: the game rig has no Animator to calibrate against.");
                return null;
            }

            Transform ikHead = null, rigHead = null;
            try { ikHead = player.IKTargetHead; } catch { }
            try { rigHead = animator.GetBoneTransform(HumanBodyBones.Head); } catch { }
            if (!Interop.Alive(ikHead) || !Interop.Alive(rigHead))
            {
                Core.Log.Warning("*** FBT: no head reference to map the calibration onto you.");
                return null;
            }

            // Yaw-only alignment between the rig's facing and yours. Full rotation would tip
            // the skeleton over with your head; people calibrate standing upright.
            //
            // Both facings come from GEOMETRY, never from a transform's forward axis. The
            // first attempt trusted `FullBody.transform.forward` and the head target's
            // forward, one of which points backwards on this rig — and a 180° yaw error
            // twists the freshly-bound pelvis half a turn and mirrors the feet (field-tested:
            // hips spun round, one foot offset double the other). A person mid-T-pose
            // defines their own axes: right = right hand minus left hand. The rig defines
            // its own the same way: right = right hip bone minus left hip bone.
            var userForward = UserForward(player, ikHead);
            var rigForward = RigForward(animator, player);
            var yaw = Quaternion.FromToRotation(rigForward, userForward);

            // Scale the rig's skeleton to YOUR height before hanging it from your head, so the
            // expected hip lands at your hip and the expected ankles at your ankles — targets
            // that sit ON your body are what makes a tracker feel bound to the foot it's
            // strapped to. Unscaled (rig proportions), a taller player's virtual feet float
            // above the real ones and the knees pop sideways chasing them. The rig itself
            // can't be resized, but VRIK's legLengthMlp stretches its legs to match; the
            // manager applies this same factor there.
            var scale = 1f;
            var userHeight = ikHead.position.y - player.transform.position.y;
            var rigHeight = 0f;
            try { rigHeight = rigHead.position.y - player.FullBody.transform.position.y; } catch { }
            // The clamp exists to reject a bad measurement.
            if (userHeight > 0.8f && rigHeight > 0.8f)
                scale = Mathf.Clamp(userHeight / rigHeight, 0.7f, 1.5f);
            LastBodyScale = scale;

            Core.Log.Msg($"    FBT: mapping the rig onto you — yaw " +
                         $"{Vector3.SignedAngle(rigForward, userForward, Vector3.up):0.0}°, " +
                         $"body scale x{scale:0.000} (you {userHeight:0.00} m, rig {rigHeight:0.00} m to the head)");

            var boneOf = new Dictionary<TrackerRole, HumanBodyBones>
            {
                { TrackerRole.Hip, HumanBodyBones.Hips },
                { TrackerRole.LeftFoot, HumanBodyBones.LeftFoot },
                { TrackerRole.RightFoot, HumanBodyBones.RightFoot },
            };

            var result = new List<CalibratedTracker>();
            foreach (var kv in roles)
            {
                Transform bone = null;
                try { bone = animator.GetBoneTransform(boneOf[kv.Key]); } catch { }
                if (!Interop.Alive(bone))
                {
                    Core.Log.Warning($"*** FBT: the game rig has no {boneOf[kv.Key]} bone.");
                    return null;
                }

                var expectedPos = ikHead.position + yaw * ((bone.position - rigHead.position) * scale);
                var expectedRot = yaw * bone.rotation;

                // Feet: sideways, believe the puck, not the rig. The mapping above places the
                // feet at the RIG's stance width under your head, so however your stance
                // differed from the rig's at lock became a constant sideways error on every
                // step ("my virtual leg is to the left of my real leg"). The tracker knows
                // exactly how far to the side your foot is; only the height (ankle above
                // instep) and the fore/aft (ankle behind instep) still need the rig's anatomy.
                // Not the hip: a puck worn on the front of the waistband is off-centre by
                // mounting, and the pelvis genuinely belongs centred under your head.
                if (kv.Key != TrackerRole.Hip)
                {
                    var userRight = Vector3.Cross(Vector3.up, userForward);
                    var lateral = Vector3.Dot(kv.Value.WorldPos - expectedPos, userRight);
                    expectedPos += userRight * lateral;
                    Core.Log.Msg($"    FBT: {kv.Key} lateral position taken from the tracker " +
                                 $"({lateral * 100f:+0.0;-0.0} cm from the rig's stance)");
                }

                var inv = Quaternion.Inverse(kv.Value.WorldRot);
                result.Add(new CalibratedTracker
                {
                    Serial = kv.Value.Serial,
                    Role = kv.Key,
                    OffsetPos = inv * (expectedPos - kv.Value.WorldPos),
                    OffsetRot = inv * expectedRot,
                });
            }
            return result;
        }

        /// <summary>
        /// Which way the person is facing, flattened. T-posed hands are the best evidence
        /// (their spread defines the right axis exactly); the head's forward is the fallback
        /// when the hands aren't spread, and world forward the fallback's fallback.
        /// </summary>
        private static Vector3 UserForward(AvatarPlayer player, Transform ikHead)
        {
            try
            {
                var lh = player.LeftHand;
                var rh = player.RightHand;
                if (Interop.Alive(lh) && Interop.Alive(rh))
                {
                    var right = rh.position - lh.position; right.y = 0;
                    if (right.sqrMagnitude > 0.25f)   // hands actually spread — a real T-pose
                        return Vector3.Cross(right, Vector3.up).normalized;
                }
            }
            catch { }
            try
            {
                var f = ikHead.forward; f.y = 0;
                if (f.sqrMagnitude > 1e-4f) return f.normalized;
            }
            catch { }
            return Vector3.forward;
        }

        /// <summary>The person's right axis, flattened — spread hands first, head as fallback.</summary>
        private static Vector3 UserRight(AvatarPlayer player, Transform head)
        {
            try
            {
                var lh = player.LeftHand;
                var rh = player.RightHand;
                if (Interop.Alive(lh) && Interop.Alive(rh))
                {
                    var right = rh.position - lh.position; right.y = 0;
                    if (right.sqrMagnitude > 0.25f) return right.normalized;
                }
            }
            catch { }
            try
            {
                var r = head.right; r.y = 0;
                if (r.sqrMagnitude > 1e-4f) return r.normalized;
            }
            catch { }
            return Vector3.right;
        }

        /// <summary>Which way the rig faces: from its own hip bones, never its root's axes.</summary>
        private static Vector3 RigForward(Animator animator, AvatarPlayer player)
        {
            try
            {
                var l = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
                var r = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
                if (Interop.Alive(l) && Interop.Alive(r))
                {
                    var right = r.position - l.position; right.y = 0;
                    if (right.sqrMagnitude > 1e-4f)
                        return Vector3.Cross(right, Vector3.up).normalized;
                }
            }
            catch { }
            try
            {
                var f = player.FullBody.transform.forward; f.y = 0;
                if (f.sqrMagnitude > 1e-4f) return f.normalized;
            }
            catch { }
            return Vector3.forward;
        }

        // ---- persistence ----------------------------------------------------------------

        /// <summary>
        /// Format version for the stored calibration. Bumped whenever the MEANING of the
        /// offsets changes (the yaw-mapping fix changed it), so a calibration captured by an
        /// older build is discarded instead of replaying its bug. Offsets that look sane can
        /// still be wrong — the 180°-mirrored set was 16–36 cm and passed every value check.
        /// </summary>
        private const string FormatVersion = "v4";   // v4: feet take their lateral position from the trackers

        /// <summary>User height ÷ rig height, captured at the last lock. 1 until calibrated.</summary>
        public float LastBodyScale { get; private set; } = 1f;

        private void Persist(List<CalibratedTracker> calibrated)
        {
            var inv = CultureInfo.InvariantCulture;
            var parts = new List<string>
            {
                FormatVersion,
                string.Format(inv, "scale|{0:F4}", LastBodyScale),
            };
            foreach (var c in calibrated)
            {
                parts.Add(string.Join("|",
                    c.Serial,
                    c.Role.ToString(),
                    string.Format(inv, "{0:F5},{1:F5},{2:F5}", c.OffsetPos.x, c.OffsetPos.y, c.OffsetPos.z),
                    string.Format(inv, "{0:F6},{1:F6},{2:F6},{3:F6}",
                                  c.OffsetRot.x, c.OffsetRot.y, c.OffsetRot.z, c.OffsetRot.w)));
            }
            ModConfig.FbtCalibration.Value = string.Join(";", parts);
            try { MelonLoader.MelonPreferences.Save(); } catch { }
        }

        /// <summary>
        /// The stored calibration, if every serial in it is connected right now — a session
        /// with the same pucks strapped on shouldn't have to T-pose again. Null means
        /// calibrate afresh.
        /// </summary>
        public static List<CalibratedTracker> TryLoadPersisted(TrackerReader reader, out float bodyScale)
        {
            bodyScale = 1f;
            var raw = ModConfig.FbtCalibration.Value;
            if (string.IsNullOrWhiteSpace(raw)) return null;

            var entries = raw.Split(';');
            if (entries.Length == 0 || entries[0] != FormatVersion)
            {
                Core.Log.Msg("FBT: stored calibration is from an older build whose offsets mean " +
                             "something different — discarding it. Recalibrate (T-pose + squeeze).");
                ModConfig.FbtCalibration.Value = "";
                return null;
            }

            var inv = CultureInfo.InvariantCulture;
            var result = new List<CalibratedTracker>();
            try
            {
                foreach (var entry in entries)
                {
                    if (entry == FormatVersion) continue;
                    var f = entry.Split('|');
                    if (f.Length == 2 && f[0] == "scale")
                    {
                        bodyScale = Mathf.Clamp(float.Parse(f[1], inv), 0.7f, 1.5f);
                        continue;
                    }
                    if (f.Length != 4) return null;
                    var p = f[2].Split(',');
                    var q = f[3].Split(',');
                    result.Add(new CalibratedTracker
                    {
                        Serial = f[0],
                        Role = (TrackerRole)Enum.Parse(typeof(TrackerRole), f[1]),
                        OffsetPos = new Vector3(
                            float.Parse(p[0], inv), float.Parse(p[1], inv), float.Parse(p[2], inv)),
                        OffsetRot = new Quaternion(
                            float.Parse(q[0], inv), float.Parse(q[1], inv),
                            float.Parse(q[2], inv), float.Parse(q[3], inv)),
                    });
                }
            }
            catch { return null; }
            if (result.Count < 3) return null;

            // float.Parse happily reads back "NaN" — and one bad session wrote exactly that;
            // another wrote a finite 2.5e9 metres. A stored calibration that isn't a plausible
            // mounting offset is a stored explosion; make it recalibrate.
            if (!FbtMath.Finite(bodyScale) || bodyScale <= 0f) bodyScale = 1f;
            foreach (var c in result)
                if (!FbtMath.Finite(c.OffsetPos) || !FbtMath.Finite(c.OffsetRot) ||
                    c.OffsetPos.magnitude > 1f)
                {
                    Core.Log.Warning("FBT: stored calibration contains impossible values (a bad " +
                                     "capture from an earlier build) — discarding it. Recalibrate.");
                    ModConfig.FbtCalibration.Value = "";
                    return null;
                }

            foreach (var c in result)
            {
                var found = false;
                foreach (var t in reader.Trackers)
                    if (t.Serial == c.Serial) { found = true; break; }
                if (!found)
                {
                    Core.Log.Msg($"FBT: stored calibration names tracker {c.Serial} ({c.Role}), " +
                                 "which isn't connected — recalibration needed.");
                    return null;
                }
            }
            return result;
        }
    }
}
