using System;
using Il2Cpp;
using Il2CppPhoton.Pun;
using LootOverhaul.Gate;
using LootOverhaul.Recon;
using Interop = LootOverhaul.Recon.Interop;

namespace LootOverhaul.Loot
{
    /// <summary>
    /// "Collect, don't wield." A postfix on <c>Prop.PickUp</c> notes a tagged pickup; the next
    /// frame the prop is dropped back out of the hand, the hand's memory of it is cleared, and
    /// a claim goes to the master. Cancelling the pickup (0.2) and dropping inside the pickup
    /// call (0.6) both left the hand frozen until the next real grab: the caller of PickUp
    /// keeps setting hand state after it returns, so the drop must come a frame later.
    /// </summary>
    public static class BagPickup
    {
        public static int Cancelled;
        private static readonly System.Collections.Generic.List<(Prop prop, PropRoot hand, LootTag tag)> Pending = new System.Collections.Generic.List<(Prop, PropRoot, LootTag)>();

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
                Pending.Add((__instance, __0, tag));
            }
            catch (Exception e) { Core.Log.Error($"BagPickup postfix threw: {e}"); }
        }

        /// <summary>Called every frame from Core: finish what the postfix noted.</summary>
        public static void Tick()
        {
            if (Pending.Count == 0) return;
            var work = Pending.ToArray();
            Pending.Clear();
            foreach (var (prop, hand, tag) in work)
            {
                try
                {
                    // Release through the HAND (PropRoot.Drop), which is what the game does when you
                    // open your fingers; Prop.Drop(root) left the hand still attached to the prop and
                    // it followed the object when the claim moved it (0.8: hand in the floor).
                    if (Interop.Alive(hand)) { try { hand.Drop(); } catch (Exception e) { Core.Log.Warning($"Hand release failed: {e.GetType().Name}: {e.Message}"); } }
                    try
                    {
                        var owner = Interop.Alive(prop) ? prop.owner : null;
                        if (Interop.Alive(owner)) { owner.Drop(); ReconLog.Line($"pickup: prop still owned after hand release; released owner too"); }
                    }
                    catch { }
                    if (Interop.Alive(hand)) { try { hand.ClearLastProp(); } catch { } }

                    if (tag.Claimed || tag.ClaimPending) continue;
                    if (!BagManager.CanCarry(tag.Item))
                    {
                        BagManager.Toast($"Bag full — {tag.Item.ColoredName} weighs {tag.Item.Weight:0.#}");
                        continue;
                    }
                    ReconLog.Line($"pickup -> claim: view {tag.ViewId} {tag.Item.Name}");
                    Claims.Request(tag);
                }
                catch (Exception e) { Core.Log.Error($"BagPickup tick threw: {e}"); }
            }
        }
    }
}
