using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;
using UnityEngine;
using VisualCues.Hud;

namespace VisualCues.Cues
{
    /// <summary>
    /// The gate cue: a marker on a gate that starts to open, and on a lock that is unlocked,
    /// because both are sounds a deaf player misses (the grinding gate, the click of the key)
    /// and the thing that moved is often out of view: behind you, around a corner, or across
    /// the room from the lever. Two hooks, both run on every client:
    ///
    ///  - <c>Gate.Open</c> (RVA 0x6D6560), the [PunRPC] every gate opening ends in.
    ///    <c>Operable.Net_Open</c> sends it with <c>RpcTarget.All</c> (offline: a direct
    ///    call), and the soul harvest calls it directly on each client when a round ends,
    ///    from the state machine every client runs. A late joiner gets gates through
    ///    <c>SyncInitialState</c> instead, so it sees no stale cue. Only a call that moves the
    ///    gate from Closed or Closing to Opening counts (the same test <c>Operable.Open</c> makes).
    ///  - <c>Lock.Remote_Unlock</c> (RVA 0xBBC4B0), the body of the <c>RPC_Unlock</c> RPC
    ///    that <c>Lock.Net_Unlock</c> sends with <c>RpcTarget.All</c> when a key is turned;
    ///    <c>Door.Remote_Unlock</c> calls it first. It also arrives for a late joiner (sent
    ///    from <c>OnPlayerEnteredRoom</c>), which is what the settle window after a scene
    ///    load is for. Only a lock that was locked counts.
    /// </summary>
    public static class GateCue
    {
        private sealed class Entry
        {
            public int Key;
            public Vector3 Pos;
            public string Label;
            public float Started, Until;
        }

        /// <summary>After a scene loads, openings and unlocks are the game syncing state, not something a player did.</summary>
        private const float SettleSeconds = 12f;

        private static readonly Dictionary<int, Entry> Entries = new Dictionary<int, Entry>();
        private static readonly List<int> Expired = new List<int>();
        private static float _sceneStartedAt;
        private static bool _gateWasShut, _lockWasLocked, _firstLogged;
        public static int Seen { get; private set; }
        public static int Shown { get; private set; }

        public static void Install()
        {
            Hooks.Patch(AccessTools.DeclaredMethod(typeof(Gate), "Open"),
                Hooks.Of(typeof(GateCue), nameof(GateOpenPrefix)), Hooks.Of(typeof(GateCue), nameof(GateOpenPostfix)), "Gate.Open");
            Hooks.Patch(AccessTools.DeclaredMethod(typeof(Il2Cpp.Lock), "Remote_Unlock"),
                Hooks.Of(typeof(GateCue), nameof(UnlockPrefix)), Hooks.Of(typeof(GateCue), nameof(UnlockPostfix)), "Lock.Remote_Unlock");
        }

        // ---- patches: never throw out of these, they run inside the game's RPC handlers.

        private static void GateOpenPrefix(Gate __instance)
        {
            _gateWasShut = false;
            try
            {
                var s = __instance.currentState;
                _gateWasShut = s == Operable.State.Closed || s == Operable.State.Closing;
            }
            catch (Exception e) { Quiet("Gate.Open prefix", e); }
        }

        private static void GateOpenPostfix(Gate __instance)
        {
            try
            {
                if (!_gateWasShut) return;
                _gateWasShut = false;
                if (__instance.currentState != Operable.State.Opening && __instance.currentState != Operable.State.Open) return;
                Raise(__instance, CenterOf(__instance), "GATE OPEN", "gate");
            }
            catch (Exception e) { Quiet("Gate.Open", e); }
        }

        private static void UnlockPrefix(Il2Cpp.Lock __instance)
        {
            _lockWasLocked = false;
            try { _lockWasLocked = __instance.isLocked; }
            catch (Exception e) { Quiet("Lock.Remote_Unlock prefix", e); }
        }

