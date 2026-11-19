using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;
using AI = Il2CppSauron.AI;

namespace CustomEnemies.Skins
{
    /// <summary>
    /// One enemy's skin: the model drawn in its place (<see cref="ISkinModel"/>: a bundle skin or
    /// the placeholder) and the vanilla renderers hidden under it.
    ///
    /// The model is a scene root, never a child of the enemy, because the enemy's own code walks
    /// its hierarchy for renderers (<c>DissolveSettings.allRenderers</c>, <c>MaterialHandler</c>,
    /// <c>CharacterOptimizer</c>, the pause list) and must never find ours. It follows the
    /// vanilla root in LateUpdate, after the game has moved and animated the enemy.
    ///
    /// Vanilla mesh renderers are hidden with <c>ShadowsOnly</c>, re-applied every frame to that
    /// one property only, exactly as the CustomAvatars swap hides the player body: a renderer that
    /// is disabled or forced off leaves culling, and its owner stops being animated. Particle,
    /// trail and line renderers (Death's aura and weapon trail) carry no animation and are simply
    /// disabled while skinned. While the game dissolves the enemy (spawn, despawn), the skin's
    /// game-shader materials take the same dissolve shader and values, so it fades the same way.
    /// </summary>
    public sealed class EnemySkin
    {
        private struct Hidden { public Renderer R; public ShadowCastingMode Was; public bool Ours; }
        private struct Effect { public Renderer R; public bool Was; public bool Ours; }

        private readonly AI _ai;
        private readonly Transform _aiT;
        private readonly List<Hidden> _hidden = new List<Hidden>();
        private readonly List<Effect> _effects = new List<Effect>();
        private Renderer _body;
        private ISkinModel _model;
        private bool _shown;
        private int _rehides, _effectRehides, _dissolves;
        private int _shaderSwaps;
        private bool _reportedShader;
        private bool _dissolving;

        // Measurements
        private readonly Stopwatch _watch = new Stopwatch();
        private double _costMs;
        private int _costFrames, _visibleFrames, _leakFrames;
        private float _sampleIn = 2f, _alive;

        public string Who { get; }
        public bool EnemyAlive => Interop.Alive(_ai) && Interop.Alive(_aiT);

        private EnemySkin(AI ai)
        {
            _ai = ai;
            _aiT = ai.transform;
            Who = SkinManager.CleanName(Interop.Name(ai));
        }

        /// <summary>A bundle skin if one replaces this enemy, else the placeholder if listed (or forced).</summary>
        public static EnemySkin Create(AI ai, SkinLibrary.Skin skin)
        {
            var s = new EnemySkin(ai);
            s.CollectVanilla();
            if (!Interop.Alive(s._body))
            {
                Core.Log.Warning($"`{s.Who}` has no skinned or mesh renderer; no skin.");
                return null;
            }
            Transform head = null;
            try { var r = ai.references; if (r != null && Interop.Alive(r.head)) head = r.head; } catch { }

            if (skin != null)
            {
                Animator anim = null;
                try { anim = ai.GetComponentInChildren<Animator>(true); } catch { }
                s._model = BundleModel.Build(skin, s._aiT, s._body, anim, out var err);
                if (s._model == null) Core.Log.Warning($"Skin `{skin.Name}` on `{s.Who}` failed ({err}); using the placeholder.");
            }
            if (s._model == null) s._model = Placeholder.Build(s.Who, s._body, s._aiT, head);
            if (s._model == null) return null;
            s._shown = s._model.Root.activeSelf;
            s.Reset();
            Report.Line($"- skin on `{s.Who}`: {s.Describe()}");
            return s;
        }

        public string Describe() =>
            $"{_model.Describe()}; {_hidden.Count} vanilla renderer(s) {(ModConfig.HideVanilla.Value ? "hidden (ShadowsOnly)" : "left shown (HideVanilla off)")}, " +
            $"{_effects.Count} vanilla effect renderer(s) {(ModConfig.HideVanillaEffects.Value ? "off" : "left on")}";

