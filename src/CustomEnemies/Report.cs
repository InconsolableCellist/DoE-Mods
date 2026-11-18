using System;
using System.IO;
using System.Text;
using CustomEnemies.Skins;

namespace CustomEnemies
{
    /// <summary>
    /// The session report: <c>UserData/CustomEnemies/recon/report-&lt;time&gt;.md</c>. Every skin's
    /// build, its pose capture and its measurements while it runs (head and hand offsets from the
    /// enemy, whether the vanilla body stayed hidden, the skin's frame cost), so a test run can be
    /// read afterwards without anyone describing what they saw. Headlines also go to the console.
    /// </summary>
    public static class Report
    {
        private static string _path;
        private static bool _failed;

        public static void Line(string text)
        {
            if (_failed) return;
            try
            {
                if (_path == null)
                {
                    var dir = Path.Combine(SkinLibrary.Folder, "recon");
                    Directory.CreateDirectory(dir);
                    _path = Path.Combine(dir, $"report-{DateTime.Now:yyyyMMdd-HHmmss}.md");
                    File.WriteAllText(_path, $"# CustomEnemies {Core.Version} — {DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}{Environment.NewLine}", Encoding.UTF8);
                    Core.Log.Msg($"Report: {_path}");
                }
                File.AppendAllText(_path, $"{DateTime.Now:HH:mm:ss} {text}{Environment.NewLine}", Encoding.UTF8);
            }
            catch (Exception e)
            {
                _failed = true;
                Core.Log.Warning($"Report file unavailable ({e.Message}); measurements go to the console only.");
            }
        }
    }
}
