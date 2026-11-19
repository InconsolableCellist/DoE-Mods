using System;
using System.Collections.Generic;
using Il2Cpp;
using UnityEngine;
using AI = Il2CppSauron.AI;
using Sauron = Il2CppSauron.Sauron;

namespace CustomEnemies.Skins
{
    /// <summary>
    /// Which enemies wear a skin, and the per-frame loop over them.
    ///
    /// Enemies are pooled: one <c>AI</c> object goes back to the pool and comes out again as a
    /// fresh spawn. A skin is built once per pooled instance, kept while the object lives, reset
    /// on each spawn (<c>AI.InitializePooledObject</c> and <c>AI.OnRespawn</c>, the two paths an
    /// enemy takes to the floor, as LootOverhaul's goblin resize uses them), and destroyed only
    /// when the enemy object is. A once-a-second scan of <c>Sauron.ActiveAIList</c> catches
    /// anything the hooks missed (an enemy already alive when the mod was switched on, or a
    /// refused patch), so the hooks make it prompt rather than make it work.
    /// </summary>
    public static class SkinManager
    {
        private static readonly Dictionary<IntPtr, EnemySkin> Skins = new Dictionary<IntPtr, EnemySkin>();
        /// <summary>Enemies Numpad1 switched by hand: true = skinned although not listed, false = left vanilla although listed.</summary>
        private static readonly Dictionary<IntPtr, bool> Manual = new Dictionary<IntPtr, bool>();
        private static readonly HashSet<string> Logged = new HashSet<string>();
        private static readonly List<IntPtr> Dead = new List<IntPtr>();
        /// <summary>Enemies whose skin failed to build: left vanilla, not retried until a reload.</summary>
        private static readonly HashSet<IntPtr> Failed = new HashSet<IntPtr>();
        private static string[] _names = new string[0];
        private static string _namesFrom;
        private static bool _allOff;
        private static float _scanIn;

        public static void Install()
        {
            var t = typeof(SkinManager);
            var spawned = Hooks.Of(t, nameof(Spawned));
            Hooks.Patch(Method("InitializePooledObject", 0), null, spawned, "AI.InitializePooledObject");
            Hooks.Patch(Method("OnRespawn", 3), null, spawned, "AI.OnRespawn(3)");
        }

        private static System.Reflection.MethodInfo Method(string name, int argc)
        {
            foreach (var m in typeof(AI).GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly))
                if (m.Name == name && m.GetParameters().Length == argc) return m;
            return null;
        }

        private static void Spawned(AI __instance)
        {
            // Runs inside the game's spawn path: never throw out of here.
            try
            {
                if (!ModConfig.Enabled.Value || _allOff || !Interop.Alive(__instance)) return;
                Consider(__instance, "spawn");
            }
            catch (Exception e) { LogOnce("spawn hook", e); }
        }

        /// <summary>Attach, reset or leave alone one enemy, by the skin files, the setting and the Numpad1 overrides.</summary>
        private static void Consider(AI ai, string why)
        {
            var skinFile = SkinFor(ai);
            var want = Wants(ai, skinFile);
            Skins.TryGetValue(ai.Pointer, out var skin);
            if (!want)
            {
                if (skin != null) Remove(ai.Pointer, "no longer listed");
                return;
            }
            if (skin == null)
            {
                skin = EnemySkin.Create(ai, skinFile, Listed(ai) || (Manual.TryGetValue(ai.Pointer, out var forced) && forced));
                if (skin == null) { Failed.Add(ai.Pointer); return; }
                Skins[ai.Pointer] = skin;
                if (ModConfig.VerboseLogging.Value) Core.Log.Msg($"Skin on `{skin.Who}` ({why}): {skin.Describe()}");
            }
            else if (why == "spawn")
            {
                skin.Reset();
                if (ModConfig.VerboseLogging.Value) Core.Log.Msg($"Skin on `{skin.Who}` reset for a new spawn from the pool.");
            }
        }

        private static SkinLibrary.Skin SkinFor(AI ai)
        {
            string id = null;
            try { var r = ai.references; if (r != null) id = r.ID.ToString(); } catch { }
            return SkinLibrary.For(CleanName(Interop.Name(ai)), id);
        }

        private static bool Wants(AI ai, SkinLibrary.Skin skinFile)
        {
            if (Manual.TryGetValue(ai.Pointer, out var forced)) return forced;
            return skinFile != null || Listed(ai);
        }

