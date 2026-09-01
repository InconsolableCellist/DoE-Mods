using System;
using System.Collections.Generic;
using UnityEngine;
using Interop = CustomAvatars.Recon.Interop;

namespace CustomAvatars.Fbt
{
    /// <summary>
    /// The pucks you see while calibrating: one marker per detected tracker, floating exactly
    /// where SteamVR says the puck is. Bend down and touch one — if the marker isn't on the
    /// physical tracker, the space conversion is wrong and calibrating would bake that in.
    ///
    /// Every detected tracker gets a marker, not just the three we'll bind: a stray fourth
    /// puck someone forgot to turn off is exactly the kind of thing you want to SEE before it
    /// gets picked as your hip.
    ///
    /// Colors: white until a role is known, then cyan hip, port-red left foot, starboard-green
    /// right foot. Each marker carries RGB axis bars so a rolled tracker is visibly rolled.
    /// </summary>
    public class TrackerVisuals
    {
        private readonly Dictionary<string, GameObject> _pucks = new Dictionary<string, GameObject>();
        private Material _material;   // template; each puck gets tinted instances
        private bool _shown;

        public bool Shown => _shown;

        public void Show() => _shown = true;

        public void Hide()
        {
            _shown = false;
            foreach (var kv in _pucks)
                if (Interop.Alive(kv.Value)) kv.Value.SetActive(false);
        }

        /// <summary>Per-frame while shown: move every marker onto its tracker.</summary>
        public void Update(TrackerReader reader, IReadOnlyDictionary<string, TrackerRole> roleOf, int layer)
        {
            if (!_shown) return;
            try
            {
                foreach (var t in reader.Trackers)
                {
                    if (!t.PoseValid || string.IsNullOrEmpty(t.Serial)) continue;

                    if (!_pucks.TryGetValue(t.Serial, out var puck) || !Interop.Alive(puck))
                    {
                        puck = BuildPuck(t.Serial, layer);
                        if (puck == null) return;   // shader missing; don't retry every frame
                        _pucks[t.Serial] = puck;
                    }

                    if (!puck.activeSelf) puck.SetActive(true);
                    puck.transform.SetPositionAndRotation(t.WorldPos, t.WorldRot);

                    TrackerRole? role = null;
                    if (roleOf != null && roleOf.TryGetValue(t.Serial, out var r)) role = r;
                    Tint(puck, role);
                }
            }
            catch (Exception e)
            {
                Core.Log.Warning($"FBT: puck rendering failed, hiding: {e.Message}");
                Hide();
            }
        }

        public void DestroyAll()
        {
            foreach (var kv in _pucks)
                if (Interop.Alive(kv.Value)) UnityEngine.Object.Destroy(kv.Value);
            _pucks.Clear();
            _shown = false;
        }

        // ---- construction ---------------------------------------------------------------

        private GameObject BuildPuck(string serial, int layer)
        {
            var shader = Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
            if (shader == null)
            {
                Core.Log.Warning("FBT: no unlit shader available for tracker pucks.");
                return null;
            }
            _material ??= new Material(shader);

            var root = new GameObject($"DFM_TrackerPuck_{serial}");
            UnityEngine.Object.DontDestroyOnLoad(root);
            root.layer = layer;

            AddPart(root, PrimitiveType.Sphere, Vector3.zero, new Vector3(0.06f, 0.06f, 0.06f), Color.white, "body");
            // Axis bars poke out along the tracker's own axes: X red, Y green, Z blue.
            AddPart(root, PrimitiveType.Cube, new Vector3(0.06f, 0, 0), new Vector3(0.12f, 0.01f, 0.01f), Color.red, "x");
            AddPart(root, PrimitiveType.Cube, new Vector3(0, 0.06f, 0), new Vector3(0.01f, 0.12f, 0.01f), Color.green, "y");
            AddPart(root, PrimitiveType.Cube, new Vector3(0, 0, 0.06f), new Vector3(0.01f, 0.01f, 0.12f), Color.blue, "z");
            return root;
        }

        private void AddPart(GameObject root, PrimitiveType type, Vector3 localPos, Vector3 scale, Color color, string name)
        {
            var part = GameObject.CreatePrimitive(type);
            part.name = name;
            part.layer = root.layer;
            part.transform.SetParent(root.transform, false);
            part.transform.localPosition = localPos;
            part.transform.localScale = scale;

            // A puck is a picture, not a thing — primitives arrive with colliders that would
            // otherwise bump the game's physics.
            var collider = part.GetComponent<Collider>();
            if (Interop.Alive(collider)) UnityEngine.Object.Destroy(collider);

            var renderer = part.GetComponent<Renderer>();
            if (Interop.Alive(renderer))
            {
                renderer.material = new Material(_material) { color = color };
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }

        private static void Tint(GameObject puck, TrackerRole? role)
        {
            var color = role switch
            {
                TrackerRole.Hip => Color.cyan,
                TrackerRole.LeftFoot => new Color(1f, 0.25f, 0.25f),    // port
                TrackerRole.RightFoot => new Color(0.25f, 1f, 0.25f),   // starboard
                _ => Color.white,
            };
            try
            {
                var body = puck.transform.Find("body");
                if (!Interop.Alive(body)) return;
                var renderer = body.GetComponent<Renderer>();
                if (Interop.Alive(renderer) && renderer.material.color != color) renderer.material.color = color;
            }
            catch { }
        }
    }
}
