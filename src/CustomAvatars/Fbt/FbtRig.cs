using System;
using UnityEngine;
using Il2Cpp;
using Il2CppRootMotion.FinalIK;
using Interop = CustomAvatars.Recon.Interop;

namespace CustomAvatars.Fbt
{
    /// <summary>
    /// Wires hip and foot targets into one game rig's VRIK, and puts back exactly what it
    /// found when it lets go.
    ///
    /// The game's own solver does all the hard work: VRIK ships with pelvis and leg target
    /// slots the game never uses (its legs are a separate LimbIK + grounder system), so
    /// full-body tracking is "fill the empty slots, zero the two systems that would fight the
    /// feet". <see cref="AvatarSwapper"/>'s retargeter then copies the solved legs onto the
    /// custom avatar without a line of new code.
    ///
    /// Everything touched is saved first and restored on <see cref="Restore"/> — this class
    /// borrows the game's rig, it doesn't own it. Deliberately NOT built on
    /// <c>ForceVanillaIk</c>, which saves only <c>ikEnabled</c>; a partial restore here would
    /// leave a player walking around with locomotion off.
    /// </summary>
    public class FbtRig
    {
        private VRIK _ik;
        private CharacterPrefab _fullBody;
        private GrounderIK _grounder;
        private bool _isLocal;
        private bool _wired;

        // What the game had before we touched it.
        private bool _savedIkEnabled, _savedComponentEnabled;
        private int _savedLod;
        private float _savedLocomotionWeight;
        private bool _savedPlantFeet;
        private Transform _savedPelvisTarget, _savedLeftLegTarget, _savedRightLegTarget;
        private float _savedPelvisPosW, _savedPelvisRotW, _savedMaintainPelvis;
        private float _savedLeftPosW, _savedLeftRotW, _savedRightPosW, _savedRightRotW;
        private float _savedLeftLegMlp = 1f, _savedRightLegMlp = 1f;
        private float _savedGrounderWeight;
        private bool _hadGrounder;

        // User height ÷ rig height, from calibration. The rig can't be resized, but VRIK's
        // legLengthMlp stretches its legs to reach targets placed at the player's real
        // proportions — without it a taller player's knees lock straight or pop sideways.
        private float _bodyScale = 1f;

        // Per-target multipliers on the full weights, set by the owner every frame before
        // AssertPerFrame: 1 while a puck is tracked, eased to 0 once it has been lost long
        // enough that holding its last pose reads as a stuck limb. At 0 the game's own
        // placement takes that part of the body back; the other two targets keep tracking.
        // Restore ignores these — it puts back what the game had, not what we multiplied.
        public float HipWeight = 1f, LeftFootWeight = 1f, RightFootWeight = 1f;

        public bool Wired => _wired;

        /// <summary>Still pointing at live native objects? False means the player was replaced.</summary>
        public bool Alive => _wired && Interop.Alive(_ik) && Interop.Alive(_fullBody);

