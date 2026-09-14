using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppTMPro;
using PartyHealth.Peers;
using UnityEngine;
using UnityEngine.Rendering;

namespace PartyHealth.Hud
{
    /// <summary>
    /// One small bar over each friend's head: a dark backing strip and a coloured fill that
    /// shrinks from the right, both turned to face the eyes every frame in LateUpdate, after
    /// the game has placed the head. Green at full, amber at half, red near empty, a brief
    /// whitening on every hit, and DOWN pulsing over an empty bar while they wait for a rescue.
    ///
    /// Quiet by design. At full health there is no bar at all unless <c>ShowWhenFull</c> is
    /// set; it fades in on the first hit and out a couple of seconds after they are back to
    /// full. Beyond <c>SizeFromMeters</c> the whole thing grows with distance, by half of what
    /// would keep it the same size on screen, so it stays legible across a hall without
    /// looming, and it fades away entirely past <c>MaxDistanceMeters</c>.
    ///
    /// The quads are generated meshes with a UI shader whose depth test is switched off, the
    /// same trick VisualCues uses, so a friend behind a pillar still shows. The rig is one
    /// DontDestroyOnLoad object; a bar whose peer is gone is destroyed with it.
    /// </summary>
    public static class HealthBars
    {
        private sealed class Bar
        {
            public GameObject Root;
            public Transform Back, Fill;
            public Material BackMat, FillMat;
            public TextMeshPro Name, Percent, Down;
            public float Alpha;          // current fade, 0..1
            public float Shown = -1f;    // displayed fraction, eased toward the real one
        }

        private static GameObject _rig;
        private static readonly Dictionary<int, Bar> Bars = new Dictionary<int, Bar>();
        private static readonly List<int> Stale = new List<int>();
        private static Mesh _quad;
        private static Material _template;
        private static bool _onTop, _materialLogged;
        private static TMP_FontAsset _font;
        private static Material _fontMaterial;
        private static float _fontRetryAt;
        private static Transform _eyes;
        private static float _eyesRetryAt;
        private static string _eyesSource = "nothing";
        private static bool _hidden;
        private static int _built, _shownEver;

        private static readonly Color BackColor = new Color(0.05f, 0.05f, 0.06f);
        private static readonly Color FullColor = new Color(0.36f, 0.86f, 0.42f);
        private static readonly Color HalfColor = new Color(0.98f, 0.72f, 0.20f);
        private static readonly Color LowColor = new Color(0.95f, 0.22f, 0.18f);
        private static readonly Color DownColor = new Color(1.00f, 0.25f, 0.20f);

        public static bool Hidden { get => _hidden; set => _hidden = value; }
        public static string Describe() => $"{_built} bar(s) built, {_shownEver} ever shown, eyes from {_eyesSource}, {(_onTop ? "drawn over everything" : "depth-tested")}";

        // ---- per frame -----------------------------------------------------------------------

        public static void LateTick()
        {
            try
            {
                var eyes = Eyes();
                var dt = Time.unscaledDeltaTime;
                var now = Time.unscaledTime;

                Stale.Clear();
                foreach (var kv in Bars) Stale.Add(kv.Key);

                foreach (var p in PeerHealth.All)
                {
                    Stale.Remove(p.Actor);
                    var want = Wanted(p, eyes, now, out var head, out var distance);
                    if (!want && !Bars.ContainsKey(p.Actor)) continue;

                    if (!Bars.TryGetValue(p.Actor, out var bar))
                    {
                        bar = Build();
                        if (bar == null) continue;
                        Bars[p.Actor] = bar;
                    }
                    Step(bar, p, want, head, distance, eyes, dt, now);
                    if (!want && bar.Alpha <= 0.001f) { Destroy(bar); Bars.Remove(p.Actor); }
                }

                foreach (var actor in Stale) { if (Bars.TryGetValue(actor, out var bar)) Destroy(bar); Bars.Remove(actor); }
            }
            catch (Exception e)
            {
                if (ModConfig.VerboseLogging.Value) Core.Log.Warning($"Bars threw: {e.GetType().Name}: {e.Message}");
            }
        }

