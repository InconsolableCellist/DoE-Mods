using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppTMPro;
using LootOverhaul.Recon;
using MelonLoader;
using UnityEngine;
using Interop = LootOverhaul.Recon.Interop;
using S = LootOverhaul.Loot.UiStyle;

namespace LootOverhaul.Loot
{
    /// <summary>
    /// The run payout as a panel in the end room (0.13.4), next to the game's own gold and XP
    /// screen (<c>UIEndMission</c>). It replaces the payout toast: the game's notifications
    /// vanish after a moment whatever the mod asks, and reposting them only made them flicker.
    ///
    /// The panel stands at an offset from the end screen's TOTAL GOLD text, in that text's own
    /// frame (right, up, away from the reader), so it lands in the same spot in every end room.
    /// Arrow buttons on the panel move and turn it; the offset is saved in
    /// [LootOverhaul_TokenPanel]. It closes with the end screen, on CLOSE, or on the next scene.
    /// Local only: every player sees their own payout.
    /// </summary>
    public static class TokenPanel
    {
        /// <summary>What the panel shows: one run's payout and how it was worked out.</summary>
        public sealed class Payout
        {
            public long Tokens;
            public int Gold;
            public float Rate;
            public double Mult;
            public long Balance = -1;
            /// <summary>Bonus lines: label and its share of the multiplier (0.5 = +50%). Zero lines are left out.</summary>
            public readonly List<KeyValuePair<string, double>> Bonuses = new List<KeyValuePair<string, double>>();
        }

        private const float Width = 1.0f;
        private const float Scale = 1.1f;
        private const float MoveStep = 0.1f;
        private const float TurnStep = 10f;
        private const float AnchorWaitSeconds = 6f;

        private static GameObject _root;
        private static Transform _content;
        private static Payout _payout;
        private static bool _placed;
        private static float _shownAt;
        private static bool _endScreenSeen;
        private static Vector3 _anchorPos;
        private static Quaternion _anchorRot = Quaternion.identity;

        public static bool IsOpen => Interop.Alive(_root) && _root.activeSelf;

        /// <summary>Hooks the end screen so the panel closes with it.</summary>
        public static void Install()
        {
            var t = typeof(TokenPanel);
            var n = 0;
            n += Hooks.Patch(typeof(UIEndMission), "ShowUI", null, Hooks.Of(t, nameof(ShowUI_Post)), paramCount: 2);
            n += Hooks.Patch(typeof(UIEndMission), "EV_ContinueToLobby", null, Hooks.Of(t, nameof(Continue_Post)), paramCount: 0);
            Core.Log.Msg($"TokenPanel: {n}/2 end-screen hooks installed.");
        }

        private static void ShowUI_Post(bool show)
        {
            if (show) _endScreenSeen = true;
            else if (IsOpen) { Log("end screen hidden - token panel closed"); Hide(); }
        }

        private static void Continue_Post()
        {
            if (IsOpen) { Log("end screen continued - token panel closed"); Hide(); }
        }

        /// <summary>Shows the payout. Called once per run by <see cref="Tokens"/>.</summary>
        public static void Show(Payout p)
        {
            if (p == null || !ModConfig.TokenPanelEnabled.Value) return;
            try
            {
                UiKit.CaptureTemplates();
                if (!UiKit.Ready) { Core.Log.Warning("TokenPanel: no UI templates yet - payout not shown (it is in the log)."); return; }
                _payout = p;
                EnsureRoot();
                _placed = false;
                _shownAt = Time.unscaledTime;
                _root.SetActive(false);
                Rebuild();
                TryPlace(false);
            }
            catch (Exception e) { Core.Log.Warning($"TokenPanel: show failed: {e.GetType().Name}: {e.Message}"); }
        }

        public static void Hide()
        {
            if (Interop.Alive(_root)) _root.SetActive(false);
            _placed = false;
            _payout = null;
        }

        /// <summary>Called from Core.OnUpdate: places the panel once the end screen exists, and closes it when the screen goes.</summary>
        public static void Tick()
        {
            if (_payout == null || !Interop.Alive(_root)) return;
            try
            {
                if (!_placed)
                {
                    TryPlace(Time.unscaledTime - _shownAt > AnchorWaitSeconds);
                    return;
                }
                // Belt and braces for the hooks: the end screen's gold text gone from view means the screen is closed.
                var gold = GoldText();
                if (_endScreenSeen && gold != null && !gold.gameObject.activeInHierarchy && IsOpen)
                {
                    Log("end screen no longer shown - token panel closed");
                    Hide();
                }
            }
            catch { }
        }

        /// <summary>A new scene: the panel (not kept across loads) is gone; forget the run.</summary>
        public static void OnSceneChanged()
        {
            _payout = null;
            _placed = false;
            _endScreenSeen = false;
        }

        private static void EnsureRoot()
        {
            if (Interop.Alive(_root)) return;
            // Not DontDestroyOnLoad: the panel belongs to this end room and goes with its scene.
            _root = new GameObject("LootOverhaul_TokenPanel");
            var content = new GameObject("Content");
            content.transform.SetParent(_root.transform, false);
            _content = content.transform;
        }