        /// <summary>
        /// Take the rig. <paramref name="isLocal"/> decides whether the VRIK component itself
        /// has to be switched on — the game deliberately disables it on your own body, since
        /// vanilla players can never see themselves; remote rigs are already solving.
        /// </summary>
        public bool Wire(CharacterPrefab fullBody, bool isLocal,
                         Transform hipTarget, Transform leftFootTarget, Transform rightFootTarget,
                         float bodyScale = 1f)
        {
            if (_wired) Restore("rewiring");
            _bodyScale = Mathf.Clamp(bodyScale, 0.7f, 1.5f);
            HipWeight = LeftFootWeight = RightFootWeight = 1f;

            if (!Interop.Alive(fullBody)) return false;
            VRIK ik = null;
            try { ik = fullBody.ik; } catch { }
            if (!Interop.Alive(ik)) { Core.Log.Warning("FBT: this body has no VRIK to wire."); return false; }

            var solver = ik.solver;
            if (solver?.spine == null || solver.leftLeg == null || solver.rightLeg == null)
            {
                Core.Log.Warning("FBT: VRIK solver is missing spine or legs.");
                return false;
            }

            _fullBody = fullBody;
            _ik = ik;
            _isLocal = isLocal;
            _grounder = FindGrounderFor(fullBody);

            try
            {
                // Save everything before changing anything, so a throw halfway through the
                // apply still restores completely.
                try { _savedIkEnabled = fullBody.ikEnabled; } catch { _savedIkEnabled = true; }
                _savedComponentEnabled = ik.enabled;
                _savedLod = solver.LOD;
                _savedLocomotionWeight = solver.locomotion?.weight ?? 0f;
                _savedPlantFeet = solver.plantFeet;
                _savedPelvisTarget = solver.spine.pelvisTarget;
                _savedPelvisPosW = solver.spine.pelvisPositionWeight;
                _savedPelvisRotW = solver.spine.pelvisRotationWeight;
                _savedMaintainPelvis = solver.spine.maintainPelvisPosition;
                _savedLeftLegTarget = solver.leftLeg.target;
                _savedLeftPosW = solver.leftLeg.positionWeight;
                _savedLeftRotW = solver.leftLeg.rotationWeight;
                _savedRightLegTarget = solver.rightLeg.target;
                _savedRightPosW = solver.rightLeg.positionWeight;
                _savedRightRotW = solver.rightLeg.rotationWeight;
                _savedLeftLegMlp = solver.leftLeg.legLengthMlp;
                _savedRightLegMlp = solver.rightLeg.legLengthMlp;
                if (Interop.Alive(_grounder)) { _hadGrounder = true; _savedGrounderWeight = _grounder.weight; }

                solver.spine.pelvisTarget = hipTarget;
                solver.spine.maintainPelvisPosition = 0f;
                solver.leftLeg.target = leftFootTarget;
                solver.rightLeg.target = rightFootTarget;

                _wired = true;
                AssertPerFrame();   // weights, enables, grounder — the same path every frame

                Core.Log.Msg($"FBT: wired {(isLocal ? "your" : "a peer's")} rig — pelvis + both legs" +
                             (_hadGrounder ? ", grounder muted" : ", no grounder found (cosmetic)"));
                return true;
            }
            catch (Exception e)
            {
                Core.Log.Warning($"FBT: wiring failed: {e.GetType().Name}: {e.Message}");
                Restore("wiring failed");
                return false;
            }
        }

        /// <summary>
        /// Re-assert every value we need, every frame. The game turned the local VRIK off on
        /// purpose and nothing stops it doing so again on its own schedule; quietly winning
        /// each frame is the same convention the mesh visibility and locomotion weight already
        /// use, and it is cheaper than finding out which game system to patch.
        /// </summary>
        public void AssertPerFrame()
        {
            if (!Alive) return;
            try
            {
                if (_isLocal)
                {
                    try { if (!_fullBody.ikEnabled) _fullBody.ikEnabled = true; } catch { }
                    if (!_ik.enabled) _ik.enabled = true;
                }

                var solver = _ik.solver;
                if (solver == null) return;
                if (_isLocal && solver.LOD != 0) solver.LOD = 0;

                var hip = Mathf.Clamp01(HipWeight);
                var left = Mathf.Clamp01(LeftFootWeight);
                var right = Mathf.Clamp01(RightFootWeight);
                var pelvisRot = Mathf.Clamp01(ModConfig.FbtPelvisRotationWeight.Value);
                var footRot = Mathf.Clamp01(ModConfig.FbtFootRotationWeight.Value);
                solver.spine.pelvisPositionWeight = hip;
                solver.spine.pelvisRotationWeight = pelvisRot * hip;
                solver.spine.maintainPelvisPosition = 0f;
                solver.leftLeg.positionWeight = left;
                solver.leftLeg.rotationWeight = footRot * left;
                solver.rightLeg.positionWeight = right;
                solver.rightLeg.rotationWeight = footRot * right;
                solver.leftLeg.legLengthMlp = _bodyScale;
                solver.rightLeg.legLengthMlp = _bodyScale;

                // Real feet and procedural feet must not share custody.
                if (solver.locomotion != null && solver.locomotion.weight != 0f) solver.locomotion.weight = 0f;
                if (solver.plantFeet) solver.plantFeet = false;

                if (_hadGrounder && ModConfig.FbtDisableGrounder.Value &&
                    Interop.Alive(_grounder) && _grounder.weight != 0f)
                    _grounder.weight = 0f;
            }
            catch { /* one bad frame must not kill the rig; Restore still knows the originals */ }
        }

