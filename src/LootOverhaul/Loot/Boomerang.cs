using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using LootOverhaul.Gate;
using LootOverhaul.Recon;
using LootOverhaul.Net;
using UnityEngine;
using UnityEngine.Rendering;
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
    ///
    /// 0.14.0, from the first playtests:
    /// - <b>Stuck in an enemy.</b> A stab no longer ends the flight. It counts as the top of the
    ///   flight, so a gripping hand or a pull calls it back, and the return first frees the
    ///   weapon with the game's own <c>DynamicAnchorStab.Break</c> (the call that pulls a stabbed
    ///   weapon out). With no call within <see cref="StuckGiveUpSeconds"/> it is freed and sent
    ///   to the holster, because the game leaves a stab in place until the enemy dies.
    /// - <b>Holsters come first.</b> A hand reaching for a holster could summon the weapon
    ///   instead. A hand can only call the weapon back if its grip began away from every holster,
    ///   it has not been near one since, it is in front of you and above the waist, and (for a
    ///   pull) it moves back level rather than down to the hip or up over the shoulder.
    /// - <b>A trail.</b> A glowing ribbon follows the weapon in flight and on the way back. Modded
    ///   friends draw it too (event 152, the prop's view ID); vanilla friends see nothing new.
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
        private const float MaxReturnMeters = 25f;     // beyond this the game's own force grab range is not trusted (15 m in 0.12: too many long throws fell back)
        private const float GiveUpSeconds = 2.5f;      // the game returns it to the holster at 1.5 s anyway
        private const float StuckGiveUpSeconds = 5f;   // stuck with no call: free it and send it to the holster
        private const float ReturnTimeout = 3f;

        // Holster guard (0.14.0): a hand that may be going for a holster never calls the weapon back.
        private const float HolsterMargin = 0.12f;     // m added to the holster's own pick-up radius
        private const float HolsterCooldown = 0.4f;    // s after being near a holster before a hand may call
        private const float MinReach = 0.22f;          // m in front of the head, horizontally
        private const float MaxDrop = 0.55f;           // m below the head: lower is the hip holsters
        private const float PullLevel = 1.0f;          // a pull's vertical speed may be at most this times its backward speed

        // Trail.
        private const float TrailSeconds = 0.35f;
        private const float TrailWidth = 0.07f;
        private const float RemoteTrailSeconds = 4f;
        private static readonly Color TrailHead = new Color(0.55f, 0.85f, 1f, 0.85f);
        private static readonly Color TrailTail = new Color(0.35f, 0.55f, 1f, 0f);

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
            public bool Stuck; public float StuckAt;
            public TrailRenderer Trail;
        }

        /// <summary>Per hand: where its grip began and whether it has been near a holster since.</summary>
        private class HandState
        {
            public bool Gripping;
            public bool Tainted;            // grip began at or passed near a holster
            public float NearHolsterAt = -10f;
        }
        private static readonly Dictionary<IntPtr, HandState> Hands = new Dictionary<IntPtr, HandState>();

        private const byte CodeTrail = 152;
        private static readonly List<KeyValuePair<TrailRenderer, float>> RemoteTrails = new List<KeyValuePair<TrailRenderer, float>>();
        private static Material _trailMat;
        private static bool _trailFailed;

        private static readonly List<Flight> Flights = new List<Flight>();
        private static readonly Dictionary<IntPtr, Vector3> PrevHandRel = new Dictionary<IntPtr, Vector3>();
        public static int Triggered, Caught, Fallbacks;

        public static void Install()
        {
            // Prop.Drop(PropRoot), RVA 0xC7C8E0, unique in dump.cs. Every throw passes through it.
            Hooks.Patch(typeof(Prop), "Drop", null, Hooks.Of(typeof(Boomerang), nameof(Drop_Postfix)), 1);
            ModNet.RegisterHandler(CodeTrail, OnTrailMessage);
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
            foreach (var f in Flights) EndTrail(f.Trail);
            Flights.Clear();
            PrevHandRel.Clear();
            Hands.Clear();
            foreach (var kv in RemoteTrails) EndTrail(kv.Key);
            RemoteTrails.Clear();
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
                var trail = AddTrail(__instance);
                Flights.Add(new Flight { Prop = __instance, Rb = rb, Hand = __0, ItemName = item.Name, Origin = t, T0 = Time.time, MaxD = 0f, Trail = trail });
                SendTrail(__instance);
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
            try { if (Interop.Alive(ctrl)) UpdateHands(ctrl, now); } catch { }
            for (var i = Flights.Count - 1; i >= 0; i--)
            {
                var f = Flights[i];
                string done;
                try { done = Step(f, ctrl, now); }
                catch (Exception e) { done = $"error {e.GetType().Name}: {e.Message}"; TryFlag(f, done); }
                if (done != null) { EndTrail(f.Trail); Flights.RemoveAt(i); }
            }
            try { if (Interop.Alive(ctrl)) UpdateRel(ctrl); } catch { }
            if (Flights.Count == 0) { PrevHandRel.Clear(); Hands.Clear(); }
        }

        /// <summary>Remote trails end on their own after a few seconds (or when the prop goes). Called every frame.</summary>
        public static void TickRemote()
        {
            if (RemoteTrails.Count == 0) return;
            var now = Time.time;
            for (var i = RemoteTrails.Count - 1; i >= 0; i--)
            {
                var kv = RemoteTrails[i];
                var held = false;
                try
                {
                    var p = Interop.Alive(kv.Key) ? kv.Key.GetComponentInParent<Prop>() : null;
                    held = p != null && (int)p.state != 0 && now - kv.Value > 0.3f;
                }
                catch { }
                if (!Interop.Alive(kv.Key) || held || now - kv.Value > RemoteTrailSeconds) { EndTrail(kv.Key); RemoteTrails.RemoveAt(i); }
            }
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

            // Stuck (0.14.0): wait for a call like at the apex. The game leaves a stab in place
            // until the enemy dies, so after a while it is freed and sent to the holster.
            if (!f.Stuck && IsStuck(p))
            {
                f.Stuck = true; f.StuckAt = now; f.Apex = true;
                Core.Log.Msg($"Boomerang: {f.ItemName} stuck in something at {d:0.0} m after {elapsed:0.00}s; waiting for a call");
            }
            if (f.Stuck)
            {
                if (now - f.StuckAt > StuckGiveUpSeconds) return Fallback(f, $"stuck with no call for {StuckGiveUpSeconds:0}s");
            }
            else if (elapsed > GiveUpSeconds) { Core.Log.Msg($"Boomerang: {f.ItemName} no trigger within {GiveUpSeconds:0.0}s; left to the game's return"); return "idle"; }
            if (elapsed < ArmSeconds) return null;

            // Pull: an empty gripping hand moving sharply toward the player, away from the item.
            if (PullOn)
            {
                foreach (var root in new[] { ctrl.propRootLeft, ctrl.propRootRight })
                {
                    if (!Interop.Alive(root) || Interop.Alive(root.prop) || Interop.Alive(root.forceGrabProp)) continue;
                    if (Grip(ctrl, root) < GripOn) continue;
                    if (!MayCall(ctrl, root, now, false)) continue;
                    var v = HandVelocity(root);
                    var away = root.transform.position - pos; away.y = 0f;
                    if (away.sqrMagnitude < 0.01f) continue;
                    var pull = Vector3.Dot(v, away.normalized);
                    if (pull < ModConfig.BoomerangPullSpeed.Value) continue;
                    if (Mathf.Abs(v.y) > pull * PullLevel) continue;   // down to the hip or up over the shoulder: a holster, not a pull
                    return Return(f, ctrl, root, $"pull {pull:0.0} m/s");
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
                var root = PickHand(ctrl, f.Hand, now, out var anyEmpty);
                if (!anyEmpty) { if (f.Stuck) return null; return Fallback(f, "both hands full"); }   // stuck: wait for a free hand
                if (root != null) return Return(f, ctrl, root, "apex");
                // A hand is free but not gripping: the game's force grab needs grip held. Wait for
                // grip (or a pull) until the game's own return takes it.
            }
            return null;
        }

        /// <summary>The throwing hand if it is empty and gripping, else the other; null if neither grips. anyEmpty: some hand is free.</summary>
        private static PropRoot PickHand(VRController ctrl, PropRoot thrower, float now, out bool anyEmpty)
        {
            anyEmpty = false;
            var other = SameRoot(thrower, ctrl.propRootLeft) ? ctrl.propRootRight : ctrl.propRootLeft;
            foreach (var root in new[] { thrower, other })
            {
                if (!Interop.Alive(root) || Interop.Alive(root.prop) || Interop.Alive(root.forceGrabProp)) continue;
                anyEmpty = true;
                if (Grip(ctrl, root) >= GripHold && MayCall(ctrl, root, now, true)) return root;
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
            if (f.Stuck || IsStuck(p)) Unstick(f);
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
                if (IsStuck(p)) Unstick(f);   // the auto-holster cannot take a weapon still in a stab
                p.FlagForAutoHolster();
                Core.Log.Msg($"Boomerang: {f.ItemName} fallback: {why}; flagged for the game's auto-holster (canHolster {p.canHolster}, autoHolster {p.autoHolster})");
            }
            catch (Exception e) { Core.Log.Warning($"Boomerang: fallback for {f.ItemName} failed: {e.GetType().Name}: {e.Message}"); }
        }

        private static bool IsStuck(Prop p)
        {
            try
            {
                var da = p.dynamicAnchor;
                return Interop.Alive(da) && (da.isConstrained || da.isConstrainedToPuppet || da.isKinematicConstrained);
            }
            catch { return false; }
        }

        /// <summary>Frees a stabbed weapon with the game's own break (what pulling it out by hand does).</summary>
        private static void Unstick(Flight f)
        {
            try
            {
                var da = f.Prop.dynamicAnchor;
                if (!Interop.Alive(da)) return;
                var before = $"constrained {da.isConstrained}, puppet {da.isConstrainedToPuppet}, kinematic {da.isKinematicConstrained}";
                da.Break();
                Core.Log.Msg($"Boomerang: {f.ItemName} pulled free ({before} -> constrained {da.isConstrained}, puppet {da.isConstrainedToPuppet}, kinematic {da.isKinematicConstrained})");
            }
            catch (Exception e) { Core.Log.Warning($"Boomerang: freeing {f.ItemName} failed: {e.GetType().Name}: {e.Message}"); }
        }

        // ------------------------------------------------------------------ holster guard
        private static HandState StateOf(PropRoot root)
        {
            if (!Hands.TryGetValue(root.Pointer, out var h)) Hands[root.Pointer] = h = new HandState();
            return h;
        }

        /// <summary>Tracks each hand's grip: a grip that begins near a holster, or passes one, is the holster's.</summary>
        private static void UpdateHands(VRController ctrl, float now)
        {
            foreach (var root in new[] { ctrl.propRootLeft, ctrl.propRootRight })
            {
                if (!Interop.Alive(root)) continue;
                var h = StateOf(root);
                var near = NearHolster(ctrl, root);
                if (near) h.NearHolsterAt = now;
                var gripping = Grip(ctrl, root) >= GripHold;
                if (gripping && !h.Gripping) h.Tainted = near || now - h.NearHolsterAt < HolsterCooldown || Interop.Alive(root.prop);
                else if (gripping && near) h.Tainted = true;
                h.Gripping = gripping;
                if (!gripping) h.Tainted = false;
            }
        }

        /// <summary>May this empty, gripping hand call the weapon back, or might it be going for a holster?</summary>
        private static bool MayCall(VRController ctrl, PropRoot root, float now, bool needReach)
        {
            var h = StateOf(root);
            if (h.Tainted || now - h.NearHolsterAt < HolsterCooldown) return false;
            try { if (Holster.InventoryHolsterExpanded) return false; } catch { }
            var cam = Camera.main;
            if (cam == null) return true;
            var head = cam.transform;
            var rel = root.transform.position - head.position;
            if (rel.y < -MaxDrop) return false;
            if (!needReach) return true;   // a pull ends near the body; its start and its level path are what count
            var fwd = head.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) return true;
            var flat = rel; flat.y = 0f;
            return Vector3.Dot(flat, fwd.normalized) >= MinReach;
        }

        private static bool NearHolster(VRController ctrl, PropRoot root)
        {
            Il2CppReferenceArray<Holster> holsters = null;
            try { holsters = ctrl.holsters; } catch { }
            if (holsters == null) return false;
            var hand = root.transform.position;
            for (var i = 0; i < holsters.Length; i++)
            {
                var h = holsters[i];
                if (!Interop.Alive(h) || !h.gameObject.activeInHierarchy) continue;
                try
                {
                    if (h.isInventoryHolster) continue;   // the inventory is covered by InventoryHolsterExpanded
                    var r = Mathf.Max(h.pickUpRadius, 0.1f) + HolsterMargin;
                    var t = h.transform;
                    if ((hand - t.position).sqrMagnitude <= r * r) return true;
                    if ((hand - t.TransformPoint(h.interactionPointOffset)).sqrMagnitude <= r * r) return true;
                }
                catch { }
            }
            return false;
        }

        // ------------------------------------------------------------------ trail
        private static Material TrailMaterial()
        {
            if (Interop.Alive(_trailMat) || _trailFailed) return _trailMat;
            var shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Transparent");
            if (shader == null) shader = Shader.Find("UI/Default");
            if (shader == null) { _trailFailed = true; Core.Log.Warning("Boomerang: no shader for the trail; it will not be drawn"); return null; }
            _trailMat = new Material(shader) { name = "LootOverhaul_BoomerangTrail", hideFlags = HideFlags.DontUnloadUnusedAsset };
            Core.Log.Msg($"Boomerang: trail drawn with {shader.name}");
            return _trailMat;
        }

        private static TrailRenderer AddTrail(Prop prop)
        {
            try
            {
                var mat = TrailMaterial();
                if (mat == null || !Interop.Alive(prop)) return null;
                var go = new GameObject("LootOverhaul_BoomerangTrail");
                go.transform.SetParent(prop.transform, false);
                Renderer biggest = null;
                try
                {
                    foreach (var r in prop.GetComponentsInChildren<Renderer>(false))
                        if (Interop.Alive(r) && r.enabled && (biggest == null || r.bounds.size.sqrMagnitude > biggest.bounds.size.sqrMagnitude)) biggest = r;
                }
                catch { }
                go.transform.position = biggest != null ? biggest.bounds.center : prop.transform.position;
                var tr = go.AddComponent(Il2CppType.Of<TrailRenderer>()).TryCast<TrailRenderer>();
                if (tr == null) { UnityEngine.Object.Destroy(go); return null; }
                tr.sharedMaterial = mat;
                tr.time = TrailSeconds;
                tr.minVertexDistance = 0.03f;
                tr.widthMultiplier = TrailWidth;
                tr.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
                var g = new Gradient();
                g.SetKeys(
                    (Il2CppStructArray<GradientColorKey>)new[] { new GradientColorKey(TrailHead, 0f), new GradientColorKey(TrailTail, 1f) },
                    (Il2CppStructArray<GradientAlphaKey>)new[] { new GradientAlphaKey(TrailHead.a, 0f), new GradientAlphaKey(0f, 1f) });
                tr.colorGradient = g;
                tr.shadowCastingMode = ShadowCastingMode.Off;
                tr.receiveShadows = false;
                tr.alignment = LineAlignment.View;
                tr.emitting = true;
                return tr;
            }
            catch (Exception e) { Core.Log.Warning($"Boomerang: trail failed: {e.GetType().Name}: {e.Message}"); return null; }
        }

        /// <summary>Stops emitting and lets the ribbon fade, then removes it.</summary>
        private static void EndTrail(TrailRenderer tr)
        {
            try
            {
                if (!Interop.Alive(tr)) return;
                tr.emitting = false;
                tr.transform.SetParent(null, true);
                UnityEngine.Object.Destroy(tr.gameObject, TrailSeconds + 0.1f);
            }
            catch { }
        }

        private static void SendTrail(Prop prop)
        {
            try
            {
                var pv = prop.GetComponent<Il2CppPhoton.Pun.PhotonView>();
                if (!Interop.Alive(pv)) return;
                ModNet.Send(CodeTrail, pv.ViewID.ToString(), false);
            }
            catch { }
        }

        private static void OnTrailMessage(int sender, Il2CppSystem.Object content)
        {
            try
            {
                if (!Enabled) return;
                if (!int.TryParse(content?.ToString(), out var view)) return;
                var pv = Il2CppPhoton.Pun.PhotonView.Find(view);
                if (!Interop.Alive(pv)) return;
                var prop = pv.GetComponent<Prop>();
                if (!Interop.Alive(prop)) return;
                foreach (var kv in RemoteTrails) if (Interop.Alive(kv.Key) && kv.Key.transform.parent != null && kv.Key.transform.parent.Pointer == prop.transform.Pointer) return;
                var tr = AddTrail(prop);
                if (tr != null) RemoteTrails.Add(new KeyValuePair<TrailRenderer, float>(tr, Time.time));
            }
            catch (Exception e) { Core.Log.Warning($"Boomerang: remote trail failed: {e.GetType().Name}: {e.Message}"); }
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