        /// <summary>Whether this peer should have a visible bar right now, and where its head is.</summary>
        private static bool Wanted(PeerHealth.Peer p, Transform eyes, float now, out Vector3 head, out float distance)
        {
            head = Vector3.zero; distance = 0f;
            if (!ModConfig.Enabled.Value || _hidden) return false;
            if (!TryHead(p, out head)) return false;
            if (Interop.Alive(eyes)) distance = Vector3.Distance(eyes.position, head);
            if (distance > Mathf.Max(1f, ModConfig.MaxDistanceMeters.Value)) return false;
            if (p.Dead) return now - p.LastChangeAt < 3f;
            if (p.Downed) return true;
            if (ModConfig.ShowWhenFull.Value) return true;
            if (!p.Full) return true;
            return now - p.LastChangeAt < ModConfig.HoldAfterFullSeconds.Value;
        }

        private static bool TryHead(PeerHealth.Peer p, out Vector3 head)
        {
            head = Vector3.zero;
            if (p.Demo) { head = p.DemoPosition; return true; }
            try
            {
                if (!Interop.Alive(p.Anchor))
                {
                    var a = p.Avatar;
                    if (!Interop.Alive(a)) return false;
                    // IKTargetHead is the networked head target, where their head really is.
                    // The display body's own head bone sits at a fixed height on remote
                    // clients whatever the player's height (CustomAvatars found 1.48 m).
                    if (Interop.Alive(a.IKTargetHead)) { p.Anchor = a.IKTargetHead; p.AnchorSource = "IKTargetHead"; }
                    else if (Interop.Alive(a.Head)) { p.Anchor = a.Head; p.AnchorSource = "Head"; }
                    else if (Interop.Alive(a.Eye)) { p.Anchor = a.Eye; p.AnchorSource = "Eye"; }
                    else if (Interop.Alive(a.transform)) { p.Anchor = a.transform; p.AnchorSource = "root (+1.6 m)"; }
                    else return false;
                }
                head = p.Anchor.position;
                if (p.AnchorSource.StartsWith("root")) head.y += 1.6f;
                return true;
            }
            catch { p.Anchor = null; return false; }
        }

        private static void Step(Bar bar, PeerHealth.Peer p, bool want, Vector3 head, float distance, Transform eyes, float dt, float now)
        {
            var fade = Mathf.Max(0.05f, ModConfig.FadeSeconds.Value);
            var target = want ? 1f : 0f;
            if (bar.Alpha == 0f && want) _shownEver++;
            bar.Alpha = Mathf.MoveTowards(bar.Alpha, target, dt / fade);
            if (bar.Alpha <= 0.001f) { if (bar.Root.activeSelf) bar.Root.SetActive(false); return; }
            if (!bar.Root.activeSelf) bar.Root.SetActive(true);

            // Distance fade over the last fifth, and the on-screen size rule.
            var max = Mathf.Max(1f, ModConfig.MaxDistanceMeters.Value);
            var distanceFade = Mathf.Clamp01((max - distance) / (0.2f * max));
            // Growth with distance is partial: distance/from would keep the bar the same size on
            // screen at any range, and that read as too big in the first test. Raising the
            // ratio to DistanceGrowth (0.5 by default) undoes half the shrinking, so a friend
            // across a hall gets a bar that is smaller than the one next to you but still legible.
            var scale = 1f;
            if (ModConfig.SizeWithDistance.Value)
            {
                var from = Mathf.Max(0.5f, ModConfig.SizeFromMeters.Value);
                if (distance > from)
                    scale = Mathf.Min(Mathf.Max(1f, ModConfig.MaxGrowth.Value),
                                      Mathf.Pow(distance / from, Mathf.Clamp01(ModConfig.DistanceGrowth.Value)));
            }

            // Pose: above the head, facing the eyes, up kept vertical so the bar never rolls.
            var w = Mathf.Max(0.02f, ModConfig.WidthMeters.Value);
            var h = Mathf.Max(0.004f, ModConfig.HeightMeters.Value);
            var t = bar.Root.transform;
            t.position = head + Vector3.up * ModConfig.AboveHeadMeters.Value * scale;
            if (Interop.Alive(eyes))
            {
                var toBar = t.position - eyes.position;
                toBar.y = 0f;
                if (toBar.sqrMagnitude > 0.0001f) t.rotation = Quaternion.LookRotation(toBar.normalized, Vector3.up);
            }
            t.localScale = Vector3.one * scale;

            // Fraction eased toward the truth; a hit shows as a quick drop, a heal as a rise.
            var fraction = p.Downed || p.Dead ? 0f : p.Fraction;
            if (bar.Shown < 0f) bar.Shown = fraction;
            bar.Shown = Mathf.Lerp(bar.Shown, fraction, 1f - Mathf.Exp(-9f * dt));
            var shown = Mathf.Clamp01(bar.Shown);

            var opacity = Mathf.Clamp01(ModConfig.Opacity.Value) * bar.Alpha * distanceFade;
            var pulse = 1f;
            if (p.Downed && !p.Dead) pulse = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin((now - p.DownedAt) * 4f));

