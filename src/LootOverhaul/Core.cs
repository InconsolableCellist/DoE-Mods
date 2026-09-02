using System;
using MelonLoader;
using LootOverhaul;
using LootOverhaul.Gate;
using LootOverhaul.Net;

[assembly: MelonInfo(typeof(Core), "LootOverhaul", "0.1.0", "dan")]
[assembly: MelonGame("Othergate LLC", "Dungeons of Eternity")]

namespace LootOverhaul
{
    /// <summary>
    /// LootOverhaul: Diablo-style weapon drops, a weight-limited loot bag, and a lobby booth
    /// to sell and equip from it. See docs/LOOT-OVERHAUL.md for the investigation and the
    /// design decisions this build follows.
    ///
    /// This is the scaffold build: gate, roster, transport and handshake carried over from
    /// CustomAvatars under this mod's own identity (`lo.*` player properties, event block
    /// 150–159, its own UserData folder). No gameplay patches yet.
    /// </summary>
    public class Core : MelonMod
    {
        public const string Version = "0.1.0";

        public static Core Instance { get; private set; }
        public static MelonLogger.Instance Log => Instance.LoggerInstance;

        private ModRoster _roster;
        private ModHandshake _handshake;

        public override void OnInitializeMelon()
        {
            Instance = this;
            ModConfig.Load();
            try { MelonPreferences.Save(); }
            catch (Exception e) { LoggerInstance.Warning($"Could not write MelonPreferences.cfg: {e.Message}"); }

            LoggerInstance.Msg($"LootOverhaul {Version} — scaffold: gate + transport only, no gameplay changes yet.");
            LoggerInstance.Msg($"Data folder: {ModPaths.Root}");

            SelfCheck.LogSelfHash(LoggerInstance);

            // One read-only prefix on Photon's inbound dispatch. CustomAvatars installs the same
            // kind of prefix from its own Harmony instance; Harmony chains them, so both mods
            // see every event and neither depends on the other being present.
            PhotonHook.Install(HarmonyInstance);
            ModNet.Init();

            _roster = new ModRoster();
            _handshake = new ModHandshake(_roster);
            ModGate.ActiveChanged += OnGateChanged;
        }

        public override void OnUpdate()
        {
            var dt = UnityEngine.Time.unscaledDeltaTime;
            _roster.Tick(dt);
            ModGate.Evaluate(_roster);
            ModNet.Pump();
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            if (ModConfig.VerboseLogging.Value)
                LoggerInstance.Msg($"Scene initialized: {sceneName} (#{buildIndex}). Gate: {ModGate.Reason}");
        }

        public override void OnApplicationQuit()
        {
            ModGate.ForceInert("application quitting");
        }

        private void OnGateChanged(bool active)
        {
            // Features arm and disarm here once they exist: drop rolls on the master, the
            // loot-tag table, the booth in the lobby. Nothing to do in the scaffold.
        }
    }
}
