using System;
using System.Collections.Generic;
using System.Reflection;
using Il2Cpp;
using Il2CppPhoton.Pun;
using UnityEngine;

namespace PartyHealth.Peers
{
    /// <summary>
    /// What the mod knows about every other player's health, from two sources that agree when
    /// both work and cover for each other when one does not.
    ///
    /// The game never runs <c>AvatarPlayer.OnDamaged</c> for anyone but the local avatar (the
    /// StayPutVR hook on it has counted zero foreign hits in every session), so that is no use
    /// here. What every client does receive is the owner's PunRPCs on its copy of the avatar:
    /// <c>RPC_OnDamaged</c> with the HP that resulted, <c>RPC_OnHealed</c> with the new HP,
    /// <c>RespawnAvatar</c> with the spawn HP (also sent to a late joiner for everyone already
    /// in the room), and the reset, revive, rescue and rescue-failed calls. Read-only postfixes
    /// on those are the first source. The second is the remote copy's own
    /// <c>AvatarPlayer.Health</c>, polled ten times a second: if the game writes the RPC values
    /// into it — likely, since the downed and rescue visuals need them — the polled HP moves,
    /// and a polled value that moves wins over the last RPC. If it never moves, the RPCs carry
    /// the display alone. Whichever it turns out to be, the log says.
    ///
    /// Max HP on a remote copy is not known to be set. When it reads as zero the highest HP
    /// ever seen for that player stands in, which the spawn RPC makes right within a second of
    /// meeting them.
    /// </summary>
    public static class PeerHealth
    {
        public sealed class Peer
        {
            public int Actor;
            public string Name;
            public AvatarPlayer Avatar;
            public Transform Anchor;
            public string AnchorSource = "";
            public bool Demo;
            public Vector3 DemoPosition;

            public float Hp;
            public float MaxHp;
            public float HighestHp;
            public bool Downed;
            public bool Dead;
            public bool Seen;

            public float LastChangeAt = -100f;
            public float LastHitAt = -100f;
            public float LastSeenAt;
            public float NameRetryAt;
            public float DownedAt = -100f;

            public float LastPollHp = float.NaN;
            public bool LastPollDowned;
            public int PollChanges, RpcChanges;
            public string LastSource = "none";

            public float Ceiling => MaxHp > 0.5f ? MaxHp : HighestHp;
            public float Fraction => Ceiling > 0.01f ? Mathf.Clamp01(Hp / Ceiling) : (Seen ? 1f : 0f);
            public bool Full => !Downed && Fraction >= 0.995f;
        }

        private static readonly Dictionary<int, Peer> Peers = new Dictionary<int, Peer>();
        private static readonly List<int> Gone = new List<int>();
        private static float _nextPollAt;
        private static int _localRpcs, _rpcsSeen, _pollsRun;
        private static bool _loggedNoList;

        private const float PollSeconds = 0.1f;
        private const float ForgetAfterSeconds = 2f;
        private const int DemoActor = -999;

        public static IEnumerable<Peer> All => Peers.Values;
        public static int Count => Peers.Count;

        // ---- hooks -------------------------------------------------------------------------

        public static void Install()
        {
            Hook("RPC_OnDamaged", nameof(OnDamaged_Postfix));
            Hook("RPC_OnHealed", nameof(OnHealed_Postfix));
            Hook("RespawnAvatar", nameof(Respawn_Postfix));
            Hook("RPC_ResetHealth", nameof(ResetHealth_Postfix));
            Hook("RPC_Revive", nameof(Revive_Postfix));
            Hook("RPC_Rescue", nameof(Rescue_Postfix));
            Hook("RPC_RescueOutOfTime", nameof(RescueOutOfTime_Postfix));
        }

        private static void Hook(string method, string postfix)
        {
            MethodInfo target = null;
            try { target = typeof(AvatarPlayer).GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic); }
            catch (Exception e) { Core.Log.Error($"Looking up AvatarPlayer.{method} threw {e.GetType().Name}: {e.Message}"); }
            Hooks.Patch(target, null, Hooks.Of(typeof(PeerHealth), postfix), $"AvatarPlayer.{method}");
        }

