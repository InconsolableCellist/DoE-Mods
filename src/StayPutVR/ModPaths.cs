using System.IO;
using MelonLoader.Utils;

namespace StayPutVR
{
    /// <summary>Where the mod keeps its files: <c>UserData/StayPutVR/</c>.</summary>
    public static class ModPaths
    {
        private const string Folder = "StayPutVR";
        private static string _root;
        public static string Root => _root ??= Path.Combine(MelonEnvironment.UserDataDirectory, Folder);
        public static string LogDir => Path.Combine(Root, "logs");
    }
}
