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
    /// A panel in the end room, next to the game's own gold and XP screen (<c>UIEndMission</c>),
    /// with THIS RUN: the damage each player dealt and took, and the bites they received when
    /// StayPutVR runs (<see cref="RunStats"/>). It was the run panel from 0.13.4; since 0.15.0
    /// there are no tokens, and the run stats are what is left.
    ///
    /// The panel stands in front of the player, a little to the right, turned to face them. Its
    /// frame is the player's eyes looking toward the end screen's TOTAL GOLD, so it lands in the
    /// same spot after every run. A wall or the end screen in the way pulls it nearer (first
    /// playtest: placed beside the end screen, it stood inside the wall). Arrow buttons on the
    /// panel move and turn it; the offset is saved in [LootOverhaul_RunPanel]. It closes with
    /// the end screen, on CLOSE, or on the next scene. Local only: every player sees their own panel.
    /// </summary>
    public static class RunPanel
    {
        private const float Width = 1.0f;
        private const float Scale = 1.1f;
        private const float MoveStep = 0.1f;
        private const float TurnStep = 10f;
        private const float AnchorWaitSeconds = 6f;

        private static GameObject _root;
        private static Transform _content;
        private static bool _shown;
        private static bool _placed;
        private static float _shownAt;
        private static bool _endScreenSeen;
        private static Vector3 _anchorPos;
        private static Quaternion _anchorRot = Quaternion.identity;
        private static float _statsCheckAt;
        private static string _statsSig;

        private static string StatsSignature()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var r in RunStats.Party()) sb.Append(r.Name).Append(r.Known).Append(Mathf.RoundToInt(r.Dealt)).Append(Mathf.RoundToInt(r.Taken)).Append(r.Bites).Append(';');
            return sb.ToString();
        }

        public static bool IsOpen => Interop.Alive(_root) && _root.activeSelf;

        /// <summary>Hooks the end screen so the panel closes with it.</summary>
        public static void Install()
        {
            var t = typeof(RunPanel);
            var n = 0;
            n += Hooks.Patch(typeof(UIEndMission), "ShowUI", null, Hooks.Of(t, nameof(ShowUI_Post)), paramCount: 2);
            n += Hooks.Patch(typeof(UIEndMission), "EV_ContinueToLobby", null, Hooks.Of(t, nameof(Continue_Post)), paramCount: 0);
            Core.Log.Msg($"RunPanel: {n}/2 end-screen hooks installed.");
        }

        private static void ShowUI_Post(bool show)
        {
            if (show) _endScreenSeen = true;
            else if (IsOpen) { Log("end screen hidden - run panel closed"); Hide(); }
        }

        private static void Continue_Post()
        {
            if (IsOpen) { Log("end screen continued - run panel closed"); Hide(); }
        }

        /// <summary>Shows the panel. Called once per run by <see cref="RunTracker"/> at the end screen.</summary>
        public static void Show()
        {
            if (!ModConfig.RunPanelEnabled.Value) return;
            try
            {
                UiKit.CaptureTemplates();
                if (!UiKit.Ready) { Core.Log.Warning("RunPanel: no UI templates yet - run stats not shown."); return; }
                _shown = true;
                EnsureRoot();
                _placed = false;
                _shownAt = Time.unscaledTime;
                _root.SetActive(false);
                _statsSig = StatsSignature();
                TryPlace(false);
            }
            catch (Exception e) { Core.Log.Warning($"RunPanel: show failed: {e.GetType().Name}: {e.Message}"); }
        }

        public static void Hide()
        {
            if (Interop.Alive(_root)) _root.SetActive(false);
            _placed = false;
            _shown = false;
        }

        /// <summary>Called from Core.OnUpdate: places the panel once the end screen exists, and closes it when the screen goes.</summary>
        public static void Tick()
        {
            if (!_shown || !Interop.Alive(_root)) return;
            try
            {
                if (!_placed)
                {
                    TryPlace(Time.unscaledTime - _shownAt > AnchorWaitSeconds);
                    return;
                }
                // Friends' stats can land a moment after the panel opens: redraw when they change.
                if (Time.unscaledTime >= _statsCheckAt)
                {
                    _statsCheckAt = Time.unscaledTime + 1f;
                    var sig = StatsSignature();
                    if (sig != _statsSig) { _statsSig = sig; Rebuild(); }
                }
                // Belt and braces for the hooks: the end screen's gold text gone from view means the screen is closed.
                var gold = GoldText();
                if (_endScreenSeen && gold != null && !gold.gameObject.activeInHierarchy && IsOpen)
                {
                    Log("end screen no longer shown - run panel closed");
                    Hide();
                }
            }
            catch { }
        }

        /// <summary>A new scene: the panel (not kept across loads) is gone; forget the run.</summary>
        public static void OnSceneChanged()
        {
            _shown = false;
            _placed = false;
            _endScreenSeen = false;
        }

        private static void EnsureRoot()
        {
            if (Interop.Alive(_root)) return;
            // Not DontDestroyOnLoad: the panel belongs to this end room and goes with its scene.
            _root = new GameObject("LootOverhaul_RunPanel");
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
        /// Places the panel relative to the player (0.13.4 playtest: anchored beside the end screen
        /// it stood inside the wall). The frame is the player's eyes, looking toward the end screen's
        /// total gold, so it is the same after every run whichever way the head happens to turn.
        /// Without an end screen it waits a few seconds; then (<paramref name="fallback"/>) it uses
        /// the way the player looks.
        /// </summary>
        private static void TryPlace(bool fallback)
        {
            var local = AvatarPlayer.LocalAvatar;
            if (!Interop.Alive(local) || !Interop.Alive(local.Head)) return;
            var head = local.Head;
            var gold = GoldText();
            Vector3 fwd;
            if (gold != null && gold.gameObject.activeInHierarchy)
            {
                fwd = gold.transform.position - head.position;
                Log($"end screen's gold text at {Fmt(gold.transform.position)}, eyes at {Fmt(head.position)} (`{Interop.ScenePath(gold.transform)}`)");
            }
            else if (fallback)
            {
                fwd = head.forward;
                Log("no end screen found - run panel placed where you look");
            }
            else return;

            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) { fwd = head.forward; fwd.y = 0f; }
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            _anchorPos = head.position;
            _anchorRot = Quaternion.LookRotation(fwd.normalized, Vector3.up);

            _placed = true;
            Apply();
            _root.SetActive(true);
            // Build the content only now that the panel is active: text laid out while it was
            // inactive stayed empty until the first arrow press rebuilt it (0.13.5 playtest).
            Rebuild();
        }

        /// <summary>
        /// Puts the panel at the saved offset from the eyes, turned to face them, then pulls it
        /// toward the player if a wall or the end screen is in the way.
        /// </summary>
        private static void Apply()
        {
            if (!Interop.Alive(_root)) return;
            var off = new Vector3(ModConfig.RunPanelRight.Value, ModConfig.RunPanelUp.Value, ModConfig.RunPanelAhead.Value);
            var pos = _anchorPos + _anchorRot * off;
            pos = OutOfWalls(_anchorPos, pos, _anchorRot);
            var face = pos - _anchorPos; face.y = 0f;
            if (face.sqrMagnitude < 1e-4f) face = _anchorRot * Vector3.forward;
            _root.transform.position = pos;
            _root.transform.rotation = Quaternion.LookRotation(face.normalized, Vector3.up) * Quaternion.Euler(0f, ModConfig.RunPanelTurn.Value, 0f);
            _root.transform.localScale = Vector3.one * Scale;
        }

        /// <summary>
        /// Casts from the eyes to the panel's centre and both side edges. Anything solid on the way
        /// (not a trigger, not the player, not this panel) moves the panel in front of it, 15 cm clear,
        /// but never nearer than 0.5 m.
        /// </summary>
        private static Vector3 OutOfWalls(Vector3 eyes, Vector3 pos, Quaternion frame)
        {
            try
            {
                var local = AvatarPlayer.LocalAvatar;
                var self = Interop.Alive(local) ? local.transform.root : null;
                var half = Width * 0.5f * Scale;
                var side = frame * Vector3.right;
                var ratio = 1f;
                foreach (var target in new[] { pos, pos - side * half, pos + side * half })
                {
                    var d = target - eyes;
                    var dist = d.magnitude;
                    if (dist < 0.01f) continue;
                    var hits = Physics.RaycastAll(eyes, d / dist, dist, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                    if (hits == null) continue;
                    foreach (var h in hits)
                    {
                        var c = h.collider;
                        if (!Interop.Alive(c)) continue;
                        var t = c.transform;
                        if (Interop.Alive(_root) && t.IsChildOf(_root.transform)) continue;
                        if (self != null && t.IsChildOf(self)) continue;
                        var r = (h.distance - 0.15f) / dist;
                        if (r < ratio)
                        {
                            ratio = r;
                            Log($"something in the way at {h.distance:0.00} m (`{Interop.ScenePath(t)}`)");
                        }
                    }
                }
                if (ratio >= 1f) return pos;
                var full = (pos - eyes).magnitude;
                ratio = Mathf.Max(ratio, full > 0.01f ? 0.5f / full : 1f);
                ratio = Mathf.Min(ratio, 1f);
                Log($"panel pulled in to {ratio * 100f:0}% of its distance to stay out of the wall");
                return eyes + (pos - eyes) * ratio;
            }
            catch (Exception e)
            {
                Core.Log.Warning($"RunPanel: wall check failed: {e.GetType().Name}: {e.Message}");
                return pos;
            }
        }

        private static void Nudge(float right, float up, float away, float turn)
        {
            ModConfig.RunPanelRight.Value = Round(ModConfig.RunPanelRight.Value + right);
            ModConfig.RunPanelUp.Value = Round(ModConfig.RunPanelUp.Value + up);
            ModConfig.RunPanelAhead.Value = Round(ModConfig.RunPanelAhead.Value + away);
            var yaw = ModConfig.RunPanelTurn.Value + turn;
            while (yaw > 180f) yaw -= 360f;
            while (yaw < -180f) yaw += 360f;
            ModConfig.RunPanelTurn.Value = Mathf.Round(yaw);
            Save();
        }

        private static void ResetPosition()
        {
            ModConfig.RunPanelRight.Value = ModConfig.RunPanelRight.DefaultValue;
            ModConfig.RunPanelUp.Value = ModConfig.RunPanelUp.DefaultValue;
            ModConfig.RunPanelAhead.Value = ModConfig.RunPanelAhead.DefaultValue;
            ModConfig.RunPanelTurn.Value = ModConfig.RunPanelTurn.DefaultValue;
            Save();
        }

        private static void Save()
        {
            try { MelonPreferences.Save(); } catch (Exception e) { Core.Log.Warning($"RunPanel: saving the position failed: {e.GetType().Name}"); }
            Apply();
            Rebuild();
            Log($"position now {ModConfig.RunPanelRight.Value:0.0} m right, {ModConfig.RunPanelUp.Value:0.0} m up, {ModConfig.RunPanelAhead.Value:0.0} m ahead, turn {ModConfig.RunPanelTurn.Value:0}°");
        }

        private static float Round(float v) => Mathf.Round(v * 100f) / 100f;

        // ------------------------------------------------------------------ drawing
        private static void Rebuild()
        {
            if (!Interop.Alive(_content) || !_shown) return;
            UiKit.DestroyChildren(_content);
            const float rowPitch = 0.052f;
            var party = RunStats.Party();
            var height = 0.15f + (party.Count + 1) * rowPitch + 0.05f + 0.40f;
            var top = height * 0.5f;
            const float L = -Width * 0.5f + S.Pad, R = Width * 0.5f - S.Pad;
            const float w = R - L;

            UiKit.Backdrop(_content, new Vector3(0f, 0f, 0.01f), Width, height, S.Panel);
            UiKit.Bar(_content, new Vector3(0f, top - 0.004f, 0.004f), Width, 0.008f, S.Accent);

            // This run's party stats (0.14.0): each player's own count, shared.
            var y = top - 0.07f;
            UiKit.Label(_content, L, y, w, 0.07f, S.Title, "<b>THIS RUN</b>", S.Text);
            y -= 0.075f;
            Rule(y + 0.02f, w);
            y -= rowPitch * 0.5f;
            {
                var bites = false;
                foreach (var r in party) if (r.Known && r.Bites >= 0) bites = true;
                var cols = bites ? new[] { 0.40f, 0.22f, 0.22f, 0.16f } : new[] { 0.46f, 0.27f, 0.27f, 0f };
                void Row(string a, string b, string c, string d, Color col, Color nameCol)
                {
                    var x = L;
                    UiKit.Label(_content, x, y, w * cols[0], 0.05f, S.Body, a, nameCol); x += w * cols[0];
                    UiKit.Label(_content, x, y, w * cols[1], 0.05f, S.Body, b, col, TextAlignmentOptions.Right); x += w * cols[1];
                    UiKit.Label(_content, x, y, w * cols[2], 0.05f, S.Body, c, col, TextAlignmentOptions.Right); x += w * cols[2];
                    if (bites) UiKit.Label(_content, x, y, w * cols[3], 0.05f, S.Body, d, col, TextAlignmentOptions.Right);
                    y -= rowPitch;
                }
                Row("", "Dealt", "Taken", "Bites", S.Text2, S.Text2);
                foreach (var r in party)
                {
                    var name = r.Name ?? "?";
                    if (name.Length > 16) name = name.Substring(0, 15) + "…";
                    if (!r.Known) { Row(name, "-", "-", "-", S.Text2, S.Text2); continue; }
                    Row(r.IsLocal ? $"<b>{name}</b>" : name,
                        $"<b>{UiKit.Number((long)Mathf.Round(r.Dealt))}</b>",
                        $"<b>{UiKit.Number((long)Mathf.Round(r.Taken))}</b>",
                        r.Bites >= 0 ? $"<b>{r.Bites}</b>" : "-",
                        S.Text, S.Text);
                }
            }

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

        private static void Log(string msg) => Core.Log.Msg("RunPanel: " + msg);
    }
}
