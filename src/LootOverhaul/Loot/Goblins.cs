using System;
using System.Collections.Generic;
using Il2Cpp;
using LootOverhaul.Gate;
using LootOverhaul.Recon;
using UnityEngine;
using Interop = LootOverhaul.Recon.Interop;
using AI = Il2CppSauron.AI;

namespace LootOverhaul.Loot
{
    /// <summary>
    /// The loot goblin. The game names it by its Sauron ID (<c>References.IDHash</c> against
    /// <c>SauronKeys.IDHashLootGoblin</c>); the roller gives it a pile of its own, and every
    /// modded client makes it bigger (<c>LootGoblinScale</c>) when the pool hands one out. The
    /// game already sizes enemies through <c>Specs.scale</c> and keeps the result in
    /// <c>References.defaultScale</c>, so the multiplier is applied to that, never compounded.
    /// </summary>
    public static class Goblins
    {
        private static int _goblinHash;
        private static bool _hashTried;
        private static readonly HashSet<int> Scaled = new HashSet<int>();
        public static int Resized;

        public static void Install()
        {
            var t = typeof(Goblins);
            // Both paths a pooled enemy takes to the floor; the spawn RPC fires on every client.
            Hooks.Patch(typeof(AI), "InitializePooledObject", null, Hooks.Of(t, nameof(Spawned)));
            Hooks.Patch(typeof(AI), "OnRespawn", null, Hooks.Of(t, nameof(Spawned)), paramCount: 3);
        }

        public static bool IsLootGoblin(AI ai)
        {
            try
            {
                if (!_hashTried) { _hashTried = true; _goblinHash = Il2CppSauron.SauronKeys.IDHashLootGoblin; }
                if (_goblinHash == 0) return false;
                var refs = ai.references;
                return refs != null && refs.IDHash == _goblinHash;
            }
            catch { return false; }
        }

        private static void Spawned(AI __instance)
        {
            try
            {
                if (!ModConfig.Enabled.Value || !ModGate.Active) return;
                var k = ModConfig.LootGoblinScale.Value;
                if (k <= 0f || Math.Abs(k - 1f) < 0.001f) return;
                if (!Interop.Alive(__instance) || !IsLootGoblin(__instance)) return;
                var t = __instance.transform;
                Vector3 baseScale;
                try { baseScale = __instance.references.defaultScale; } catch { baseScale = Vector3.zero; }
                var id = __instance.GetInstanceID();
                if (baseScale.sqrMagnitude < 1e-6f)
                {
                    // No recorded default: scale the current size once per object and never again.
                    if (Scaled.Contains(id)) return;
                    baseScale = t.localScale;
                }
                Scaled.Add(id);
                t.localScale = baseScale * k;
                Resized++;
                ReconLog.Line($"loot goblin `{__instance.name}` resized ×{k:0.00} from {Interop.Vec(baseScale)} (view {ViewId(__instance)})");
            }
            catch (Exception e) { Core.Log.Warning($"Goblin resize failed: {e.GetType().Name}: {e.Message}"); }
        }

        private static int ViewId(AI ai)
        {
            try { var pv = ai.references.PVO; return Interop.Alive(pv) ? pv.ViewID : -1; } catch { return -1; }
        }
    }
}
