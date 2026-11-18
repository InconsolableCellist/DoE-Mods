using MelonLoader;

namespace CustomEnemies
{
    /// <summary>
    /// Settings in UserData/MelonPreferences.cfg under [CustomEnemies]. Numpad3 reloads them.
    /// </summary>
    public static class ModConfig
    {
        public static MelonPreferences_Category Main;

        public static MelonPreferences_Entry<bool> Enabled;
        /// <summary>Enemies that get the placeholder skin, by name or Sauron ID, comma separated.</summary>
        public static MelonPreferences_Entry<string> PlaceholderOn;
        /// <summary>Hide the vanilla body (ShadowsOnly) under a skin. Off shows both, for lining them up.</summary>
        public static MelonPreferences_Entry<bool> HideVanilla;
        /// <summary>The placeholder's head follows the vanilla head bone, so its attacks show.</summary>
        public static MelonPreferences_Entry<bool> HeadFollowsBone;
        public static MelonPreferences_Entry<bool> VerboseLogging;
        /// <summary>For later migrations: MelonLoader keeps saved values, so a changed default needs one.</summary>
        public static MelonPreferences_Entry<int> ConfigVersion;

        public static void Load()
        {
            Main = MelonPreferences.CreateCategory("CustomEnemies");
            Enabled = Main.CreateEntry("Enabled", true);
            PlaceholderOn = Main.CreateEntry("PlaceholderOn", "Death",
                description: "Enemies that get the built-in placeholder skin (a test dummy made of primitives), by name or Sauron ID, comma separated. Empty = none. Only changes what you see.");
            HideVanilla = Main.CreateEntry("HideVanilla", true,
                description: "Hide the vanilla body under a skin. It stays in the scene as a shadow, which keeps it animating. Off draws both, for lining a skin up.");
            HeadFollowsBone = Main.CreateEntry("HeadFollowsBone", true,
                description: "The placeholder's head follows the enemy's head bone, so attacks show. Off: the whole placeholder is rigid.");
            VerboseLogging = Main.CreateEntry("VerboseLogging", true,
                description: "Log every skin attached, reset and removed.");
            ConfigVersion = Main.CreateEntry("ConfigVersion", 1, is_hidden: true);
        }
    }
}
