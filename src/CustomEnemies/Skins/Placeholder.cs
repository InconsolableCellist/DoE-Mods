using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace CustomEnemies.Skins
{
    /// <summary>
    /// The built-in test skin: a hooded, robed wolf-ish dummy made of Unity primitives at
    /// runtime, so the attach, hide, follow and pooling can be tested and released with no model
    /// on disk. Sized for Death's rig before its 1.44 spec scale (about 2.1 m to the hood).
    ///
    /// Materials are copies of the enemy's own body material, tinted, so the dummy is lit the
    /// way the enemy is (Death: <c>PotoVR/Characters_Normal</c> from the Bakery volume). The eyes
    /// reuse the enemy's own eye material when it has one (Death's <c>fx_skeleton-eyes</c>,
    /// <c>PotoVR/Candle</c>), otherwise a bright tint of the body copy. Every primitive's collider
    /// is destroyed on the spot: the dummy must never touch physics.
    /// </summary>
    public static class Placeholder
    {
        public sealed class Built
        {
            public GameObject Root;
            public Transform Head;
            public Vector3 HeadRest;
            public string EyesFrom;
            public readonly List<Renderer> Renderers = new List<Renderer>();
            public readonly List<Material> Owned = new List<Material>();

            public void Destroy()
            {
                if (Interop.Alive(Root)) Object.Destroy(Root);
                foreach (var m in Owned) if (Interop.Alive(m)) Object.Destroy(m);
                Owned.Clear();
                Renderers.Clear();
            }
        }

        private static readonly Color Fur = new Color(0.20f, 0.21f, 0.24f, 1f);
        private static readonly Color Robe = new Color(0.16f, 0.06f, 0.05f, 1f);
        private static readonly Color Eye = new Color(1f, 0.08f, 0.04f, 1f);

        public static Built Build(string who, Renderer body, Transform enemy)
        {
            var b = new Built();
            try
            {
                b.Root = new GameObject($"CustomEnemies skin ({who})");
                var layer = body.gameObject.layer;
                var src = body.sharedMaterial;
                var fur = Tinted(b, src, Fur, "fur");
                var robe = Tinted(b, src, Robe, "robe");
                var eye = EyeMaterial(b, enemy, src);

                var root = b.Root.transform;
                Part(b, PrimitiveType.Cylinder, root, robe, layer, new Vector3(0f, 0.55f, 0f), Vector3.zero, new Vector3(0.85f, 0.55f, 0.85f));
                Part(b, PrimitiveType.Capsule, root, fur, layer, new Vector3(0f, 1.30f, 0f), Vector3.zero, new Vector3(0.62f, 0.45f, 0.46f));
                Part(b, PrimitiveType.Capsule, root, robe, layer, new Vector3(0f, 1.42f, -0.04f), Vector3.zero, new Vector3(0.7f, 0.38f, 0.5f));

                var head = new GameObject("head").transform;
                head.SetParent(root, false);
                b.HeadRest = new Vector3(0f, 1.85f, 0.02f);
                head.localPosition = b.HeadRest;
                b.Head = head;
                Part(b, PrimitiveType.Sphere, head, fur, layer, Vector3.zero, Vector3.zero, new Vector3(0.38f, 0.38f, 0.42f));
                Part(b, PrimitiveType.Cube, head, fur, layer, new Vector3(0f, -0.06f, 0.26f), new Vector3(8f, 0f, 0f), new Vector3(0.17f, 0.15f, 0.30f));
                Part(b, PrimitiveType.Cube, head, fur, layer, new Vector3(-0.12f, 0.22f, -0.02f), new Vector3(0f, 0f, 15f), new Vector3(0.07f, 0.24f, 0.11f));
                Part(b, PrimitiveType.Cube, head, fur, layer, new Vector3(0.12f, 0.22f, -0.02f), new Vector3(0f, 0f, -15f), new Vector3(0.07f, 0.24f, 0.11f));
                // Hood: a flattened sphere over the back of the head.
                Part(b, PrimitiveType.Sphere, head, robe, layer, new Vector3(0f, 0.05f, -0.07f), Vector3.zero, new Vector3(0.46f, 0.44f, 0.44f));
                // Eyes: quads facing forward (a quad's visible side looks down -Z, so turned round).
                var eyeL = Part(b, PrimitiveType.Quad, head, eye, layer, new Vector3(-0.08f, 0.04f, 0.205f), new Vector3(0f, 180f, 0f), new Vector3(0.07f, 0.05f, 1f));
                var eyeR = Part(b, PrimitiveType.Quad, head, eye, layer, new Vector3(0.08f, 0.04f, 0.205f), new Vector3(0f, 180f, 0f), new Vector3(0.07f, 0.05f, 1f));
                foreach (var e in new[] { eyeL, eyeR }) if (e != null) e.receiveShadows = false;

                b.Root.transform.SetPositionAndRotation(enemy.position, enemy.rotation);
                return b;
            }
            catch (Exception e)
            {
                Core.Log.Warning($"Placeholder for `{who}` failed: {e.GetType().Name}: {e.Message}");
                b.Destroy();
                return null;
            }
        }

        private static MeshRenderer Part(Built b, PrimitiveType type, Transform parent, Material mat, int layer,
                                         Vector3 pos, Vector3 euler, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(type);
            var col = go.GetComponent<Collider>();
            if (Interop.Alive(col)) Object.DestroyImmediate(col);
            go.layer = layer;
            var t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = pos;
            t.localEulerAngles = euler;
            t.localScale = scale;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;   // the hidden vanilla body still casts the shadow
            r.lightProbeUsage = LightProbeUsage.Off;
            b.Renderers.Add(r);
            return r;
        }

        private static Material Tinted(Built b, Material src, Color c, string what)
        {
            var m = Interop.Alive(src) ? new Material(src) : new Material(Shader.Find("Unlit/Color"));
            m.name = "CustomEnemies placeholder " + what;
            foreach (var tex in new[] { "_MainTex", "_BaseMap" })
                if (m.HasProperty(tex)) m.SetTexture(tex, null);
            foreach (var col in new[] { "_Color", "_BaseColor", "_MainColor" })
                if (m.HasProperty(col)) m.SetColor(col, c);
            b.Owned.Add(m);
            return m;
        }

        /// <summary>The enemy's own eye material if it carries one, else a bright tint.</summary>
        private static Material EyeMaterial(Built b, Transform enemy, Material bodySrc)
        {
            foreach (var r in enemy.GetComponentsInChildren<Renderer>(true))
            {
                if (!Interop.Alive(r)) continue;
                var n = Interop.Name(r.gameObject);
                if (n.IndexOf("eye", StringComparison.OrdinalIgnoreCase) < 0) continue;
                var m = r.sharedMaterial;
                if (!Interop.Alive(m)) continue;
                b.EyesFrom = $"`{Interop.Name(m)}` ({Interop.Name(m.shader)}) from the enemy";
                return m;   // shared, not copied: it is the game's, and we never change it
            }
            b.EyesFrom = "a red tint (the enemy has no eye material)";
            var t = Tinted(b, bodySrc, Eye, "eyes");
            foreach (var k in new[] { "_EmissionColor", "_Emission" })
                if (t.HasProperty(k)) t.SetColor(k, Eye * 2f);
            return t;
        }
    }
}
