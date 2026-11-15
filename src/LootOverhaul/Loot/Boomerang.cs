using System;
using System.Collections.Generic;
using Il2Cpp;
using LootOverhaul.Gate;
using LootOverhaul.Recon;
using UnityEngine;
using Interop = LootOverhaul.Recon.Interop;

namespace LootOverhaul.Loot
{
    /// <summary>
    /// The Boomerang enchantment (0.12.0). It works on throwables: axe, dagger, spear, hammer and
    /// long axe, plus a sword with Spire. A thrown Boomerang weapon flies back to an empty hand at
    /// the top of its flight, or when the player pulls a gripping empty hand back sharply.
    ///
    /// Nothing is spawned and nothing is destroyed. The thrown prop is the same object the
    /// whole time, and the return uses only the game's own networked mechanisms:
    /// - The return is the game's own VR force grab, the same steps as
    ///   <c>VRControllerProps.TryForceGrab</c>: get the pose, set <c>forceGrabProp</c>, call
    ///   <c>StartForceGrab</c>, then run the <c>ForceGrab</c> coroutine. The prop flies to the
    ///   hand while grip is held, and the hand catches it as it would any force grab.
    /// - Whenever that cannot happen, the fallback is <c>Prop.FlagForAutoHolster</c>. That is
    ///   the call the game makes on respawn and after arrow impacts. The game's own
    ///   <c>TryAutoHolster</c> then puts the prop back in its holster about 20 physics steps
    ///   later. That covers a stuck item, both hands full, out of range, flat-screen (where the
    ///   game's own fallback pick-up puts it in the hand) and a force grab released early.
    /// - With no trigger, nothing is done. The game's own return to the holster (1.5 s after a
    ///   throw) runs as it always did.
    ///
    /// The gate decides: outside a fully modded private room nothing is tracked.
    /// </summary>
    public static class Boomerang
    {
        public const string Name = "Boomerang";
        private const int SpirePerk = 41;

        // Fixed tuning. Only the trigger mode and the pull speed are settings.
        private const float MinThrowSpeed = 2.5f;      // m/s right after Drop: slower is letting go, not a throw
        private const float ArmSeconds = 0.15f;        // no trigger this soon after the throw
        private const float ApexDrop = 0.15f;          // m closer than its farthest point: on its way back down/in
        private const float StallSpeed = 0.6f;         // m/s: it hit something or stopped
        private const float GripOn = 0.7f;             // grip for the pull trigger (the game's force grab holds above 0.6)
        private const float GripHold = 0.6f;
        private const float MaxReturnMeters = 15f;     // beyond this the game's own force grab range is not trusted
        private const float GiveUpSeconds = 2.5f;      // the game returns it to the holster at 1.5 s anyway
        private const float ReturnTimeout = 3f;

        private class Flight
        {
            public Prop Prop;
            public Rigidbody Rb;
            public PropRoot Hand;
            public string ItemName;
            public Vector3 Origin;
            public float T0, MaxD;
            public bool Apex;               // apex reached, waiting for a gripping empty hand
            public PropRoot ReturnRoot;     // set once the return has started
            public float ReturnAt;
            public Vector3 LastRel; public bool HasRel;
        }

        private static readonly List<Flight> Flights = new List<Flight>();
        private static readonly Dictionary<IntPtr, Vector3> PrevHandRel = new Dictionary<IntPtr, Vector3>();
        public static int Triggered, Caught, Fallbacks;

        public static void Install()
        {
            // Prop.Drop(PropRoot), RVA 0xC7C8E0, unique in dump.cs. Every throw passes through it.
            Hooks.Patch(typeof(Prop), "Drop", null, Hooks.Of(typeof(Boomerang), nameof(Drop_Postfix)), 1);
        }

        public static bool Enabled => !string.Equals(ModConfig.BoomerangTrigger.Value, "off", StringComparison.OrdinalIgnoreCase);
        private static bool ApexOn => Mode("apex");
        private static bool PullOn => Mode("pull");
        private static bool Mode(string m)
        {
            var v = (ModConfig.BoomerangTrigger.Value ?? "").Trim().ToLowerInvariant();
            return v == "both" || v == m;
        }

