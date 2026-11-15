using System;
using CustomAvatars.Recon;
using Il2Cpp;
using UnityEngine;
using Interop = CustomAvatars.Recon.Interop;

namespace CustomAvatars.Avatars
{
    /// <summary>
    /// Keeps the game body's walk cycle honest on frames when the game itself isn't solving it.
    ///
    /// The game's body walks through FinalIK's animated locomotion: `IKSolverVR.Locomotion`
    /// writes `VRIK_IsMoving`, `VRIK_Horizontal`, `VRIK_Vertical`, `VRIK_Speed` and `VRIK_Turn`
    /// on the body's Animator from inside the VRIK solve. `CharacterPrefab.OnLateUpdate` only
    /// runs that solve while `isVisibleToLocalPlayerThisFrame` is set, but
    /// `CharacterPrefab.UpdatePhysics` calls `Animator.Update` every tick regardless. So the
    /// moment the game stops solving a body that was moving, the Animator goes on playing the
    /// last walk state it was given, for as long as the solve stays off. Nobody sees that in
    /// the plain game, because the body is unseen exactly then. With a custom avatar copying
    /// its pose, the avatar runs on the spot.
    ///
    /// Your own body is the worst case: the game never solves it in normal VR play
    /// (`ikEnabled` is false on every self swap), so your avatar's legs keep whatever walk
    /// state they last had. Peers hit it whenever their body drops out of the solve, which
    /// the logs show at swaps and respawns.
    ///
    /// So on any frame the game didn't solve, this writes the same parameters from how fast
    /// the body's root is actually moving, the way FinalIK would have: root-local velocity,
    /// smoothed with the solver's own `animationSmoothTime`, and moving or not by the solver's
    /// own `moveThreshold` with the same 0.9 hysteresis and 0.05 s stop delay. On frames the
    /// game does solve, it leaves everything to the game. Nothing goes over the network: each
    /// client already has every player's root, so each one works it out locally.
    /// </summary>
    public class WalkState
    {
        private static readonly int HashIsMoving = Animator.StringToHash("VRIK_IsMoving");
        private static readonly int HashHorizontal = Animator.StringToHash("VRIK_Horizontal");
        private static readonly int HashVertical = Animator.StringToHash("VRIK_Vertical");
        private static readonly int HashSpeed = Animator.StringToHash("VRIK_Speed");
        private static readonly int HashTurn = Animator.StringToHash("VRIK_Turn");

        // FinalIK's defaults, used only if the solver's own values can't be read.
        private const float DefaultMoveThreshold = 0.3f;
        private const float DefaultSmoothTime = 0.2f;
        private const float StopDelay = 0.05f;          // FinalIK: stopMoveTimer < 0.05
        private const float MaxPlausibleSpeed = 20f;    // m/s; above this the root was teleported

        private readonly AvatarPlayer _player;
        private readonly CharacterPrefab _body;
        private readonly string _who;

        private bool _haveLast;
        private Vector3 _lastPos;
        private Vector3 _velocityLocal;
        private Vector3 _velocityLocalV;
        private float _stopTimer = 1f;
        private bool _moving;

        private bool _driving;
        private int _takeovers;
        private float _nextLogAt;

        public WalkState(AvatarPlayer player, CharacterPrefab body, string who)
        {
            _player = player;
            _body = body;
            _who = who;
        }

        /// <summary>Call once a frame, after the game's own LateUpdate has run.</summary>
        public void Tick(float deltaTime)
        {
            if (!Interop.Alive(_body) || !Interop.Alive(_player)) return;

            Transform root;
            try { root = _body.transform; }
            catch { return; }
            if (!Interop.Alive(root)) return;

            // Track the root every frame, solved or not, so the first unsolved frame starts
            // from a real velocity rather than from zero or a stale position.
            Vector3 pos;
            try { pos = root.position; }
            catch { return; }
            var dt = Mathf.Max(deltaTime, 1e-4f);
            var worldVel = _haveLast ? (pos - _lastPos) / dt : Vector3.zero;
            _lastPos = pos;
            _haveLast = true;
            worldVel.y = 0f;
            if (worldVel.magnitude > MaxPlausibleSpeed) worldVel = Vector3.zero;

            var solved = GameSolvedThisFrame(out var solver);
            if (solved)
            {
                if (_driving)
                {
                    _driving = false;
                }
                // Follow the game's values so a hand-back the other way starts where it left off.
                try
                {
                    var animator = _player.RemoteAnimator;
                    if (Interop.Alive(animator))
                    {
                        _moving = animator.GetBool(HashIsMoving);
                        _velocityLocal = new Vector3(animator.GetFloat(HashHorizontal), 0f, animator.GetFloat(HashVertical))
                                         * BodyScale(root);
                        _stopTimer = _moving ? 0f : 1f;
                    }
                }
                catch { }
                return;
            }

            Drive(root, worldVel, dt, solver);
        }

