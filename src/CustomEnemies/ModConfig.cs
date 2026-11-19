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
        /// <summary>Switch off the enemy's particle, trail and line renderers (Death's aura and weapon trail) under a skin.</summary>
        public static MelonPreferences_Entry<bool> HideVanillaEffects;
        public static MelonPreferences_Entry<bool> VerboseLogging;
        /// <summary>Read by the shared PoseRetargeter (as in CustomAvatars' [Dev] section).</summary>
        public static MelonPreferences_Entry<float> RetargetHipsFollow;
        public static MelonPreferences_Entry<bool> RetargetAlignAtCapture;
        /// <summary>For later migrations: MelonLoader keeps saved values, so a changed default needs one.</summary>
        public static MelonPreferences_Entry<int> ConfigVersion;

        public static void Load()
        {
            Main = MelonPreferences.CreateCategory("CustomEnemies");
            Enabled = Main.CreateEntry("Enabled", true);
            PlaceholderOn = Main.CreateEntry("PlaceholderOn", "",
                description: "Enemies that get the built-in placeholder skin (a test dummy made of primitives), by name or Sauron ID, comma separated. Empty = none. Only changes what you see.");
            HideVanilla = Main.CreateEntry("HideVanilla", true,
                description: "Hide the vanilla body under a skin. It stays in the scene as a shadow, which keeps it animating. Off draws both, for lining a skin up.");
            HeadFollowsBone = Main.CreateEntry("HeadFollowsBone", true,
                description: "The placeholder's head follows the enemy's head bone, so attacks show. Off: the whole placeholder is rigid.");
            HideVanillaEffects = Main.CreateEntry("HideVanillaEffects", true,
                description: "Switch off the enemy's own effects (Death's shadow aura and weapon trail) while it wears a skin. They carry no animation, so this changes nothing but the look.");
            VerboseLogging = Main.CreateEntry("VerboseLogging", true,
                description: "Log every skin attached, reset and removed.");
            RetargetHipsFollow = Main.CreateEntry("RetargetHipsFollow", 1.0f,
                description: "How much of the enemy's hip movement (bob, lean) the skin's hips copy. 1 = all, 0 = none.");
            RetargetAlignAtCapture = Main.CreateEntry("RetargetAlignAtCapture", true,
                description: "Line each of the skin's limbs up with the enemy's when the pose copy starts (the skin is loaded in its bind pose, the enemy is standing). Leave on.");
            ConfigVersion = Main.CreateEntry("ConfigVersion", 1, is_hidden: true);

            // v2: the placeholder is a testing aid, off by default. A player without a skin file
            // for an enemy sees the vanilla enemy. The old default ("Death") is cleared; anything
            // else the player typed is theirs and stays.
            if (ConfigVersion.Value < 2)
            {
                if (string.Equals((PlaceholderOn.Value ?? "").Trim(), "Death", System.StringComparison.OrdinalIgnoreCase))
                    PlaceholderOn.Value = "";
                ConfigVersion.Value = 2;
            }
        }
    }
}