        /// <summary>Can this weapon take Boomerang: a thrown type, or a sword with Spire.</summary>
        public static bool CanTake(LootItem i)
        {
            if (i == null || !i.IsWeapon) return false;
            switch (i.PropType)
            {
                case LootTables.Axe: case LootTables.Dagger: case LootTables.Spear: case LootTables.Hammer: case LootTables.LongAxe: return true;
                case LootTables.Sword: return i.PerkA == SpirePerk || i.PerkB == SpirePerk || i.PerkC == SpirePerk;
                default: return false;
            }
        }

        public static void Clear(string why)
        {
            if (Flights.Count > 0) Core.Log.Msg($"Boomerang: {Flights.Count} flight(s) dropped ({why}); the game returns them as usual");
            Flights.Clear();
            PrevHandRel.Clear();
        }

        private static void Drop_Postfix(Prop __instance, PropRoot __0)
        {
            try
            {
                if (!ModGate.Active || !Enabled || !Interop.Alive(__instance) || !Interop.Alive(__0)) return;
                var ctrl = BattleManager.Controller;
                if (!Interop.Alive(ctrl)) return;
                if (!SameRoot(__0, ctrl.propRootLeft) && !SameRoot(__0, ctrl.propRootRight)) return;   // not a hand of ours (e.g. holster, death drop)
                if (!__instance.IsMine) return;
                var local = AvatarPlayer.LocalAvatar;
                if (!Interop.Alive(local) || local.IsDead) return;   // Holster.LocalPlayerDied drops through here too
                var weapon = __instance.TryCast<Weapon>();
                if (weapon == null) return;
                var item = FindItem((int)__instance.type, weapon.RandomSeed);
                if (item == null || !item.Boomerang) return;
                if (!CanTake(item)) { ReconLog.Line($"boomerang: {item.Name} has Boomerang but cannot be thrown (no Spire?); ignored"); return; }
                var rb = __instance.GetComponent<Rigidbody>();
                var speed = Interop.Alive(rb) ? rb.velocity.magnitude : 0f;
                if (speed < MinThrowSpeed) return;   // let go, not thrown
                foreach (var f in Flights) if (f.Prop.Pointer == __instance.Pointer) return;
                var t = __instance.transform.position;
                Flights.Add(new Flight { Prop = __instance, Rb = rb, Hand = __0, ItemName = item.Name, Origin = t, T0 = Time.time, MaxD = 0f });
                Core.Log.Msg($"Boomerang: {item.Name} thrown from the {HandName(__0)} hand at {speed:0.0} m/s; trigger {ModConfig.BoomerangTrigger.Value}");
            }
            catch (Exception e) { Core.Log.Warning($"Boomerang: throw hook failed: {e.GetType().Name}: {e.Message}"); }
        }

        /// <summary>A bag weapon in the loadout, by its prop type and seed (the only bag weapons that can be in a hand).</summary>
        private static LootItem FindItem(int propType, int seed)
        {
            var inv = BagManager.Inventory;
            for (var s = 0; s < inv.Loadout.Length; s++)
            {
                var l = inv.Loadout[s];
                if (l == null) continue;
                var live = inv.Find(l.Id);
                if (live != null && live.IsWeapon && live.PropType == propType && live.RandomSeed == seed) return live;
            }
            return null;
        }

        public static void Tick()
        {
            if (Flights.Count == 0) return;
            if (!ModGate.Active) { Clear("gate closed"); return; }
            VRController ctrl = null;
            try { ctrl = BattleManager.Controller; } catch { }
            var now = Time.time;
            for (var i = Flights.Count - 1; i >= 0; i--)
            {
                var f = Flights[i];
                string done;
                try { done = Step(f, ctrl, now); }
                catch (Exception e) { done = $"error {e.GetType().Name}: {e.Message}"; TryFlag(f, done); }
                if (done != null) Flights.RemoveAt(i);
            }
            try { if (Interop.Alive(ctrl)) UpdateRel(ctrl); } catch { }
            if (Flights.Count == 0) PrevHandRel.Clear();
        }

