using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;
using Il2CppOthergate.Audio;
using UnityEngine;
using VisualCues.Hud;
using AI = Il2CppSauron.AI;
using Sauron = Il2CppSauron.Sauron;

namespace VisualCues.Cues
{
    /// <summary>
    /// The noise cue: a marker toward every nearby enemy that just made a sound you could not
    /// see. Three sources, all local (every client animates and plays sound for every enemy):
    ///
    ///  - <c>AI.AE_Footstep</c>, the animation event behind every enemy footstep;
    ///  - <c>AI.AE_FX</c>, the animation event behind attack, cast and vocal effects;
    ///  - <c>AudioManager.PlaySoundAt(Vector3, SoundFX, …)</c>, the funnel every positioned
    ///    sound in the game goes through. A sound is attributed to the nearest active enemy
    ///    within <c>NoiseAttributeRadius</c>, unless the local player is closer to it (then it
    ///    is our own sword, footstep or spell). With <c>LogSounds</c> on, every attribution
    ///    decision is written to the session log so the radius can be tuned from real data.
    ///
    /// "Seen" means inside <c>NoiseSeenAngle</c> of straight ahead with a clear ray from the
    /// eyes to the enemy's head, or inside <c>NoiseFacingAngle</c> regardless of the ray. A
    /// marker for an enemy you turn to look at disappears; one for an enemy that goes quiet
    /// fades after <c>NoiseDurationSeconds</c>; one that has been up <c>NoiseMaxSeconds</c>
    /// fades anyway and that enemy is left alone for <c>NoiseRepeatSeconds</c>.
    /// </summary>
    public static class NoiseCue
    {
        private sealed class Entry
        {
            public int Key;
            public AI Ai;
            public Vector3 Pos;
            public float Started, Until, LastReport, LastSeenCheck;
            public string Kind;
            public float Dist;
            public bool Boss;
        }

        private static readonly Dictionary<int, Entry> Entries = new Dictionary<int, Entry>();
        /// <summary>Enemies whose marker timed out: key → time the next marker is allowed.</summary>
        private static readonly Dictionary<int, float> Suppressed = new Dictionary<int, float>();
        private static readonly List<Entry> Scratch = new List<Entry>();
        private static readonly List<int> ScratchKeys = new List<int>();
        private static bool _losFailed, _listFailed;
        private static int _logLinesThisFrame, _logFrame;
        public static int Reports { get; private set; }
        public static int Shown { get; private set; }

        public static void Install()
        {
            Hooks.Patch(AccessTools.Method(typeof(AI), "AE_Footstep"), null, Hooks.Of(typeof(NoiseCue), nameof(FootstepPostfix)), "AI.AE_Footstep");
            Hooks.Patch(AccessTools.Method(typeof(AI), "AE_FX"), null, Hooks.Of(typeof(NoiseCue), nameof(FxPostfix)), "AI.AE_FX");
            var playAt = AccessTools.Method(typeof(AudioManager), "PlaySoundAt",
                new[] { typeof(Vector3), typeof(SoundFX), typeof(EmitterChannel), typeof(float), typeof(float), typeof(float), typeof(bool) });
            Hooks.Patch(playAt, Hooks.Of(typeof(NoiseCue), nameof(SoundPrefix)), null, "AudioManager.PlaySoundAt(Vector3, SoundFX, …)");
        }

        // ---- patches: never throw out of these, they run inside the game's animation and audio paths.

        private static void FootstepPostfix(AI __instance)
        {
            try { if (ModConfig.NoiseFootsteps.Value) Report(__instance, "footstep", false); }
            catch (Exception e) { Quiet("AE_Footstep", e); }
        }

        private static void FxPostfix(AI __instance, string __0)
        {
            try { if (ModConfig.NoiseAnimationFx.Value) Report(__instance, "fx:" + (__0 ?? ""), false); }
            catch (Exception e) { Quiet("AE_FX", e); }
        }

