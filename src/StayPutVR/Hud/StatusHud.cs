using System;
using System.Collections.Generic;
using StayPutVR.Bite;
using StayPutVR.Net;
using StayPutVR.Osc;
using StayPutVR.Trigger;
using UnityEngine;

namespace StayPutVR.Hud
{
    /// <summary>
    /// A small always-there panel in the top right of the desktop window: whether the link is
    /// armed, where triggers are going, what has gone out, and which key does what.
    ///
    /// Desktop rather than in the headset, the same as the CustomAvatars overlay, and for the
    /// same reason: Unity's IMGUI draws to the desktop mirror only. A head-locked panel was the
    /// first attempt and it is the wrong thing here — the arm state is something you check while
    /// setting the link up, not something that should sit in your eyeline for a whole dungeon.
    /// What confirms the state in VR is the shock itself, and the arm gesture is on the sticks
    /// precisely so it needs no display.
    /// </summary>
    public static class StatusHud
    {
        private const int Width = 460;
        private const int Pad = 12;
        private const int LineHeight = 19;
        private const int TitleHeight = 26;

        private static readonly Color ArmedColor = new Color(1.00f, 0.36f, 0.32f);
        private static readonly Color DisarmedColor = new Color(0.72f, 0.76f, 0.80f);
        private static readonly Color TroubleColor = new Color(1.00f, 0.78f, 0.20f);

        private static GUIStyle _centre, _state;
        private static readonly List<string> Lines = new List<string>(16);
        private static bool _drawn;

        private static void EnsureStyles()
        {
            if (_centre != null) return;
            // Built here, not in a field initialiser: GUI.skin only exists inside OnGUI.
            _centre = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            _state = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold };
        }

        public static void Draw()
        {
            if (!ModConfig.Enabled.Value || !ModConfig.HudEnabled.Value) return;

            const int width = Width;
            BuildLines(out var state, out var stateColor);

            var height = Pad * 2 + TitleHeight + (Lines.Count + 1) * LineHeight;
            var rect = new Rect(Screen.width - width - Pad, Pad, width, height);

            GUI.Box(rect, "");
            EnsureStyles();

            GUI.Label(new Rect(rect.x + Pad, rect.y + 4, width - Pad * 2, TitleHeight - 6),
                      $"StayPutVR {Core.Version}", _centre);

            var y = rect.y + TitleHeight;
            var wasColor = GUI.contentColor;
            GUI.contentColor = stateColor;
            GUI.Label(new Rect(rect.x + Pad, y, width - Pad * 2, LineHeight + 4), state, _state);
            GUI.contentColor = wasColor;
            y += LineHeight;

            foreach (var line in Lines)
            {
                // A taller rect than the line advance, so descenders are not clipped.
                GUI.Label(new Rect(rect.x + Pad, y, width - Pad * 2, LineHeight + 4), line);
                y += LineHeight;
            }

            if (!_drawn) { _drawn = true; Core.Log.Msg("Desktop panel drawn (top right of the game window)."); }
        }

        private static void BuildLines(out string state, out Color stateColor)
        {
            var armed = ShockPolicy.Armed;
            state = armed ? "ARMED — hits fire your device" : "DISARMED — nothing will be sent";
            stateColor = armed ? ArmedColor : DisarmedColor;

            Lines.Clear();
            Lines.Add($"Link: {OscSender.TargetDescription}   {ShockPolicy.PathSummary()}");
            Lines.Add($"Limits: {ShockPolicy.LimitSummary()}");

            var last = ShockPolicy.LastFireAt > 0f ? $"{Time.unscaledTime - ShockPolicy.LastFireAt:0} s ago" : "never";
            Lines.Add($"Sent: {ShockPolicy.Fired}   this minute: {ShockPolicy.FiresThisMinute()}   last: {last}");
            Lines.Add($"Held back: {ShockPolicy.HeldBack}{(ShockPolicy.LastHold.Length > 0 ? " — " + ShockPolicy.LastHold : "")}");
            Lines.Add($"Last hit: {(ShockPolicy.LastHit.Length > 0 ? ShockPolicy.LastHit : "none yet")}");

            BiteLines();

            // A link that cannot send is worse news than an armed one, and an armed-looking
            // panel that silently does nothing is what this panel exists to prevent. Shown for
            // a while after the last failure, then dropped, so a recovered link stops reading
            // as broken.
            var failedRecently = OscSender.LastFailureAt > 0f && Time.unscaledTime - OscSender.LastFailureAt < 20f;
            if (failedRecently && OscSender.LastError.Length > 0)
            {
                stateColor = TroubleColor;
                state = "LINK TROUBLE — " + Shorten(OscSender.LastError, 60);
                Lines.Insert(0, armed ? "(armed, but the last send failed)" : "(disarmed)");
            }
            else if (!DamageWatch.Installed)
            {
                stateColor = TroubleColor;
                state = "NO DAMAGE HOOK — the mod cannot see hits";
            }

            Lines.Add("");
            Lines.Add("Click both thumbsticks: a moment disarms, 1.5 s arms.");
            Lines.Add("Settings: UserData/MelonPreferences.cfg");
        }

        /// <summary>
        /// The bite block, only when biting is switched on at one end or the other. The jaw value
        /// and phase are here because tuning the chomp thresholds is otherwise blind — you can
        /// watch your own jaw cross the threshold on the desktop while you experiment.
        /// </summary>
        private static void BiteLines()
        {
            var canBite = ModConfig.BiteEnabled.Value;
            var canBeBitten = ModConfig.BiteVictimEnabled.Value;
            if (!canBite && !canBeBitten) return;

            var mine = canBite ? "you can bite" : "you cannot bite";
            var theirs = canBeBitten ? "others can bite you" : "nobody can bite you";
            Lines.Add($"Bite: {mine}, {theirs}");

            if (canBite)
            {
                Lines.Add(FaceLink.Ready
                    ? $"Jaw: {JawWatch.Jaw:0.00} ({JawWatch.PhaseName})   willing peers: {BiteNet.BitablePeerCount} of {BiteNet.PeerCount}"
                    : $"Jaw: unreadable — {Shorten(FaceLink.Status, 46)}");
                Lines.Add($"Chomps: {JawWatch.Chomps}   bites sent: {BiteSense.Sent}   no target: {BiteSense.Missed}");
                if (BiteSense.LastOutcome.Length > 0) Lines.Add($"Last chomp: {Shorten(BiteSense.LastOutcome, 48)}");
            }
            if (canBeBitten) Lines.Add($"Bitten: {BiteSense.Received} time(s), {ShockPolicy.BitesThisMinute()} this minute");
        }

        private static string Shorten(string s, int max) => s.Length <= max ? s : s.Substring(0, max - 1) + "…";

        public static string Describe() => _drawn ? "drawn" : "never drawn";
    }
}
