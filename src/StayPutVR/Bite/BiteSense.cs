using System;
using Il2Cpp;
using StayPutVR.Net;
using StayPutVR.Trigger;
using UnityEngine;

namespace StayPutVR.Bite
{
    /// <summary>
    /// Biting, both ends of it.
    ///
    /// <b>As the biter:</b> a chomp from <see cref="JawWatch"/> plus a peer close enough and in
    /// front of you sends one bite to that peer alone. Nothing else happens locally — you do not
    /// shock yourself, and the game state on this client is untouched.
    ///
    /// <b>As the bitten:</b> your own client decides what a bite means. It applies the hit point
    /// to your own avatar through the game's own <c>OnDamaged</c>, and asks
    /// <see cref="ShockPolicy"/> for the shock. Doing it this way, rather than having the biter
    /// call <c>ApplyRemoteDamage</c> on you, is the whole safety design: a player who does not
    /// run the mod, or who has bites switched off, cannot be damaged or shocked by a biter no
    /// matter what the biter sends.
    ///
    /// The range test is <b>horizontal</b> distance plus a generous vertical window rather than a
    /// plain sphere, because the game's remote player puppet never lifts its head above about
    /// 1.48 m: a tall friend's head reads as much lower than it is, so true 3D distance would
    /// refuse bites that visually connect. Horizontal distance is unaffected by that pin.
    /// </summary>
    public static class BiteSense
    {
        public static int Sent { get; private set; }
        public static int Missed { get; private set; }
        public static int Received { get; private set; }
        private static float _lastSentAt = -1000f;
        private static string _lastOutcome = "";

        /// <summary>What happened to the last chomp, for the panel.</summary>
        public static string LastOutcome => _lastOutcome;

        public static void Init() => BiteNet.BiteReceived += OnBiteReceived;

        // ---- as the biter -----------------------------------------------------------------

        public static void Tick()
        {
            if (!ModConfig.BiteEnabled.Value) return;
            if (!JawWatch.Tick()) return;

            var now = Time.unscaledTime;
            if (now - _lastSentAt < Mathf.Max(0.2f, ModConfig.BiteCooldownSeconds.Value))
            {
                _lastOutcome = "chomp too soon after the last";
                ShockLog.Line($"chomp ignored: {_lastOutcome}");
                return;
            }

            var target = FindTarget(out var distance, out var why);
            if (target == null)
            {
                Missed++;
                _lastOutcome = why;
                ShockLog.Line($"chomp with no target: {why}");
                return;
            }

            if (BiteNet.SendBite(target.ActorNumber))
            {
                Sent++;
                _lastSentAt = now;
                _lastOutcome = $"bit actor {target.ActorNumber} at {distance:0.00} m";
                ShockLog.Headline($"Bite sent: {_lastOutcome}");
            }
            else
            {
                Missed++;
                _lastOutcome = $"actor {target.ActorNumber} would not take the bite";
            }
        }

