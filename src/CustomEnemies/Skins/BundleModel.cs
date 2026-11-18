using System;
using System.Collections.Generic;
using CustomAvatars.Avatars;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace CustomEnemies.Skins
{
    /// <summary>
    /// A skin from an avatar-exporter bundle, posed from the enemy's own humanoid Animator by the
    /// shared <see cref="PoseRetargeter"/>, exactly as CustomAvatars poses an avatar from the
    /// player rig. On top of the copy: hanging legs (the glide design), weapons held by a skin
    /// bone, and eye halos.
    ///
    /// The pose copy starts once the enemy has settled (out of its spawn intro), because the
    /// retargeter captures both rigs' rest at that moment; until then the skin stays hidden.
    /// </summary>
    public sealed class BundleModel : ISkinModel
    {
        private readonly SkinLibrary.Skin _skin;
        private readonly Transform _enemy;
        private readonly Animator _source;
        private AvatarBundle _bundle;
        private GameObject _root, _instance;
        private readonly List<Renderer> _renderers = new List<Renderer>();
        private readonly List<Material> _owned = new List<Material>();
        private readonly List<Material> _gameShader = new List<Material>();
        private PoseRetargeter _retarget;
        private float _settle;
        private bool _ready;
        private string _captureNote = "waiting for the enemy to settle";

        // Legs (glide) and weapons
        private Transform _hips, _thighL, _thighR, _kneeL, _kneeR;
        private Quaternion _thighLRest, _thighRRest, _kneeLRest, _kneeRRest;
        private Vector3 _lastEnemyPos;
        private Vector2 _trail;   // x: back/forward swing, y: side swing, degrees, smoothed
        private readonly List<(Transform bone, Transform to, Quaternion rot, Vector3 off)> _weapons = new List<(Transform, Transform, Quaternion, Vector3)>();
        private Halos _halos;

        // Measurements
        private Transform _srcHead, _dstHead, _srcHandR, _dstHandR;
        private float _headOffSum, _headOffMax, _handOffSum;
        private int _samples;
        private string _scaleNote = "";

        public GameObject Root => _root;
        public IReadOnlyList<Renderer> Renderers => _renderers;
        public IReadOnlyList<Material> GameShaderMaterials => _gameShader;
        public bool Ready => _ready;

        private BundleModel(SkinLibrary.Skin skin, Transform enemy, Animator source)
        {
            _skin = skin; _enemy = enemy; _source = source;
        }

        public static BundleModel Build(SkinLibrary.Skin skin, Transform enemy, Renderer body, Animator source, out string error)
        {
            error = null;
            if (!Interop.Alive(source)) { error = "the enemy has no Animator"; return null; }
            bool human = false;
            try { human = source.isHuman; } catch { }
            if (!human) { error = "the enemy's Animator is not humanoid; bundle skins need one"; return null; }

            var m = new BundleModel(skin, enemy, source);
            try
            {
                m._bundle = AvatarBundle.Acquire(skin.Manifest, out var berr);
                if (m._bundle == null) { error = "bundle: " + berr; return null; }
                m._root = new GameObject($"CustomEnemies skin ({skin.Name})");
                m._instance = Object.Instantiate(m._bundle.Prefab);
                m._instance.name = skin.Name;
                m._instance.transform.SetParent(m._root.transform, false);
                m._instance.transform.localRotation = Quaternion.Euler(0f, skin.Def.yaw, 0f);
                m._instance.transform.localPosition = Vec(skin.Def.offset, Vector3.zero);
                m.Strip(body.gameObject.layer);
                m.Materials(body);
                m.FindBones();
                m._halos = Halos.Build(m._instance.transform, skin.Def, enemy, m._owned);
                m._root.SetActive(false);
                return m;
            }
            catch (Exception e)
            {
                error = $"{e.GetType().Name}: {e.Message}";
                m.Destroy();
                return null;
            }
        }

        /// <summary>Nothing of the skin may touch physics or animate itself: the enemy's Animator is the only one that drives it.</summary>
        private void Strip(int layer)
        {
            foreach (var c in _instance.GetComponentsInChildren<Collider>(true)) if (Interop.Alive(c)) Object.DestroyImmediate(c);
            foreach (var rb in _instance.GetComponentsInChildren<Rigidbody>(true)) if (Interop.Alive(rb)) Object.DestroyImmediate(rb);
            foreach (var a in _instance.GetComponentsInChildren<Animator>(true)) if (Interop.Alive(a)) a.enabled = false;
            foreach (var t in _instance.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
            foreach (var r in _instance.GetComponentsInChildren<Renderer>(true))
            {
                if (!Interop.Alive(r)) continue;
                r.shadowCastingMode = ShadowCastingMode.Off;   // the hidden vanilla body still casts the one shadow
                var smr = r.TryCast<SkinnedMeshRenderer>();
                if (smr != null) smr.updateWhenOffscreen = true;
                _renderers.Add(r);
            }
        }

        /// <summary>
        /// The skin's materials as copies of the enemy's own body material carrying the skin's
        /// base colour texture, so it is lit like the enemy (Death: PotoVR/Characters_Normal from
        /// the Bakery volume). Materials named in <c>keepMaterials</c> stay as exported.
        /// </summary>
        private void Materials(Renderer body)
        {
            var template = body.sharedMaterial;
            var keep = new HashSet<string>(_skin.Def.keepMaterials ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            var made = new Dictionary<IntPtr, Material>();
            foreach (var r in _renderers)
            {
                var mats = r.sharedMaterials;
                if (mats == null) continue;
                var changed = false;
                for (var i = 0; i < mats.Length; i++)
                {
                    var src = mats[i];
                    if (!Interop.Alive(src)) continue;
                    var name = src.name.Replace(" (Instance)", "");
                    if (keep.Contains(name) || !_skin.Def.gameShader || !Interop.Alive(template)) continue;
                    if (!made.TryGetValue(src.Pointer, out var copy))
                    {
                        copy = new Material(template) { name = "CustomEnemies " + name };
                        Texture tex = null;
                        try { tex = src.mainTexture; } catch { }
                        if (!Interop.Alive(tex))
                            foreach (var p in new[] { "_MainTex", "_BaseMap", "_BaseColorMap" })
                                if (src.HasProperty(p)) { tex = src.GetTexture(p); if (Interop.Alive(tex)) break; }
                        if (copy.HasProperty("_MainTex")) copy.SetTexture("_MainTex", tex);
                        foreach (var c in new[] { "_Color", "_BaseColor", "_MainColor" })
                            if (copy.HasProperty(c)) copy.SetColor(c, Color.white);
                        made[src.Pointer] = copy;
                        _owned.Add(copy);
                        _gameShader.Add(copy);
                    }
                    mats[i] = copy;
                    changed = true;
                }
                if (changed) r.sharedMaterials = mats;
            }
        }

        private Transform Bone(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var map = _skin.Manifest.rig?.humanoidBones;
            if (map != null && map.TryGetValue(name, out var path) && !string.IsNullOrEmpty(path))
            {
                var t = _instance.transform.Find(path);
                if (Interop.Alive(t)) return t;
            }
            foreach (var t in _instance.GetComponentsInChildren<Transform>(true))
                if (string.Equals(t.name, name, StringComparison.OrdinalIgnoreCase)) return t;
            return null;
        }

        private void FindBones()
        {
            _hips = Bone("Hips");
            _thighL = Bone("LeftUpperLeg"); _thighR = Bone("RightUpperLeg");
            _kneeL = Bone("LeftLowerLeg"); _kneeR = Bone("RightLowerLeg");
            if (Interop.Alive(_thighL)) _thighLRest = _thighL.localRotation;
            if (Interop.Alive(_thighR)) _thighRRest = _thighR.localRotation;
            if (Interop.Alive(_kneeL)) _kneeLRest = _kneeL.localRotation;
            if (Interop.Alive(_kneeR)) _kneeRRest = _kneeR.localRotation;
            _dstHead = Bone("Head"); _dstHandR = Bone("RightHand");
            try { _srcHead = _source.GetBoneTransform(HumanBodyBones.Head); _srcHandR = _source.GetBoneTransform(HumanBodyBones.RightHand); } catch { }
            foreach (var w in _skin.Def.weapons ?? new List<SkinLibrary.Weapon>())
            {
                var b = Bone(w.bone); var to = Bone(w.to);
                if (!Interop.Alive(b) || !Interop.Alive(to)) { Core.Log.Warning($"Skin `{_skin.Name}`: weapon bone `{w.bone}` or holder `{w.to}` not found."); continue; }
                var e = Vec(w.euler, Vector3.zero);
                _weapons.Add((b, to, Quaternion.Euler(e), Vec(w.offset, Vector3.zero)));
            }
        }

        public void Reset()
        {
            _ready = false;
            _settle = 0f;
            _retarget = null;
            _captureNote = "waiting for the enemy to settle";
            if (Interop.Alive(_instance))
            {
                // Back to the bind pose the capture expects: the retargeter only restores bones it paired.
                if (Interop.Alive(_thighL)) _thighL.localRotation = _thighLRest;
                if (Interop.Alive(_thighR)) _thighR.localRotation = _thighRRest;
                if (Interop.Alive(_kneeL)) _kneeL.localRotation = _kneeLRest;
                if (Interop.Alive(_kneeR)) _kneeR.localRotation = _kneeRRest;
            }
            _trail = Vector2.zero;
        }

        public void Tick(float dt)
        {
            if (!Interop.Alive(_instance) || !Interop.Alive(_source)) return;
            if (!_ready) { TrySettle(dt); if (!_ready) return; }

            _retarget.Apply();
            HangLegs(dt);
            foreach (var (bone, to, rot, off) in _weapons)
            {
                if (!Interop.Alive(bone) || !Interop.Alive(to)) continue;
                bone.SetPositionAndRotation(to.TransformPoint(off), to.rotation * rot);
            }
            _halos?.Tick();
        }

        /// <summary>Wait out the spawn intro, fit the scale, then capture both rigs.</summary>
        private void TrySettle(float dt)
        {
            _settle += dt;
            var intro = false;
            try { intro = _source.GetCurrentAnimatorStateInfo(0).IsName("Spawn Intro"); } catch { }
            if ((intro && _settle < 6f) || _settle < 0.5f) return;
            FitScale();
            _retarget = _retarget ?? new PoseRetargeter();
            _captureNote = _retarget.Build(_source, _instance, _skin.Manifest);
            _ready = _retarget.LinkCount > 0;
            _lastEnemyPos = _enemy.position;
            Core.Log.Msg($"Skin `{_skin.Name}` on `{SkinManager.CleanName(Interop.Name(_enemy))}`: {_captureNote}; {_scaleNote}.");
            Report.Line($"- **{_skin.Name}** pose copy: {_captureNote}; {_scaleNote}; paired: {_retarget.Describe()}");
        }

        /// <summary>The skin's head at the enemy's head height (rest pose, measured from each root), times the sidecar scale.</summary>
        private void FitScale()
        {
            var root = _root.transform;
            root.localScale = Vector3.one;
            if (!Interop.Alive(_srcHead) || !Interop.Alive(_dstHead)) { _scaleNote = "no head bone on one side, scale 1"; root.localScale = Vector3.one * _skin.Def.scale; return; }
            var src = _srcHead.position.y - _enemy.position.y;
            var dst = _dstHead.position.y - root.position.y;
            var k = dst > 0.05f && src > 0.05f ? src / dst : 1f;
            k *= _skin.Def.scale <= 0f ? 1f : _skin.Def.scale;
            root.localScale = Vector3.one * k;
            _scaleNote = $"scaled x{k:0.###} (enemy head {src:0.00} m, skin head {dst:0.00} m at x1)";
        }

        private void HangLegs(float dt)
        {
            var legs = _skin.Def.legs;
            if (legs == null || !Interop.Alive(_hips)) return;
            var root = _root.transform;
            // Trail behind the enemy's travel, in the root's frame, smoothed (about 0.25 s).
            var v = dt > 1e-4f ? root.InverseTransformDirection(_enemy.position - _lastEnemyPos) / dt : Vector3.zero;
            _lastEnemyPos = _enemy.position;
            var want = new Vector2(Mathf.Clamp(v.z * legs.lag, -35f, 35f), Mathf.Clamp(-v.x * legs.lag, -25f, 25f));
            _trail = Vector2.Lerp(_trail, want, 1f - Mathf.Exp(-dt / 0.25f));
            var t = Time.time * Mathf.PI * 2f / Mathf.Max(0.2f, legs.swayPeriod);
            Leg(_thighL, _thighLRest, _kneeL, _kneeLRest, legs, Mathf.Sin(t) * legs.sway, root);
            Leg(_thighR, _thighRRest, _kneeR, _kneeRRest, legs, Mathf.Sin(t + Mathf.PI) * legs.sway, root);
        }

        private void Leg(Transform thigh, Quaternion thighRest, Transform knee, Quaternion kneeRest, SkinLibrary.Legs legs, float sway, Transform root)
        {
            if (!Interop.Alive(thigh)) return;
            // From the bind pose (legs straight down), swung forward about the root's right axis:
            // a negative angle about +X carries a downward bone forward in Unity's frame.
            var pitch = -legs.thighPitch + _trail.x + sway;
            thigh.rotation = Quaternion.AngleAxis(_trail.y, root.forward) * Quaternion.AngleAxis(pitch, root.right) * (thigh.parent.rotation * thighRest);
            if (!Interop.Alive(knee)) return;
            knee.rotation = Quaternion.AngleAxis(legs.kneeBend, root.right) * (knee.parent.rotation * kneeRest) ;
        }

        public string Sample()
        {
            if (!_ready || !Interop.Alive(_srcHead) || !Interop.Alive(_dstHead)) return null;
            var head = Vector3.Distance(_srcHead.position, _dstHead.position);
            var hand = Interop.Alive(_srcHandR) && Interop.Alive(_dstHandR) ? Vector3.Distance(_srcHandR.position, _dstHandR.position) : -1f;
            _samples++; _headOffSum += head; _headOffMax = Mathf.Max(_headOffMax, head); if (hand >= 0) _handOffSum += hand;
            var over = _retarget.OverwrittenDegrees;
            return $"head {head:0.00} m from the enemy's (mean {_headOffSum / _samples:0.00}, max {_headOffMax:0.00}), " +
                   $"right hand {(hand < 0 ? "?" : hand.ToString("0.00"))} m, " +
                   $"pose overwritten by something else {(over < 0 ? "n/a" : over.ToString("0.0") + "°")}";
        }

        public string Describe() =>
            $"bundle `{_skin.Name}` ({_renderers.Count} renderer(s), {_gameShader.Count} material(s) on the enemy's shader, " +
            $"{_weapons.Count} weapon(s), {_halos?.Count ?? 0} eye halo(s), legs {(_skin.Def.legs == null ? "copied" : "hanging")}), {_captureNote}";

        public void Destroy()
        {
            _halos?.Destroy();
            if (Interop.Alive(_root)) Object.Destroy(_root);
            foreach (var m in _owned) if (Interop.Alive(m)) Object.Destroy(m);
            _owned.Clear(); _gameShader.Clear(); _renderers.Clear();
            try { _bundle?.Release(); } catch { }
            _bundle = null;
        }

        internal static Vector3 Vec(float[] a, Vector3 fallback) =>
            a != null && a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : fallback;
    }
}