        private static TextMeshPro GoldText()
        {
            try
            {
                var ui = UIEndMission.instance;
                if (!Interop.Alive(ui)) return null;
                var t = ui.totalGold;
                return Interop.Alive(t) ? t : null;
            }
            catch { return null; }
        }

        /// <summary>
        /// Finds the end screen's frame and places the panel in it. Without an end screen it waits
        /// a few seconds; then (<paramref name="fallback"/>) it stands to the right of the player.
        /// </summary>
        private static void TryPlace(bool fallback)
        {
            var gold = GoldText();
            if (gold != null && gold.gameObject.activeInHierarchy)
            {
                var tr = gold.transform;
                var fwd = tr.forward; fwd.y = 0f;
                if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
                _anchorPos = tr.position;
                _anchorRot = Quaternion.LookRotation(fwd.normalized, Vector3.up);
                Log($"anchored to the end screen's gold text at {Fmt(tr.position)}, facing {Fmt(fwd.normalized)} (`{Interop.ScenePath(tr)}`)");
            }
            else if (fallback)
            {
                var local = AvatarPlayer.LocalAvatar;
                if (!Interop.Alive(local)) return;
                var head = local.Head;
                var fwd = head.forward; fwd.y = 0f;
                if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
                fwd.Normalize();
                // As if the end screen were 2 m ahead of the player at eye height.
                _anchorPos = head.position + fwd * 2f;
                _anchorRot = Quaternion.LookRotation(fwd, Vector3.up);
                Log("no end screen found - token panel placed relative to the player");
            }
            else return;

            _placed = true;
            Apply();
            _root.SetActive(true);
        }

        /// <summary>Puts the panel at the saved offset from the anchor.</summary>
        private static void Apply()
        {
            if (!Interop.Alive(_root)) return;
            var off = new Vector3(ModConfig.TokenPanelRight.Value, ModConfig.TokenPanelUp.Value, ModConfig.TokenPanelAway.Value);
            _root.transform.position = _anchorPos + _anchorRot * off;
            _root.transform.rotation = _anchorRot * Quaternion.Euler(0f, ModConfig.TokenPanelTurn.Value, 0f);
            _root.transform.localScale = Vector3.one * Scale;
        }

        private static void Nudge(float right, float up, float away, float turn)
        {
            ModConfig.TokenPanelRight.Value = Round(ModConfig.TokenPanelRight.Value + right);
            ModConfig.TokenPanelUp.Value = Round(ModConfig.TokenPanelUp.Value + up);
            ModConfig.TokenPanelAway.Value = Round(ModConfig.TokenPanelAway.Value + away);
            var yaw = ModConfig.TokenPanelTurn.Value + turn;
            while (yaw > 180f) yaw -= 360f;
            while (yaw < -180f) yaw += 360f;
            ModConfig.TokenPanelTurn.Value = Mathf.Round(yaw);
            Save();
        }

        private static void ResetPosition()
        {
            ModConfig.TokenPanelRight.Value = ModConfig.TokenPanelRight.DefaultValue;
            ModConfig.TokenPanelUp.Value = ModConfig.TokenPanelUp.DefaultValue;
            ModConfig.TokenPanelAway.Value = ModConfig.TokenPanelAway.DefaultValue;
            ModConfig.TokenPanelTurn.Value = ModConfig.TokenPanelTurn.DefaultValue;
            Save();
        }

        private static void Save()
        {
            try { MelonPreferences.Save(); } catch (Exception e) { Core.Log.Warning($"TokenPanel: saving the position failed: {e.GetType().Name}"); }
            Apply();
            Rebuild();
            Log($"position now right {ModConfig.TokenPanelRight.Value:0.0} m, up {ModConfig.TokenPanelUp.Value:0.0} m, away {ModConfig.TokenPanelAway.Value:0.0} m, turn {ModConfig.TokenPanelTurn.Value:0}°");
        }

        private static float Round(float v) => Mathf.Round(v * 100f) / 100f;

