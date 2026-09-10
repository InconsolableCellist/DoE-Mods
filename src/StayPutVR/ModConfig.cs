using MelonLoader;

namespace StayPutVR
{
    /// <summary>
    /// Settings in <c>UserData/MelonPreferences.cfg</c>, section <c>[StayPutVR]</c>. Read where
    /// they are used, so an edit applies on the next relaunch without a rebuild.
    /// </summary>
    public static class ModConfig
    {
        public static MelonPreferences_Category Main;
        public static MelonPreferences_Category Dev;

        public static MelonPreferences_Entry<bool> Enabled;
        /// <summary>Armed or not. Written whenever the sticks change it, so it survives a restart.</summary>
        public static MelonPreferences_Entry<bool> Armed;

        public static MelonPreferences_Entry<string> Host;
        public static MelonPreferences_Entry<int> Port;
        public static MelonPreferences_Entry<string> ShockPath;
        public static MelonPreferences_Entry<string> ValueType;
        public static MelonPreferences_Entry<float> ReleaseSeconds;

        public static MelonPreferences_Entry<float> MinDamage;
        public static MelonPreferences_Entry<float> MinDamageFraction;
        public static MelonPreferences_Entry<float> CooldownSeconds;
        public static MelonPreferences_Entry<int> MaxPerMinute;
        public static MelonPreferences_Entry<string> IgnoreDamageTypes;

        public static MelonPreferences_Entry<bool> BiteEnabled;
        public static MelonPreferences_Entry<bool> BiteVictimEnabled;
        public static MelonPreferences_Entry<string> BitePath;
        public static MelonPreferences_Entry<float> BiteDamage;
        public static MelonPreferences_Entry<string> BiteDamageType;
        public static MelonPreferences_Entry<int> BiteMaxPerMinute;
        public static MelonPreferences_Entry<string> BiteJawParam;
        public static MelonPreferences_Entry<float> BiteOpenThreshold;
        public static MelonPreferences_Entry<float> BiteCloseThreshold;
        public static MelonPreferences_Entry<float> BiteMinOpenSeconds;
        public static MelonPreferences_Entry<float> BiteMaxOpenSeconds;
        public static MelonPreferences_Entry<float> BiteSnapSeconds;
        public static MelonPreferences_Entry<float> BiteGestureCooldownSeconds;
        public static MelonPreferences_Entry<float> BiteRangeMeters;
        public static MelonPreferences_Entry<float> BiteVerticalMeters;
        public static MelonPreferences_Entry<float> BiteFacingAngle;
        public static MelonPreferences_Entry<float> BiteCooldownSeconds;

        public static MelonPreferences_Entry<bool> HudEnabled;

        public static MelonPreferences_Entry<bool> VerboseLogging;
        public static MelonPreferences_Entry<bool> LogEveryHit;
        public static MelonPreferences_Entry<bool> LogDatagrams;

        public static void Load()
        {
            Main = MelonPreferences.CreateCategory("StayPutVR");
            Enabled = Main.CreateEntry("Enabled", true);
            Armed = Main.CreateEntry("Armed", false, description: "Remembered from your last session. Click both sticks to change it.");

            Host = Main.CreateEntry("Host", "127.0.0.1");
            Port = Main.CreateEntry("Port", 9001, description: "StayPutVR's OSC receive port. Turn OSC Query off in StayPutVR or it binds a random port instead.");
            ShockPath = Main.CreateEntry("ShockPath", "/avatar/parameters/Shock");
            ValueType = Main.CreateEntry("ValueType", "bool", description: "bool, int or float.");
            ReleaseSeconds = Main.CreateEntry("ReleaseSeconds", 0.15f, description: "Seconds between the trigger and the false that releases it.");

            MinDamage = Main.CreateEntry("MinDamage", 0f);
            MinDamageFraction = Main.CreateEntry("MinDamageFraction", 0f);
            CooldownSeconds = Main.CreateEntry("CooldownSeconds", 2f);
            MaxPerMinute = Main.CreateEntry("MaxPerMinute", 15, description: "Max triggers per rolling minute. 0 = no limit.");
            IgnoreDamageTypes = Main.CreateEntry("IgnoreDamageTypes", "", description: "Comma-separated types that never fire: Melee, Projectile, Magic, Splash, Kinetics, Fall, Trap, Poison, Other, Web, Wraith, Fire, Ice, LastChanceFailed, Devour, GeoCollision, Mimic, PvP.");

            BiteEnabled = Main.CreateEntry("BiteEnabled", true);
            BiteVictimEnabled = Main.CreateEntry("BiteVictimEnabled", true);
            BitePath = Main.CreateEntry("BitePath", "/avatar/parameters/SPVR_Bite");
            BiteDamage = Main.CreateEntry("BiteDamage", 1f);
            BiteDamageType = Main.CreateEntry("BiteDamageType", "Melee", description: "Melee, Projectile, Magic, Splash, Kinetics, Fall, Trap, Poison, Other, Web, Wraith, Fire, Ice, Devour, GeoCollision, Mimic, PvP.");
            BiteMaxPerMinute = Main.CreateEntry("BiteMaxPerMinute", 6);
            BiteJawParam = Main.CreateEntry("BiteJawParam", "");
            BiteOpenThreshold = Main.CreateEntry("BiteOpenThreshold", 0.55f);
            BiteCloseThreshold = Main.CreateEntry("BiteCloseThreshold", 0.15f);
            BiteMinOpenSeconds = Main.CreateEntry("BiteMinOpenSeconds", 0.08f);
            BiteMaxOpenSeconds = Main.CreateEntry("BiteMaxOpenSeconds", 1.2f);
            BiteSnapSeconds = Main.CreateEntry("BiteSnapSeconds", 0.2f);
            BiteGestureCooldownSeconds = Main.CreateEntry("BiteGestureCooldownSeconds", 0.6f);
            BiteRangeMeters = Main.CreateEntry("BiteRangeMeters", 0.6f);
            BiteVerticalMeters = Main.CreateEntry("BiteVerticalMeters", 0.9f);
            BiteFacingAngle = Main.CreateEntry("BiteFacingAngle", 70f);
            BiteCooldownSeconds = Main.CreateEntry("BiteCooldownSeconds", 3f);

            HudEnabled = Main.CreateEntry("HudEnabled", true);

            Dev = MelonPreferences.CreateCategory("StayPutVR_Dev");
            VerboseLogging = Dev.CreateEntry("VerboseLogging", false);
            LogEveryHit = Dev.CreateEntry("LogEveryHit", true);
            LogDatagrams = Dev.CreateEntry("LogDatagrams", false);
        }

        /// <summary>Write the armed state back to disk so it is still there next launch.</summary>
        public static void SaveArmed(bool armed)
        {
            try
            {
                Armed.Value = armed;
                MelonPreferences.Save();
            }
            catch (System.Exception e) { Core.Log.Warning($"Could not save the armed state: {e.Message}"); }
        }
    }
}
