using System;
using System.IO;
using MelonLoader;
using MelonLoader.Utils;

namespace CustomAvatars
{
    /// <summary>
    /// Where the mod keeps its files, and the one-time move from the old name.
    ///
    /// The mod used to be called DoEFriendsMod, so an existing install has its avatars in
    /// <c>UserData/DoEFriendsMod/</c>. Those files are hand-copied and irreplaceable — a friend's
    /// avatar may not exist anywhere else — so renaming the mod must not strand them. If the old
    /// folder is there and the new one isn't, we move it and say so. If both exist we touch
    /// neither and let the user sort it out, because merging two folders silently is how you
    /// overwrite the file someone wanted to keep.
    /// </summary>
    public static class ModPaths
    {
        private const string OldFolder = "DoEFriendsMod";
        private const string NewFolder = "CustomAvatars";

        private static string _root;

        public static string Root
        {
            get
            {
                if (_root != null) return _root;
                _root = Path.Combine(MelonEnvironment.UserDataDirectory, NewFolder);
                var old = Path.Combine(MelonEnvironment.UserDataDirectory, OldFolder);

                try
                {
                    if (Directory.Exists(old) && !Directory.Exists(_root))
                    {
                        Directory.Move(old, _root);
                        Core.Log.Msg($"Moved your files from UserData/{OldFolder}/ to UserData/{NewFolder}/ — the mod was renamed.");
                    }
                    else if (Directory.Exists(old))
                    {
                        Core.Log.Warning($"UserData/{OldFolder}/ is still there from the old name. Nothing reads it any more — " +
                                         $"move anything you want to keep into UserData/{NewFolder}/ and delete it.");
                    }
                }
                catch (Exception e)
                {
                    Core.Log.Warning($"Could not move UserData/{OldFolder}/ to UserData/{NewFolder}/: {e.Message}");
                }

                return _root;
            }
        }

        public static string AvatarsDir => Path.Combine(Root, "Avatars");
        public static string ReconDir => Path.Combine(Root, "recon");
    }
}