        private static void SoundPrefix(Vector3 __0, SoundFX __1)
        {
            try { if (ModConfig.NoiseAttributeSounds.Value || ModConfig.LogSounds.Value) Attribute(__0, __1); }
            catch (Exception e) { Quiet("PlaySoundAt", e); }
        }

        private static int _quietCount;
        private static void Quiet(string where, Exception e)
        {
            if (_quietCount++ < 5) Core.Log.Warning($"Noise cue {where} threw: {e.GetType().Name}: {e.Message}");
        }

        // ---- attribution ------------------------------------------------------------------

        private static void Attribute(Vector3 pos, SoundFX sfx)
        {
            var list = ActiveAIs();
            if (list == null) return;
            var radius = Mathf.Max(0.25f, ModConfig.NoiseAttributeRadius.Value);
            AI best = null;
            var bestD = radius;
            for (var i = 0; i < list.Count; i++)
            {
                var ai = list[i];
                if (!Interop.Alive(ai)) continue;
                try
                {
                    if (!ai.isAliveAndActive) continue;
                    var d = Mathf.Min(Vector3.Distance(pos, ai.Position), Vector3.Distance(pos, ai.HeadPos));
                    if (d < bestD) { bestD = d; best = ai; }
                }
                catch { }
            }

            var mine = DistanceToLocalPlayer(pos);
            var attributed = best != null && bestD <= mine;

            if (ModConfig.LogSounds.Value)
            {
                if (_logFrame != Time.frameCount) { _logFrame = Time.frameCount; _logLinesThisFrame = 0; }
                if (_logLinesThisFrame++ < 12)
                {
                    string name;
                    try { name = sfx == null ? "<null>" : sfx.name; } catch { name = "<unreadable>"; }
                    var enemy = best == null ? "no enemy within radius" : $"nearest enemy {bestD:0.00} m";
                    CueLog.Line($"sound \"{name}\" at {Interop.Vec(pos)}: {enemy}, player {mine:0.00} m -> {(attributed ? "ENEMY" : "skip")}");
                }
            }

            if (attributed && ModConfig.NoiseAttributeSounds.Value)
            {
                string kind;
                try { kind = "sound:" + (sfx == null ? "?" : sfx.name); } catch { kind = "sound"; }
                Report(best, kind, false);
            }
        }

        private static Il2CppSystem.Collections.Generic.List<AI> ActiveAIs()
        {
            try { return Sauron.ActiveAIList; }
            catch (Exception e)
            {
                if (!_listFailed) { _listFailed = true; Core.Log.Warning($"Sauron.ActiveAIList unavailable ({e.GetType().Name}); sound attribution off, animation events still work."); }
                return null;
            }
        }

        private static float DistanceToLocalPlayer(Vector3 pos)
        {
            var best = float.MaxValue;
            try
            {
                var local = AvatarPlayer.LocalAvatar;
                if (Interop.Alive(local))
                {
                    if (Interop.Alive(local.Head)) best = Mathf.Min(best, Vector3.Distance(pos, local.Head.position));
                    if (Interop.Alive(local.LeftHand)) best = Mathf.Min(best, Vector3.Distance(pos, local.LeftHand.position));
                    if (Interop.Alive(local.RightHand)) best = Mathf.Min(best, Vector3.Distance(pos, local.RightHand.position));
                    // Feet: the body's ground point.
                    best = Mathf.Min(best, Vector3.Distance(pos, local.Position));
                }
            }
            catch { }
            if (best == float.MaxValue)
            {
                var head = CueHud.HeadTransform;
                if (Interop.Alive(head)) best = Vector3.Distance(pos, head.position);
            }
            return best;
        }

        // ---- reports ----------------------------------------------------------------------

