using System;
using System.Collections.Generic;
using System.Reflection;
using Il2Cpp;
using Il2CppExitGames.Client.Photon;
using Il2CppPhoton.Pun;
using Il2CppPhoton.Realtime;
using LootOverhaul.Gate;
using LootOverhaul.Recon;
using UnityEngine;
using AI = Il2CppSauron.AI;
using Interop = LootOverhaul.Recon.Interop;

namespace LootOverhaul.Loot
{
    /// <summary>
    /// Per-run party stats for the run panel (0.14.0): damage dealt, damage taken and, when
    /// StayPutVR runs, bites received.
    ///
    /// <b>Each player counts only themselves and shares the totals.</b> Counting other players'
    /// hits locally could drift: an enemy's health is simulated by its owner, a hit lands on the
    /// attacker's client first, and a missed or late RPC on one client would leave its copy of
    /// the numbers different from everyone else's. So nobody counts anybody else. Your own client
    /// counts the hits it deals (<c>AI.Net_Damage</c> with your actor number, the call your
    /// client makes when your weapon lands) and the damage your own avatar takes
    /// (<c>AvatarPlayer.OnDamaged</c> on the local avatar). It publishes the three numbers as a
    /// Photon player property, <c>lo.run</c>, which every client reads, so everyone sees the
    /// same table.
    ///
    /// - Overkill is not counted: a hit counts up to the health the enemy (or you) had left.
    /// - The property is written only while the gate is open (a fully modded private room), like
    ///   everything else this mod sends; a vanilla friend shows as "-". Offline, only your own row.
    /// - Bites come from StayPutVR's own counter (<c>StayPutVR.Bite.BiteSense.Received</c>), read
    ///   by reflection so neither mod needs the other. Without StayPutVR the column is left out.
    /// - The numbers reset when a dungeon loads and are kept through the lobby, where the end
    ///   screen (and the panel) show up.
    /// </summary>
    public static class RunStats
    {
        public const string Key = "lo.run";
        private const float PublishEvery = 2f;

        public static float Dealt { get; private set; }
        public static float Taken { get; private set; }
        public static int Hits { get; private set; }

        private static bool _counting;
        private static int _bitesAtStart = -1;
        private static bool _dirty;
        private static float _nextPublish;
        private static string _lastPublished;
        private static int _loggedHits;
        private static Il2CppSystem.String _key;

        private static PropertyInfo _bitesProp;
        private static bool _bitesLooked;

        public sealed class Row
        {
            public string Name;
            public bool IsLocal;
            public bool Known;
            public float Dealt, Taken;
            public int Bites = -1;
        }

        public static void Install()
        {
            var t = typeof(RunStats);
            var n = 0;
            // AI.Net_Damage(totalDamage, weaponDamage, damageType, actorNr, criticalHit), RVA 0xB19DD0, unique in dump.cs.
            n += Hooks.Patch(typeof(AI), "Net_Damage", Hooks.Of(t, nameof(NetDamage_Prefix)), null, paramCount: 5);
            // AvatarPlayer.OnDamaged(damage, knockBackDist, damagePosition, damageType), RVA 0x5759D0. Also patched by StayPutVR and recon.
            n += Hooks.Patch(typeof(AvatarPlayer), "OnDamaged", Hooks.Of(t, nameof(OnDamaged_Prefix)), Hooks.Of(t, nameof(OnDamaged_Postfix)), paramCount: 4);
            Core.Log.Msg($"RunStats: {n}/2 damage hooks installed.");
        }

        // ------------------------------------------------------------------ run lifecycle (from RunTracker)
        public static void NewRun()
        {
            Dealt = 0f; Taken = 0f; Hits = 0; _loggedHits = 0;
            _counting = true;
            var b = SpvrBites();
            _bitesAtStart = b;
            _dirty = true;
            _nextPublish = 0f;
        }

        public static void RunLeft()
        {
            _counting = false;
            _dirty = true;
            _nextPublish = 0f;
            Core.Log.Msg($"RunStats: run over - dealt {Dealt:0} in {Hits} hit(s), taken {Taken:0}, bites {LocalBites()}");
        }

        // ------------------------------------------------------------------ counting
        private static void NetDamage_Prefix(AI __instance, float __0, int __3)
        {
            try
            {
                if (!_counting || __0 <= 0f || !Interop.Alive(__instance)) return;
                var me = LocalActor();
                if (me >= 0 && __3 != me) return;   // someone else's hit (we are the enemy's owner): they count it
                float hp = -1f;
                try { var h = __instance.health; if (h != null) hp = h.HP.Value; } catch { }
                if (hp == 0f) return;               // already dead
                var counted = hp > 0f ? Mathf.Min(__0, hp) : __0;
                Dealt += counted;
                Hits++;
                _dirty = true;
                if (_loggedHits < 5) { _loggedHits++; ReconLog.Line($"runstats: hit {__0:0.#} (counted {counted:0.#}, enemy hp {hp:0.#}) actor {__3}, me {me}"); }
            }
            catch { }
        }