        /// <summary>The enemy's mesh renderers (to hide) and effect renderers (to switch off).</summary>
        private void CollectVanilla()
        {
            Restore();
            _hidden.Clear();
            _effects.Clear();
            _body = null;
            var bestVerts = -1;
            foreach (var r in _ai.GetComponentsInChildren<Renderer>(true))
            {
                if (!Interop.Alive(r)) continue;
                var smr = r.TryCast<SkinnedMeshRenderer>();
                var isMesh = smr != null || r.TryCast<MeshRenderer>() != null;
                if (!isMesh)
                {
                    _effects.Add(new Effect { R = r, Was = r.enabled });
                    continue;
                }
                _hidden.Add(new Hidden { R = r, Was = r.shadowCastingMode });
                if (smr == null) continue;
                var verts = 0;
                try { verts = Interop.Alive(smr.sharedMesh) ? smr.sharedMesh.vertexCount : 0; } catch { }
                if (verts > bestVerts) { bestVerts = verts; _body = r; }
            }
            if (_body == null && _hidden.Count > 0) _body = _hidden[0].R;
        }

        /// <summary>A new spawn from the pool: re-read the renderers and start the model over.</summary>
        public void Reset()
        {
            CollectVanilla();
            _rehides = 0;
            _model.Reset();
            ApplyHide();
            Follow();
        }

        /// <summary>Per frame. False when the enemy object is gone and the skin should go too.</summary>
        public bool LateTick()
        {
            if (!EnemyAlive || _model == null || !Interop.Alive(_model.Root)) return false;
            _watch.Restart();
            var dt = Time.deltaTime;
            Follow();
            _model.Tick(dt);
            var show = ShouldShow() && _model.Ready;
            if (show != _shown)
            {
                _shown = show;
                _model.Root.SetActive(show);
            }
            ApplyHide();
            if (show) MirrorDissolve();
            _watch.Stop();
            Measure(dt, show);
            return true;
        }

        /// <summary>Shown while the enemy is in the scene and the game itself draws it (a paused enemy has its renderers disabled).</summary>
        private bool ShouldShow()
        {
            try
            {
                if (!_ai.gameObject.activeInHierarchy) return false;
                if (Interop.Alive(_body) && (!_body.enabled || !_body.gameObject.activeInHierarchy)) return false;
                return true;
            }
            catch { return false; }
        }

        private void ApplyHide()
        {
            var hide = ModConfig.HideVanilla.Value;
            for (var i = 0; i < _hidden.Count; i++)
            {
                var h = _hidden[i];
                if (!Interop.Alive(h.R)) continue;
                var now = h.R.shadowCastingMode;
                if (hide)
                {
                    if (now == ShadowCastingMode.ShadowsOnly) continue;
                    if (h.Ours) _rehides++;   // the game put it back; take it again
                    h.Was = now;
                    h.R.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                    h.Ours = true;
                }
                else if (h.Ours)
                {
                    if (now == ShadowCastingMode.ShadowsOnly) h.R.shadowCastingMode = h.Was;
                    h.Ours = false;
                }
                _hidden[i] = h;
            }
            var off = ModConfig.HideVanillaEffects.Value && _shown;
            for (var i = 0; i < _effects.Count; i++)
            {
                var e = _effects[i];
                if (!Interop.Alive(e.R)) continue;
                if (off)
                {
                    if (!e.R.enabled) { _effects[i] = e; continue; }
                    if (e.Ours) _effectRehides++;
                    e.Was = true;
                    e.R.enabled = false;
                    e.Ours = true;
                }
                else if (e.Ours)
                {
                    e.R.enabled = e.Was;
                    e.Ours = false;
                }
                _effects[i] = e;
            }
        }