        /// <summary>An enemy made a noise. <paramref name="force"/> skips the seen test (the test key).</summary>
        public static void Report(AI ai, string kind, bool force)
        {
            if (!ModConfig.Enabled.Value || !ModConfig.NoiseEnabled.Value) return;
            if (!Interop.Alive(ai)) return;
            var head = CueHud.HeadTransform;
            if (!Interop.Alive(head)) return;

            Vector3 aiHead;
            try { aiHead = ai.HeadPos; } catch { try { aiHead = ai.Position + Vector3.up; } catch { return; } }
            var dist = Vector3.Distance(head.position, aiHead);
            if (dist > ModConfig.NoiseRangeMeters.Value) return;
            try { if (!ai.isAliveAndActive) return; } catch { }

            var key = KeyOf(ai);
            var now = Time.unscaledTime;
            if (!force && Suppressed.TryGetValue(key, out var allowedAt))
            {
                if (now < allowedAt) return;
                Suppressed.Remove(key);
            }
            Entries.TryGetValue(key, out var e);
            if (e != null && now - e.LastReport < 0.2f)
            {
                e.Until = now + Mathf.Max(0.5f, ModConfig.NoiseDurationSeconds.Value);
                e.Pos = aiHead; e.Dist = dist;
                return;
            }
            Reports++;

            if (!force)
            {
                var seen = IsSeen(head, aiHead, ai, lineOfSight: true);
                if (seen && !ModConfig.NoiseIncludeVisible.Value)
                {
                    if (e != null) Entries.Remove(key);
                    if (ModConfig.VerboseLogging.Value) CueLog.Line($"noise {kind} from enemy {key} at {dist:0.0} m: seen, no marker");
                    return;
                }
            }

            if (e == null)
            {
                e = new Entry { Key = key, Started = now };
                Entries[key] = e;
                Shown++;
                CueLog.Line($"noise {kind} from enemy {key} at {dist:0.0} m: marker shown");
            }
            e.Ai = ai; e.Pos = aiHead; e.Dist = dist; e.Kind = kind; e.LastReport = now; e.LastSeenCheck = now;
            e.Until = now + Mathf.Max(0.5f, ModConfig.NoiseDurationSeconds.Value);
            try { e.Boss = ai.IsBoss; } catch { e.Boss = false; }
        }

        private static int KeyOf(AI ai)
        {
            try { var v = ai.ViewID; if (v != 0) return v; } catch { }
            try { return ai.GetInstanceID(); } catch { return ai.Pointer.GetHashCode(); }
        }