        // The postfixes never touch the game. Each one finds (or starts) the peer for the
        // avatar the RPC ran on and records what the RPC said. A throw out of a Harmony
        // postfix on an il2cpp method is not survivable, so nothing is allowed to leave them.

        private static void OnDamaged_Postfix(AvatarPlayer __instance, Vector3 __0, float __1, int __2, float __3, bool __4)
        {
            try
            {
                var p = ForRpc(__instance, "RPC_OnDamaged");
                if (p == null) return;
                var type = "?"; try { type = ((DamageType)__2).ToString(); } catch { }
                Apply(p, __1, $"RPC_OnDamaged dmg={__3:0.#} type={type} rescue={__4}");
                p.LastHitAt = Time.unscaledTime;
                if (__4 || __1 <= 0.01f) SetDowned(p, true, "RPC_OnDamaged");
            }
            catch (Exception e) { Swallow("RPC_OnDamaged", e); }
        }

        private static void OnHealed_Postfix(AvatarPlayer __instance, float __0, bool __1)
        {
            try
            {
                var p = ForRpc(__instance, "RPC_OnHealed");
                if (p == null) return;
                Apply(p, __0, $"RPC_OnHealed ahhh={__1}");
                if (__0 > 0.01f) SetDowned(p, false, "RPC_OnHealed");
            }
            catch (Exception e) { Swallow("RPC_OnHealed", e); }
        }

        private static void Respawn_Postfix(AvatarPlayer __instance, Vector3 __0, Vector3 __1, bool __2, bool __3, float __4, bool __5)
        {
            try
            {
                var p = ForRpc(__instance, "RespawnAvatar");
                if (p == null) return;
                Apply(p, __4, $"RespawnAvatar initial={__2} updatingNewPlayer={__3}");
                p.Dead = false;
                SetDowned(p, false, "RespawnAvatar");
            }
            catch (Exception e) { Swallow("RespawnAvatar", e); }
        }

        private static void ResetHealth_Postfix(AvatarPlayer __instance)
        {
            try
            {
                var p = ForRpc(__instance, "RPC_ResetHealth");
                if (p == null) return;
                if (p.Ceiling > 0.01f) Apply(p, p.Ceiling, "RPC_ResetHealth");
                p.Dead = false;
                SetDowned(p, false, "RPC_ResetHealth");
            }
            catch (Exception e) { Swallow("RPC_ResetHealth", e); }
        }

        private static void Revive_Postfix(AvatarPlayer __instance)
        {
            try { var p = ForRpc(__instance, "RPC_Revive"); if (p != null) { p.Dead = false; SetDowned(p, false, "RPC_Revive"); } }
            catch (Exception e) { Swallow("RPC_Revive", e); }
        }

        private static void Rescue_Postfix(AvatarPlayer __instance)
        {
            try { var p = ForRpc(__instance, "RPC_Rescue"); if (p != null) { p.Dead = false; SetDowned(p, false, "RPC_Rescue"); } }
            catch (Exception e) { Swallow("RPC_Rescue", e); }
        }

        private static void RescueOutOfTime_Postfix(AvatarPlayer __instance)
        {
            try
            {
                var p = ForRpc(__instance, "RPC_RescueOutOfTime");
                if (p == null) return;
                p.Dead = true;
                p.LastChangeAt = Time.unscaledTime;
                SetDowned(p, true, "RPC_RescueOutOfTime");
                HealthLog.Line($"{Describe(p)}: rescue ran out, they are dead until the next respawn");
            }
            catch (Exception e) { Swallow("RPC_RescueOutOfTime", e); }
        }

        private static void Swallow(string where, Exception e)
        {
            try { Core.Log.Warning($"{where} postfix threw: {e.GetType().Name}: {e.Message}"); } catch { }
        }

        /// <summary>The peer an RPC ran on, or null for the local avatar (counted, not tracked) and anything unreadable.</summary>
        private static Peer ForRpc(AvatarPlayer avatar, string rpc)
        {
            _rpcsSeen++;
            if (!Interop.Alive(avatar)) return null;
            if (IsLocal(avatar)) { _localRpcs++; return null; }
            int actor;
            try { actor = avatar.ActorNumber; } catch { return null; }
            var p = Get(actor, avatar);
            if (!p.Seen) FirstSight(p, $"first seen through {rpc}");
            p.LastSeenAt = Time.unscaledTime;
            return p;
        }

