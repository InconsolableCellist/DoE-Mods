using System;
using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.Rendering;

namespace PartyHealth.Hud
{
    /// <summary>
    /// Your own health as a red arc low in your view (0.2.0), shown only while you are below full.
    ///
    /// It sits just under StayPutVR's heal-shield arc and is drawn the same way: a thin mesh of
    /// the mod's own, parented to the camera so it moves with the head on the same frame, drawn
    /// over everything. The full span is a dim track; the bright red part is your health, centred
    /// and shrinking from both ends as you lose it, so half health reads as half the arc. It pulses
    /// below a quarter, and fades out a moment after you are back to full. Local only: it reads
    /// the local avatar's health object and sends nothing.
    /// </summary>
    public static class SelfHealthArc
    {
        private const int Segments = 96;
        private const float Distance = 0.6f;       // metres in front of the eyes, as StayPutVR's arc
        private const float RingDegrees = 31.5f;   // StayPutVR's shield arc is at 28°: this is just below it
        private const float Thickness = 0.008f;
        private const float HalfSpanDegrees = 50f;
        private const float FullAt = 0.995f;
        private static readonly Color Fill = new Color(1.00f, 0.18f, 0.15f, 1f);
        private static readonly Color Track = new Color(0.35f, 0.04f, 0.04f, 1f);

        private static GameObject _go;
        private static Mesh _mesh;
        private static Material _material;
        private static Transform _eyes;
        private static float _eyesRetryAt;
        private static float _fullSince = -1f;
        private static float _alpha;
        private static bool _failed, _logged;
        private static readonly Vector3[] Verts = new Vector3[(Segments + 1) * 2];
        private static readonly Color[] Colors = new Color[(Segments + 1) * 2];

        public static void Reset()
        {
            try { if (Interop.Alive(_go)) UnityEngine.Object.Destroy(_go); } catch { }
            _go = null; _eyes = null; _alpha = 0f; _fullSince = -1f;
        }

        public static void Tick()
        {
            if (_failed) return;
            try
            {
                var now = Time.unscaledTime;
                var want = false;
                var fraction = 1f;
                if (ModConfig.Enabled.Value && ModConfig.SelfHealthArc.Value && !HealthBars.Hidden && TryHealth(out fraction))
                {
                    if (fraction < FullAt) { want = true; _fullSince = -1f; }
                    else
                    {
                        if (_fullSince < 0f) _fullSince = now;
                        want = now - _fullSince < ModConfig.SelfHealthArcHoldSeconds.Value;
                    }
                }
                var fade = Mathf.Max(0.05f, ModConfig.FadeSeconds.Value);
                _alpha = Mathf.MoveTowards(_alpha, want ? 1f : 0f, Time.unscaledDeltaTime / fade);
                if (_alpha <= 0f)
                {
                    if (Interop.Alive(_go) && _go.activeSelf) _go.SetActive(false);
                    return;
                }
                if (!Ensure()) return;
                if (!_go.activeSelf) _go.SetActive(true);
                Shape(Mathf.Clamp01(fraction), now);
            }
            catch (Exception e)
            {
                _failed = true;
                Core.Log.Warning($"Own health arc off for this session: {e.GetType().Name}: {e.Message}");
            }
        }

        private static bool TryHealth(out float fraction)
        {
            fraction = 1f;
            var local = AvatarPlayer.LocalAvatar;
            if (!Interop.Alive(local)) return false;
            var h = local.health;
            if (!Interop.Alive(h)) return false;
            float n = float.NaN;
            try { n = h.normalizedHP; } catch { }
            if (float.IsNaN(n) || float.IsInfinity(n))
            {
                float max = 0f, hp = float.NaN;
                try { max = h.GetMaxHP(); } catch { try { max = h.maxHP; } catch { } }
                try { hp = h.HP.Value; } catch { }
                if (max <= 0.01f || float.IsNaN(hp)) return false;
                n = hp / max;
            }
            fraction = Mathf.Clamp01(n);
            return true;
        }