            var back = BackColor; back.a = opacity * 0.6f * (p.Downed ? pulse : 1f);
            var fill = FillColor(shown);
            var sinceHit = now - p.LastHitAt;
            if (sinceHit < 0.35f) fill = Color.Lerp(Color.white, fill, sinceHit / 0.35f);
            fill.a = opacity;

            // Backing strip is the unit quad scaled; the fill is anchored on the left edge.
            if (Interop.Alive(bar.Back)) bar.Back.localScale = new Vector3(w, h, 1f);
            var inner = h * 0.72f;
            var fw = Mathf.Max(0f, (w - h * 0.28f) * shown);
            bar.Fill.localScale = new Vector3(fw, inner, 1f);
            bar.Fill.localPosition = new Vector3(-(w - h * 0.28f) * 0.5f + fw * 0.5f, 0f, -0.001f);
            bar.Fill.gameObject.SetActive(fw > 0.0005f);
            if (bar.BackMat != null) bar.BackMat.color = back;
            if (bar.FillMat != null) bar.FillMat.color = fill;

            // Labels.
            Label(bar.Name, ModConfig.ShowName.Value ? p.Name : null, new Vector3(0f, h * 1.9f, -0.001f), new Color(1f, 1f, 1f, opacity * 0.9f));
            var pct = ModConfig.ShowPercent.Value && !p.Downed && !p.Dead ? $"{Mathf.RoundToInt(p.Fraction * 100f)}%" : null;
            Label(bar.Percent, pct, new Vector3(w * 0.5f + h * 0.4f, 0f, -0.001f), new Color(1f, 1f, 1f, opacity * 0.9f), TextAlignmentOptions.MidlineLeft);
            string down = null;
            if (ModConfig.ShowDownedLabel.Value) { if (p.Dead) down = "DEAD"; else if (p.Downed) down = "DOWN"; }
            var dc = DownColor; dc.a = opacity * (p.Dead ? 0.8f : pulse);
            Label(bar.Down, down, new Vector3(0f, 0f, -0.002f), dc);
        }

        private static Color FillColor(float f)
        {
            if (f > 0.5f) return Color.Lerp(HalfColor, FullColor, (f - 0.5f) * 2f);
            return Color.Lerp(LowColor, HalfColor, f * 2f);
        }

        private static void Label(TextMeshPro tmp, string text, Vector3 localPos, Color color, TextAlignmentOptions align = TextAlignmentOptions.Center)
        {
            if (!Interop.Alive(tmp)) return;
            try
            {
                if (string.IsNullOrEmpty(text)) { if (tmp.gameObject.activeSelf) tmp.gameObject.SetActive(false); return; }
                if (!tmp.gameObject.activeSelf) tmp.gameObject.SetActive(true);
                if (tmp.text != text) tmp.text = text;
                tmp.alignment = align;
                tmp.transform.localPosition = localPos;
                tmp.color = color;
            }
            catch { }
        }