        // ---- polling -------------------------------------------------------------------------

        public static void Tick()
        {
            var now = Time.unscaledTime;
            if (now < _nextPollAt) return;
            _nextPollAt = now + PollSeconds;
            _pollsRun++;

            try { PollPlayers(now); }
            catch (Exception e) { if (ModConfig.VerboseLogging.Value) Core.Log.Warning($"Poll threw: {e.GetType().Name}: {e.Message}"); }

            Gone.Clear();
            foreach (var kv in Peers)
            {
                var p = kv.Value;
                if (p.Demo) { TickDemo(p, now); continue; }
                if (now - p.LastSeenAt > ForgetAfterSeconds) Gone.Add(kv.Key);
            }
            foreach (var actor in Gone)
            {
                var p = Peers[actor];
                HealthLog.Line($"{Describe(p)}: gone (not in the player list for {ForgetAfterSeconds:0} s); {p.PollChanges} polled, {p.RpcChanges} RPC change(s)");
                Peers.Remove(actor);
            }
        }

        private static void PollPlayers(float now)
        {
            var list = AvatarPlayer.RemotePlayers;
            var usingAll = false;
            if (list == null) { list = AvatarPlayer.AllPlayers; usingAll = true; }
            if (list == null)
            {
                if (!_loggedNoList) { _loggedNoList = true; HealthLog.Line("AvatarPlayer.RemotePlayers and AllPlayers are both null; waiting."); }
                return;
            }

            for (var i = 0; i < list.Count; i++)
            {
                var avatar = list[i];
                if (!Interop.Alive(avatar)) continue;
                if (usingAll && IsLocal(avatar)) continue;
                if (!usingAll && IsLocal(avatar)) continue;   // belt and braces: never a bar over your own head

                int actor;
                try { actor = avatar.ActorNumber; } catch { continue; }
                var p = Get(actor, avatar);
                p.LastSeenAt = now;
                if (!p.Seen) FirstSight(p, usingAll ? "first seen in AllPlayers" : "first seen in RemotePlayers");
                if (string.IsNullOrEmpty(p.Name) || p.Name.StartsWith("Player ")) { if (now >= p.NameRetryAt) { p.NameRetryAt = now + 5f; p.Name = ResolveName(actor, avatar); } }

                PollHealth(p, avatar);
            }
        }

        private static void PollHealth(Peer p, AvatarPlayer avatar)
        {
            AvatarPlayer.Health h;
            try { h = avatar.health; } catch { return; }
            if (!Interop.Alive(h)) return;

            float max = 0f, hp = float.NaN;
            try { max = h.maxHP; } catch { }
            try { hp = h.HP.Value; } catch { }
            if (float.IsNaN(hp) || float.IsInfinity(hp))
            {
                try { var n = h.normalizedHP; if (max > 0.01f && !float.IsNaN(n) && !float.IsInfinity(n)) hp = n * max; } catch { }
            }
            if (max > 0.5f && Mathf.Abs(max - p.MaxHp) > 0.01f)
            {
                if (p.MaxHp > 0.5f) HealthLog.Line($"{Describe(p)}: max HP {p.MaxHp:0.#} -> {max:0.#} (polled)");
                p.MaxHp = max;
            }

            var downed = false;
            try { downed = !h.IsAlive || h.lastChance || h.waitingForRescue; } catch { }

            if (!float.IsNaN(hp) && !float.IsInfinity(hp) && hp >= 0f)
            {
                // A polled value only counts when it moves: a health object the game never
                // writes to sits at its spawn value and must not override the RPCs.
                if (float.IsNaN(p.LastPollHp)) { p.LastPollHp = hp; if (p.LastSource == "none") Apply(p, hp, "poll (first read)"); }
                else if (Mathf.Abs(hp - p.LastPollHp) > 0.01f)
                {
                    if (hp < p.LastPollHp - 0.01f) p.LastHitAt = Time.unscaledTime;
                    p.LastPollHp = hp;
                    Apply(p, hp, "poll");
                }
            }
            if (downed != p.LastPollDowned)
            {
                p.LastPollDowned = downed;
                SetDowned(p, downed, "poll");
            }
        }

