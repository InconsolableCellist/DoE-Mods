using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace VisualCues.Hud
{
    /// <summary>
    /// The head-locked HUD: a plane <c>HudDistance</c> in front of the eyes that the cues are
    /// drawn on. A cue whose target is inside the view is a thin circle around the target; a
    /// cue outside the view is a short arc of the edge ring in the target's direction with a
    /// small tip pointing outward, and a cue behind you is on the ring too, on whichever side
    /// is the shorter turn (straight behind = bottom of the ring). Every marker carries an
    /// optional name and the distance in metres, just inside the arc.
    ///
    /// The rig is repositioned in LateUpdate to the camera pose, not parented to it, so it
    /// ignores any scale on the rig and never gets destroyed with a scene. The arc and the
    /// circle are two generated double-sided meshes with a UI shader whose depth test is
    /// switched off, so walls and enemies closer than the plane do not cover the marker; the
    /// log names the shader it got. Labels are 3D TextMeshPro with the game's own font,
    /// captured from any live text.
    /// </summary>
    public static class CueHud
    {
        public struct Cue
        {
            public string Key;
            public Vector3 World;
            public string Label;
            public Color Color;
            public float Started, Until;
            public int Priority;
            /// <summary>Quieter: lower alpha, and the in-view circle is thin and faint. Used for the call, which can sit on screen for seconds.</summary>
            public bool Subtle;
        }

        public static readonly Color SummonColor = new Color(0.30f, 0.90f, 1.00f);
        public static readonly Color NoiseColor = new Color(1.00f, 0.50f, 0.15f);
        public static readonly Color BossColor = new Color(1.00f, 0.30f, 0.80f);

        private sealed class Marker
        {
            public string Key;
            public GameObject Root;
            public Transform Shape;
            public MeshFilter ShapeFilter;
            public Material ShapeMaterial;
            public TextMeshPro Label;
            public string LastText;
            public bool Used;
        }

        private static readonly List<Cue> Pending = new List<Cue>();
        private static readonly List<Marker> Markers = new List<Marker>();
        private static GameObject _rig;
        private static Transform _head;
        private static float _headCheckedAt = -100f;
        private static string _headSource = "";

        private static Mesh _arcMesh, _circleMesh, _thinCircleMesh;
        private static float _meshScaleKey = -1f;
        private static Material _shapeTemplate;
        private static bool _onTop, _materialLogged;
        private static TMP_FontAsset _font;
        private static Material _fontMaterial;
        private static float _fontRetryAt = -100f;

        private static TextMeshPro _flash;
        private static float _flashUntil = -1f;

        // ---- head -------------------------------------------------------------------------

        /// <summary>The eyes: the main camera when there is one, else the local avatar's eye or head transform.</summary>
        public static Transform HeadTransform
        {
            get
            {
                var now = Time.unscaledTime;
                if (Interop.Alive(_head) && now - _headCheckedAt < 3f) return _head;
                _headCheckedAt = now;
                var found = ResolveHead(out var source);
                if (Interop.Alive(found) && (found != _head || source != _headSource))
                {
                    Core.Log.Msg($"HUD follows {source}: {Interop.ScenePath(found)}");
                }
                _head = found; _headSource = source;
                return _head;
            }
        }

        private static Transform ResolveHead(out string source)
        {
            source = "nothing";
            try
            {
                var cam = Camera.main;
                if (Interop.Alive(cam) && cam.enabled) { source = "Camera.main"; return cam.transform; }
            }
            catch { }
            try
            {
                var local = AvatarPlayer.LocalAvatar;
                if (Interop.Alive(local))
                {
                    if (Interop.Alive(local.Eye)) { source = "AvatarPlayer.Eye"; return local.Eye; }
                    if (Interop.Alive(local.Head)) { source = "AvatarPlayer.Head"; return local.Head; }
                }
            }
            catch { }
            return null;
        }

        // ---- API --------------------------------------------------------------------------

        public static void Submit(Cue cue) => Pending.Add(cue);

        public static void Flash(string text, Color color, float seconds)
        {
            try
            {
                EnsureRig();
                if (_flash == null) return;
                _flash.text = text;
                _flash.color = color;
                _flash.gameObject.SetActive(true);
                _flashUntil = Time.unscaledTime + seconds;
            }
            catch (Exception e) { Core.Log.Msg($"[flash] {text} ({e.GetType().Name})"); }
        }

        /// <summary>Scene changed: forget the head, keep the rig (it lives in DontDestroyOnLoad).</summary>
        public static void Reset()
        {
            _head = null; _headCheckedAt = -100f;
            Pending.Clear();
            foreach (var m in Markers) SafeSetActive(m.Root, false);
        }

        // ---- per frame (LateUpdate) ------------------------------------------------------

        public static void LateTick()
        {
            try { Layout(); }
            catch (Exception e) { Core.Log.Warning($"HUD layout threw: {e.GetType().Name}: {e.Message}"); }
            Pending.Clear();
        }

        private static void Layout()
        {
            var head = ModConfig.Enabled.Value ? HeadTransform : null;
            if (!Interop.Alive(head))
            {
                if (Interop.Alive(_rig) && _rig.activeSelf) _rig.SetActive(false);
                return;
            }
            EnsureRig();
            if (!_rig.activeSelf) _rig.SetActive(true);
            _rig.transform.position = head.position;
            _rig.transform.rotation = head.rotation;

            var D = Mathf.Clamp(ModConfig.HudDistance.Value, 0.4f, 5f);
            var scale = Mathf.Clamp(ModConfig.HudScale.Value, 0.3f, 4f);
            var ringR = D * Mathf.Tan(Mathf.Clamp(ModConfig.HudRingDegrees.Value, 8f, 45f) * Mathf.Deg2Rad);
            var circleR = 0.045f * D * scale;
            var fontSize = 0.22f * D * scale;
            var now = Time.unscaledTime;
            EnsureShapeAssets(scale);

            foreach (var m in Markers) m.Used = false;
            Pending.Sort((a, b) => b.Priority.CompareTo(a.Priority));

            foreach (var cue in Pending)
            {
                var m = Acquire(cue.Key);
                if (m == null) continue;
                m.Used = true;

                var local = _rig.transform.InverseTransformPoint(cue.World);
                var dist = local.magnitude;
                var inFront = local.z > 0.05f;
                var behind = !inFront;
                var onTarget = false;
                Vector2 dir = Vector2.down, proj = Vector2.zero;
                if (inFront)
                {
                    proj = new Vector2(local.x / local.z * D, local.y / local.z * D);
                    if (proj.magnitude <= ringR) onTarget = true;
                    else dir = proj.normalized;
                }
                else
                {
                    dir = new Vector2(local.x, local.y);
                    if (dir.sqrMagnitude < 1e-4f) dir = Vector2.down;
                    dir.Normalize();
                }

                // Pulse briefly, sit quietly, fade over the last 0.6 s.
                var age = now - cue.Started;
                var left = cue.Until - now;
                var alpha = cue.Subtle ? 0.5f : 0.72f;
                if (age < 0.6f) alpha = 0.45f + 0.5f * Mathf.Abs(Mathf.Sin(age * 10f));
                if (behind) alpha = Mathf.Min(1f, alpha + 0.12f);
                if (onTarget && cue.Subtle) alpha *= 0.6f;
                if (left < 0.6f) alpha *= Mathf.Clamp01(left / 0.6f);
                var c = cue.Color; c.a = alpha;

                m.Root.transform.localPosition = new Vector3(0f, 0f, D);
                m.Root.transform.localRotation = Quaternion.identity;
                Vector3 labelPos;
                if (onTarget)
                {
                    // A thin circle around the target, label under it.
                    var circle = cue.Subtle && _thinCircleMesh != null ? _thinCircleMesh : _circleMesh;
                    if (m.ShapeFilter != null && circle != null && m.ShapeFilter.sharedMesh != circle) m.ShapeFilter.sharedMesh = circle;
                    m.Shape.localPosition = new Vector3(proj.x, proj.y, 0f);
                    m.Shape.localRotation = Quaternion.identity;
                    m.Shape.localScale = new Vector3(circleR, circleR, 1f);
                    labelPos = new Vector3(proj.x, proj.y - circleR - fontSize * 0.09f, 0f);
                }
                else
                {
                    // An arc of the edge ring centred on the direction, tip outward; label just inside it.
                    if (m.ShapeFilter != null && _arcMesh != null && m.ShapeFilter.sharedMesh != _arcMesh) m.ShapeFilter.sharedMesh = _arcMesh;
                    var deg = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;
                    m.Shape.localPosition = Vector3.zero;
                    m.Shape.localRotation = Quaternion.Euler(0f, 0f, deg);
                    m.Shape.localScale = new Vector3(ringR, ringR, 1f);
                    var inset = ringR - fontSize * 0.14f - 0.02f * D * scale;
                    labelPos = new Vector3(dir.x * inset, dir.y * inset, 0f);
                }
                if (m.ShapeMaterial != null) m.ShapeMaterial.color = c;

                if (m.Label != null)
                {
                    var text = cue.Label ?? "";
                    if (ModConfig.HudShowDistance.Value) text = text.Length == 0 ? $"{dist:0} m" : $"{text}  {dist:0} m";
                    if (behind) text = text.Length == 0 ? "behind" : $"{text}  (behind)";
                    if (text != m.LastText) { m.Label.text = text; m.LastText = text; }
                    m.Label.fontSize = fontSize;
                    m.Label.color = c;
                    m.Label.transform.localPosition = labelPos;
                    m.Label.gameObject.SetActive(text.Length > 0);
                }
                if (!m.Root.activeSelf) m.Root.SetActive(true);
            }

            for (var i = Markers.Count - 1; i >= 0; i--)
            {
                var m = Markers[i];
                if (m.Used) continue;
                m.Key = null;
                SafeSetActive(m.Root, false);
                if (Markers.Count > 24) { SafeDestroy(m.Root); Markers.RemoveAt(i); }
            }

            if (_flash != null)
            {
                if (now >= _flashUntil) { if (_flash.gameObject.activeSelf) _flash.gameObject.SetActive(false); }
                else
                {
                    _flash.fontSize = fontSize * 1.8f;
                    _flash.transform.localPosition = new Vector3(0f, -ringR * 0.55f, D);
                    var c = _flash.color; c.a = Mathf.Clamp01((_flashUntil - now) / 0.3f); _flash.color = c;
                }
            }
        }

        private static Marker Acquire(string key)
        {
            Marker free = null;
            foreach (var m in Markers)
            {
                if (m.Key == key && !m.Used) return m;
                if (free == null && m.Key == null) free = m;
            }
            if (free == null)
            {
                free = Build();
                if (free == null) return null;
                Markers.Add(free);
            }
            free.Key = key;
            free.LastText = null;
            return free;
        }

        // ---- construction ------------------------------------------------------------------

        private static void EnsureRig()
        {
            if (Interop.Alive(_rig)) return;
            _rig = new GameObject("VisualCues_HUD");
            UnityEngine.Object.DontDestroyOnLoad(_rig);
            Markers.Clear();
            _flash = null;
            EnsureFont();
            _flash = MakeLabel(_rig.transform, "Flash", TextAlignmentOptions.Center);
            if (_flash != null) _flash.gameObject.SetActive(false);
        }

        private static Marker Build()
        {
            try
            {
                EnsureShapeAssets(Mathf.Clamp(ModConfig.HudScale.Value, 0.3f, 4f));
                EnsureFont();
                var root = new GameObject("Cue");
                root.transform.SetParent(_rig.transform, false);

                var shape = new GameObject("Shape");
                shape.transform.SetParent(root.transform, false);
                Material mat = null;
                MeshFilter mf = null;
                if (_arcMesh != null && _shapeTemplate != null)
                {
                    mf = shape.AddComponent(Il2CppType.Of<MeshFilter>()).TryCast<MeshFilter>();
                    var mr = shape.AddComponent(Il2CppType.Of<MeshRenderer>()).TryCast<MeshRenderer>();
                    mf.sharedMesh = _arcMesh;
                    mat = new Material(_shapeTemplate);
                    mr.sharedMaterial = mat;
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                    mr.receiveShadows = false;
                    mr.lightProbeUsage = LightProbeUsage.Off;
                    mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                }

                var label = MakeLabel(root.transform, "Label", TextAlignmentOptions.Center);
                return new Marker { Root = root, Shape = shape.transform, ShapeFilter = mf, ShapeMaterial = mat, Label = label };
            }
            catch (Exception e)
            {
                Core.Log.Warning($"HUD marker build failed: {e.GetType().Name}: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// The two shapes, at unit radius so the marker's scale sets the radius. The band width
        /// and the tip are relative to that radius and multiplied by <c>HudScale</c>; both
        /// meshes are rebuilt when the scale changes. Double-sided so nothing ever culls away.
        /// </summary>
        private static void EnsureShapeAssets(float scale)
        {
            if (_arcMesh == null || _circleMesh == null || _thinCircleMesh == null || Mathf.Abs(scale - _meshScaleKey) > 0.001f)
            {
                _meshScaleKey = scale;
                // Edge-ring arc: ±16° of band, 2.5 % of the radius wide, with a small outward tip.
                var arc = BuildArc(16f, 0.025f * scale, tipHeight: 0.07f * scale, tipHalfWidth: 0.035f * scale);
                // In-view circle: full ring, a bit heavier in relative terms because it is much smaller.
                var circle = BuildArc(180f, 0.10f * Mathf.Sqrt(scale), tipHeight: 0f, tipHalfWidth: 0f);
                if (_arcMesh != null) SwapMesh(_arcMesh, arc); else _arcMesh = arc;
                if (_circleMesh != null) SwapMesh(_circleMesh, circle); else _circleMesh = circle;
                var thin = BuildArc(180f, 0.04f * Mathf.Sqrt(scale), tipHeight: 0f, tipHalfWidth: 0f);
                if (_thinCircleMesh != null) SwapMesh(_thinCircleMesh, thin); else _thinCircleMesh = thin;
            }
            if (_shapeTemplate == null)
            {
                Shader shader = null;
                var name = "";
                var wantTop = ModConfig.HudOnTop.Value;
                if (wantTop) { shader = Shader.Find("UI/Default"); name = "UI/Default"; }
                if (shader == null) { shader = Shader.Find("Sprites/Default"); name = "Sprites/Default"; }
                if (shader == null) { shader = Shader.Find("Unlit/Color"); name = "Unlit/Color"; }
                if (shader == null) { shader = Shader.Find("Unlit/Transparent"); name = "Unlit/Transparent"; }
                if (shader == null) { Core.Log.Warning("HUD: no usable shader for markers; labels only."); return; }
                var mat = new Material(shader) { name = "VisualCues_Marker" };
                _onTop = false;
                if (wantTop && name == "UI/Default")
                {
                    try { mat.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always); mat.renderQueue = 4000; _onTop = true; }
                    catch (Exception e) { Core.Log.Msg($"HUD: depth-test switch failed ({e.GetType().Name}); markers can be covered by near walls."); }
                }
                _shapeTemplate = mat;
                if (!_materialLogged)
                {
                    _materialLogged = true;
                    Core.Log.Msg($"HUD markers use shader {name}{(_onTop ? ", drawn over everything" : ", depth-tested (near walls can cover them)")}.");
                }
            }
        }

        /// <summary>Copy geometry from a freshly built mesh into one that markers already reference.</summary>
        private static void SwapMesh(Mesh target, Mesh fresh)
        {
            try
            {
                target.Clear();
                target.vertices = fresh.vertices;
                target.uv = fresh.uv;
                target.triangles = fresh.triangles;
                target.RecalculateBounds();
                UnityEngine.Object.Destroy(fresh);
            }
            catch (Exception e) { Core.Log.Msg($"HUD mesh rebuild failed: {e.GetType().Name}"); }
        }

        /// <summary>
        /// A band of a unit-radius ring centred on +Y spanning ±<paramref name="halfDeg"/>,
        /// <paramref name="width"/> thick, plus an optional triangular tip on the outside at the
        /// centre of the arc. Both windings are emitted.
        /// </summary>
        private static Mesh BuildArc(float halfDeg, float width, float tipHeight, float tipHalfWidth)
        {
            var segments = Mathf.Max(6, Mathf.CeilToInt(halfDeg / 3f));
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            var ri = 1f - width * 0.5f;
            var ro = 1f + width * 0.5f;
            for (var i = 0; i <= segments; i++)
            {
                var a = (90f - halfDeg + 2f * halfDeg * i / segments) * Mathf.Deg2Rad;
                var cx = Mathf.Cos(a); var cy = Mathf.Sin(a);
                verts.Add(new Vector3(cx * ri, cy * ri, 0f)); uvs.Add(new Vector2((float)i / segments, 0f));
                verts.Add(new Vector3(cx * ro, cy * ro, 0f)); uvs.Add(new Vector2((float)i / segments, 1f));
            }
            for (var i = 0; i < segments; i++)
            {
                var b = i * 2;
                Quad(tris, b, b + 1, b + 3, b + 2);
            }
            if (tipHeight > 0f)
            {
                var b = verts.Count;
                verts.Add(new Vector3(-tipHalfWidth, ro - width * 0.25f, 0f)); uvs.Add(new Vector2(0f, 0f));
                verts.Add(new Vector3(tipHalfWidth, ro - width * 0.25f, 0f)); uvs.Add(new Vector2(1f, 0f));
                verts.Add(new Vector3(0f, ro + tipHeight, 0f)); uvs.Add(new Vector2(0.5f, 1f));
                tris.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 1 });
            }
            var mesh = new Mesh { name = tipHeight > 0f ? "VisualCues_Arc" : "VisualCues_Circle" };
            mesh.vertices = (Il2CppStructArray<Vector3>)verts.ToArray();
            mesh.uv = (Il2CppStructArray<Vector2>)uvs.ToArray();
            mesh.triangles = (Il2CppStructArray<int>)tris.ToArray();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void Quad(List<int> tris, int a, int b, int c, int d)
        {
            tris.AddRange(new[] { a, b, c, a, c, d, a, c, b, a, d, c });
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
                    Core.Log.Msg($"HUD font captured from `{Interop.ScenePath(t.transform)}` ({_font.name}).");
                    break;
                }
            }
            catch (Exception e) { Core.Log.Msg($"HUD font search failed: {e.GetType().Name}"); }
        }

        private static TextMeshPro MakeLabel(Transform parent, string name, TextAlignmentOptions align)
        {
            try
            {
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                var tmp = go.AddComponent(Il2CppType.Of<TextMeshPro>()).TryCast<TextMeshPro>();
                if (tmp == null) { UnityEngine.Object.Destroy(go); return null; }
                if (Interop.Alive(_font)) tmp.font = _font;
                if (Interop.Alive(_fontMaterial)) tmp.fontSharedMaterial = _fontMaterial;
                tmp.fontSize = 0.3f;
                tmp.alignment = align;
                tmp.enableWordWrapping = false;
                tmp.overflowMode = TextOverflowModes.Overflow;
                tmp.richText = false;
                tmp.rectTransform.sizeDelta = new Vector2(2f, 0.2f);
                tmp.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                tmp.text = "";
                if (ModConfig.HudOnTop.Value)
                {
                    try
                    {
                        // fontMaterial (not fontSharedMaterial) is this label's own instance.
                        var m = tmp.fontMaterial;
                        m.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
                        m.renderQueue = 4001;
                    }
                    catch { }
                }
                var r = go.GetComponent<Renderer>();
                if (Interop.Alive(r)) { r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; }
                return tmp;
            }
            catch (Exception e)
            {
                Core.Log.Msg($"HUD label build failed: {e.GetType().Name}: {e.Message}");
                return null;
            }
        }

        private static void SafeSetActive(GameObject go, bool on)
        {
            try { if (Interop.Alive(go) && go.activeSelf != on) go.SetActive(on); } catch { }
        }

        private static void SafeDestroy(GameObject go)
        {
            try { if (Interop.Alive(go)) UnityEngine.Object.Destroy(go); } catch { }
        }

        public static string Describe() => $"head={_headSource}, markers pooled {Markers.Count}, markers {(_shapeTemplate == null ? "none" : _onTop ? "on top" : "depth-tested")}, font {(Interop.Alive(_font) ? _font.name : "none")}";
    }
}