        // ------------------------------------------------------------------ drawing
        private static void Rebuild()
        {
            if (!Interop.Alive(_content) || _payout == null) return;
            UiKit.DestroyChildren(_content);
            var p = _payout;
            const float pitch = 0.062f;
            var lines = 3 + p.Bonuses.Count + (p.Balance >= 0 ? 1 : 0);
            var height = 0.32f + lines * pitch + 0.40f;
            var top = height * 0.5f;
            const float L = -Width * 0.5f + S.Pad, R = Width * 0.5f - S.Pad;
            const float w = R - L;

            UiKit.Backdrop(_content, new Vector3(0f, 0f, 0.01f), Width, height, S.Panel);
            UiKit.Bar(_content, new Vector3(0f, top - 0.004f, 0.004f), Width, 0.008f, S.Accent);

            // Header and the total.
            UiKit.Label(_content, L, top - 0.07f, w, 0.07f, S.Title, "<b>TOKENS EARNED</b>", S.Text);
            UiKit.CoinNumber(_content, R, top - 0.19f, w, p.Tokens, S.Gold, S.Sign, 0.07f);
            Rule(top - 0.265f, w);

            // The sum, one line per part.
            var y = top - 0.32f;
            void Line(string label, string value, Color valueColor)
            {
                UiKit.Label(_content, L, y, w * 0.62f, 0.055f, S.Body, label, S.Text2);
                UiKit.Label(_content, L + w * 0.62f, y, w * 0.38f, 0.055f, S.Body, $"<b>{value}</b>", valueColor, TextAlignmentOptions.Right);
                y -= pitch;
            }
            Line("Gold earned", UiKit.Number(p.Gold), S.Text);
            Line("Tokens per gold", $"x{p.Rate:0.##}", S.Text);
            foreach (var b in p.Bonuses) Line(b.Key, $"+{b.Value * 100.0:0.#}%", S.Green);
            Line("Multiplier", $"x{p.Mult:0.##}", S.Text);
            if (p.Balance >= 0) Line("Your tokens now", UiKit.Number(p.Balance), S.Gold);

            // Position controls, always at the bottom.
            var bottom = -top;
            var y3 = bottom + 0.06f;
            var y2 = y3 + 0.1f;
            var y1 = y2 + 0.1f;
            Rule(y1 + 0.11f, w);
            UiKit.Label(_content, L, y1 + 0.075f, w, 0.05f, S.Body, "MOVE THIS PANEL  (saved)", S.Text2, TextAlignmentOptions.Center);
            var sz = new Vector2(0.11f, 0.08f);
            var g = S.Gap;
            // Arrow cross on the left: up, left, right, down.
            var cx = L + sz.x * 1.5f + g;
            ArrowBtn(new Vector3(cx, y1, 0f), sz, 90f, () => Nudge(0f, MoveStep, 0f, 0f));
            ArrowBtn(new Vector3(cx - sz.x - g, y2, 0f), sz, 180f, () => Nudge(-MoveStep, 0f, 0f, 0f));
            ArrowBtn(new Vector3(cx + sz.x + g, y2, 0f), sz, 0f, () => Nudge(MoveStep, 0f, 0f, 0f));
            ArrowBtn(new Vector3(cx, y3, 0f), sz, -90f, () => Nudge(0f, -MoveStep, 0f, 0f));
            // Near, far and turn on the right, reset and close under them.
            var bw = new Vector2((R - (cx + sz.x * 1.5f + 3f * g)) * 0.5f - g * 0.5f, sz.y);
            var bx1 = R - bw.x * 1.5f - g;
            var bx2 = R - bw.x * 0.5f;
            UiKit.Btn(_content, new Vector3(bx1, y1, 0f), bw, "NEARER", () => Nudge(0f, 0f, -MoveStep, 0f), S.Variant.Secondary);
            UiKit.Btn(_content, new Vector3(bx2, y1, 0f), bw, "FARTHER", () => Nudge(0f, 0f, MoveStep, 0f), S.Variant.Secondary);
            UiKit.Btn(_content, new Vector3(bx1, y2, 0f), bw, "TURN LEFT", () => Nudge(0f, 0f, 0f, -TurnStep), S.Variant.Secondary);
            UiKit.Btn(_content, new Vector3(bx2, y2, 0f), bw, "TURN RIGHT", () => Nudge(0f, 0f, 0f, TurnStep), S.Variant.Secondary);
            UiKit.Btn(_content, new Vector3(bx1, y3, 0f), bw, "RESET", ResetPosition, S.Variant.Secondary);
            UiKit.Btn(_content, new Vector3(bx2, y3, 0f), bw, "CLOSE", Hide, S.Variant.Primary);
        }

        private static void Rule(float y, float w) => UiKit.Bar(_content, new Vector3(0f, y, 0.004f), w, 0.004f, S.Rule);

        /// <summary>A button with an arrow drawn from bars (no font glyph): a shaft and a chevron, pointing at <paramref name="angle"/> degrees (0 = right, 90 = up).</summary>
        private static void ArrowBtn(Vector3 pos, Vector2 size, float angle, Action onPressed)
        {
            UiKit.Btn(_content, pos, size, null, onPressed, S.Variant.Secondary);
            var col = S.ButtonLook(S.Variant.Secondary, S.State.Normal).Label;
            var holder = new GameObject("Arrow");
            holder.transform.SetParent(_content, false);
            holder.transform.localPosition = pos + new Vector3(0f, 0f, -0.006f);
            holder.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
            const float th = 0.009f;
            UiKit.Bar(holder.transform, new Vector3(-0.004f, 0f, 0f), 0.04f, th, col);
            UiKit.Bar(holder.transform, new Vector3(0.006f, 0.0085f, 0f), 0.026f, th, col).transform.localRotation = Quaternion.Euler(0f, 0f, -45f);
            UiKit.Bar(holder.transform, new Vector3(0.006f, -0.0085f, 0f), 0.026f, th, col).transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
        }

        private static string Fmt(Vector3 v) => $"({v.x:0.00}, {v.y:0.00}, {v.z:0.00})";

        private static void Log(string msg) => Core.Log.Msg("TokenPanel: " + msg);
    }
}
