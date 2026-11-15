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
        public static MelonPreferences_Entry<float> SeverityCurve;
        public static MelonPreferences_Entry<float> FallSeverityFloor;
        public static MelonPreferences_Entry<float> ReleaseSeconds;

        public static MelonPreferences_Entry<float> MinDamage;
        public static MelonPreferences_Entry<float> MinDamageFraction;
        public static MelonPreferences_Entry<float> CooldownSeconds;
        public static MelonPreferences_Entry<int> MaxPerMinute;
        public static MelonPreferences_Entry<string> IgnoreDamageTypes;
        public static MelonPreferences_Entry<float> ShieldMinorPotionSeconds;
        public static MelonPreferences_Entry<float> ShieldMajorPotionSeconds;
        public static MelonPreferences_Entry<float> ShieldStaffSeconds;
        public static MelonPreferences_Entry<float> ShieldLifeStealSeconds;
        public static MelonPreferences_Entry<float> ShieldStackMaxSeconds;
        /// <summary>True when Load moved a 0.5.1 <c>ShieldStaffSeconds</c> of 45 to the new per-tick default.</summary>
        public static bool StaffSecondsMigrated;

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

        public static MelonPreferences_Entry<bool> MimicShock;
        public static MelonPreferences_Entry<float> MimicShockStrength;
        public static MelonPreferences_Entry<string> MimicPath;

        public static MelonPreferences_Entry<bool> HudEnabled;

        public static MelonPreferences_Entry<bool> VerboseLogging;
        public static MelonPreferences_Entry<bool> LogEveryHit;
        public static MelonPreferences_Entry<bool> LogDatagrams;

        public static void Load()
        {
            Main = MelonPreferences.CreateCategory("StayPutVR");
            Enabled = Main.CreateEntry("Enabled", true);
            Armed = Main.CreateEntry("Armed", false, description: "Remembered from your last session. Click both sticks to change it.");

            Host = Main.CreateEntry("Host", "127.0.0.1", description: "Host and Port are the fallback, used only while the StayPutVR app is not found over OSC Query.");
            Port = Main.CreateEntry("Port", 9001, description: "The app's Settings > OSC receive port, which it only keeps with OSC Query off there.");
            ShockPath = Main.CreateEntry("ShockPath", "/avatar/parameters/Shock");
            SeverityCurve = Main.CreateEntry("SeverityCurve", 0.5f, description: "Exponent on the share of your remaining health a hit took; below 1 lifts small hits.");
            FallSeverityFloor = Main.CreateEntry("FallSeverityFloor", 0.5f, description: "Fall damage counts as at least this share, 0 to 1.");
            ReleaseSeconds = Main.CreateEntry("ReleaseSeconds", 0.15f, description: "Seconds between the trigger and the false that releases it.");

            MinDamage = Main.CreateEntry("MinDamage", 0f);
            MinDamageFraction = Main.CreateEntry("MinDamageFraction", 0f);
            CooldownSeconds = Main.CreateEntry("CooldownSeconds", 2f);
            MaxPerMinute = Main.CreateEntry("MaxPerMinute", 15, description: "Max triggers per rolling minute. 0 = no limit.");
            IgnoreDamageTypes = Main.CreateEntry("IgnoreDamageTypes", "", description: "Comma-separated types that never fire: Melee, Projectile, Magic, Splash, Kinetics, Fall, Trap, Poison, Other, Web, Wraith, Fire, Ice, LastChanceFailed, Devour, GeoCollision, Mimic, PvP, DarkLight.");
            ShieldMinorPotionSeconds = Main.CreateEntry("ShieldMinorPotionSeconds", 10f, description: "Heal shield: no shocks for this long after each kind of heal. 0 turns that kind off; the killing blow always fires.");
            ShieldMajorPotionSeconds = Main.CreateEntry("ShieldMajorPotionSeconds", 20f);
            ShieldStaffSeconds = Main.CreateEntry("ShieldStaffSeconds", 2f, description: "Added per beam tick (about one a second), up to ShieldStackMaxSeconds.");
            ShieldLifeStealSeconds = Main.CreateEntry("ShieldLifeStealSeconds", 2f, description: "Added per steal (shield absorb, vampire perks, Bloodlust ring on a kill), up to ShieldStackMaxSeconds.");
            ShieldStackMaxSeconds = Main.CreateEntry("ShieldStackMaxSeconds", 45f, description: "Most the staff and life steal can stack up to.");
            // 0.5.1 wrote 45 here as a flat length; as a per-tick amount that would be the whole cap at once.
            if (ShieldStaffSeconds.Value == 45f) { ShieldStaffSeconds.Value = 2f; StaffSecondsMigrated = true; }

            BiteEnabled = Main.CreateEntry("BiteEnabled", true);
            BiteVictimEnabled = Main.CreateEntry("BiteVictimEnabled", true);
            BitePath = Main.CreateEntry("BitePath", "/avatar/parameters/SPVR_Bite");
            BiteDamage = Main.CreateEntry("BiteDamage", 1f);
            BiteDamageType = Main.CreateEntry("BiteDamageType", "Melee", description: "Melee, Projectile, Magic, Splash, Kinetics, Fall, Trap, Poison, Other, Web, Wraith, Fire, Ice, Devour, GeoCollision, Mimic, PvP, DarkLight.");
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

            MimicShock = Main.CreateEntry("MimicShock", true, description: "Waking a mimic (a key in its lock, or a weapon touching it) shocks the player who did it, once per mimic. Its attacks then do not shock.");
            MimicShockStrength = Main.CreateEntry("MimicShockStrength", 0.5f, description: "How hard the mimic shock is, 0 to 1 of the max the app holds for the parameter it fires.");
            MimicPath = Main.CreateEntry("MimicPath", "", description: "What waking a mimic fires. Empty = ShockPath. Give it its own parameter to set its own intensity and duration in the app.");

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