        // ---- eyes ------------------------------------------------------------------------------

        private static Transform Eyes()
        {
            if (Interop.Alive(_eyes)) return _eyes;
            if (Time.unscaledTime < _eyesRetryAt) return null;
            _eyesRetryAt = Time.unscaledTime + 1f;
            _eyes = null;
            try
            {
                var cam = Camera.main;
                if (Interop.Alive(cam) && cam.enabled) { _eyes = cam.transform; _eyesSource = "Camera.main"; }
            }
            catch { }
            if (_eyes == null)
            {
                try
                {
                    var local = AvatarPlayer.LocalAvatar;
                    if (Interop.Alive(local))
                    {
                        if (Interop.Alive(local.Eye)) { _eyes = local.Eye; _eyesSource = "AvatarPlayer.Eye"; }
                        else if (Interop.Alive(local.Head)) { _eyes = local.Head; _eyesSource = "AvatarPlayer.Head"; }
                    }
                }
                catch { }
            }
            return _eyes;
        }

        /// <summary>Where the eyes are and which way they look, for the demo bar. False before the camera exists.</summary>
        public static bool TryEyes(out Vector3 position, out Vector3 forward)
        {
            position = Vector3.zero; forward = Vector3.forward;
            var e = Eyes();
            if (!Interop.Alive(e)) return false;
            try { position = e.position; forward = e.forward; return true; } catch { return false; }
        }

        // ---- construction -----------------------------------------------------------------------

        private static void EnsureRig()
        {
            if (Interop.Alive(_rig)) return;
            _rig = new GameObject("PartyHealth_Bars");
            UnityEngine.Object.DontDestroyOnLoad(_rig);
            Bars.Clear();
        }

        private static Bar Build()
        {
            try
            {
                EnsureRig();
                EnsureAssets();
                EnsureFont();
                if (_quad == null || _template == null) return null;

                var root = new GameObject("Bar");
                root.transform.SetParent(_rig.transform, false);
                var back = Quad(root.transform, "Back", out var backMat);
                var fill = Quad(root.transform, "Fill", out var fillMat);
                if (backMat != null) backMat.renderQueue = _template.renderQueue;
                if (fillMat != null) fillMat.renderQueue = _template.renderQueue + 1;

                var bar = new Bar
                {
                    Root = root,
                    Back = back,
                    Fill = fill,
                    BackMat = backMat,
                    FillMat = fillMat,
                    Name = MakeLabel(root.transform, "Name", 0.32f),
                    Percent = MakeLabel(root.transform, "Percent", 0.26f),
                    Down = MakeLabel(root.transform, "Down", 0.30f),
                };
                root.SetActive(false);
                _built++;
                return bar;
            }
            catch (Exception e)
            {
                Core.Log.Warning($"Bar build failed: {e.GetType().Name}: {e.Message}");
                return null;
            }
        }

        private static Transform Quad(Transform parent, string name, out Material mat)
        {
            mat = null;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var mf = go.AddComponent(Il2CppType.Of<MeshFilter>()).TryCast<MeshFilter>();
            var mr = go.AddComponent(Il2CppType.Of<MeshRenderer>()).TryCast<MeshRenderer>();
            if (mf == null || mr == null) return go.transform;
            mf.sharedMesh = _quad;
            mat = new Material(_template);
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return go.transform;
        }

        private static void Destroy(Bar bar)
        {
            try { if (Interop.Alive(bar.Root)) UnityEngine.Object.Destroy(bar.Root); } catch { }
            try { if (bar.BackMat != null) UnityEngine.Object.Destroy(bar.BackMat); } catch { }
            try { if (bar.FillMat != null) UnityEngine.Object.Destroy(bar.FillMat); } catch { }
        }

