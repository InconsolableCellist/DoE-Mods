using System;
using System.Collections.Generic;
using StayPutVR.Osc;
using UnityEngine;

namespace StayPutVR.Trigger
{
    /// <summary>
    /// Everything between "you were hit" and "a datagram left the process": the arm switch, the
    /// damage floor, the cooldown, the rolling per-minute limit and the ignored damage types.
    ///
    /// The order of the checks is deliberate. Arm state is first, so a disarmed link cannot be
    /// talked into firing by any combination of the others. The killing blow is the one hit
    /// allowed past the cooldown and the limit. Every refusal is named and written to the session
    /// log, so an unexpected shock — or an unexpectedly quiet run — has a paper trail.
    ///
    /// Intensity is not ours to set: the StayPutVR app holds the intensity and duration for each
    /// parameter it listens on, and the trigger this end sends carries no magnitude.
    /// </summary>
    public static class ShockPolicy
    {
        private static bool _armed;
        private static float _lastFireAt = -1000f;
        private static readonly List<float> RecentFires = new List<float>();

        private static string _pendingReleasePath;
        private static float _pendingReleaseAt = -1f;
        private static float _suppressUntil = -1f;
        private static readonly List<float> RecentBites = new List<float>();

        private static string _ignoreSource;
        private static readonly HashSet<string> Ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static int Fired { get; private set; }
        public static int HeldBack { get; private set; }
        public static int Bites { get; private set; }
        /// <summary>Why the last hit did not fire, for the HUD and the log. Empty after a hit that did.</summary>
        public static string LastHold { get; private set; } = "";
        /// <summary>The last hit the mod saw, fired or not, as one short phrase.</summary>
        public static string LastHit { get; private set; } = "";
        public static float LastHitAt { get; private set; } = -1f;
        public static float LastFireAt => _lastFireAt;

        public static bool Armed => _armed;

        // ---- arming ----------------------------------------------------------------------

        public static void SetArmed(bool armed, string why)
        {
            if (_armed == armed) return;
            _armed = armed;
            ModConfig.SaveArmed(armed);
            ShockLog.Headline(armed
                ? $"ARMED ({why}) — hits now fire {Describe()}"
                : $"DISARMED ({why}) — nothing will be sent");
            // A trigger still waiting for its release is closed now rather than dropped, so
            // disarming can never leave a parameter latched true on StayPutVR's side.
            if (!armed) { FlushRelease(); _suppressUntil = -1f; }
        }

        /// <summary>Send any release that is still pending, without waiting for its due time.</summary>
        public static void FlushRelease()
        {
            if (_pendingReleasePath == null) return;
            var path = _pendingReleasePath;
            _pendingReleasePath = null;
            SendRelease(path);
        }

        public static void Toggle(string why) => SetArmed(!_armed, why);

        /// <summary>Where triggers are going and under what limits, as one line for the log and the startup banner.</summary>
        public static string Describe() => $"{OscSender.TargetDescription} {PathSummary()} ({LimitSummary()})";

        /// <summary>The parameter a hit fires.</summary>
        public static string PathSummary() => (ModConfig.ShockPath.Value ?? "").Trim();

        /// <summary>The cooldown and the ceiling, as one phrase.</summary>
        public static string LimitSummary()
        {
            var limits = ModConfig.CooldownSeconds.Value > 0f ? $"{ModConfig.CooldownSeconds.Value:0.#} s apart" : "no cooldown";
            if (ModConfig.MaxPerMinute.Value > 0) limits += $", at most {ModConfig.MaxPerMinute.Value}/min";
            return limits + "; death always fires";
        }

        // ---- the hit ---------------------------------------------------------------------

