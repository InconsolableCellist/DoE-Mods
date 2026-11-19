using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CustomEnemies.Skins
{
    /// <summary>
    /// A camera-facing additive glow on each eye bone, so the eyes read as glowing with or without
    /// the player's bloom setting (bloom is a player option, see GAME-INTERNALS → Enemies). The glow
    /// material is a tinted copy of the enemy's own additive particle material (Death's weapon
    /// trail uses FX/Particles/Additive), so no shader ships with the mod. It grows a little with
    /// distance so it still reads across a room.
    /// </summary>
    public sealed class Halos
    {
        private readonly List<(Transform quad, float size)> _quads = new List<(Transform, float)>();
        public int Count => _quads.Count;
        /// <summary>Size multiplier (the growl swells the eyes).</summary>
        public float Boost = 1f;
        private Material _mat;

        /// <summary>Recolour every halo.</summary>
        public void Tint(Color c)
        {
            if (!Interop.Alive(_mat)) return;
            foreach (var p in new[] { "_TintColor", "_Color", "_BaseColor" }) if (_mat.HasProperty(p)) _mat.SetColor(p, c);
        }

        public static Halos Build(Transform skin, SkinLibrary.Sidecar def, Transform enemy, List<Material> owned)
        {
            if (def.eyes == null || def.eyes.Count == 0) return null;
            var mat = AdditiveFrom(enemy);
            if (mat == null) { Core.Log.Warning("Eye halos: the enemy has no additive material to borrow; no halos."); return null; }
            var copy = new Material(mat) { name = "CustomEnemies eye halo" };
            var c = def.haloColour != null && def.haloColour.Length >= 3
                ? new Color(def.haloColour[0], def.haloColour[1], def.haloColour[2], def.haloColour.Length > 3 ? def.haloColour[3] : 1f)
                : new Color(1f, 0.15f, 0.05f, 1f);
            foreach (var p in new[] { "_TintColor", "_Color", "_BaseColor" }) if (copy.HasProperty(p)) copy.SetColor(p, c);
            owned.Add(copy);

            var h = new Halos { _mat = copy };
            foreach (var name in def.eyes)
            {
                Transform eye = null;
                foreach (var t in skin.GetComponentsInChildren<Transform>(true))
                    if (string.Equals(t.name, name, StringComparison.OrdinalIgnoreCase)) { eye = t; break; }
                if (!Interop.Alive(eye)) { Core.Log.Warning($"Eye halos: bone `{name}` not found in the skin."); continue; }
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                var col = q.GetComponent<Collider>();
                if (Interop.Alive(col)) Object.DestroyImmediate(col);
                q.name = "eye halo";
                q.layer = skin.gameObject.layer;
                q.transform.SetParent(eye, false);
                var r = q.GetComponent<MeshRenderer>();
                r.sharedMaterial = copy;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                h._quads.Add((q.transform, Mathf.Max(0.01f, def.haloSize)));
            }
            return h;
        }

        private static Material AdditiveFrom(Transform enemy)
        {
            foreach (var r in enemy.GetComponentsInChildren<Renderer>(true))
            {
                if (!Interop.Alive(r)) continue;
                var m = r.sharedMaterial;
                if (!Interop.Alive(m) || !Interop.Alive(m.shader)) continue;
                if (m.shader.name.IndexOf("Additive", StringComparison.OrdinalIgnoreCase) >= 0) return m;
            }
            var s = Shader.Find("FX/Particles/Additive") ?? Shader.Find("Legacy Shaders/Particles/Additive");
            return Interop.Alive(s) ? new Material(s) : null;
        }

        public void Tick()
        {
            var cam = CameraFinder.Get();
            if (!Interop.Alive(cam)) return;
            var cp = cam.transform.position;
            foreach (var (q, size) in _quads)
            {
                if (!Interop.Alive(q)) continue;
                var to = cp - q.position;
                var d = to.magnitude;
                if (d < 1e-3f) continue;
                // A quad's visible side looks down -Z: point -Z at the camera.
                q.rotation = Quaternion.LookRotation(-to / d, cam.transform.up);
                var world = size * Boost * Mathf.Clamp(d / 4f, 1f, 2.5f);
                var parentScale = q.parent != null ? q.parent.lossyScale.x : 1f;
                q.localScale = Vector3.one * (world / Mathf.Max(1e-4f, Mathf.Abs(parentScale)));
            }
        }

        public void Destroy()
        {
            foreach (var (q, _) in _quads) if (Interop.Alive(q)) Object.Destroy(q.gameObject);
            _quads.Clear();
        }
    }

    /// <summary>
    /// The camera the player sees through. <c>Camera.main</c> is null in this game's VR rig (the
    /// probe found it so), so this takes the enabled on-screen camera with the highest depth and
    /// keeps it until it dies.
    /// </summary>
    public static class CameraFinder
    {
        private static Camera _cam;
        private static float _retryAt;

        public static Camera Get()
        {
            if (Interop.Alive(_cam) && _cam.isActiveAndEnabled) return _cam;
            if (Time.unscaledTime < _retryAt) return null;
            _retryAt = Time.unscaledTime + 1f;
            _cam = null;
            try
            {
                var main = Camera.main;
                if (Interop.Alive(main) && main.isActiveAndEnabled) return _cam = main;
                foreach (var c in Camera.allCameras)
                {
                    if (!Interop.Alive(c) || !c.isActiveAndEnabled || c.targetTexture != null) continue;
                    if (_cam == null || c.depth > _cam.depth) _cam = c;
                }
            }
            catch { }
            return _cam;
        }
    }
}