        // ---- state ---------------------------------------------------------------------------

        private static Peer Get(int actor, AvatarPlayer avatar)
        {
            if (!Peers.TryGetValue(actor, out var p))
            {
                p = new Peer { Actor = actor, Avatar = avatar, LastSeenAt = Time.unscaledTime };
                Peers[actor] = p;
            }
            else if (Interop.Alive(avatar) && (!Interop.Alive(p.Avatar) || p.Avatar.Pointer != avatar.Pointer))
            {
                p.Avatar = avatar;
                p.Anchor = null;   // a respawned avatar is a new object; the bar re-anchors
            }
            return p;
        }

        private static void Apply(Peer p, float hp, string source)
        {
            var rpc = source.StartsWith("RPC") || source.StartsWith("Respawn");
            if (rpc) p.RpcChanges++; else p.PollChanges++;
            var previous = p.Hp;
            var changed = Mathf.Abs(hp - p.Hp) > 0.01f || p.LastSource == "none";
            p.Hp = hp;
            if (hp > p.HighestHp) p.HighestHp = hp;
            p.LastSource = source;
            if (changed) p.LastChangeAt = Time.unscaledTime;
            if (changed && ModConfig.LogHealthChanges.Value)
                HealthLog.Line($"{Describe(p)}: hp {previous:0.#} -> {hp:0.#} of {p.Ceiling:0.#}{(p.MaxHp > 0.5f ? "" : " (highest seen; max HP reads 0)")} = {p.Fraction * 100f:0}%  [{source}]");
        }

        private static void SetDowned(Peer p, bool downed, string source)
        {
            if (p.Downed == downed) return;
            p.Downed = downed;
            p.LastChangeAt = Time.unscaledTime;
            if (downed) p.DownedAt = Time.unscaledTime;
            HealthLog.Line($"{Describe(p)}: {(downed ? "DOWN" : "back up")}  [{source}]");
        }

        /// <summary>
        /// Everything readable off a peer the first time: this block, once per friend per
        /// session, is the probe for the open question of what a remote health object holds.
        /// </summary>
        private static void FirstSight(Peer p, string how)
        {
            p.Seen = true;
            p.Name = ResolveName(p.Actor, p.Avatar);
            var a = p.Avatar;
            string hpRaw = "?", max = "?", norm = "?", alive = "?", lastChance = "?", waiting = "?", healthAlive = "no";
            try
            {
                var h = a.health;
                if (Interop.Alive(h))
                {
                    healthAlive = "yes";
                    try { max = h.maxHP.ToString("0.#"); } catch (Exception e) { max = e.GetType().Name; }
                    try { hpRaw = h.HP.Value.ToString("0.#"); } catch (Exception e) { hpRaw = e.GetType().Name; }
                    try { norm = h.normalizedHP.ToString("0.###"); } catch (Exception e) { norm = e.GetType().Name; }
                    try { alive = h.IsAlive.ToString(); } catch { }
                    try { lastChance = h.lastChance.ToString(); } catch { }
                    try { waiting = h.waitingForRescue.ToString(); } catch { }
                }
            }
            catch (Exception e) { healthAlive = e.GetType().Name; }

            string head = "<null>", eye = "<null>", ik = "<null>", headY = "?", ikY = "?", posY = "?", level = "?";
            try { head = Interop.ScenePath(a.Head); if (Interop.Alive(a.Head)) headY = a.Head.position.y.ToString("0.00"); } catch { }
            try { ik = Interop.ScenePath(a.IKTargetHead); if (Interop.Alive(a.IKTargetHead)) ikY = a.IKTargetHead.position.y.ToString("0.00"); } catch { }
            try { eye = Interop.ScenePath(a.Eye); } catch { }
            try { posY = a.Position.y.ToString("0.00"); } catch { }
            try { level = a.PlayerLevel.Value.ToString(); } catch { }

            HealthLog.Headline($"Tracking {Describe(p)} ({how}): health object {healthAlive}, HP {hpRaw}, max {max}, normalized {norm}, alive {alive}, lastChance {lastChance}, waitingForRescue {waiting}; level {level}; head target `{ik}` at y {ikY}, head bone `{head}` at y {headY} (feet y {posY}); eye `{eye}`");
        }

