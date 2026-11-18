using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using AI = Il2CppSauron.AI;

namespace CustomEnemies.Skins
{
    /// <summary>
    /// One enemy's skin: the model drawn in its place and the vanilla renderers hidden under it.
    ///
    /// The model is a scene root, never a child of the enemy, because the enemy's own code walks
    /// its hierarchy for renderers (<c>DissolveSettings.allRenderers</c>, <c>MaterialHandler</c>,
    /// <c>CharacterOptimizer</c>, the pause list) and must never find ours. It follows the
    /// vanilla root in LateUpdate, after the game has moved and animated the enemy.
    ///
    /// The vanilla renderers are hidden with <c>ShadowsOnly</c>, re-applied every frame to that
    /// one property only, exactly as the CustomAvatars swap hides the player body: a renderer that
    /// is disabled or forced off leaves culling, and its owner stops being animated. The enemy
    /// still casts its vanilla shadow; the skin casts none, so there is one shadow, not two.
    /// </summary>
    public sealed class EnemySkin
    {
        private struct Hidden { public Renderer R; public ShadowCastingMode Was; public bool Ours; }

        private readonly AI _ai;
        private readonly Transform _aiT;
        private readonly List<Hidden> _hidden = new List<Hidden>();
        private Renderer _body;
        private Transform _headBone;
        private Placeholder.Built _model;
        private bool _shown;
        private int _rehides;

        public string Who { get; }
        public bool EnemyAlive => Interop.Alive(_ai) && Interop.Alive(_aiT);

        private EnemySkin(AI ai)
        {
            _ai = ai;
            _aiT = ai.transform;
            Who = SkinManager.CleanName(Interop.Name(ai));
        }

        public static EnemySkin Create(AI ai)
        {
            var s = new EnemySkin(ai);
            s.CollectVanilla();
            if (!Interop.Alive(s._body))
            {
                Core.Log.Warning($"`{s.Who}` has no skinned or mesh renderer; no skin.");
                return null;
            }
            try { var r = ai.references; if (r != null && Interop.Alive(r.head)) s._headBone = r.head; } catch { }
            s._model = Placeholder.Build(s.Who, s._body, s._aiT);
            if (s._model == null) return null;
            s._shown = true;
            s.Reset();
            return s;
        }

        public string Describe() =>
            $"placeholder, {_model.Renderers.Count} part(s) on layer {LayerMask.LayerToName(_model.Root.layer)}, " +
            $"{_hidden.Count} vanilla renderer(s) {(ModConfig.HideVanilla.Value ? "hidden (ShadowsOnly)" : "left shown (HideVanilla off)")}, " +
            $"head {(Interop.Alive(_headBone) ? "follows `" + _headBone.name + "`" : "rigid (no head bone)")}, " +
            $"eyes {_model.EyesFrom}";

        /// <summary>The enemy's mesh renderers (skinned and static). Particles, trails and lines stay vanilla.</summary>
        private void CollectVanilla()
        {
            Restore();
            _hidden.Clear();
            _body = null;
            var bestVerts = -1;
            foreach (var r in _ai.GetComponentsInChildren<Renderer>(true))
            {
                if (!Interop.Alive(r)) continue;
                var smr = r.TryCast<SkinnedMeshRenderer>();
                var isMesh = smr != null || r.TryCast<MeshRenderer>() != null;
                if (!isMesh) continue;
                _hidden.Add(new Hidden { R = r, Was = r.shadowCastingMode });
                if (smr == null) continue;
                var verts = 0;
                try { verts = Interop.Alive(smr.sharedMesh) ? smr.sharedMesh.vertexCount : 0; } catch { }
                if (verts > bestVerts) { bestVerts = verts; _body = r; }
            }
            if (_body == null && _hidden.Count > 0) _body = _hidden[0].R;
        }

        /// <summary>A new spawn from the pool: re-read the renderers (the dissolve may have swapped materials) and start over.</summary>
        public void Reset()
        {
            CollectVanilla();
            _rehides = 0;
            ApplyHide();
            Follow();
        }

        /// <summary>Per frame. False when the enemy object is gone and the skin should go too.</summary>
        public bool LateTick()
        {
            if (!EnemyAlive || _model == null || !Interop.Alive(_model.Root)) return false;
            var show = ShouldShow();
            if (show != _shown)
            {
                _shown = show;
                _model.Root.SetActive(show);
            }
            ApplyHide();
            if (show) Follow();
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
        }

        private void Follow()
        {
            var root = _model.Root.transform;
            root.SetPositionAndRotation(_aiT.position, _aiT.rotation);
            root.localScale = _aiT.lossyScale;
            if (_model.Head == null) return;
            if (ModConfig.HeadFollowsBone.Value && Interop.Alive(_headBone))
                _model.Head.position = _headBone.position;
            else
                _model.Head.localPosition = _model.HeadRest;
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
        }

        public void Destroy()
        {
            Restore();
            _model?.Destroy();
            _model = null;
            if (_rehides > 0 && ModConfig.VerboseLogging.Value)
                Core.Log.Msg($"  `{Who}`: the game put a hidden renderer back {_rehides} time(s) while skinned.");
        }
    }
}