        /// <summary>Put the rig back exactly as found. Idempotent; safe on dead objects.</summary>
        public void Restore(string why)
        {
            if (!_wired) return;
            _wired = false;

            if (Interop.Alive(_ik))
            {
                var solver = _ik.solver;
                if (solver != null)
                {
                    try { solver.spine.pelvisTarget = _savedPelvisTarget; } catch { }
                    try { solver.spine.pelvisPositionWeight = _savedPelvisPosW; } catch { }
                    try { solver.spine.pelvisRotationWeight = _savedPelvisRotW; } catch { }
                    try { solver.spine.maintainPelvisPosition = _savedMaintainPelvis; } catch { }
                    try { solver.leftLeg.target = _savedLeftLegTarget; } catch { }
                    try { solver.leftLeg.positionWeight = _savedLeftPosW; } catch { }
                    try { solver.leftLeg.rotationWeight = _savedLeftRotW; } catch { }
                    try { solver.rightLeg.target = _savedRightLegTarget; } catch { }
                    try { solver.rightLeg.positionWeight = _savedRightPosW; } catch { }
                    try { solver.rightLeg.rotationWeight = _savedRightRotW; } catch { }
                    try { solver.leftLeg.legLengthMlp = _savedLeftLegMlp; } catch { }
                    try { solver.rightLeg.legLengthMlp = _savedRightLegMlp; } catch { }
                    try { if (solver.locomotion != null) solver.locomotion.weight = _savedLocomotionWeight; } catch { }
                    try { solver.plantFeet = _savedPlantFeet; } catch { }
                    try { solver.LOD = _savedLod; } catch { }
                }
                try { _ik.enabled = _savedComponentEnabled; } catch { }
            }
            if (Interop.Alive(_fullBody))
            {
                try { _fullBody.ikEnabled = _savedIkEnabled; } catch { }
            }
            if (_hadGrounder && Interop.Alive(_grounder))
            {
                try { _grounder.weight = _savedGrounderWeight; } catch { }
            }

            Core.Log.Msg($"FBT: rig released ({why}).");
            _ik = null;
            _fullBody = null;
            _grounder = null;
            _hadGrounder = false;
        }

        /// <summary>
        /// The grounder lives on its own scene object (`Player Grounder`), not on the body, and
        /// the instance field on CharacterPrefab is private. So: scan, and claim the one whose
        /// pelvis or character root sits under this body's hierarchy. Missing it degrades to
        /// cosmetics — the grounder drives the vanilla LimbIK legs, which the retargeter
        /// overwrites bone by bone anyway — so null is an acceptable answer.
        /// </summary>
        private static GrounderIK FindGrounderFor(CharacterPrefab fullBody)
        {
            try
            {
                var bodyRoot = fullBody.transform;
                var grounders = UnityEngine.Object.FindObjectsOfType<GrounderIK>();
                if (grounders == null) return null;

                GrounderIK only = null;
                foreach (var g in grounders)
                {
                    if (!Interop.Alive(g)) continue;
                    only = grounders.Length == 1 ? g : only;
                    if (IsUnder(g.pelvis, bodyRoot) || IsUnder(g.characterRoot, bodyRoot)) return g;
                }
                // One grounder in the whole scene can't belong to anyone else.
                return only;
            }
            catch { return null; }
        }

        private static bool IsUnder(Transform t, Transform root)
        {
            var guard = 0;
            for (var cur = t; Interop.Alive(cur) && guard++ < 64; cur = cur.parent)
                if (cur == root) return true;
            return false;
        }
    }
}