        private static string ResolveName(int actor, AvatarPlayer avatar)
        {
            try
            {
                var room = PhotonNetwork.CurrentRoom;
                var player = room?.GetPlayer(actor);
                if (player != null && !string.IsNullOrWhiteSpace(player.NickName)) return player.NickName;
            }
            catch { }
            try { if (Interop.Alive(avatar) && !string.IsNullOrWhiteSpace(avatar.PlayerName)) return avatar.PlayerName; } catch { }
            return $"Player {actor}";
        }

        private static bool IsLocal(AvatarPlayer avatar)
        {
            try
            {
                var local = AvatarPlayer.LocalAvatar;
                return Interop.Alive(local) && avatar.Pointer == local.Pointer;
            }
            catch { return false; }
        }

        public static string Describe(Peer p) => p.Demo ? "demo bar" : $"{p.Name ?? "?"} (actor {p.Actor})";

        public static void Clear(string why)
        {
            if (Peers.Count > 0) HealthLog.Line($"Forgetting {Peers.Count} player(s): {why}");
            Peers.Clear();
        }

        public static string Stats()
        {
            int polled = 0, rpc = 0;
            foreach (var p in Peers.Values) { polled += p.PollChanges; rpc += p.RpcChanges; }
            return $"{Peers.Count} player(s) tracked at quit, {_pollsRun} polls, {_rpcsSeen} RPC(s) seen ({_localRpcs} on the local avatar), health changes: {polled} from polling, {rpc} from RPCs";
        }

        // ---- demo ------------------------------------------------------------------------------

        private static float _demoStartedAt;

        /// <summary>
        /// A pretend friend 1.5 m in front of the eyes: full, drains to nothing over five
        /// seconds, lies DOWN for two, is rescued to a third, heals to full, and is forgotten
        /// after the hold. Everything the real bar can do, without a friend.
        /// </summary>
        public static void StartDemo(Vector3 position)
        {
            var p = Get(DemoActor, null);
            p.Demo = true; p.Name = "Demo"; p.Seen = true;
            p.DemoPosition = position;
            p.MaxHp = 100f; p.HighestHp = 100f; p.Hp = 100f;
            p.Downed = false; p.Dead = false;
            p.LastChangeAt = Time.unscaledTime; p.LastSeenAt = Time.unscaledTime;
            p.LastSource = "demo";
            _demoStartedAt = Time.unscaledTime;
            HealthLog.Line("Demo bar started");
        }

        private static void TickDemo(Peer p, float now)
        {
            var t = now - _demoStartedAt;
            p.LastSeenAt = now;
            float hp; var downed = false;
            if (t < 0.8f) hp = 100f;
            else if (t < 5.8f) { hp = Mathf.Lerp(100f, 0f, (t - 0.8f) / 5f); if (Mathf.FloorToInt(t * 2f) != Mathf.FloorToInt((t - Time.unscaledDeltaTime) * 2f)) p.LastHitAt = now; }
            else if (t < 7.8f) { hp = 0f; downed = true; }
            else if (t < 8.3f) hp = 33f;
            else if (t < 11.3f) hp = Mathf.Lerp(33f, 100f, (t - 8.3f) / 3f);
            else if (t < 11.3f + ModConfig.HoldAfterFullSeconds.Value + 1f) hp = 100f;
            else { Peers.Remove(DemoActor); HealthLog.Line("Demo bar finished"); return; }
            if (Mathf.Abs(hp - p.Hp) > 0.01f) { p.Hp = hp; p.LastChangeAt = now; }
            if (downed != p.Downed) { p.Downed = downed; p.LastChangeAt = now; if (downed) p.DownedAt = now; }
        }
    }
}
