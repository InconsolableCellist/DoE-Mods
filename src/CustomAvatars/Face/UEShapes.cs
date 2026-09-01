using System;
using System.Collections.Generic;

namespace CustomAvatars.Face
{
    /// <summary>
    /// The canonical ordered list of Unified Expressions shapes. **This is the wire contract**:
    /// the face stream sends an index into this array, so reordering it would silently make two
    /// builds disagree about which shape is which. Append only, never reorder.
    ///
    /// Note this is our own ordering, matching the exporter's, not VRCFaceTracking's internal
    /// enum order — which differs and omits EyeClosed/EyeLook entirely. Both ends of our wire
    /// use this list, so what matters is that it never changes, not whose order it matches.
    /// </summary>
    public static class UEShapes
    {
        public static readonly string[] Canonical =
        {
            "EyeLookOutRight","EyeLookInRight","EyeLookUpRight","EyeLookDownRight",
            "EyeLookOutLeft","EyeLookInLeft","EyeLookUpLeft","EyeLookDownLeft",

            "EyeClosedRight","EyeClosedLeft","EyeSquintRight","EyeSquintLeft",
            "EyeWideRight","EyeWideLeft","EyeDilationRight","EyeDilationLeft",
            "EyeConstrictRight","EyeConstrictLeft",

            "BrowPinchRight","BrowPinchLeft","BrowLowererRight","BrowLowererLeft",
            "BrowInnerUpRight","BrowInnerUpLeft","BrowOuterUpRight","BrowOuterUpLeft",

            "NoseSneerRight","NoseSneerLeft","NasalDilationRight","NasalDilationLeft",
            "NasalConstrictRight","NasalConstrictLeft",

            "CheekSquintRight","CheekSquintLeft","CheekPuffRight","CheekPuffLeft",
            "CheekSuckRight","CheekSuckLeft",

            "JawOpen","MouthClosed","JawRight","JawLeft","JawForward","JawBackward",
            "JawClench","JawMandibleRaise",

            "LipSuckUpperRight","LipSuckUpperLeft","LipSuckLowerRight","LipSuckLowerLeft",
            "LipSuckCornerRight","LipSuckCornerLeft",
            "LipFunnelUpperRight","LipFunnelUpperLeft","LipFunnelLowerRight","LipFunnelLowerLeft",
            "LipPuckerUpperRight","LipPuckerUpperLeft","LipPuckerLowerRight","LipPuckerLowerLeft",

            "MouthUpperUpRight","MouthUpperUpLeft","MouthLowerDownRight","MouthLowerDownLeft",
            "MouthUpperDeepenRight","MouthUpperDeepenLeft",
            "MouthUpperRight","MouthUpperLeft","MouthLowerRight","MouthLowerLeft",
            "MouthCornerPullRight","MouthCornerPullLeft","MouthCornerSlantRight","MouthCornerSlantLeft",
            "MouthFrownRight","MouthFrownLeft","MouthStretchRight","MouthStretchLeft",
            "MouthDimpleRight","MouthDimpleLeft","MouthRaiserUpper","MouthRaiserLower",
            "MouthPressRight","MouthPressLeft","MouthTightenerRight","MouthTightenerLeft",

            "TongueOut","TongueUp","TongueDown","TongueRight","TongueLeft","TongueRoll",
            "TongueBendDown","TongueCurlUp","TongueSquish","TongueFlat",
            "TongueTwistRight","TongueTwistLeft",
        };

        public static int Count => Canonical.Length;

        private static readonly Dictionary<string, int> Index = Build();

        private static Dictionary<string, int> Build()
        {
            var map = new Dictionary<string, int>(Canonical.Length, StringComparer.Ordinal);
            for (var i = 0; i < Canonical.Length; i++) map[Canonical[i]] = i;
            return map;
        }

        /// <summary>Wire id for a shape name, or -1 if it isn't one we carry.</summary>
        public static int IdOf(string name) =>
            name != null && Index.TryGetValue(name, out var id) ? id : -1;
    }
}
