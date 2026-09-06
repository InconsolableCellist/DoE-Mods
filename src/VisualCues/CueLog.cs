using System;
using System.IO;
using System.Text;

namespace VisualCues
{
    /// <summary>
    /// Session log under <c>UserData/VisualCues/logs/</c>: every cue the mod raised or refused
    /// and, when <c>LogSounds</c> is on, every positioned sound it tried to attribute to an
    /// enemy. That second stream is the tuning tool for the noise cue: it says which sound
    /// names came from which enemy at what distance, so the attribution radius and the
    /// name filters can be adjusted from a real session instead of guessed.
    /// </summary>
    public static class CueLog
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
                CurrentFile = Path.Combine(ModPaths.LogDir, $"cues-{DateTime.Now:yyyyMMdd-HHmmss}.md");
                _writer = new StreamWriter(CurrentFile, append: true, Encoding.UTF8) { AutoFlush = true };
                _writer.WriteLine($"# VisualCues session — {DateTime.Now:yyyy-MM-dd HH:mm:ss}, mod {Core.Version}");
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
