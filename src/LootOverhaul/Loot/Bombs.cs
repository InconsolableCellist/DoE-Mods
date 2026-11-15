using System;
using System.Collections.Generic;
using System.Globalization;
using Il2Cpp;
using Il2CppOthergate.FX;
using Il2CppPhoton.Pun;
using Il2CppPhoton.Realtime;
using LootOverhaul.Gate;
using LootOverhaul.Recon;
using Hashtable = Il2CppExitGames.Client.Photon.Hashtable;

namespace LootOverhaul.Loot
{
    /// <summary>
    /// The two armor stats with no exosuit perk behind them: bomb damage and bomb radius (0.13.8).
    ///
    /// How the game blows a bomb up (read from the assembly 2026-11-15): <c>Bomb.RPC_OnBreak</c>
    /// runs on every client and plays the bomb's <c>fxExplosion</c> through <c>FXRunner.PlayFX</c>
    /// with the bomb's own PhotonView ID as the controller. The FX's area-damage node
    /// (<c>FXDef.FXNode.StartNode</c>) then calls <c>DamageHandler.DamageEnemiesInRadius</c>,
    /// <c>DamageMimicsInRadius</c>, <c>DamageBreakablesInRadius</c> and <c>DamageCryptLidsInRadius</c>
    /// with the node's <c>damageRadius</c> and <c>damage</c>. Enemies are only hurt by the client
    /// that owns them (<c>AI.IsMine</c>, usually the host), so the host has to know the thrower's
    /// armor. Each player's two multipliers sit in a Photon player property (only while the room
    /// is gated), and the client doing the damage reads the thrower's.
    ///
    /// The thrower is the bomb's PhotonView owner when it breaks (picking a prop up moves its
    /// ownership to you). Damage to players (the thrower included) is not changed, so a bigger
    /// radius does not reach further into your own party. The explosion's look does not grow.
    /// </summary>
    public static class Bombs
    {
        public const string Damage = "Bomb_Damage";
        public const string Radius = "Bomb_Radius";
        private const string Key = "LO_Bomb";

        private static float _localDamage = 1f, _localRadius = 1f;
        private static string _published;
        private static string _publishedRoom;
        private static float _nextPublish;

        /// <summary>Bomb view ID -> (thrower's actor number, when it broke). Kept a few seconds for delayed FX nodes.</summary>
        private static readonly Dictionary<int, (int actor, float at)> Broken = new Dictionary<int, (int, float)>();

        /// <summary>The multipliers for the explosion node running right now; (1, 1) outside one.</summary>
        private static float _curDamage = 1f, _curRadius = 1f;
        private static bool _inBomb;

        public static bool IsBombStat(string stat) => stat == Damage || stat == Radius;

        public static void Install()
        {
            Hooks.Patch(typeof(Bomb), "RPC_OnBreak", Hooks.Of(typeof(Bombs), nameof(OnBreak)), null);
            Hooks.Patch(typeof(FXDef.FXNode), "StartNode", Hooks.Of(typeof(Bombs), nameof(NodeStart)), Hooks.Of(typeof(Bombs), nameof(NodeEnd)));
            Hooks.Patch(typeof(DamageHandler), "DamageEnemiesInRadius", Hooks.Of(typeof(Bombs), nameof(RadiusAndDamage)), null);
            Hooks.Patch(typeof(DamageHandler), "DamageMimicsInRadius", Hooks.Of(typeof(Bombs), nameof(RadiusAndDamage)), null);
            Hooks.Patch(typeof(DamageHandler), "DamageBreakablesInRadius", Hooks.Of(typeof(Bombs), nameof(RadiusOnly)), null);
            Hooks.Patch(typeof(DamageHandler), "DamageCryptLidsInRadius", Hooks.Of(typeof(Bombs), nameof(RadiusOnly)), null);
        }

        /// <summary>The local player's worn bomb multipliers; called whenever armor changes.</summary>
        public static void SetLocal(Dictionary<string, float> worn)
        {
            _localDamage = worn.TryGetValue(Damage, out var d) && d > 0f ? d : 1f;
            _localRadius = worn.TryGetValue(Radius, out var r) && r > 0f ? r : 1f;
            _nextPublish = 0f;
            if (_localDamage != 1f || _localRadius != 1f)
                Core.Log.Msg($"bombs: damage ×{_localDamage:0.###}, radius ×{_localRadius:0.###}");
        }

