using System;
using MelonLoader;
using StayPutVR.Bite;
using StayPutVR.Hud;
using StayPutVR.Net;
using StayPutVR.Osc;
using StayPutVR.Trigger;

[assembly: MelonInfo(typeof(StayPutVR.Core), "StayPutVR", "0.3.0", "dan")]
[assembly: MelonGame("Othergate LLC", "Dungeons of Eternity")]

namespace StayPutVR
{
    /// <summary>
    /// StayPutVR: taking damage in the dungeon fires a shock through StayPutVR.
    ///
    /// One patch, one socket, one overlay. A postfix on <c>AvatarPlayer.OnDamaged</c> sees every
    /// hit that lands on your own avatar; <see cref="ShockPolicy"/> decides whether it should
    /// fire; <see cref="OscSender"/> sends one OSC datagram to StayPutVR's receive port. The
    /// traffic is one-way — StayPutVR's shock and bite parameters are fire-and-forget triggers,
    /// so there is no OSCQuery handshake and nothing to read back. That does mean OSC Query has
    /// to be <b>off</b> in StayPutVR, because with it on StayPutVR binds a random receive port
    /// instead of the configured one. See README.md.
    ///
    /// Arming is a single gesture — both thumbsticks clicked in, briefly to disarm and longer to
    /// arm — and there are no keyboard keys at all. The state is written back to
    /// <c>MelonPreferences.cfg</c> as it changes, so the link comes back next session however you
    /// left it. The desktop panel in the top right of the game window says which state you are in;
    /// in the headset the shock itself is the confirmation, and the gesture is on the sticks
    /// precisely so it needs no display.
    ///
    /// Biting is the one part that touches the network. A chomp — read from CustomAvatars' face
    /// tracking, since that mod owns the socket VRCFaceTracking sends to — bites the nearest
    /// player who has said they accept bites, and their own client applies the hit point to
    /// itself and fires its own device. Two Photon events on codes 180 and 181 carry the consent
    /// and the bite; a player without the mod, or with bites switched off, cannot be damaged or
    /// shocked by any of it. Both bite switches are off by default.
    ///
    /// Nothing is written to the game's profile. There is no mod gate: without biting the mod
    /// changes nothing any other player can observe, and biting has its own consent check that a
    /// gate would only duplicate.
    /// </summary>
    public class Core : MelonMod
    {
        public const string Version = "0.3.0";

        public static Core Instance { get; private set; }
        public static MelonLogger.Instance Log => Instance.LoggerInstance;

        public override void OnInitializeMelon()
        {
            Instance = this;
            ModConfig.Load();
            try { MelonPreferences.Save(); }
            catch (Exception e) { LoggerInstance.Warning($"Could not write MelonPreferences.cfg: {e.Message}"); }

            if (!ModConfig.Enabled.Value)
            {
                LoggerInstance.Msg("StayPutVR Enabled=false: no patches, no socket, no overlay.");
                return;
            }

            LoggerInstance.Msg($"StayPutVR {Version} — a hit fires {ModConfig.ShockPath.Value} at {ModConfig.Host.Value}:{ModConfig.Port.Value}. Logs in {ModPaths.LogDir}");
            LoggerInstance.Msg("Click BOTH thumbsticks in: a moment disarms, a second and a half arms. There are no keyboard keys.");
            LoggerInstance.Warning("The StayPutVR app must have OSC Query OFF for its receive port to be the configured one.");

            Hooks.Init(HarmonyInstance);
            DamageWatch.Install();
            Hooks.Report();

            PhotonHook.Install(HarmonyInstance);
            BiteNet.Init();
            BiteSense.Init();
            if (ModConfig.BiteEnabled.Value || ModConfig.BiteVictimEnabled.Value)
                LoggerInstance.Msg($"Biting: {(ModConfig.BiteEnabled.Value ? "you can bite" : "you cannot bite")}, " +
                                   $"{(ModConfig.BiteVictimEnabled.Value ? $"others can bite you ({ModConfig.BiteDamage.Value:0.#} HP, fires {ModConfig.BitePath.Value})" : "nobody can bite you")}. " +
                                   $"Events {BiteNet.CodePresence}/{BiteNet.CodeBite}.");
            else
                LoggerInstance.Msg("Biting is off at both ends (BiteEnabled and BiteVictimEnabled).");

            OscSender.Ensure(ModConfig.Host.Value, ModConfig.Port.Value);

            ShockLog.Headline($"StayPutVR {Version} started. Damage hook {(DamageWatch.Installed ? "installed" : "MISSING")}; link {OscSender.TargetDescription}.");
            // Whatever it was last session, that is what it is now.
            if (ModConfig.Armed.Value) ShockPolicy.SetArmed(true, "remembered from last session");
            else LoggerInstance.Msg("Disarmed. Hold both thumbsticks in to arm.");
        }

        public override void OnUpdate()
        {
            if (!ModConfig.Enabled.Value) return;

            ShockPolicy.Tick();
            VrToggle.Tick();
            BiteNet.Pump();
            BiteSense.Tick();
        }

        public override void OnGUI()
        {
            try { StatusHud.Draw(); }
            catch (Exception e) { LoggerInstance.Warning($"Panel draw threw: {e.GetType().Name}: {e.Message}"); }
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            // The roster is per-room; actor numbers do not survive a scene change.
            BiteNet.Clear($"scene changed to {sceneName}");
        }

        public override void OnApplicationQuit()
        {
            // Never leave the parameter latched true in StayPutVR on the way out: disarming
            // flushes a pending release, and the flush covers the already-disarmed case.
            if (ModConfig.Enabled.Value) ShockPolicy.SetArmed(false, "quitting");
            ShockPolicy.FlushRelease();
            ShockLog.Headline($"Quit. {ShockPolicy.Stats()}; {DamageWatch.Stats()}; {BiteSense.Describe()}; {JawWatch.Describe()}; {BiteNet.Stats()}; panel {StatusHud.Describe()}.");
            ShockLog.Close();
            OscSender.Close();
        }
    }
}
