using System;
using System.Reflection;

namespace StayPutVR.Bite
{
    /// <summary>
    /// Reads the live jaw value out of the CustomAvatars mod, entirely by reflection.
    ///
    /// CustomAvatars already owns the UDP socket that VRCFaceTracking sends to, and two
    /// processes cannot bind the same port, so StayPutVR cannot listen for itself. Rather than
    /// take a build reference on a much larger mod — and inherit its load order, its gate and
    /// its version — this walks to <c>CustomAvatars.Core.Instance.FaceState</c> at runtime and
    /// calls <c>TryGet</c> on it. If CustomAvatars is absent, or renamed, or its face bridge is
    /// off, every lookup simply fails and biting stays switched off with a reason in the log.
    ///
    /// <c>FaceState</c> is keyed by the full OSC address, so the jaw arrives as
    /// <c>/avatar/parameters/FT/v2/JawOpen</c> from VRCFaceTracking's v2 set. Several spellings
    /// are tried because which one a rig sends depends on the tracking module, and
    /// <c>TryGet</c> rather than <c>Get</c> is used so "the parameter never arrived" is
    /// distinguishable from "the mouth is shut".
    /// </summary>
    public static class FaceLink
    {
        /// <summary>Tried in order; the first one present wins. A configured JawParam is tried before all of them.</summary>
        private static readonly string[] Candidates =
        {
            "/avatar/parameters/FT/v2/JawOpen",
            "/avatar/parameters/v2/JawOpen",
            "/avatar/parameters/JawOpen",
            "/avatar/parameters/SPVR_JawOpen",
        };

        private static object _faceState;
        private static MethodInfo _tryGet;
        private static PropertyInfo _secondsSince;
        private static float _retryAt = -100f;
        private static string _resolvedParam;
        private static string _why = "not looked for yet";
        private static bool _loggedFound;

        /// <summary>Why the jaw is unreadable, or the parameter it is being read from.</summary>
        public static string Status => _tryGet == null ? _why : $"{_resolvedParam ?? "(no jaw parameter yet)"}";
        public static bool Ready => _tryGet != null && _resolvedParam != null;

        /// <summary>Seconds since CustomAvatars last received any face message, or -1 if it never has.</summary>
        public static double SecondsSinceFaceData
        {
            get
            {
                try
                {
                    if (_faceState == null || _secondsSince == null) return -1;
                    return (double)_secondsSince.GetValue(_faceState);
                }
                catch { return -1; }
            }
        }

        /// <summary>The current jaw-open value, 0..1. False when there is nothing to read.</summary>
        public static bool TryJaw(out float value)
        {
            value = 0f;
            if (!Resolve()) return false;

            // A parameter that was working can stop arriving (tracking dropped, avatar changed),
            // so the resolved name is re-checked rather than trusted forever.
            if (_resolvedParam != null && Read(_resolvedParam, out value)) return true;

            _resolvedParam = null;
            foreach (var name in Names())
            {
                if (!Read(name, out value)) continue;
                _resolvedParam = name;
                if (!_loggedFound)
                {
                    _loggedFound = true;
                    Core.Log.Msg($"Jaw read from CustomAvatars: {name}");
                    ShockLog.Line($"jaw parameter {name} found in CustomAvatars' face state");
                }
                return true;
            }
            return false;
        }

        private static System.Collections.Generic.IEnumerable<string> Names()
        {
            var configured = (ModConfig.BiteJawParam.Value ?? "").Trim();
            if (configured.Length > 0) yield return configured;
            foreach (var name in Candidates) yield return name;
        }

        private static bool Read(string name, out float value)
        {
            value = 0f;
            try
            {
                var args = new object[] { name, 0f };
                var found = (bool)_tryGet.Invoke(_faceState, args);
                if (!found) return false;
                value = (float)args[1];
                return true;
            }
            catch { return false; }
        }

        /// <summary>Walk to CustomAvatars' FaceState, at most once every couple of seconds.</summary>
        private static bool Resolve()
        {
            if (_tryGet != null && _faceState != null) return true;
            if (UnityEngine.Time.unscaledTime < _retryAt) return false;
            _retryAt = UnityEngine.Time.unscaledTime + 2f;

            try
            {
                Assembly assembly = null;
                foreach (var candidate in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (candidate.GetName().Name == "CustomAvatars") { assembly = candidate; break; }
                }
                if (assembly == null) { _why = "CustomAvatars is not loaded (biting needs its face-tracking bridge)"; return false; }

                var coreType = assembly.GetType("CustomAvatars.Core");
                if (coreType == null) { _why = "CustomAvatars.Core not found"; return false; }

                var instance = coreType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (instance == null) { _why = "CustomAvatars has not finished starting"; return false; }

                var faceProperty = coreType.GetProperty("FaceState", BindingFlags.Public | BindingFlags.Instance);
                if (faceProperty == null) { _why = "CustomAvatars.Core has no FaceState property"; return false; }

                var state = faceProperty.GetValue(instance);
                if (state == null) { _why = "CustomAvatars' face bridge is off (FaceOscEnabled / FaceOscListenPort)"; return false; }

                var tryGet = state.GetType().GetMethod("TryGet", BindingFlags.Public | BindingFlags.Instance);
                if (tryGet == null) { _why = "FaceState has no TryGet method"; return false; }

                _faceState = state;
                _tryGet = tryGet;
                _secondsSince = state.GetType().GetProperty("SecondsSinceLastMessage", BindingFlags.Public | BindingFlags.Instance);
                _why = "";
                Core.Log.Msg("Face link established with CustomAvatars (jaw values available).");
                return true;
            }
            catch (Exception e)
            {
                _why = $"reflection into CustomAvatars threw {e.GetType().Name}";
                return false;
            }
        }
    }
}
