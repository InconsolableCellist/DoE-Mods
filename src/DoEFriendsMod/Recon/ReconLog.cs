using System;
using System.IO;
using System.Text;
using MelonLoader;
using MelonLoader.Utils;

namespace DoEFriendsMod.Recon
{
    /// <summary>
    /// Session-scoped recon transcript. Hierarchy and blendshape dumps run to thousands of
    /// lines, which is unusable in the MelonLoader console but exactly right in a file you
    /// can grep after taking the headset off — so the file is the primary sink and the
    /// console gets headlines only.
    /// </summary>
    public static class ReconLog
    {
        private static StreamWriter _writer;
        private static bool _closed;
        private static readonly object Gate = new object();

        public static string OutputDir =>
            Path.Combine(MelonEnvironment.UserDataDirectory, "DoEFriendsMod", "recon");

        public static string CurrentFile { get; private set; }

        private static StreamWriter Writer
        {
            get
            {
                if (_writer != null) return _writer;
                // OnApplicationQuit fires while Update is still running for a few frames;
                // without this the shutdown ticks opened a second, near-empty transcript.
                if (_closed) return StreamWriter.Null;
                Directory.CreateDirectory(OutputDir);
                CurrentFile = Path.Combine(OutputDir, $"recon-{DateTime.Now:yyyyMMdd-HHmmss}.md");
                _writer = new StreamWriter(CurrentFile, append: true, Encoding.UTF8) { AutoFlush = true };
                _writer.WriteLine($"# DoEFriendsMod recon — {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                _writer.WriteLine($"mod version {Core.Version}");
                _writer.WriteLine();
                return _writer;
            }
        }

        /// <summary>A headline: goes to both the console and the file.</summary>
        public static void Headline(string text)
        {
            lock (Gate)
            {
                Core.Log.Msg(text);
                Writer.WriteLine(text);
            }
        }

        /// <summary>Detail: file only, unless MirrorReconToConsole is on.</summary>
        public static void Line(string text = "")
        {
            lock (Gate)
            {
                Writer.WriteLine(text);
                if (ModConfig.MirrorReconToConsole.Value && text.Length > 0)
                    Core.Log.Msg(text);
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

        public static void KeyValue(string key, object value) => Line($"- **{key}**: {Describe(value)}");

        public static void Error(string context, Exception e)
        {
            lock (Gate)
            {
                Core.Log.Warning($"recon: {context}: {e.GetType().Name}: {e.Message}");
                Writer.WriteLine($"- _{context} FAILED: {e.GetType().Name}: {e.Message}_");
            }
        }

        /// <summary>
        /// Wraps a probe so one unavailable member can't abort a whole dump — half a recon
        /// pass is still worth a headset session, and the failure line names what to fix.
        /// </summary>
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

        private static string Describe(object value) => value == null ? "null" : value.ToString();

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