        public static bool Listed(AI ai)
        {
            var src = ModConfig.PlaceholderOn.Value ?? "";
            if (src != _namesFrom)
            {
                _namesFrom = src;
                var parts = new List<string>();
                foreach (var p in src.Split(',', ';'))
                {
                    var s = p.Trim();
                    if (s.Length > 0) parts.Add(s);
                }
                _names = parts.ToArray();
            }
            if (_names.Length == 0) return false;
            var name = CleanName(Interop.Name(ai));
            string id = null;
            try { var r = ai.references; if (r != null) id = r.ID.ToString(); } catch { }
            foreach (var n in _names)
                if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase) ||
                    (id != null && string.Equals(n, id, StringComparison.OrdinalIgnoreCase))) return true;
            return false;
        }

        public static string CleanName(string n)
        {
            if (string.IsNullOrEmpty(n)) return "";
            var i = n.IndexOf("(Clone)", StringComparison.Ordinal);
            return (i >= 0 ? n.Substring(0, i) : n).Trim();
        }

        public static void Tick(float dt)
        {
            if (_allOff) return;
            _scanIn -= dt;
            if (_scanIn > 0f) return;
            _scanIn = 1f;
            var all = Live();
            foreach (var ai in all)
                if (!Skins.ContainsKey(ai.Pointer) && !Failed.Contains(ai.Pointer)) Consider(ai, "scan");
        }

        public static void LateTick()
        {
            if (Skins.Count == 0) return;
            Dead.Clear();
            foreach (var kv in Skins)
            {
                try { if (!kv.Value.LateTick()) Dead.Add(kv.Key); }
                catch (Exception e) { LogOnce("skin " + kv.Value.Who, e); Dead.Add(kv.Key); }
            }
            foreach (var p in Dead) Remove(p, "enemy object gone");
        }

        private static void Remove(IntPtr key, string why)
        {
            if (!Skins.TryGetValue(key, out var skin)) return;
            Skins.Remove(key);
            try { skin.Destroy(); } catch (Exception e) { LogOnce("remove", e); }
            if (ModConfig.VerboseLogging.Value) Core.Log.Msg($"Skin off `{skin.Who}` ({why}).");
        }

        public static void Purge(string why)
        {
            Failed.Clear();
            Dead.Clear();
            foreach (var kv in Skins) if (!kv.Value.EnemyAlive) Dead.Add(kv.Key);
            foreach (var p in Dead) Remove(p, why);
            var stale = new List<IntPtr>();
            foreach (var kv in Manual) if (!Skins.ContainsKey(kv.Key)) stale.Add(kv.Key);
            foreach (var p in stale) Manual.Remove(p);
        }

        /// <summary>Numpad1: the nearest enemy to the camera gets the placeholder, or loses its skin.</summary>
        public static void ToggleNearest()
        {
            var cam = CameraFinder.Get();
            var from = Interop.Alive(cam) ? cam.transform.position : Vector3.zero;
            AI best = null;
            var bestD = float.MaxValue;
            foreach (var ai in Live())
            {
                var d = (ai.transform.position - from).sqrMagnitude;
                if (d < bestD) { bestD = d; best = ai; }
            }
            if (best == null) { Core.Log.Msg("Numpad1: no live enemy."); return; }
            if (Skins.ContainsKey(best.Pointer) == false && SkinFor(best) == null && !Listed(best))
                Core.Log.Msg($"Numpad1: `{Interop.Name(best)}` has no skin file; it gets the placeholder.");
            var on = !Skins.ContainsKey(best.Pointer);
            Failed.Remove(best.Pointer);
            Manual[best.Pointer] = on;
            if (_allOff && on) { _allOff = false; Core.Log.Msg("Skins back on."); }
            Consider(best, "Numpad1");
            Core.Log.Msg($"Numpad1: `{Interop.Name(best)}` {Mathf.Sqrt(bestD):0.0} m away, skin {(on ? "on" : "off")}.");
        }

        /// <summary>Numpad2: every skin off (vanilla everywhere), or back on.</summary>
        public static void ToggleAll()
        {
            _allOff = !_allOff;
            if (_allOff)
            {
                foreach (var k in new List<IntPtr>(Skins.Keys)) Remove(k, "Numpad2");
                Core.Log.Msg("Numpad2: all skins off; every enemy is vanilla until Numpad2 again.");
            }
            else
            {
                _scanIn = 0f;
                Core.Log.Msg("Numpad2: skins back on.");
            }
        }

        public static void Reloaded()
        {
            _namesFrom = null;
            Failed.Clear();
            SkinLibrary.Rescan();
            if (!ModConfig.Enabled.Value)
            {
                foreach (var k in new List<IntPtr>(Skins.Keys)) Remove(k, "Enabled=false");
                return;
            }
            foreach (var k in new List<IntPtr>(Skins.Keys)) Remove(k, "settings reloaded");
            _scanIn = 0f;
        }

        private static List<AI> Live()
        {
            var list = new List<AI>();
            try
            {
                var all = Sauron.ActiveAIList;
                if (all == null) return list;
                for (var i = 0; i < all.Count; i++)
                {
                    var ai = all[i];
                    if (Interop.Alive(ai)) list.Add(ai);
                }
            }
            catch (Exception e) { LogOnce("ActiveAIList", e); }
            return list;
        }

        public static void LogOnce(string where, Exception e)
        {
            if (!Logged.Add(where)) return;
            Core.Log.Warning($"{where}: {e.GetType().Name}: {e.Message} (logged once)");
        }
    }
}
