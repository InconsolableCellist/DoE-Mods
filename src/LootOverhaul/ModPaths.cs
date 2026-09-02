using System.IO;
using MelonLoader.Utils;

namespace LootOverhaul
{
    /// <summary>Where the mod keeps its files: <c>UserData/LootOverhaul/</c>.</summary>
    public static class ModPaths
    {
        private const string Folder = "LootOverhaul";

        private static string _root;

        public static string Root => _root ??= Path.Combine(MelonEnvironment.UserDataDirectory, Folder);

        /// <summary>One inventory file per account, so two people sharing a PC don't share a bag.</summary>
        public static string InventoryFile(string accountId) =>
            Path.Combine(Root, "inventory", $"{Sanitize(accountId)}.json");

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
