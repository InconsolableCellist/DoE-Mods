using System.IO;
using MelonLoader.Utils;

namespace Descent
{
    /// <summary>Where the mod keeps its files: <c>UserData/Descent/</c>.</summary>
    public static class ModPaths
    {
        private const string Folder = "Descent";

        private static string _root;

        public static string Root => _root ??= Path.Combine(MelonEnvironment.UserDataDirectory, Folder);

        public static string ReconDir => Path.Combine(Root, "recon");

        /// <summary>The run file: every descent this machine has seen, shared by the whole party.
        public static string RunsFile =>
            Path.Combine(Root, "runs.json");

        private static string Sanitize(string id)
        {
            if (string.IsNullOrEmpty(id)) return "default";
            var invalid = Path.GetInvalidFileNameChars();
            var chars = id.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
                if (System.Array.IndexOf(invalid, chars[i]) >= 0) chars[i] = '_';
            return new string(chars);
        }
    }
}
