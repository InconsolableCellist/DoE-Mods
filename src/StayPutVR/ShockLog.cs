using System;
using System.IO;
using System.Text;

namespace StayPutVR
{
    /// <summary>
    /// Session log under <c>UserData/StayPutVR/logs/</c>. Every hit the mod saw and what it
    /// decided goes in here — fired, or held back with the reason: disarmed, under the damage
    /// floor, inside the cooldown, over the per-minute budget, an ignored damage type, no
    /// socket. A shock device that fires when you did not expect it, or fails to when you
    /// did, is only diagnosable if the refusals are written down as plainly as the shots, so
    /// nothing here is conditional on a verbose flag.
    /// </summary>
    public static class ShockLog
    {
        private static StreamWriter _writer;
        private static bool _closed;
        private static readonly object Gate = new object();

        public static string CurrentFile { get; private set; }

        private static StreamWriter Writer
        {
            get
            {
                if (_writer != null) return _writer;
                if (_closed) return StreamWriter.Null;
                Directory.CreateDirectory(ModPaths.LogDir);
                CurrentFile = Path.Combine(ModPaths.LogDir, $"shocks-{DateTime.Now:yyyyMMdd-HHmmss}.md");
                _writer = new StreamWriter(CurrentFile, append: true, Encoding.UTF8) { AutoFlush = true };
                _writer.WriteLine($"# StayPutVR session — {DateTime.Now:yyyy-MM-dd HH:mm:ss}, mod {Core.Version}");
                _writer.WriteLine();
                return _writer;
            }
        }

        private static string Stamp => $"[{DateTime.Now:HH:mm:ss.fff}]";

        /// <summary>File plus console.</summary>
        public static void Headline(string text)
        {
            lock (Gate)
            {
                Core.Log.Msg(text);
                Writer.WriteLine($"{Stamp} {text}");
            }
        }

        /// <summary>File always; console as well when VerboseLogging is on.</summary>
        public static void Line(string text)
        {
            lock (Gate)
            {
                Writer.WriteLine($"{Stamp} {text}");
                if (ModConfig.VerboseLogging != null && ModConfig.VerboseLogging.Value) Core.Log.Msg(text);
            }
        }

        public static void Close()
        {
            lock (Gate)
            {
                if (_writer == null) { _closed = true; return; }
                _closed = true;
                _writer.WriteLine();
                _writer.WriteLine($"_closed {DateTime.Now:HH:mm:ss}_");
                _writer.Dispose();
                _writer = null;
            }
        }
    }
}
