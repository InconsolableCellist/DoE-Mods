using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using VisualCues.Hud;

namespace VisualCues.Cues
{
    /// <summary>
    /// The puzzle cue: the rune chest (the crypt-gem chest with four cube slots) plays one
    /// sound when a cube goes into the right slot and another for the wrong one. This shows a
    /// tick labelled RIGHT or an X labelled WRONG on the slot instead, green or red, so shape
    /// and text carry the answer as well as colour.
    ///
    /// One hook: <c>RuneChest.RPC_SyncLockState(int[] data, int changedIndex)</c>
    /// (RVA 0x70D540). The chest's owner decides right or wrong in <c>OnKeyAdded</c> and sends
    /// this RPC with <c>RpcTarget.All</c>; every client, the owner included, plays the
    /// valid or invalid sound inside it, at <c>receptacles[changedIndex]</c>, when
    /// <c>data[changedIndex]</c> is 1 (Valid) or 2 (Invalid). The cue makes the same test at
    /// the same place. A late joiner's sync (<c>SyncStateToPlayer</c>) sends
    /// <c>changedIndex = -1</c>, which plays nothing and shows nothing; taking a cube out
    /// sets the slot to 0 (Empty), which also shows nothing.
    /// </summary>
    public static class PuzzleCue
    {
        private sealed class Entry
        {
            public string Key;
            public Vector3 Pos;
            public bool Right;
            public string Label;
            public float Started, Until;
        }

        private const int Valid = 1, Invalid = 2;

        private static readonly Dictionary<string, Entry> Entries = new Dictionary<string, Entry>();
        private static readonly List<string> Expired = new List<string>();
        private static bool _firstLogged;
        public static int Shown { get; private set; }

        public static void Install()
        {
            Hooks.Patch(AccessTools.DeclaredMethod(typeof(RuneChest), "RPC_SyncLockState"),
                null, Hooks.Of(typeof(PuzzleCue), nameof(SyncPostfix)), "RuneChest.RPC_SyncLockState");
        }

        // ---- patch: never throw out of it, it runs inside the game's RPC handler.

        private static void SyncPostfix(RuneChest __instance, Il2CppStructArray<int> __0, int __1)
        {
            try
            {
                if (!ModConfig.Enabled.Value || !ModConfig.PuzzleEnabled.Value) return;
                var data = __0;
                var index = __1;
                if (data == null || index < 0 || index >= data.Length) return;
                var state = data[index];
                if (state != Valid && state != Invalid) return;

                var receptacles = __instance.receptacles;
                if (receptacles == null || index >= receptacles.Length) return;
                var slot = receptacles[index];
                if (!Interop.Alive(slot)) return;
                var pos = slot.transform.position;

                var head = CueHud.HeadTransform;
                if (!Interop.Alive(head)) return;
                var dist = Vector3.Distance(head.position, pos);
                var right = state == Valid;
                var chestId = __instance.GetInstanceID();
                if (dist > ModConfig.PuzzleRangeMeters.Value)
                {
                    CueLog.Line($"puzzle cue: chest {chestId} slot {index + 1} {(right ? "right" : "wrong")} at {dist:0} m: out of range, no marker");
                    return;
                }

                // All four right: the chest is solved; say so on the last slot.
                var solved = right;
                for (var i = 0; i < data.Length && solved; i++) if (data[i] != Valid) solved = false;

                var now = Time.unscaledTime;
                var key = $"slot:{chestId}:{index}";
                Entries[key] = new Entry
                {
                    Key = key, Pos = pos, Right = right, Started = now,
                    Label = right ? (solved ? "RIGHT - SOLVED" : "RIGHT") : "WRONG",
                    Until = now + Mathf.Max(1f, ModConfig.PuzzleDurationSeconds.Value),
                };
                Shown++;
                var line = $"puzzle cue: chest {chestId} slot {index + 1} of {data.Length} {(right ? "RIGHT" : "WRONG")}{(solved ? ", all slots right" : "")} at {Interop.Vec(pos)}, {dist:0.0} m: marker shown";
                if (!_firstLogged) { _firstLogged = true; CueLog.Headline($"First puzzle cue fired — {line}"); }
                else CueLog.Line(line);
            }
            catch (Exception e) { Quiet(e); }
        }

        private static int _quietCount;
        private static void Quiet(Exception e)
        {
            if (_quietCount++ < 5) Core.Log.Warning($"Puzzle cue RPC_SyncLockState threw: {e.GetType().Name}: {e.Message}");
        }

        // ---- per frame --------------------------------------------------------------------

        public static void Tick()
        {
            if (Entries.Count == 0) return;
            var now = Time.unscaledTime;
            var on = ModConfig.Enabled.Value && ModConfig.PuzzleEnabled.Value;
            Expired.Clear();
            foreach (var e in Entries.Values)
            {
                if (!on || now >= e.Until) { Expired.Add(e.Key); continue; }
                CueHud.Submit(new CueHud.Cue
                {
                    Key = e.Key,
                    World = e.Pos,
                    Label = e.Label,
                    Color = e.Right ? CueHud.PuzzleRightColor : CueHud.PuzzleWrongColor,
                    Shape = e.Right ? CueHud.CueShape.Check : CueHud.CueShape.Cross,
                    Started = e.Started,
                    Until = e.Until,
                    Priority = 70,
                });
            }
            foreach (var k in Expired) Entries.Remove(k);
        }

        public static void Clear(string why)
        {
            if (Entries.Count > 0 && ModConfig.VerboseLogging.Value) Core.Log.Msg($"Dropping {Entries.Count} puzzle marker(s): {why}");
            Entries.Clear();
        }

        public static string Describe() => $"puzzle markers shown {Shown}";
    }
}
