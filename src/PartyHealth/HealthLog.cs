using System;
using System.IO;
using System.Text;
using System.Threading;

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
        // Lines are buffered and written to disk once a second from a timer thread, so a
        // burst of log lines never turns into a synchronous disk write per line on the
        // game's main thread. At worst the last second is missing after a hard crash.
        private static Timer _flushTimer;
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
                _writer = new StreamWriter(CurrentFile, append: true, Encoding.UTF8) { AutoFlush = false };
                _flushTimer = new Timer(_ => FlushNow(), null, 1000, 1000);
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

        /// <summary>Writes buffered lines to disk. Called once a second by the timer.</summary>
        private static void FlushNow()
        {
            lock (Gate)
            {
                try { _writer?.Flush(); }
                catch (Exception) { /* disk full or file gone: the next flush tries again */ }
            }
        }

        public static void Close()
        {
            lock (Gate)
            {
                _flushTimer?.Dispose();
                _flushTimer = null;
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
