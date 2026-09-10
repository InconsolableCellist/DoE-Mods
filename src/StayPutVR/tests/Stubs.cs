// The minimum host OscSender.cs needs, so the real file can be compiled and exercised
// outside the game: a logger, the session log, the two config entries it reads, and
// UnityEngine.Time.unscaledTime. Nothing here is a reimplementation of anything under test.
using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public static class Time
    {
        private static readonly DateTime Start = DateTime.UtcNow;
        public static float unscaledTime => (float)(DateTime.UtcNow - Start).TotalSeconds;
    }
}

namespace StayPutVR
{
    public sealed class Logger
    {
        public static readonly List<string> Lines = new List<string>();
        public void Msg(string s) { Lines.Add("MSG " + s); Console.WriteLine("      [log] " + s); }
        public void Warning(string s) { Lines.Add("WARN " + s); Console.WriteLine("      [warn] " + s); }
        public void Error(string s) { Lines.Add("ERR " + s); Console.WriteLine("      [error] " + s); }
    }

    public static class Core
    {
        public static Logger Log { get; } = new Logger();
    }

    public static class ShockLog
    {
        public static readonly List<string> Lines = new List<string>();
        public static void Line(string s) => Lines.Add(s);
        public static void Headline(string s) => Lines.Add(s);
    }
}