        /// <summary>
        /// While the vanilla body wears a dissolve shader, the skin's game-shader materials wear it
        /// too and copy its numbers and textures (other than the base colour), every frame.
        /// </summary>
        private void MirrorDissolve()
        {
            var mats = _model.GameShaderMaterials;
            if (mats.Count == 0 || !Interop.Alive(_body)) return;
            Material v;
            try { v = _body.sharedMaterial; } catch { return; }
            if (!Interop.Alive(v) || !Interop.Alive(v.shader)) return;
            var dissolving = v.shader.name.IndexOf("Dissolve", StringComparison.OrdinalIgnoreCase) >= 0;
            // The skin wears whatever shader the vanilla body wears now. Its materials were copied
            // from the body at spawn, when the body is mid fade-in on the dissolve shader, fully
            // dissolved; keeping that copy left the skin invisible (2026-11-18, first wolf test).
            foreach (var m in mats)
                if (Interop.Alive(m) && m.shader != v.shader) { m.shader = v.shader; _shaderSwaps++; }
            if (!dissolving && !_reportedShader)
            {
                _reportedShader = true;
                Report.Line($"- `{Who}` skin materials on `{v.shader.name}` (vanilla body's material `{v.name}`)");
            }
            if (dissolving != _dissolving)
            {
                _dissolving = dissolving;
                if (dissolving) _dissolves++;
                if (ModConfig.VerboseLogging.Value) Report.Line($"- `{Who}` {(dissolving ? "dissolving (" + v.shader.name + "); the skin follows" : "done dissolving")}");
            }
            if (!dissolving) return;
            try
            {
                var sh = v.shader;
                var n = sh.GetPropertyCount();
                for (var i = 0; i < n; i++)
                {
                    var name = sh.GetPropertyName(i);
                    if (name == "_MainTex" || name == "_Color" || name == "_BaseColor") continue;
                    var type = sh.GetPropertyType(i);
                    // Only what the dissolve itself needs: the skin's own texture slots and its
                    // switched-off emission stay as the skin set them.
                    if (type == ShaderPropertyType.Texture && !DissolveTex(name)) continue;
                    if (name.IndexOf("Emiss", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    foreach (var m in mats)
                    {
                        if (!Interop.Alive(m) || !m.HasProperty(name)) continue;
                        switch (type)
                        {
                            case ShaderPropertyType.Float:
                            case ShaderPropertyType.Range: m.SetFloat(name, v.GetFloat(name)); break;
                            case ShaderPropertyType.Vector: m.SetVector(name, v.GetVector(name)); break;
                            case ShaderPropertyType.Color: m.SetColor(name, v.GetColor(name)); break;
                            case ShaderPropertyType.Texture: m.SetTexture(name, v.GetTexture(name)); break;
                        }
                    }
                }
            }
            catch (Exception e) { SkinManager.LogOnce("dissolve mirror", e); }
        }

        private static bool DissolveTex(string n) =>
            n.IndexOf("Dissolve", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Noise", StringComparison.OrdinalIgnoreCase) >= 0 ||
            n.IndexOf("Burn", StringComparison.OrdinalIgnoreCase) >= 0;

        private void Follow()
        {
            var root = _model.Root.transform;
            root.SetPositionAndRotation(_aiT.position, _aiT.rotation);
            if (_model is Placeholder.Built) root.localScale = _aiT.lossyScale;
        }

        /// <summary>Frame cost, leaks (a vanilla mesh renderer drawn while skinned) and the model's own numbers, every 2 s for 20 s, then every 10 s.</summary>
        private void Measure(float dt, bool show)
        {
            _costMs += _watch.Elapsed.TotalMilliseconds;
            _costFrames++;
            _alive += dt;
            if (show)
            {
                _visibleFrames++;
                if (ModConfig.HideVanilla.Value)
                    foreach (var h in _hidden)
                        if (Interop.Alive(h.R) && h.R.enabled && h.R.gameObject.activeInHierarchy && h.R.shadowCastingMode != ShadowCastingMode.ShadowsOnly) { _leakFrames++; break; }
            }
            _sampleIn -= dt;
            if (_sampleIn > 0f) return;
            _sampleIn = _alive < 20f ? 2f : 10f;
            var m = _model.Sample();
            Report.Line($"  - `{Who}` {_alive:0}s: {(show ? "shown" : "hidden")}, cost {_costMs / Math.Max(1, _costFrames):0.000} ms/frame, " +
                        $"vanilla leaked on {_leakFrames}/{_visibleFrames} shown frame(s), re-hidden {_rehides}+{_effectRehides}, dissolves {_dissolves}, shader swaps {_shaderSwaps}" +
                        (m == null ? "" : "; " + m));
            _costMs = 0; _costFrames = 0;
        }

        /// <summary>Hand every renderer property we took back to the game.</summary>
        private void Restore()
        {
            for (var i = 0; i < _hidden.Count; i++)
            {
                var h = _hidden[i];
                if (!h.Ours || !Interop.Alive(h.R)) continue;
                try { if (h.R.shadowCastingMode == ShadowCastingMode.ShadowsOnly) h.R.shadowCastingMode = h.Was; } catch { }
                h.Ours = false;
                _hidden[i] = h;
            }
            for (var i = 0; i < _effects.Count; i++)
            {
                var e = _effects[i];
                if (!e.Ours || !Interop.Alive(e.R)) continue;
                try { e.R.enabled = e.Was; } catch { }
                e.Ours = false;
                _effects[i] = e;
            }
        }

        public void Destroy()
        {
            Restore();
            _model?.Destroy();
            _model = null;
            Report.Line($"- skin off `{Who}` after {_alive:0} s: re-hidden {_rehides}+{_effectRehides}, vanilla leaked on {_leakFrames}/{_visibleFrames} shown frame(s), dissolves {_dissolves}");
        }
    }
}