        /// <summary>Once a second: put our multipliers on our Photon player while the room is gated.</summary>
        public static void Tick()
        {
            var now = UnityEngine.Time.unscaledTime;
            if (now < _nextPublish) return;
            _nextPublish = now + 1f;
            if (Broken.Count > 0)
            {
                List<int> old = null;
                foreach (var kv in Broken) if (now - kv.Value.at > 10f) (old ??= new List<int>()).Add(kv.Key);
                if (old != null) foreach (var k in old) Broken.Remove(k);
            }
            if (!ModGate.Active) return;
            try
            {
                if (!PhotonNetwork.InRoom || PhotonNetwork.LocalPlayer == null) return;
                var room = PhotonNetwork.CurrentRoom?.Name ?? "";
                var value = Encode(_localDamage, _localRadius);
                if (value == _published && room == _publishedRoom) return;
                var props = new Hashtable();
                props[(Il2CppSystem.String)Key] = (Il2CppSystem.String)value;
                PhotonNetwork.LocalPlayer.SetCustomProperties(props, null, null);
                _published = value; _publishedRoom = room;
                Core.Log.Msg($"bombs: published {Key}={value} in room {room}");
            }
            catch (Exception e) { Core.Log.Warning($"bombs: publish failed: {e.GetType().Name}: {e.Message}"); }
        }

        private static string Encode(float d, float r) =>
            d.ToString("0.####", CultureInfo.InvariantCulture) + "|" + r.ToString("0.####", CultureInfo.InvariantCulture);

        /// <summary>A player's (damage, radius). Ourselves from the worn armor; others from their property, only while gated.</summary>
        private static (float d, float r) For(int actor)
        {
            try
            {
                var local = PhotonNetwork.LocalPlayer;
                if (local == null || actor == local.ActorNumber) return (_localDamage, _localRadius);
                if (actor <= 0 || !ModGate.Active) return (1f, 1f);
                foreach (var p in PhotonNetwork.PlayerList)
                {
                    if (p == null || p.ActorNumber != actor) continue;
                    var props = p.CustomProperties;
                    var k = (Il2CppSystem.String)Key;
                    if (ReferenceEquals(props, null) || !props.ContainsKey(k)) return (1f, 1f);
                    var s = props[k]?.ToString() ?? "";
                    var bar = s.IndexOf('|');
                    if (bar <= 0) return (1f, 1f);
                    float.TryParse(s.Substring(0, bar), NumberStyles.Float, CultureInfo.InvariantCulture, out var d);
                    float.TryParse(s.Substring(bar + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var r);
                    return (Sane(d), Sane(r));
                }
            }
            catch { }
            return (1f, 1f);
        }

        /// <summary>Between ×1 and ×2: anything else in a property is ignored.</summary>
        private static float Sane(float m) => m >= 1f && m <= 2f ? m : 1f;

        private static void OnBreak(Bomb __instance)
        {
            try
            {
                if (__instance == null) return;
                var pv = PhotonView.Get(__instance);
                if (pv == null) return;
                var actor = pv.OwnerActorNr;
                if (actor <= 0) actor = pv.ControllerActorNr;
                Broken[pv.ViewID] = (actor, UnityEngine.Time.unscaledTime);
                Core.Log.Msg($"bombs: {__instance.name} view {pv.ViewID} broke, thrower actor {actor}");
            }
            catch { }
        }

        private static void NodeStart(FXInstance instance)
        {
            _inBomb = false;
            try
            {
                if (instance == null || Broken.Count == 0) return;
                if (!Broken.TryGetValue(instance.ControllerViewID, out var b)) return;
                var (d, r) = For(b.actor);
                if (d == 1f && r == 1f) return;
                _curDamage = d; _curRadius = r; _inBomb = true;
            }
            catch { }
        }

        private static void NodeEnd() { _inBomb = false; _curDamage = 1f; _curRadius = 1f; }

        private static void RadiusAndDamage(ref float radius, ref float damage)
        {
            if (!_inBomb) return;
            var r0 = radius; var d0 = damage;
            radius *= _curRadius;
            damage *= _curDamage;
            Core.Log.Msg($"bombs: blast radius {r0:0.##} -> {radius:0.##}, damage {d0:0.#} -> {damage:0.#}");
        }

        private static void RadiusOnly(ref float radius)
        {
            if (_inBomb) radius *= _curRadius;
        }
    }
}