        private static void UnlockPostfix(Il2Cpp.Lock __instance)
        {
            try
            {
                if (!_lockWasLocked) return;
                _lockWasLocked = false;
                Vector3 pos;
                try { pos = __instance.transform.position; } catch { return; }
                var what = "lock";
                try { if (__instance.TryCast<Door>() != null) what = "door"; } catch { }
                Raise(__instance, pos, "UNLOCKED", what);
            }
            catch (Exception e) { Quiet("Lock.Remote_Unlock", e); }
        }

        private static int _quietCount;
        private static void Quiet(string where, Exception e)
        {
            if (_quietCount++ < 5) Core.Log.Warning($"Gate cue {where} threw: {e.GetType().Name}: {e.Message}");
        }

        // ---- cues -------------------------------------------------------------------------

        private static void Raise(Component source, Vector3 pos, string label, string what)
        {
            if (!ModConfig.Enabled.Value || !ModConfig.GateEnabled.Value) return;
            Seen++;
            var name = Interop.Name(source);
            var now = Time.unscaledTime;
            if (now - _sceneStartedAt < SettleSeconds)
            {
                CueLog.Line($"gate cue: {what} `{name}` {label.ToLowerInvariant()} {now - _sceneStartedAt:0.0} s after the scene loaded: state sync, no marker");
                return;
            }
            var head = CueHud.HeadTransform;
            if (!Interop.Alive(head)) return;
            var dist = Vector3.Distance(head.position, pos);
            if (dist > ModConfig.GateRangeMeters.Value)
            {
                CueLog.Line($"gate cue: {what} `{name}` {label.ToLowerInvariant()} at {dist:0} m: out of range, no marker");
                return;
            }

            int key;
            try { key = source.GetInstanceID(); } catch { key = source.Pointer.GetHashCode(); }
            Entries[key] = new Entry
            {
                Key = key, Pos = pos, Label = label, Started = now,
                Until = now + Mathf.Max(1f, ModConfig.GateDurationSeconds.Value),
            };
            Shown++;
            var line = $"gate cue: {what} `{name}` {label.ToLowerInvariant()} at {Interop.Vec(pos)}, {dist:0.0} m: marker shown";
            if (!_firstLogged) { _firstLogged = true; CueLog.Headline($"First gate cue fired — {line}"); }
            else CueLog.Line(line);
        }

        /// <summary>
        /// Middle of the gate's visible parts while it is still shut (the transform is often
        /// its pivot on the floor or a hinge), else a metre above the transform.
        /// </summary>
        private static Vector3 CenterOf(Component c)
        {
            var pos = c.transform.position + Vector3.up;
            try
            {
                var renderers = c.GetComponentsInChildren<Renderer>(false);
                var any = false;
                var b = new Bounds();
                for (var i = 0; i < renderers.Length && i < 64; i++)
                {
                    var r = renderers[i];
                    if (!Interop.Alive(r) || !r.enabled) continue;
                    if (!any) { b = r.bounds; any = true; }
                    else b.Encapsulate(r.bounds);
                }
                // A huge box means the gate shares a parent with half the room; keep the fallback.
                if (any && b.size.magnitude < 25f) pos = b.center;
            }
            catch { }
            return pos;
        }

        // ---- per frame --------------------------------------------------------------------

        public static void Tick()
        {
            if (Entries.Count == 0) return;
            var now = Time.unscaledTime;
            var on = ModConfig.Enabled.Value && ModConfig.GateEnabled.Value;
            Expired.Clear();
            foreach (var e in Entries.Values)
            {
                if (!on || now >= e.Until) { Expired.Add(e.Key); continue; }
                CueHud.Submit(new CueHud.Cue
                {
                    Key = $"gate:{e.Key}",
                    World = e.Pos,
                    Label = e.Label,
                    Color = CueHud.GateColor,
                    Started = e.Started,
                    Until = e.Until,
                    Priority = 55,
                });
            }
            foreach (var k in Expired) Entries.Remove(k);
        }

        public static void Clear(string why)
        {
            if (Entries.Count > 0 && ModConfig.VerboseLogging.Value) Core.Log.Msg($"Dropping {Entries.Count} gate marker(s): {why}");
            Entries.Clear();
            _sceneStartedAt = Time.unscaledTime;
        }

        public static string Describe() => $"gate events {Seen}, gate markers shown {Shown}";
    }
}
