using System;
using System.IO;
using System.Text;

namespace LootOverhaul.Recon
{
    /// <summary>
    /// Session-scoped recon transcript under <c>UserData/LootOverhaul/recon/</c>. The file is
    /// the primary sink; the console (shared with every other mod in MelonLoader's single
    /// Latest.log, each line prefixed <c>[LootOverhaul]</c>) gets headlines only.
    /// </summary>
    public static class ReconLog
    {
        private static StreamWriter _writer;
        private static bool _closed;
        private static readonly object Gate = new object();

        public static string OutputDir => ModPaths.ReconDir;
        public static string CurrentFile { get; private set; }

        private static StreamWriter Writer
        {
            get
            {
                if (_writer != null) return _writer;
                if (_closed) return StreamWriter.Null;
                Directory.CreateDirectory(OutputDir);
                CurrentFile = Path.Combine(OutputDir, $"recon-{DateTime.Now:yyyyMMdd-HHmmss}.md");
                _writer = new StreamWriter(CurrentFile, append: true, Encoding.UTF8) { AutoFlush = true };
                _writer.WriteLine($"# LootOverhaul recon — {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                _writer.WriteLine($"mod version {Core.Version}, DLL {SelfCheck.ShortHash}");
                _writer.WriteLine();
                return _writer;
            }
        }

        private static string Stamp => $"[{DateTime.Now:HH:mm:ss.fff}]";

        /// <summary>A headline: goes to both the console and the file.</summary>
        public static void Headline(string text)
        {
            lock (Gate)
            {
                Core.Log.Msg(text);
                Writer.WriteLine($"{Stamp} {text}");
            }
        }

        /// <summary>Detail: file only, unless MirrorReconToConsole is on.</summary>
        public static void Line(string text = "")
        {
            lock (Gate)
            {
                Writer.WriteLine(text.Length == 0 ? "" : $"{Stamp} {text}");
                if (ModConfig.MirrorReconToConsole.Value && text.Length > 0) Core.Log.Msg(text);
            }
        }

        public static void Section(string title)
        {
            lock (Gate)
            {
                Writer.WriteLine();
                Writer.WriteLine($"## {title}");
                Writer.WriteLine();
                Core.Log.Msg($"--- recon: {title} ---");
            }
        }

        public static void KeyValue(string key, object value) => Line($"- **{key}**: {(value == null ? "null" : value.ToString())}");

        public static void Error(string context, Exception e)
        {
            lock (Gate)
            {
                Core.Log.Warning($"recon: {context}: {e.GetType().Name}: {e.Message}");
                Writer.WriteLine($"{Stamp} - _{context} FAILED: {e.GetType().Name}: {e.Message}_");
            }
        }

        /// <summary>Wraps a probe so one unavailable member can't abort a whole dump.</summary>
        public static void Try(string context, Action probe)
        {
            try { probe(); }
            catch (Exception e) { Error(context, e); }
        }

        public static void TryKeyValue(string key, Func<object> probe)
        {
            try { KeyValue(key, probe()); }
            catch (Exception e) { Line($"- **{key}**: _unavailable ({e.GetType().Name}: {e.Message})_"); }
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
