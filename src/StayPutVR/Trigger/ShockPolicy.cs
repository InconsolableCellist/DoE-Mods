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
    /// Intensity is the StayPutVR app's: it holds the intensity and duration for each parameter
    /// it listens on. What this end says is how hard the hit was — a float from 0 to 1 that the
    /// app (1.5.2 and up) fires at that fraction of its Shock max.
    /// <see cref="Severity"/> decides the number. Every trigger is a float; there is no bool
    /// mode, because an older app reads a float under 0.5 as false and would drop light hits,
    /// and a setting that quietly kept an old install on bool was worse than requiring the app.
    ///
    /// Going down is one shock, not a stream. The game keeps reporting hits while you lie there
    /// waiting for rescue, every one of them flagged as downed; the first is the killing blow
    /// and is allowed past the cooldown and the ceiling, the rest are held until you are up.
    ///
    /// For a while after a healing potion, a healing staff or life steal heals you, hits are held
    /// too (<see cref="HealShield"/>): a set length per potion, and for the staff and life steal a
    /// few seconds per tick that stack up to a cap. Full health counts. Not the killing blow:
    /// death always fires.
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

        /// <summary>Set by the hit that put you down, cleared by the first hit taken standing or by a scene change.</summary>
        private static bool _down;

        /// <summary>The time after a heal in which hits do not fire.</summary>
        public static readonly HealShield Shield = new HealShield();

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
            limits += $", heal shield {ShieldSummary()}";
            return limits + "; death always fires";
        }

        // ---- the hit ---------------------------------------------------------------------

        /// <summary>
        /// One hit on the local player, already confirmed to have landed. <paramref name="fraction"/>
        /// is the share of max HP it removed and <paramref name="remaining"/> the share left after
        /// it, which between them say how hard it was; <paramref name="damage"/> is the raw HP for
        /// the absolute floor.
        /// </summary>
        public static void OnHit(float damage, float fraction, float remaining, string damageType, bool downed)
        {
            var now = Time.unscaledTime;

            // A bite has already been decided and fired by OnBite; the hit point it then takes
            // comes back through the game's own OnDamaged and must not fire a second time.
            if (now < _suppressUntil)
            {
                _suppressUntil = -1f;
                _down = downed;   // a killing bite is still the death: the hits after it are not a new one
                if (ModConfig.LogEveryHit.Value) ShockLog.Line($"hit {damage:0.#} HP {damageType} — ignored, already handled as a bite");
                return;
            }

            LastHitAt = now;
            var magnitude = Severity.Compute(fraction, remaining, damageType, downed,
                                             ModConfig.SeverityCurve.Value, ModConfig.FallSeverityFloor.Value);
            LastHit = $"{damage:0.#} HP ({fraction * 100f:0}% of max, {Severity.Share(fraction, remaining) * 100f:0}% of what was left) {damageType}{(downed ? ", downed" : "")}";

            // The killing blow is the one hit allowed past the cooldown and the ceiling. Only the
            // first one: hits keep arriving while you are down, and they are not more deaths.
            var lethal = downed && !_down;
            var alreadyDown = downed && _down;
            _down = downed;
            if (lethal) Shield.Clear();   // it lets the killing blow through, and you are down now

            string hold = null;
            if (!_armed) hold = "disarmed";
            else if (alreadyDown) hold = "already down; one shock per death";
            else if (IsIgnoredType(damageType)) hold = $"{damageType} is in IgnoreDamageTypes";
            else if (ModConfig.MimicShock.Value && string.Equals(damageType, "Mimic", StringComparison.OrdinalIgnoreCase)) hold = "a mimic's attack; with MimicShock on, only waking it shocks";
            else if (damage < ModConfig.MinDamage.Value) hold = $"{damage:0.#} HP is under MinDamage {ModConfig.MinDamage.Value:0.#}";
            else if (fraction < ModConfig.MinDamageFraction.Value) hold = $"{fraction * 100f:0}% is under MinDamageFraction {ModConfig.MinDamageFraction.Value * 100f:0}%";
            else if (Shield.Holds(now, lethal)) hold = $"heal shield, {Shield.Left(now):0.0} s left";
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

            if (Fire(path, $"hit {LastHit}", magnitude) && ModConfig.LogEveryHit.Value)
                ShockLog.Line($"hit {LastHit} — fired {path} at {magnitude:0.00}{(pastLimits ? " (lethal, allowed past the limits)" : "")}");
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

        /// <summary>
        /// You woke a mimic (<see cref="MimicWatch"/>, which already makes it once per mimic).
        /// Fires <c>MimicPath</c> (or <c>ShockPath</c> when that is empty) at
        /// <c>MimicShockStrength</c>. Only the arm switch holds it: it happens once, so like the
        /// killing blow it goes past the cooldown and the per-minute ceiling, and the heal shield
        /// does not apply since this is not damage.
        /// </summary>
        public static void OnMimic(string what)
        {
            var now = Time.unscaledTime;
            LastHitAt = now;
            LastHit = what;

            if (!_armed)
            {
                HeldBack++;
                LastHold = "disarmed";
                ShockLog.Line($"{what} — held back: disarmed");
                return;
            }

            LastHold = "";
            var path = OscPacket.IsUsableAddress(ModConfig.MimicPath.Value)
                ? ModConfig.MimicPath.Value.Trim()
                : PathSummary();
            var magnitude = Mathf.Clamp01(ModConfig.MimicShockStrength.Value);
            if (Fire(path, what, magnitude))
                ShockLog.Headline($"{what} — fired {path} at {magnitude:0.00}");
        }

        /// <summary>Seconds of shield a heal from <paramref name="source"/> buys, from its setting. 0 = none.</summary>
        public static float ShieldSeconds(HealSource source) => source switch
        {
            HealSource.MinorPotion => ModConfig.ShieldMinorPotionSeconds.Value,
            HealSource.MajorPotion => ModConfig.ShieldMajorPotionSeconds.Value,
            HealSource.Staff => ModConfig.ShieldStaffSeconds.Value,
            HealSource.LifeSteal => ModConfig.ShieldLifeStealSeconds.Value,
            _ => 0f,
        };

        private static string SettingName(HealSource source) => source switch
        {
            HealSource.MinorPotion => "ShieldMinorPotionSeconds",
            HealSource.MajorPotion => "ShieldMajorPotionSeconds",
            HealSource.Staff => "ShieldStaffSeconds",
            _ => "ShieldLifeStealSeconds",
        };

        /// <summary>Whether a source adds to the shield per tick (<see cref="HealShield.Stack"/>) rather than setting a length.</summary>
        public static bool Stacks(HealSource source) => source == HealSource.Staff || source == HealSource.LifeSteal;

        /// <summary>ShieldStackMaxSeconds: the most the staff and life steal can stack to.</summary>
        public static float StackCap() => ModConfig.ShieldStackMaxSeconds.Value;

        /// <summary>The lengths as one phrase, e.g. "10/20 s potions, +2 s per staff tick and +2 s per life steal up to 45 s".</summary>
        public static string ShieldSummary() =>
            $"{ShieldSeconds(HealSource.MinorPotion):0.#}/{ShieldSeconds(HealSource.MajorPotion):0.#} s potions, " +
            $"+{ShieldSeconds(HealSource.Staff):0.#} s per staff tick and +{ShieldSeconds(HealSource.LifeSteal):0.#} s per life steal up to {StackCap():0.#} s";

        /// <summary>
        /// You were healed, by <paramref name="what"/>; <paramref name="share"/> is the health it
        /// added, 0 at full health. A potion starts the heal shield or pushes its end later; the
        /// staff and life steal add to it up to the cap; anything else — a revive, the game's own
        /// regain — is only logged. True when the shield's end moved.
        /// </summary>
        public static bool OnHealed(HealSource source, string what, float share)
        {
            var heal = share > 0.0001f ? $"healed {share * 100f:0}% ({what})" : $"heal at full health ({what})";
            if (source == HealSource.None)
            {
                if (ModConfig.LogEveryHit.Value) ShockLog.Line($"{heal} — no heal shield");
                return false;
            }
            var now = Time.unscaledTime;
            var seconds = ShieldSeconds(source);
            var name = HealSources.Name(source);
            var stacks = Stacks(source);
            var cap = Mathf.Max(StackCap(), seconds);
            // A staff beam ticks about once a second; the first tick of a run and reaching the cap
            // are worth a line, the ticks between are not.
            var sameRun = Shield.Active(now) && Shield.Source == name;
            var leftBefore = Shield.Left(now);
            var moved = stacks ? Shield.Stack(now, seconds, cap, name) : Shield.Start(now, seconds, name);
            if (moved)
            {
                var left = Shield.Left(now);
                var atCap = stacks && left >= cap - 0.01f;
                if (!sameRun || !stacks)
                    ShockLog.Line($"{heal} — heal shield for {left:0.#} s" + (stacks ? $" (+{seconds:0.#} s a tick, up to {cap:0.#} s)" : "")
                                  + (_armed ? "" : " (heal shield not shown: disarmed)"));
                else if (atCap && leftBefore < cap - 0.01f)
                    ShockLog.Line($"{heal} — heal shield at its {cap:0.#} s cap");
                return true;
            }
            if (ModConfig.LogEveryHit.Value && !(stacks && sameRun))
                ShockLog.Line(seconds <= 0f
                    ? $"{heal} — no heal shield, {SettingName(source)} is 0"
                    : $"{heal} — heal shield already runs longer ({leftBefore:0.0} s left, {Shield.Source})");
            return false;
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

        /// <summary>Forget that you were down, so the next downed hit counts as a death again.</summary>
        public static void ClearDown() => _down = false;

        public static void ClearShield() => Shield.Clear();

        /// <param name="magnitude">How hard, 0..1. Bites send 1.</param>
        private static bool Fire(string path, string what, float magnitude = 1f)
        {
            if (!OscPacket.IsUsableAddress(path))
            {
                HeldBack++;
                LastHold = $"'{path}' is not a usable OSC address";
                ShockLog.Headline($"{what} — refused: {LastHold}");
                return false;
            }
            var (host, port) = Discovery.Target(ModConfig.Host.Value, ModConfig.Port.Value);
            if (!OscSender.Ensure(host, port))
            {
                HeldBack++;
                LastHold = "no OSC socket";
                return false;
            }

            var datagram = Encode(path, true, magnitude);
            if (ModConfig.LogDatagrams.Value) ShockLog.Line($"-> {path} {magnitude:0.00}, {datagram.Length} bytes: {BitConverter.ToString(datagram)}");
            if (!OscSender.Send(datagram, what))
            {
                HeldBack++;
                LastHold = "the send failed";
                return false;
            }

            Fired++;
            _lastFireAt = Time.unscaledTime;
            RecentFires.Add(_lastFireAt);
            // A release still pending for another parameter is sent now: overwriting it left that
            // parameter latched on in the app (a mimic wake or a bite followed at once by the killing blow).
            if (_pendingReleasePath != null && _pendingReleasePath != path) FlushRelease();
            _pendingReleasePath = path;
            _pendingReleaseAt = _lastFireAt + Mathf.Clamp(ModConfig.ReleaseSeconds.Value, 0.02f, 2f);
            return true;
        }

        /// <summary>Always a float: the magnitude on the trigger, 0 on the release.</summary>
        private static byte[] Encode(string path, bool value, float magnitude = 1f)
            => OscPacket.Float(path, value ? Mathf.Clamp(magnitude, Severity.Least, 1f) : 0f);

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