        /// <summary>A unit quad centred on the origin, both windings, so nothing ever culls away.</summary>
        private static void EnsureAssets()
        {
            if (_quad == null)
            {
                var verts = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
                var uvs = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
                var tris = new[] { 0, 1, 2, 0, 2, 3, 0, 2, 1, 0, 3, 2 };
                var mesh = new Mesh { name = "PartyHealth_Quad" };
                mesh.vertices = (Il2CppStructArray<Vector3>)verts;
                mesh.uv = (Il2CppStructArray<Vector2>)uvs;
                mesh.triangles = (Il2CppStructArray<int>)tris;
                mesh.RecalculateBounds();
                _quad = mesh;
            }
            if (_template == null)
            {
                Shader shader = null;
                var name = "";
                var wantTop = ModConfig.OnTop.Value;
                if (wantTop) { shader = Shader.Find("UI/Default"); name = "UI/Default"; }
                if (shader == null) { shader = Shader.Find("Sprites/Default"); name = "Sprites/Default"; }
                if (shader == null) { shader = Shader.Find("Unlit/Color"); name = "Unlit/Color"; }
                if (shader == null) { shader = Shader.Find("Unlit/Transparent"); name = "Unlit/Transparent"; }
                if (shader == null) { Core.Log.Warning("No usable shader for the bars; nothing will be drawn."); return; }
                var mat = new Material(shader) { name = "PartyHealth_Bar" };
                _onTop = false;
                if (wantTop && name == "UI/Default")
                {
                    try { mat.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always); mat.renderQueue = 4000; _onTop = true; }
                    catch (Exception e) { Core.Log.Msg($"Depth-test switch failed ({e.GetType().Name}); walls can cover the bars."); }
                }
                _template = mat;
                if (!_materialLogged)
                {
                    _materialLogged = true;
                    Core.Log.Msg($"Bars use shader {name}{(_onTop ? ", drawn over everything" : ", depth-tested (walls can cover them)")}.");
                }
            }
        }

        private static void EnsureFont()
        {
            if (Interop.Alive(_font) || Time.unscaledTime < _fontRetryAt) return;
            _fontRetryAt = Time.unscaledTime + 2f;
            try
            {
                foreach (var t in UnityEngine.Object.FindObjectsOfType<TextMeshPro>(true))
                {
                    if (!Interop.Alive(t) || !Interop.Alive(t.font)) continue;
                    _font = t.font;
                    _fontMaterial = t.fontSharedMaterial;
                    Core.Log.Msg($"Label font captured from `{Interop.ScenePath(t.transform)}` ({_font.name}).");
                    break;
                }
            }
            catch (Exception e) { Core.Log.Msg($"Font search failed: {e.GetType().Name}"); }
        }

        private static TextMeshPro MakeLabel(Transform parent, string name, float size)
        {
            try
            {
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                var tmp = go.AddComponent(Il2CppType.Of<TextMeshPro>()).TryCast<TextMeshPro>();
                if (tmp == null) { UnityEngine.Object.Destroy(go); return null; }
                if (Interop.Alive(_font)) tmp.font = _font;
                if (Interop.Alive(_fontMaterial)) tmp.fontSharedMaterial = _fontMaterial;
                tmp.fontSize = size;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.enableWordWrapping = false;
                tmp.overflowMode = TextOverflowModes.Overflow;
                tmp.richText = false;
                tmp.rectTransform.sizeDelta = new Vector2(2f, 0.2f);
                tmp.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                tmp.text = "";
                if (ModConfig.OnTop.Value)
                {
                    try
                    {
                        var m = tmp.fontMaterial;   // this label's own instance, not the shared one
                        m.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
                        m.renderQueue = 4002;
                    }
                    catch { }
                }
                var r = go.GetComponent<Renderer>();
                if (Interop.Alive(r)) { r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; }
                go.SetActive(false);
                return tmp;
            }
            catch (Exception e)
            {
                Core.Log.Msg($"Label build failed: {e.GetType().Name}: {e.Message}");
                return null;
            }
        }

        public static void Reset()
        {
            foreach (var kv in Bars) Destroy(kv.Value);
            Bars.Clear();
            _eyes = null;
        }
    }
}
