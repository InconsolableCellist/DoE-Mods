using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace Descent.Recon
{
    /// <summary>
    /// The game's Player.log prints IL2CPP exceptions as a bare message line; the callback
    /// form carries the stack trace. Errors and exceptions go into the transcript, throttled
    /// and de-duplicated, so a storm of one NullReferenceException (43 of them after a hub
    /// layout pass on 2026-09-07) shows up as one entry with a count and a trace.
    /// </summary>
    public static class UnityLogTap
    {
        private static readonly Dictionary<string, int> Seen = new Dictionary<string, int>();
        private static int _distinct;
        private const int MaxDistinct = 60;
        public static bool Installed { get; private set; }

        public static void Install()
        {
            if (Installed) return;
            try
            {
                Application.add_logMessageReceived(DelegateSupport.ConvertDelegate<Application.LogCallback>((Action<string, string, LogType>)OnLog));
                // Release IL2CPP prints exceptions as a bare message; ask Unity for the full (native) trace
                // so the frames can be mapped back to methods through dump/script.json.
                try { Application.SetStackTraceLogType(LogType.Exception, StackTraceLogType.Full); }
                catch (Exception e) { Core.Log.Warning($"SetStackTraceLogType failed: {e.GetType().Name}"); }
                Installed = true;
                Core.Log.Msg("Unity log tap installed: exceptions and errors go to the recon transcript with stack traces.");
            }
            catch (Exception e) { Core.Log.Warning($"Unity log tap failed ({e.GetType().Name}: {e.Message}); Player.log is the only record of exceptions."); }
        }

        private static void OnLog(string condition, string stackTrace, LogType type)
        {
            try
            {
                if (type != LogType.Exception && type != LogType.Error) return;
                if (condition == null) return;
                if (condition.StartsWith("[Descent]") || condition.Contains("recon:")) return;
                var firstFrame = "";
                if (!string.IsNullOrEmpty(stackTrace))
                {
                    var nl = stackTrace.IndexOf('\n');
                    firstFrame = nl > 0 ? stackTrace.Substring(0, nl) : stackTrace;
                }
                var key = condition.Length > 160 ? condition.Substring(0, 160) : condition;
                key += "|" + firstFrame;
                if (Seen.TryGetValue(key, out var n))
                {
                    Seen[key] = n + 1;
                    if (n + 1 == 2 || n + 1 == 10 || n + 1 == 100 || (n + 1) % 1000 == 0) ReconLog.Line($"unity {type} ×{n + 1}: {key}");
                    return;
                }
                if (_distinct >= MaxDistinct) return;
                _distinct++;
                Seen[key] = 1;
                var trace = string.IsNullOrEmpty(stackTrace) ? "(no stack trace)" : stackTrace.Replace("\r", "").TrimEnd();
                if (trace.Length > 4000) trace = trace.Substring(0, 4000) + " …";
                ReconLog.Line($"unity {type}: {condition}\n```\n{trace}\n```");
            }
            catch { }
        }

        public static string Summary()
        {
            var total = 0;
            foreach (var kv in Seen) total += kv.Value;
            return $"{Seen.Count} distinct error/exception(s), {total} total";
        }
    }
}