        /// <summary>
        /// The closest peer that runs the mod, accepts bites, is within range and is in front of
        /// you. <paramref name="why"/> explains a refusal, which is most of what the log is for
        /// while the thresholds are being tuned.
        /// </summary>
        private static AvatarPlayer FindTarget(out float distance, out string why)
        {
            distance = 0f;
            why = "no reason recorded";

            AvatarPlayer local = null;
            try { local = AvatarPlayer.LocalAvatar; } catch { }
            if (!Interop.Alive(local)) { why = "no local avatar"; return null; }

            var head = Interop.Alive(local.Eye) ? local.Eye : local.Head;
            if (!Interop.Alive(head)) { why = "no local head transform"; return null; }

            if (BiteNet.BitablePeerCount == 0) { why = "nobody in the room accepts bites"; return null; }

            var range = Mathf.Max(0.05f, ModConfig.BiteRangeMeters.Value);
            var vertical = Mathf.Max(0.1f, ModConfig.BiteVerticalMeters.Value);
            var cosLimit = Mathf.Cos(Mathf.Clamp(ModConfig.BiteFacingAngle.Value, 5f, 180f) * Mathf.Deg2Rad);

            AvatarPlayer best = null;
            var bestDistance = float.MaxValue;
            var nearestRefused = float.MaxValue;
            var refusedWhy = "";

            try
            {
                var peers = AvatarPlayer.RemotePlayers;
                if (peers == null) { why = "no remote players"; return null; }

                for (var i = 0; i < peers.Count; i++)
                {
                    var peer = peers[i];
                    if (!Interop.Alive(peer)) continue;

                    int actor;
                    try { actor = peer.ActorNumber; } catch { continue; }
                    if (!BiteNet.Accepts(actor)) continue;

                    var peerHead = Interop.Alive(peer.Eye) ? peer.Eye : peer.Head;
                    var point = Interop.Alive(peerHead) ? peerHead.position
                              : Interop.Alive(peer.transform) ? peer.transform.position
                              : Vector3.zero;
                    if (point == Vector3.zero) continue;

                    var delta = point - head.position;
                    var flat = new Vector2(delta.x, delta.z).magnitude;

                    if (flat > range)
                    {
                        if (flat < nearestRefused) { nearestRefused = flat; refusedWhy = $"nearest willing peer is {flat:0.00} m away, range is {range:0.00} m"; }
                        continue;
                    }
                    if (Mathf.Abs(delta.y) > vertical)
                    {
                        if (flat < nearestRefused) { nearestRefused = flat; refusedWhy = $"actor {actor} is {Mathf.Abs(delta.y):0.00} m above or below you"; }
                        continue;
                    }

                    // Facing, measured horizontally for the same reason the range is: the
                    // puppet's pinned head height would otherwise tilt the angle.
                    var toPeer = new Vector3(delta.x, 0f, delta.z);
                    var forward = new Vector3(head.forward.x, 0f, head.forward.z);
                    if (toPeer.sqrMagnitude > 1e-4f && forward.sqrMagnitude > 1e-4f)
                    {
                        var cos = Vector3.Dot(forward.normalized, toPeer.normalized);
                        if (cos < cosLimit)
                        {
                            if (flat < nearestRefused)
                            {
                                nearestRefused = flat;
                                refusedWhy = $"actor {actor} is {Mathf.Acos(Mathf.Clamp(cos, -1f, 1f)) * Mathf.Rad2Deg:0}° off your gaze";
                            }
                            continue;
                        }
                    }

                    if (flat < bestDistance) { bestDistance = flat; best = peer; }
                }
            }
            catch (Exception e)
            {
                why = $"scanning peers threw {e.GetType().Name}";
                return null;
            }

            if (best == null)
            {
                why = refusedWhy.Length > 0 ? refusedWhy : "nobody in range";
                return null;
            }
            distance = bestDistance;
            return best;
        }

        // ---- as the bitten ----------------------------------------------------------------

        /// <summary>
        /// A bite arrived. The switch was already checked in <see cref="BiteNet"/>; this decides
        /// the shock and applies the hit point, in that order, so the shock is not held hostage
        /// to the damage call succeeding.
        /// </summary>
        private static void OnBiteReceived(int fromActor)
        {
            Received++;
            try { ShockPolicy.OnBite(fromActor); }
            catch (Exception e) { Core.Log.Warning($"Bite shock decision threw: {e.GetType().Name}: {e.Message}"); }
            try { ApplyDamage(fromActor); }
            catch (Exception e) { Core.Log.Warning($"Bite damage threw: {e.GetType().Name}: {e.Message}"); }
        }

        /// <summary>
        /// Take the bite's hit point through the game's own damage entry, on our own avatar. The
        /// resulting <c>OnDamaged</c> would otherwise come straight back to our own postfix and
        /// fire a second shock, so the policy is told to ignore the next hit.
        /// </summary>
        private static void ApplyDamage(int fromActor)
        {
            var amount = ModConfig.BiteDamage.Value;
            if (amount <= 0f) return;

            AvatarPlayer local = null;
            try { local = AvatarPlayer.LocalAvatar; } catch { }
            if (!Interop.Alive(local)) { ShockLog.Line("bite damage skipped: no local avatar"); return; }

            var type = DamageType.Melee;
            try
            {
                var name = (ModConfig.BiteDamageType.Value ?? "Melee").Trim();
                if (Enum.TryParse<DamageType>(name, true, out var parsed)) type = parsed;
            }
            catch { }

            var at = Vector3.zero;
            try
            {
                var head = Interop.Alive(local.Eye) ? local.Eye : local.Head;
                if (Interop.Alive(head)) at = head.position;
            }
            catch { }

            // The suppression window covers this one re-entrant call and nothing else.
            ShockPolicy.SuppressNextHit(0.5f);
            try
            {
                local.OnDamaged(amount, 0f, at, type);
                ShockLog.Line($"bite from actor {fromActor}: took {amount:0.#} HP as {type}");
            }
            catch (Exception e)
            {
                ShockPolicy.ClearSuppression();
                ShockLog.Line($"bite from actor {fromActor}: OnDamaged threw {e.GetType().Name}: {e.Message}");
            }
        }

        public static string Describe() =>
            $"bites sent {Sent}, chomps with no target {Missed}, bites received {Received}";
    }
}