        private void Drive(Transform root, Vector3 worldVel, float dt, Il2CppRootMotion.FinalIK.IKSolverVR solver)
        {
            Animator animator;
            try { animator = _player.RemoteAnimator; }
            catch { return; }
            if (!Interop.Alive(animator)) return;

            var moveThreshold = DefaultMoveThreshold;
            var smoothTime = DefaultSmoothTime;
            float minSpeed = 0.2f, maxSpeed = 3f;
            try
            {
                var loco = solver?.locomotion;
                if (loco != null)
                {
                    if (loco.moveThreshold > 0f) moveThreshold = loco.moveThreshold;
                    if (loco.animationSmoothTime > 0f) smoothTime = loco.animationSmoothTime;
                    if (loco.minAnimationSpeed > 0f) minSpeed = loco.minAnimationSpeed;
                    if (loco.maxAnimationSpeed > minSpeed) maxSpeed = loco.maxAnimationSpeed;
                }
            }
            catch { }

            var scale = BodyScale(root);
            Vector3 target;
            try { target = Quaternion.Inverse(root.rotation) * worldVel; }
            catch { return; }
            target.y = 0f;

            _velocityLocal = Vector3.SmoothDamp(_velocityLocal, target, ref _velocityLocalV, smoothTime,
                                                Mathf.Infinity, dt);

            var m = moveThreshold * scale;
            if (_moving) m *= 0.9f;
            if (_velocityLocal.sqrMagnitude > m * m) _stopTimer = 0f;
            else _stopTimer += dt;
            _moving = _stopTimer < StopDelay;

            try
            {
                animator.SetFloat(HashHorizontal, _velocityLocal.x / scale);
                animator.SetFloat(HashVertical, _velocityLocal.z / scale);
                animator.SetBool(HashIsMoving, _moving);
                animator.SetFloat(HashTurn, 0f);
                // The game zeroes Speed on a peer whose stream went quiet; a zero here would
                // play the walk at a standstill once they move again. Otherwise it is the
                // solver's playback rate and is left as the game set it.
                var speed = animator.GetFloat(HashSpeed);
                if (speed < minSpeed || speed > maxSpeed) animator.SetFloat(HashSpeed, Mathf.Clamp(1f, minSpeed, maxSpeed));
            }
            catch (Exception e)
            {
                Core.Log.Warning($"walk state: could not set {_who} animator ({e.GetType().Name}: {e.Message})");
                return;
            }

            if (!_driving)
            {
                _driving = true;
                _takeovers++;
                // Once at first, then at most every 30 s with a count, so a peer flickering in
                // and out of the solve can't flood the log.
                if (_takeovers == 1 || Time.unscaledTime >= _nextLogAt)
                {
                    _nextLogAt = Time.unscaledTime + 30f;
                    Core.Log.Msg($"    walk state: the game stopped animating {_who} legs " +
                                 $"({(_moving ? "moving" : "standing")}); driving them from their real speed " +
                                 $"({_takeovers} time(s) this swap).");
                }
            }
        }

        /// <summary>
        /// Did `CharacterPrefab.OnLateUpdate` run the VRIK solve this frame? It does only while
        /// the body counts as visible; an `ikEnabled` of false or a solver at LOD 2 means the
        /// locomotion step didn't run either.
        /// </summary>
        private bool GameSolvedThisFrame(out Il2CppRootMotion.FinalIK.IKSolverVR solver)
        {
            solver = null;
            try
            {
                var ik = _body.ik;
                if (Interop.Alive(ik)) solver = ik.solver;
                if (!_body.isVisibleToLocalPlayerThisFrame) return false;
                if (!_body.ikEnabled) return false;
                return solver != null && solver.LOD == 0;
            }
            catch { return false; }
        }

        private static float BodyScale(Transform root)
        {
            try
            {
                var s = root.lossyScale.y;
                return s > 0.01f ? s : 1f;
            }
            catch { return 1f; }
        }
    }
}