        private static void OnDamaged_Prefix(AvatarPlayer __instance, out float __state)
        {
            __state = -1f;
            try
            {
                if (!_counting || !IsLocal(__instance)) return;
                __state = __instance.health.HP.Value;
            }
            catch { }
        }

        private static void OnDamaged_Postfix(AvatarPlayer __instance, float __0, bool __result, float __state)
        {
            try
            {
                if (!_counting || !__result || __0 <= 0f || __state < 0f) return;
                var counted = __state > 0f ? Mathf.Min(__0, __state) : 0f;
                if (counted <= 0f) return;
                Taken += counted;
                _dirty = true;
            }
            catch { }
        }

        // ------------------------------------------------------------------ sharing
        /// <summary>Called every frame: publishes our numbers every couple of seconds while they change.</summary>
        public static void Tick()
        {
            if (!_dirty) return;
            var now = Time.unscaledTime;
            if (now < _nextPublish) return;
            _nextPublish = now + PublishEvery;
            try
            {
                if (!ModGate.Active || !PhotonNetwork.InRoom || PhotonNetwork.OfflineMode) return;   // stays dirty until it can go out
                var value = $"{Mathf.RoundToInt(Dealt)}|{Mathf.RoundToInt(Taken)}|{LocalBites()}";
                _dirty = false;
                if (value == _lastPublished) return;
                var props = new Hashtable();
                props[KeyString()] = (Il2CppSystem.String)value;
                PhotonNetwork.LocalPlayer.SetCustomProperties(props, null, null);
                _lastPublished = value;
            }
            catch (Exception e) { Core.Log.Warning($"RunStats: publish failed: {e.GetType().Name}: {e.Message}"); }
        }

        /// <summary>One row per player in the room (or just us offline), ours first.</summary>
        public static List<Row> Party()
        {
            var rows = new List<Row>();
            var mine = new Row { Name = "You", IsLocal = true, Known = true, Dealt = Dealt, Taken = Taken, Bites = LocalBites() };
            rows.Add(mine);
            try
            {
                if (!PhotonNetwork.InRoom || PhotonNetwork.OfflineMode) return rows;
                foreach (var p in PhotonNetwork.PlayerList)
                {
                    if (p == null || p.IsLocal) continue;
                    var r = new Row { Name = Nick(p) };
                    var raw = ReadProp(p);
                    if (raw != null)
                    {
                        var parts = raw.Split('|');
                        if (parts.Length >= 3 && float.TryParse(parts[0], out var d) && float.TryParse(parts[1], out var t) && int.TryParse(parts[2], out var b))
                        { r.Known = true; r.Dealt = d; r.Taken = t; r.Bites = b; }
                    }
                    rows.Add(r);
                }
            }
            catch (Exception e) { Core.Log.Warning($"RunStats: reading the party failed: {e.GetType().Name}: {e.Message}"); }
            return rows;
        }

        private static string ReadProp(Player p)
        {
            try
            {
                var ht = p.CustomProperties;
                if (ReferenceEquals(ht, null)) return null;
                var k = KeyString();
                if (!ht.ContainsKey(k)) return null;
                var v = ht[k];
                return ReferenceEquals(v, null) ? null : v.ToString();
            }
            catch { return null; }
        }

        private static Il2CppSystem.String KeyString() => _key ??= (Il2CppSystem.String)Key;

        // ------------------------------------------------------------------ StayPutVR bites
        /// <summary>Bites received this run, or -1 without StayPutVR.</summary>
        public static int LocalBites()
        {
            var b = SpvrBites();
            if (b < 0) return -1;
            if (_bitesAtStart < 0) _bitesAtStart = b;   // StayPutVR seen only after the run began
            return Math.Max(0, b - _bitesAtStart);
        }

        private static int SpvrBites()
        {
            try
            {
                if (!_bitesLooked)
                {
                    _bitesLooked = true;
                    foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        if (a.GetName().Name != "StayPutVR") continue;
                        _bitesProp = a.GetType("StayPutVR.Bite.BiteSense")?.GetProperty("Received", BindingFlags.Public | BindingFlags.Static);
                        break;
                    }
                    Core.Log.Msg(_bitesProp != null ? "RunStats: StayPutVR found - bites received are shown on the run panel" : "RunStats: no StayPutVR - no bites column");
                }
                return _bitesProp == null ? -1 : (int)_bitesProp.GetValue(null);
            }
            catch { _bitesProp = null; return -1; }
        }

        // ------------------------------------------------------------------ helpers
        private static int LocalActor()
        {
            try { return PhotonNetwork.InRoom && !PhotonNetwork.OfflineMode && PhotonNetwork.LocalPlayer != null ? PhotonNetwork.LocalPlayer.ActorNumber : -1; }
            catch { return -1; }
        }

        private static bool IsLocal(AvatarPlayer a)
        {
            try { var l = AvatarPlayer.LocalAvatar; return Interop.Alive(a) && Interop.Alive(l) && a.Pointer == l.Pointer; }
            catch { return false; }
        }

        private static string Nick(Player p)
        {
            try { var n = p.NickName; return string.IsNullOrEmpty(n) ? $"Player {p.ActorNumber}" : n; } catch { return "?"; }
        }
    }
}
