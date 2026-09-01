using System;
using HarmonyLib;
using Il2CppPhoton.Realtime;
using Il2CppExitGames.Client.Photon;

namespace CustomAvatars.Net
{
    /// <summary>
    /// The single Harmony patch we place on Photon: a read-only prefix on
    /// <c>LoadBalancingClient.OnEvent</c>, which every inbound event passes through.
    ///
    /// One hook, several subscribers (the recon tally, the mod's own event dispatch) — rather
    /// than each subsystem patching separately, or registering an <c>IOnEventCallback</c>,
    /// which would mean implementing an Il2Cpp interface from managed code.
    /// </summary>
    public static class PhotonHook
    {
        /// <summary>(code, senderActorNumber, content). Raised on Photon's dispatch, main thread.</summary>
        public static event Action<byte, int, Il2CppSystem.Object> RawEvent;

        public static bool Installed { get; private set; }

        public static void Install(HarmonyLib.Harmony harmony)
        {
            if (Installed) return;
            try
            {
                var target = AccessTools.Method(typeof(LoadBalancingClient), nameof(LoadBalancingClient.OnEvent));
                if (target == null)
                {
                    Core.Log.Warning("PhotonHook: LoadBalancingClient.OnEvent not found — inbound events unavailable.");
                    return;
                }

                harmony.Patch(target, prefix: new HarmonyMethod(
                    AccessTools.Method(typeof(PhotonHook), nameof(OnEvent_Prefix))));
                Installed = true;
                Core.Log.Msg("PhotonHook installed on LoadBalancingClient.OnEvent.");
            }
            catch (Exception e)
            {
                Core.Log.Warning($"PhotonHook install failed ({e.GetType().Name}: {e.Message}).");
            }
        }

        private static void OnEvent_Prefix(EventData photonEvent)
        {
            // Never throw out of here: this runs inside Photon's own dispatch, and an
            // exception escaping a prefix would take the game's networking with it.
            try
            {
                if (ReferenceEquals(photonEvent, null)) return;
                RawEvent?.Invoke(photonEvent.Code, photonEvent.Sender, photonEvent.CustomData);
            }
            catch (Exception e)
            {
                try { Core.Log.Warning($"PhotonHook subscriber threw: {e.GetType().Name}: {e.Message}"); }
                catch { }
            }
        }
    }
}