        /// <summary>Inside the view cone, and (optionally) nothing solid between the eyes and the enemy's head.</summary>
        private static bool IsSeen(Transform head, Vector3 target, AI ai, bool lineOfSight)
        {
            var dir = target - head.position;
            var d = dir.magnitude;
            if (d < 0.05f) return true;
            var angle = Vector3.Angle(head.forward, dir);
            if (angle > ModConfig.NoiseSeenAngle.Value) return false;
            // Facing it squarely: the arrow has done its job whatever the ray says. A gate, bars
            // or a railing stop the ray but not the eyes (an enemy behind a gate kept its marker
            // up while the player stared straight at it, 2026-09-05).
            if (angle <= ModConfig.NoiseFacingAngle.Value) return true;
            if (!lineOfSight || _losFailed) return true;
            try
            {
                var n = dir / d;
                var origin = head.position + n * 0.2f;
                if (Physics.Raycast(origin, n, out RaycastHit hit, Mathf.Max(0.01f, d - 0.2f), Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    var t = hit.collider != null ? hit.collider.transform : null;
                    if (t != null && Interop.Alive(ai.Transform) && t.IsChildOf(ai.Transform)) return true;
                    // A ragdoll piece that is not parented under the agent, or a very near wall the enemy is pressed against.
                    if (Vector3.Distance(hit.point, target) < 0.75f) return true;
                    return false;
                }
                return true;
            }
            catch (Exception e)
            {
                _losFailed = true;
                Core.Log.Warning($"Line-of-sight test unavailable ({e.GetType().Name}: {e.Message}); the view cone alone decides what counts as seen.");
                return true;
            }
        }

        // ---- per frame --------------------------------------------------------------------

        public static void Tick()
        {
            if (Entries.Count == 0 && Suppressed.Count == 0) return;
            var now = Time.unscaledTime;
            var head = CueHud.HeadTransform;
            var headAlive = Interop.Alive(head);
            Scratch.Clear();
            var maxLife = ModConfig.NoiseMaxSeconds.Value;
            foreach (var e in Entries.Values)
            {
                if (now >= e.Until || !ModConfig.NoiseEnabled.Value || !ModConfig.Enabled.Value) { Scratch.Add(e); continue; }
                if (maxLife > 0f && now - e.Started >= maxLife)
                {
                    // Up long enough: fade, and do not come straight back for a noisy pacer.
                    Suppressed[e.Key] = now + Mathf.Max(0f, ModConfig.NoiseRepeatSeconds.Value);
                    Scratch.Add(e);
                    continue;
                }
                if (!Interop.Alive(e.Ai)) { Scratch.Add(e); continue; }
                try
                {
                    if (!e.Ai.isAliveAndActive) { Scratch.Add(e); continue; }
                    e.Pos = e.Ai.HeadPos;
                }
                catch { Scratch.Add(e); continue; }
                if (headAlive)
                {
                    e.Dist = Vector3.Distance(head.position, e.Pos);
                    // Turning to look at it dismisses the marker: the cone test every 0.15 s, the ray only on reports.
                    if (!ModConfig.NoiseIncludeVisible.Value && now - e.LastSeenCheck > 0.15f)
                    {
                        e.LastSeenCheck = now;
                        if (IsSeen(head, e.Pos, e.Ai, lineOfSight: true)) { Scratch.Add(e); continue; }
                    }
                }
            }
            foreach (var dead in Scratch) Entries.Remove(dead.Key);
            if (Suppressed.Count > 0)
            {
                ScratchKeys.Clear();
                foreach (var kv in Suppressed) if (now >= kv.Value) ScratchKeys.Add(kv.Key);
                foreach (var k in ScratchKeys) Suppressed.Remove(k);
            }
            if (Entries.Count == 0) return;

            Scratch.Clear();
            Scratch.AddRange(Entries.Values);
            Scratch.Sort((a, b) => a.Dist.CompareTo(b.Dist));
            var max = Mathf.Max(1, ModConfig.NoiseMaxMarkers.Value);
            for (var i = 0; i < Scratch.Count && i < max; i++)
            {
                var e = Scratch[i];
                CueHud.Submit(new CueHud.Cue
                {
                    Key = $"noise:{e.Key}",
                    World = e.Pos,
                    Label = e.Boss ? "BOSS" : "",
                    Color = e.Boss ? CueHud.BossColor : CueHud.NoiseColor,
                    Started = e.Started,
                    Until = e.Until,
                    Priority = e.Boss ? 60 : 50,
                });
            }
        }

        /// <summary>The . key: mark the nearest enemy whether or not it is seen, to check the HUD without a fight.</summary>
        public static void Test()
        {
            var head = CueHud.HeadTransform;
            var list = ActiveAIs();
            if (!Interop.Alive(head) || list == null) { Core.Log.Msg("Noise test: no head or no enemy list."); return; }
            AI best = null; var bestD = float.MaxValue;
            for (var i = 0; i < list.Count; i++)
            {
                var ai = list[i];
                if (!Interop.Alive(ai)) continue;
                try
                {
                    if (!ai.isAliveAndActive) continue;
                    var d = Vector3.Distance(head.position, ai.HeadPos);
                    if (d < bestD) { bestD = d; best = ai; }
                }
                catch { }
            }
            if (best == null) { Core.Log.Msg("Noise test: no active enemy in the scene."); CueHud.Flash("No enemy to mark", Color.gray, 1.5f); return; }
            Core.Log.Msg($"Noise test: marking enemy {KeyOf(best)} at {bestD:0.0} m.");
            Report(best, "test", true);
        }

        public static void Clear(string why)
        {
            if (Entries.Count > 0 && ModConfig.VerboseLogging.Value) Core.Log.Msg($"Dropping {Entries.Count} noise marker(s): {why}");
            Entries.Clear();
            Suppressed.Clear();
        }

        public static string Describe() => $"noise reports {Reports}, markers shown {Shown}, active {Entries.Count}";
    }
}