        private static bool Ensure()
        {
            var eyes = Eyes();
            if (!Interop.Alive(eyes)) return false;
            if (Interop.Alive(_go) && _go.transform.parent != null && _go.transform.parent.Pointer == eyes.Pointer) return true;
            if (Interop.Alive(_go)) UnityEngine.Object.Destroy(_go);

            if (!Interop.Alive(_material))
            {
                Shader shader = Shader.Find("UI/Default");
                var onTop = shader != null;
                if (shader == null) shader = Shader.Find("Sprites/Default");
                if (shader == null) shader = Shader.Find("Unlit/Transparent");
                if (shader == null) { _failed = true; Core.Log.Warning("No usable shader for the own health arc; it will not be drawn."); return false; }
                _material = new Material(shader) { name = "PartyHealth_SelfArc", color = Color.white };
                if (onTop)
                {
                    try { _material.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always); } catch { }
                    _material.renderQueue = 4000;
                }
                _material.hideFlags = HideFlags.DontUnloadUnusedAsset;
            }
            if (!Interop.Alive(_mesh))
            {
                _mesh = new Mesh { name = "PartyHealth_SelfArc" };
                _mesh.MarkDynamic();
                var tris = new int[Segments * 12];
                for (var i = 0; i < Segments; i++)
                {
                    int a = i * 2, b = a + 1, c = a + 2, d = a + 3, t = i * 12;
                    tris[t] = a; tris[t + 1] = c; tris[t + 2] = b;
                    tris[t + 3] = b; tris[t + 4] = c; tris[t + 5] = d;
                    tris[t + 6] = a; tris[t + 7] = b; tris[t + 8] = c;
                    tris[t + 9] = b; tris[t + 10] = d; tris[t + 11] = c;
                }
                _mesh.vertices = (Il2CppStructArray<Vector3>)Verts;
                _mesh.colors = (Il2CppStructArray<Color>)Colors;
                _mesh.triangles = (Il2CppStructArray<int>)tris;
                _mesh.hideFlags = HideFlags.DontUnloadUnusedAsset;
            }

            _go = new GameObject("PartyHealth_SelfArc");
            _go.transform.SetParent(eyes, false);
            _go.transform.localPosition = Vector3.zero;
            _go.transform.localRotation = Quaternion.identity;
            _go.transform.localScale = Vector3.one;
            var mf = _go.AddComponent(Il2CppType.Of<MeshFilter>()).TryCast<MeshFilter>();
            var mr = _go.AddComponent(Il2CppType.Of<MeshRenderer>()).TryCast<MeshRenderer>();
            if (mf == null || mr == null) { _failed = true; return false; }
            mf.sharedMesh = _mesh;
            mr.sharedMaterial = _material;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            if (!_logged) { _logged = true; Core.Log.Msg($"Own health arc drawn with {_material.shader.name}."); }
            return true;
        }

        private static void Shape(float fraction, float now)
        {
            var radius = Distance * Mathf.Tan(RingDegrees * Mathf.Deg2Rad);
            var half = HalfSpanDegrees * Mathf.Deg2Rad;
            var opacity = Mathf.Clamp01(ModConfig.SelfHealthArcOpacity.Value) * _alpha;
            var pulse = fraction < 0.25f ? 0.7f + 0.3f * Mathf.Sin(now * 8f) : 1f;
            for (var i = 0; i <= Segments; i++)
            {
                var u = (float)i / Segments;
                var angle = -Mathf.PI / 2f - half + 2f * half * u;
                var dir = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                Verts[i * 2] = dir * (radius - Thickness / 2f) + new Vector3(0f, 0f, Distance);
                Verts[i * 2 + 1] = dir * (radius + Thickness / 2f) + new Vector3(0f, 0f, Distance);
                var lit = Mathf.Abs(u - 0.5f) * 2f <= fraction;
                var taper = Mathf.SmoothStep(0f, 1f, Mathf.Min(u, 1f - u) / 0.08f);
                var c = lit ? new Color(Fill.r, Fill.g, Fill.b, opacity * pulse * taper) : new Color(Track.r, Track.g, Track.b, opacity * 0.45f * taper);
                Colors[i * 2] = c;
                Colors[i * 2 + 1] = c;
            }
            _mesh.vertices = (Il2CppStructArray<Vector3>)Verts;
            _mesh.colors = (Il2CppStructArray<Color>)Colors;
            _mesh.RecalculateBounds();
        }

        private static Transform Eyes()
        {
            if (Interop.Alive(_eyes) && _eyes.gameObject.activeInHierarchy) return _eyes;
            if (Time.unscaledTime < _eyesRetryAt) return null;
            _eyesRetryAt = Time.unscaledTime + 1f;
            _eyes = null;
            try
            {
                var cam = Camera.main;
                if (Interop.Alive(cam) && cam.enabled) _eyes = cam.transform;
            }
            catch { }
            if (_eyes == null)
            {
                try
                {
                    var local = AvatarPlayer.LocalAvatar;
                    if (Interop.Alive(local) && Interop.Alive(local.Eye)) _eyes = local.Eye;
                }
                catch { }
            }
            return _eyes;
        }
    }
}
