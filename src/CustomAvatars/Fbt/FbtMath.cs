using UnityEngine;

namespace CustomAvatars.Fbt
{
    /// <summary>
    /// Finite checks for everything that touches the IK targets. OpenVR can hand back a pose
    /// flagged valid whose matrix is garbage (seen in testing: NaN and Infinity calibration
    /// offsets), and one NaN written into a bone or a target poisons everything downstream —
    /// lerps hold NaN forever, so the damage outlives its cause. Nothing reaches the rig
    /// without passing these.
    /// </summary>
    public static class FbtMath
    {
        public static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);

        public static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);

        public static bool Finite(Quaternion q) =>
            Finite(q.x) && Finite(q.y) && Finite(q.z) && Finite(q.w);
    }
}