        /// <summary>One frame of one flight. Returns null to keep tracking, or why it ended.</summary>
        private static string Step(Flight f, VRController ctrl, float now)
        {
            var p = f.Prop;
            if (!Interop.Alive(p)) { Core.Log.Msg($"Boomerang: {f.ItemName} destroyed or despawned in flight; nothing to return"); return "gone"; }
            if (!p.IsMine) { Core.Log.Msg($"Boomerang: {f.ItemName} changed owner in flight; left to the game"); return "owner"; }
            var local = AvatarPlayer.LocalAvatar;
            if (!Interop.Alive(local) || local.IsDead || !Interop.Alive(ctrl))
            {
                if (f.ReturnRoot != null) EndReturn(f);
                return Fallback(f, "player dead or gone");
            }
            var state = (int)p.state;
            if (state == 1)
            {
                if (f.ReturnRoot != null) { Caught++; Core.Log.Msg($"Boomerang: {f.ItemName} caught in the {HandName(f.ReturnRoot)} hand after {now - f.ReturnAt:0.00}s"); }
                else Core.Log.Msg($"Boomerang: {f.ItemName} picked up by hand");
                return "caught";
            }
            if (state == 2) { Core.Log.Msg($"Boomerang: {f.ItemName} back in its holster{(f.ReturnRoot != null ? " (during the return)" : " (the game's own return)")}"); return "holstered"; }

            if (f.ReturnRoot != null)
            {
                // The force grab ends by itself on a catch (state 1 above) or when grip opens.
                if (!Interop.Alive(p.forceGrabPropRoot)) return Fallback(f, "the return ended without a catch (grip released?)");
                if (now - f.ReturnAt > ReturnTimeout) { EndReturn(f); return Fallback(f, $"the return took over {ReturnTimeout:0}s"); }
                return null;
            }

            var elapsed = now - f.T0;
            var pos = p.transform.position;
            var d = Vector3.Distance(pos, f.Origin);
            if (d > f.MaxD) f.MaxD = d;
            if (elapsed > GiveUpSeconds) { Core.Log.Msg($"Boomerang: {f.ItemName} no trigger within {GiveUpSeconds:0.0}s; left to the game's return"); return "idle"; }
            if (elapsed < ArmSeconds) return null;

            var da = p.dynamicAnchor;
            if (Interop.Alive(da) && (da.isConstrained || da.isConstrainedToPuppet)) return Fallback(f, "stuck in something");

            // Pull: an empty gripping hand moving sharply toward the player, away from the item.
            if (PullOn)
            {
                foreach (var root in new[] { ctrl.propRootLeft, ctrl.propRootRight })
                {
                    if (!Interop.Alive(root) || Interop.Alive(root.prop) || Interop.Alive(root.forceGrabProp)) continue;
                    if (Grip(ctrl, root) < GripOn) continue;
                    var v = HandVelocity(root);
                    var away = root.transform.position - pos; away.y = 0f;
                    if (away.sqrMagnitude < 0.01f) continue;
                    var pull = Vector3.Dot(v, away.normalized);
                    if (pull >= ModConfig.BoomerangPullSpeed.Value) return Return(f, ctrl, root, $"pull {pull:0.0} m/s");
                }
            }

            if (ApexOn && !f.Apex)
            {
                var speed = Interop.Alive(f.Rb) ? f.Rb.velocity.magnitude : 0f;
                if (f.MaxD - d >= ApexDrop) { f.Apex = true; Core.Log.Msg($"Boomerang: {f.ItemName} apex at {f.MaxD:0.0} m after {elapsed:0.00}s"); }
                else if (elapsed >= 0.3f && speed < StallSpeed) { f.Apex = true; Core.Log.Msg($"Boomerang: {f.ItemName} stopped at {d:0.0} m after {elapsed:0.00}s (hit something?)"); }
            }
            if (f.Apex)
            {
                var root = PickHand(ctrl, f.Hand, out var anyEmpty);
                if (!anyEmpty) return Fallback(f, "both hands full");
                if (root != null) return Return(f, ctrl, root, "apex");
                // A hand is free but not gripping: the game's force grab needs grip held. Wait for
                // grip (or a pull) until the game's own return takes it.
            }
            return null;
        }

        /// <summary>The throwing hand if it is empty and gripping, else the other; null if neither grips. anyEmpty: some hand is free.</summary>
        private static PropRoot PickHand(VRController ctrl, PropRoot thrower, out bool anyEmpty)
        {
            anyEmpty = false;
            var other = SameRoot(thrower, ctrl.propRootLeft) ? ctrl.propRootRight : ctrl.propRootLeft;
            foreach (var root in new[] { thrower, other })
            {
                if (!Interop.Alive(root) || Interop.Alive(root.prop) || Interop.Alive(root.forceGrabProp)) continue;
                anyEmpty = true;
                if (Grip(ctrl, root) >= GripHold) return root;
            }
            return null;
        }

        /// <summary>Start the game's own force grab of the prop into this hand. Falls back to the holster when the game would not.</summary>
        private static string Return(Flight f, VRController ctrl, PropRoot root, string why)
        {
            var p = f.Prop;
            Triggered++;
            if (XRRig.IsFallbackRig) return Fallback(f, $"{why}, flat-screen: the game's own fallback pick-up returns it");
            var dist = Vector3.Distance(p.transform.position, root.transform.position);
            if (dist > MaxReturnMeters) return Fallback(f, $"{why} at {dist:0.0} m, beyond {MaxReturnMeters:0} m");
            if (!p.IsValidForForceGrab()) return Fallback(f, $"{why}, but the game says it cannot be force-grabbed now");
            var pose = p.GetForceGrabPose(root);
            if (pose == null) return Fallback(f, $"{why}, but it has no force-grab pose");
            pose.SetMirror(root);
            pose.ResetPivot();
            root.forceGrabProp = p;
            p.isBallistic = false;
            p.StartForceGrab(root, pose);
            ctrl.StartCoroutine(ctrl.ForceGrab(p, pose, root));
            f.ReturnRoot = root;
            f.ReturnAt = Time.time;
            Core.Log.Msg($"Boomerang: {f.ItemName} returning to the {HandName(root)} hand ({why}, {dist:0.0} m)");
            return null;
        }

        private static void EndReturn(Flight f)
        {
            try { if (Interop.Alive(f.ReturnRoot) && f.ReturnRoot.forceGrabProp != null && f.ReturnRoot.forceGrabProp.Pointer == f.Prop.Pointer) f.ReturnRoot.forceGrabProp = null; } catch { }
        }

        private static string Fallback(Flight f, string why)
        {
            Fallbacks++;
            TryFlag(f, why);
            return "fallback";
        }

        /// <summary>The game's own return to the holster, as on respawn: the prop goes back to the holster it came from.</summary>
        private static void TryFlag(Flight f, string why)
        {
            try
            {
                var p = f.Prop;
                if (!Interop.Alive(p) || !p.IsMine) { Core.Log.Msg($"Boomerang: {f.ItemName} fallback ({why}) skipped: prop gone or not ours"); return; }
                if ((int)p.state != 0) { Core.Log.Msg($"Boomerang: {f.ItemName} fallback ({why}) not needed: state {p.state}"); return; }
                p.FlagForAutoHolster();
                Core.Log.Msg($"Boomerang: {f.ItemName} fallback: {why}; flagged for the game's auto-holster (canHolster {p.canHolster}, autoHolster {p.autoHolster})");
            }
            catch (Exception e) { Core.Log.Warning($"Boomerang: fallback for {f.ItemName} failed: {e.GetType().Name}: {e.Message}"); }
        }

        private static float Grip(VRController ctrl, PropRoot root)
        {
            try { return (int)root.handedness == 1 ? ctrl.leftHandTrigger : ctrl.rightHandTrigger; } catch { return 0f; }
        }

        /// <summary>Hand velocity relative to the head, from the hand positions this frame and last frame (so walking does not count).</summary>
        private static Vector3 HandVelocity(PropRoot root)
        {
            var cam = Camera.main;
            var rel = cam == null ? root.transform.position : root.transform.position - cam.transform.position;
            var dt = Mathf.Max(Time.deltaTime, 0.001f);
            var v = PrevHandRel.TryGetValue(root.Pointer, out var prev) ? (rel - prev) / dt : Vector3.zero;
            return v;
        }

        private static void UpdateRel(VRController ctrl)
        {
            var cam = Camera.main;
            foreach (var root in new[] { ctrl.propRootLeft, ctrl.propRootRight })
            {
                if (!Interop.Alive(root)) continue;
                PrevHandRel[root.Pointer] = cam == null ? root.transform.position : root.transform.position - cam.transform.position;
            }
        }

        private static bool SameRoot(PropRoot a, PropRoot b) => a != null && b != null && a.Pointer == b.Pointer;
        private static string HandName(PropRoot r) { try { return (int)r.handedness == 1 ? "left" : "right"; } catch { return "?"; } }
    }
}
