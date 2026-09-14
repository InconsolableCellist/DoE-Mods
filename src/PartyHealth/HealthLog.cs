using System;
using System.IO;
using System.Text;

namespace PartyHealth
{
    /// <summary>
    /// Session log under <c>UserData/PartyHealth/logs/</c>: every friend the mod started
    /// tracking with everything it could read off them the first time, every health change
    /// with which source reported it (the game's own RPC, or the polled health object), and
    /// every bar that appeared or went away. The first-sight block and the source counts on
    /// the quit line are what settle the open question in the README: whether a remote
    /// avatar's health object is kept up to date on this client, or only the RPCs carry it.
    /// </summary>
    public static class HealthLog
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
                CurrentFile = Path.Combine(ModPaths.LogDir, $"health-{DateTime.Now:yyyyMMdd-HHmmss}.md");
                _writer = new StreamWriter(CurrentFile, append: true, Encoding.UTF8) { AutoFlush = true };
                _writer.WriteLine($"# PartyHealth session — {DateTime.Now:yyyy-MM-dd HH:mm:ss}, mod {Core.Version}");
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

        /// <summary>File only (console too with VerboseLogging).</summary>
        public static void Line(string text)
        {
            lock (Gate)
            {
                Writer.WriteLine($"{Stamp} {text}");
                if (ModConfig.VerboseLogging.Value) Core.Log.Msg(text);
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
