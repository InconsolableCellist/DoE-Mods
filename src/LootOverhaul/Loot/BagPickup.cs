using System;
using Il2Cpp;
using Il2CppPhoton.Pun;
using LootOverhaul.Gate;
using LootOverhaul.Recon;
using Interop = LootOverhaul.Recon.Interop;

namespace LootOverhaul.Loot
{
    /// <summary>
    /// "Collect, don't wield." A prefix on <c>Prop.PickUp</c>: if the prop is tagged loot,
    /// the pickup is cancelled and a claim goes to the master instead. Untagged props are
    /// untouched — this is the one place the mod alters game behaviour, and only for
    /// objects the mod itself spawned.
    /// </summary>
    public static class BagPickup
    {
        public static int Cancelled;

        public static void Install()
        {
            Hooks.Patch(typeof(Prop), "PickUp", Hooks.Of(typeof(BagPickup), nameof(Prefix)), null);
        }

        private static bool Prefix(Prop __instance)
        {
            try
            {
                if (!ModGate.Active || LootRegistry.Count == 0) return true;
                if (!Interop.Alive(__instance)) return true;
                var pv = __instance.GetComponent<PhotonView>();
                if (!Interop.Alive(pv)) return true;
                if (!LootRegistry.TryGet(pv.ViewID, out var tag)) return true;

                Cancelled++;
                if (tag.Claimed || tag.ClaimPending) return false;
                if (!BagManager.CanCarry(tag.Item))
                {
                    BagManager.Toast($"Bag full — {tag.Item.ColoredName} weighs {tag.Item.Weight:0.#}");
                    return false;
                }
                ReconLog.Line($"pickup -> claim: view {pv.ViewID} {tag.Item.Name}");
                Claims.Request(tag);
                return false;
            }
            catch (Exception e)
            {
                Core.Log.Error($"BagPickup prefix threw, letting the game proceed: {e}");
                return true;
            }
        }
    }
}