        /// <summary>
        /// One hit on the local player, already confirmed to have landed. <paramref name="fraction"/>
        /// is the share of max HP it removed, which is what severity is judged on;
        /// <paramref name="damage"/> is the raw HP for the absolute floor.
        /// </summary>
        public static void OnHit(float damage, float fraction, string damageType, bool downed)
        {
            var now = Time.unscaledTime;

            // A bite has already been decided and fired by OnBite; the hit point it then takes
            // comes back through the game's own OnDamaged and must not fire a second time.
            if (now < _suppressUntil)
            {
                _suppressUntil = -1f;
                if (ModConfig.LogEveryHit.Value) ShockLog.Line($"hit {damage:0.#} HP {damageType} — ignored, already handled as a bite");
                return;
            }

            LastHitAt = now;
            LastHit = $"{damage:0.#} HP ({fraction * 100f:0}%) {damageType}{(downed ? ", downed" : "")}";

            // The killing blow is the one hit allowed past the cooldown and the ceiling.
            var lethal = downed;

            string hold = null;
            if (!_armed) hold = "disarmed";
            else if (IsIgnoredType(damageType)) hold = $"{damageType} is in IgnoreDamageTypes";
            else if (damage < ModConfig.MinDamage.Value) hold = $"{damage:0.#} HP is under MinDamage {ModConfig.MinDamage.Value:0.#}";
            else if (fraction < ModConfig.MinDamageFraction.Value) hold = $"{fraction * 100f:0}% is under MinDamageFraction {ModConfig.MinDamageFraction.Value * 100f:0}%";
            else if (!lethal && now - _lastFireAt < ModConfig.CooldownSeconds.Value) hold = $"cooldown, {ModConfig.CooldownSeconds.Value - (now - _lastFireAt):0.#} s left";
            else if (!lethal && OverBudget(now)) hold = $"per-minute ceiling of {ModConfig.MaxPerMinute.Value} reached";

            if (hold != null)
            {
                HeldBack++;
                LastHold = hold;
                if (ModConfig.LogEveryHit.Value) ShockLog.Line($"hit {LastHit} — held back: {hold}");
                return;
            }

            LastHold = "";
            var path = PathSummary();
            // Noted before Fire() moves _lastFireAt: this is the one hit the limits let through.
            var pastLimits = lethal && (now - _lastFireAt < ModConfig.CooldownSeconds.Value || OverBudget(now));

            if (Fire(path, $"hit {LastHit}") && ModConfig.LogEveryHit.Value)
                ShockLog.Line($"hit {LastHit} — fired {path}{(pastLimits ? " (lethal, allowed past the limits)" : "")}");
        }

        /// <summary>
        /// A bite from another player. Its own ceiling applies on top of the ordinary cooldown,
        /// because a bite is a shock someone else asked for on your behalf: even with consent
        /// given and the peer behaving, being chain-bitten should not be possible.
        /// </summary>
        public static void OnBite(int fromActor)
        {
            var now = Time.unscaledTime;
            LastHitAt = now;
            LastHit = $"bite from actor {fromActor}";

            string hold = null;
            if (!_armed) hold = "disarmed";
            else if (now - _lastFireAt < ModConfig.CooldownSeconds.Value) hold = $"cooldown, {ModConfig.CooldownSeconds.Value - (now - _lastFireAt):0.#} s left";
            else if (OverBiteBudget(now)) hold = $"bite ceiling of {ModConfig.BiteMaxPerMinute.Value}/min reached";
            else if (OverBudget(now)) hold = $"per-minute ceiling of {ModConfig.MaxPerMinute.Value} reached";

            if (hold != null)
            {
                HeldBack++;
                LastHold = hold;
                ShockLog.Line($"bite from actor {fromActor} — held back: {hold}");
                return;
            }

            LastHold = "";
            var path = OscPacket.IsUsableAddress(ModConfig.BitePath.Value)
                ? ModConfig.BitePath.Value.Trim()
                : PathSummary();

            if (Fire(path, $"bite from actor {fromActor}"))
            {
                Bites++;
                RecentBites.Add(now);
                ShockLog.Headline($"Bitten by actor {fromActor} — fired {path}");
            }
        }

        /// <summary>Ignore the next hit that arrives within this many seconds. Used for the bite's own hit point.</summary>
        public static void SuppressNextHit(float seconds) => _suppressUntil = Time.unscaledTime + Mathf.Clamp(seconds, 0.05f, 2f);

        public static void ClearSuppression() => _suppressUntil = -1f;

        private static bool OverBiteBudget(float now)
        {
            var ceiling = ModConfig.BiteMaxPerMinute.Value;
            if (ceiling <= 0) return false;
            var count = 0;
            foreach (var t in RecentBites) if (now - t <= 60f) count++;
            return count >= ceiling;
        }

