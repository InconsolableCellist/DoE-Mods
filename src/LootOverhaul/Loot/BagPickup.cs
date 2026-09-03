using System;
using Il2Cpp;
using Il2CppPhoton.Pun;
using LootOverhaul.Gate;
using LootOverhaul.Recon;
using Interop = LootOverhaul.Recon.Interop;

namespace LootOverhaul.Loot
{
    /// <summary>
    /// "Collect, don't wield." A postfix on <c>Prop.PickUp</c>: the game completes its pickup
    /// (so the hand's state machine runs to the end — cancelling it in 0.2 left the hand
    /// frozen until the next real grab), then we drop the prop straight back out of the hand
    /// and send a claim to the master. Untagged props are untouched.
    /// </summary>
    public static class BagPickup
    {
        public static int Cancelled;

        public static void Install()
        {
            Hooks.Patch(typeof(Prop), "PickUp", null, Hooks.Of(typeof(BagPickup), nameof(Postfix)));
        }

        private static void Postfix(Prop __instance, PropRoot __0)
        {
            try
            {
                if (!ModGate.Active || LootRegistry.Count == 0) return;
                if (!Interop.Alive(__instance)) return;
                var pv = __instance.GetComponent<PhotonView>();
                if (!Interop.Alive(pv)) return;
                if (!LootRegistry.TryGet(pv.ViewID, out var tag)) return;

                Cancelled++;
                try { __instance.Drop(__0); }
                catch (Exception e) { Core.Log.Warning($"Loot drop-back failed: {e.GetType().Name}: {e.Message}"); }

                if (tag.Claimed || tag.ClaimPending) return;
                if (!BagManager.CanCarry(tag.Item))
                {
                    BagManager.Toast($"Bag full — {tag.Item.ColoredName} weighs {tag.Item.Weight:0.#}");
                    return;
                }
                ReconLog.Line($"pickup -> claim: view {pv.ViewID} {tag.Item.Name}");
                Claims.Request(tag);
            }
            catch (Exception e) { Core.Log.Error($"BagPickup postfix threw: {e}"); }
        }
    }
}