        /// <summary>Bites in the last rolling minute, for the panel.</summary>
        public static int BitesThisMinute()
        {
            var now = Time.unscaledTime;
            var count = 0;
            foreach (var t in RecentBites) if (now - t <= 60f) count++;
            return count;
        }

        // ---- sending ---------------------------------------------------------------------

        private static bool Fire(string path, string what)
        {
            if (!OscPacket.IsUsableAddress(path))
            {
                HeldBack++;
                LastHold = $"'{path}' is not a usable OSC address";
                ShockLog.Headline($"{what} — refused: {LastHold}");
                return false;
            }
            if (!OscSender.Ensure(ModConfig.Host.Value, ModConfig.Port.Value))
            {
                HeldBack++;
                LastHold = "no OSC socket";
                return false;
            }

            var datagram = Encode(path, true);
            if (ModConfig.LogDatagrams.Value) ShockLog.Line($"-> {path} true, {datagram.Length} bytes: {BitConverter.ToString(datagram)}");
            if (!OscSender.Send(datagram, what))
            {
                HeldBack++;
                LastHold = "the send failed";
                return false;
            }

            Fired++;
            _lastFireAt = Time.unscaledTime;
            RecentFires.Add(_lastFireAt);
            _pendingReleasePath = path;
            _pendingReleaseAt = _lastFireAt + Mathf.Clamp(ModConfig.ReleaseSeconds.Value, 0.02f, 2f);
            return true;
        }

        private static byte[] Encode(string path, bool value)
        {
            switch ((ModConfig.ValueType.Value ?? "bool").Trim().ToLowerInvariant())
            {
                case "int": return OscPacket.Int(path, value ? 1 : 0);
                case "float": return OscPacket.Float(path, value ? 1f : 0f);
                default: return OscPacket.Bool(path, value);
            }
        }

        /// <summary>Called every frame: sends the release that follows a trigger, and forgets fires older than a minute.</summary>
        public static void Tick()
        {
            var now = Time.unscaledTime;
            if (_pendingReleasePath != null && now >= _pendingReleaseAt)
            {
                var path = _pendingReleasePath;
                _pendingReleasePath = null;
                SendRelease(path);
            }
            for (var i = RecentFires.Count - 1; i >= 0; i--)
                if (now - RecentFires[i] > 60f) RecentFires.RemoveAt(i);
            for (var i = RecentBites.Count - 1; i >= 0; i--)
                if (now - RecentBites[i] > 60f) RecentBites.RemoveAt(i);
        }

        private static void SendRelease(string path)
        {
            var datagram = Encode(path, false);
            if (ModConfig.LogDatagrams.Value) ShockLog.Line($"-> {path} false, {datagram.Length} bytes: {BitConverter.ToString(datagram)}");
            OscSender.Send(datagram, "release");
        }

        private static bool OverBudget(float now)
        {
            var ceiling = ModConfig.MaxPerMinute.Value;
            if (ceiling <= 0) return false;
            var count = 0;
            foreach (var t in RecentFires) if (now - t <= 60f) count++;
            return count >= ceiling;
        }

        /// <summary>Triggers in the last rolling minute, for the overlay.</summary>
        public static int FiresThisMinute()
        {
            var now = Time.unscaledTime;
            var count = 0;
            foreach (var t in RecentFires) if (now - t <= 60f) count++;
            return count;
        }

        // ---- configuration that needs parsing --------------------------------------------

        private static bool IsIgnoredType(string damageType)
        {
            var source = ModConfig.IgnoreDamageTypes.Value ?? "";
            if (source != _ignoreSource)
            {
                _ignoreSource = source;
                Ignored.Clear();
                foreach (var part in source.Split(','))
                {
                    var name = part.Trim();
                    if (name.Length > 0) Ignored.Add(name);
                }
            }
            return Ignored.Count > 0 && Ignored.Contains(damageType);
        }

        public static string Stats() =>
            $"{Fired} trigger(s) sent ({Bites} from bites), {HeldBack} held back; {OscSender.Stats()}";
    }
}
