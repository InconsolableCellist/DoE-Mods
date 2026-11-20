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
    public sealed partial class BundleModel : ISkinModel
    {
        private readonly SkinLibrary.Skin _skin;
        private readonly Transform _enemy;
        private readonly Animator _source;
        private AvatarBundle _bundle;
        private GameObject _root, _instance;
        private readonly List<Renderer> _renderers = new List<Renderer>();
        private readonly List<Material> _owned = new List<Material>();
        private readonly List<Material> _gameShader = new List<Material>();
        /// <summary>The skin's own exported materials, by name, for textures carried in the bundle.</summary>
        private readonly Dictionary<string, Material> _sourceMats = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
        private PoseRetargeter _retarget;
        private float _settle;
        private bool _ready;
        private string _captureNote = "waiting for the enemy to settle";

        // Legs (glide) and weapons
        private Transform _hips, _thighL, _thighR, _kneeL, _kneeR, _footL, _footR, _toesL, _toesR;
        private Quaternion _footLRest, _footRRest, _toesLRest, _toesRRest;
        private Quaternion _thighLRest, _thighRRest, _kneeLRest, _kneeRRest;
        private Vector3 _lastEnemyPos;
        private Vector2 _trail;   // x: back/forward swing, y: side swing, degrees, smoothed
        private readonly List<(Transform bone, Transform to, Quaternion rot, Vector3 off)> _weapons = new List<(Transform, Transform, Quaternion, Vector3)>();
        private Halos _halos;

        // Secondary motion
        private SpringBones _springs;
        private string _springNote = "no springs";
        private Transform _chest, _upperChest, _neck;
        private float _phase;
        private HandPoser _hands;
        private Transform _eyeL, _eyeR;
        private Quaternion _eyeLRest = Quaternion.identity, _eyeRRest = Quaternion.identity;
        private Vector3 _headFwd, _headUp, _eyeLFwd, _eyeLUp, _eyeRFwd, _eyeRUp;   // bone-local, from the bind pose
        private Vector2 _look, _lookEye;   // smoothed yaw/pitch, degrees
        private string _lookNote = "off";
        private Quaternion _upperChestRest = Quaternion.identity, _neckRest = Quaternion.identity, _headRest = Quaternion.identity;
        private readonly List<(Transform t, Quaternion rot, Vector3 pos)> _bind = new List<(Transform, Quaternion, Vector3)>();

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
        /// The skin's materials as copies of the enemy's own body material, so it is lit like the
        /// enemy (Death: PotoVR/Characters_Normal from the Bakery volume). Every texture slot of
        /// the copy is re-filled: the enemy's maps are laid out for the enemy's UVs, and on the
        /// skin they light up and shade arbitrary patches (2026-11-18, first wolf test: orange
        /// glow on the legs, forearms and scythes, faceted shading). Keywords in
        /// <c>keywordsOff</c> (default: the X-ray outline, which draws every part of the skin
        /// that the skin itself hides) are switched off. Materials named in <c>keepMaterials</c>
        /// stay as exported.
        /// </summary>
        private void Materials(Renderer body)
        {
            var template = body.sharedMaterial;
            var keep = new HashSet<string>(_skin.Def.keepMaterials ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            var made = new Dictionary<IntPtr, Material>();
            var first = true;
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
                        var log = first ? new System.Text.StringBuilder() : null;
                        Refill(copy, src, log);
                        if (log != null)
                        {
                            first = false;
                            Report.Line($"- **{_skin.Name}** materials: copies of `{template.name}` (`{template.shader.name}`), " +
                                        $"keywords now `{string.Join(" ", copy.shaderKeywords)}`. Slots, for `{name}`:\n{log}");
                        }
                        made[src.Pointer] = copy;
                        _owned.Add(copy);
                        _gameShader.Add(copy);
                        _sourceMats[name] = src;
                    }
                    mats[i] = copy;
                    changed = true;
                }
                if (changed) r.sharedMaterials = mats;
            }
        }

        private static readonly string[] DefaultKeywordsOff = { "_XRAY_ON" };
        private static Texture2D _flatNormal, _grey;

        private void Refill(Material copy, Material src, System.Text.StringBuilder log)
        {
            foreach (var k in _skin.Def.keywordsOff ?? new List<string>(DefaultKeywordsOff))
                if (!string.IsNullOrEmpty(k)) copy.DisableKeyword(k);
            foreach (var c in new[] { "_Color", "_BaseColor", "_MainColor" })
                if (copy.HasProperty(c)) copy.SetColor(c, Color.white);

            var sh = copy.shader;
            var n = sh.GetPropertyCount();
            for (var i = 0; i < n; i++)
            {
                var prop = sh.GetPropertyName(i);
                var type = sh.GetPropertyType(i);
                if (type != ShaderPropertyType.Texture)
                {
                    if (log == null) continue;
                    string val;
                    try
                    {
                        val = type == ShaderPropertyType.Color ? copy.GetColor(prop).ToString()
                            : type == ShaderPropertyType.Vector ? copy.GetVector(prop).ToString()
                            : copy.GetFloat(prop).ToString("0.###");
                    }
                    catch { val = "?"; }
                    if (prop.IndexOf("Emiss", StringComparison.OrdinalIgnoreCase) >= 0 && type == ShaderPropertyType.Color)
                    {
                        copy.SetColor(prop, SkinTex(src, "_EmissionMap") != null ? src.GetColor("_EmissionColor") : Color.black);
                        val += " -> " + copy.GetColor(prop);
                    }
                    log.AppendLine($"    - `{prop}` {type} {val}");
                    continue;
                }
                var was = copy.GetTexture(prop);
                string rule = null;
                if (_skin.Def.textures != null) _skin.Def.textures.TryGetValue(prop, out rule);
                var guessed = rule == null;
                if (guessed) rule = Guess(prop);
                var tex = Pick(rule, src, was);
                copy.SetTexture(prop, tex);
                log?.AppendLine($"    - `{prop}` texture `{(Interop.Alive(was) ? was.name : "none")}` -> {rule}{(guessed ? " (guessed from the name)" : "")}");
            }
        }

        private static string Guess(string prop)
        {
            bool Has(params string[] w) { foreach (var x in w) if (prop.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0) return true; return false; }
            if (prop == "_MainTex" || Has("BaseMap", "BaseColor", "Albedo", "Diffuse")) return "skin:_MainTex";
            if (Has("Bump", "Normal")) return "skin:_BumpMap";
            if (Has("Emiss", "Glow", "Illum")) return "skin:_EmissionMap";
            if (Has("Dissolve", "Noise", "Burn", "Ramp", "MatCap", "Matcap", "Cube", "LUT", "Gradient", "Toon")) return "keep";
            if (Has("Occlusion", "AO")) return "white";
            return "black";   // masks, metal, spec, detail: laid out for the enemy's UVs
        }

        private static Texture Pick(string rule, Material src, Texture was)
        {
            switch (rule.Trim().ToLowerInvariant())
            {
                case "keep": return was;
                case "none": return null;
                case "white": return Texture2D.whiteTexture;
                case "black": return Texture2D.blackTexture;
                case "grey": case "gray": return _grey = _grey ?? Solid(new Color(0.5f, 0.5f, 0.5f, 0.5f));
                case "bump": return FlatNormal();
            }
            if (rule.StartsWith("skin:", StringComparison.OrdinalIgnoreCase))
            {
                var p = rule.Substring(5).Trim();
                var t = SkinTex(src, p);
                if (t != null) return t;
                if (p.IndexOf("Bump", StringComparison.OrdinalIgnoreCase) >= 0 || p.IndexOf("Normal", StringComparison.OrdinalIgnoreCase) >= 0) return FlatNormal();
                return p == "_MainTex" ? Texture2D.whiteTexture : Texture2D.blackTexture;
            }
            return was;
        }

        private static Texture SkinTex(Material src, string prop)
        {
            try
            {
                if (prop == "_MainTex") { var m = src.mainTexture; if (Interop.Alive(m)) return m; }
                if (src.HasProperty(prop)) { var t = src.GetTexture(prop); if (Interop.Alive(t)) return t; }
            }
            catch { }
            return null;
        }

        /// <summary>A flat tangent-space normal that unpacks to (0,0,1) whether read as RGB or as DXT5nm (AG).</summary>
        private static Texture2D FlatNormal() => _flatNormal = Interop.Alive(_flatNormal) ? _flatNormal : Solid(new Color(0.5f, 0.5f, 1f, 0.5f));

        private static Texture2D Solid(Color c)
        {
            var t = new Texture2D(4, 4, TextureFormat.RGBA32, false, true) { name = "CustomEnemies solid", hideFlags = HideFlags.DontUnloadUnusedAsset };
            var px = new Color[16];
            for (var i = 0; i < 16; i++) px[i] = c;
            t.SetPixels(px);
            t.Apply(false, true);
            return t;
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
            foreach (var t in _instance.GetComponentsInChildren<Transform>(true))
                _bind.Add((t, t.localRotation, t.localPosition));
            _chest = Bone("Chest"); _upperChest = Bone("UpperChest"); _neck = Bone("Neck");
            if (Interop.Alive(_upperChest)) _upperChestRest = _upperChest.localRotation;
            if (Interop.Alive(_neck)) _neckRest = _neck.localRotation;
            var head = Bone("Head");
            if (Interop.Alive(head)) _headRest = head.localRotation;
            LookSetup(head);
            FaceSetup();
            _phase = UnityEngine.Random.value * 100f;
            _hips = Bone("Hips");
            _thighL = Bone("LeftUpperLeg"); _thighR = Bone("RightUpperLeg");
            _kneeL = Bone("LeftLowerLeg"); _kneeR = Bone("RightLowerLeg");
            _footL = Bone("LeftFoot"); _footR = Bone("RightFoot"); _toesL = Bone("LeftToes"); _toesR = Bone("RightToes");
            if (Interop.Alive(_footL)) _footLRest = _footL.localRotation;
            if (Interop.Alive(_footR)) _footRRest = _footR.localRotation;
            if (Interop.Alive(_toesL)) _toesLRest = _toesL.localRotation;
            if (Interop.Alive(_toesR)) _toesRRest = _toesR.localRotation;
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
            _springs = null;
            _hands = null;
            _look = _lookEye = Vector2.zero;
            FaceReset();
            // Back to the bind pose the capture expects: the retargeter only restores bones it
            // paired, and the springs and legs moved others.
            foreach (var (t, rot, pos) in _bind)
                if (Interop.Alive(t)) { t.localRotation = rot; t.localPosition = pos; }
            _trail = Vector2.zero;
        }

        public void Tick(float dt)
        {
            if (!Interop.Alive(_instance) || !Interop.Alive(_source)) return;
            if (!_ready) { TrySettle(dt); if (!_ready) return; }

            // Idle motion turns these on top of the copied pose; one the retargeter doesn't drive
            // (the wolf's UpperChest) would otherwise add up frame after frame.
            if (Interop.Alive(_upperChest)) _upperChest.localRotation = _upperChestRest;
            if (Interop.Alive(_neck)) _neck.localRotation = _neckRest;
            if (Interop.Alive(_dstHead)) _dstHead.localRotation = _headRest;
            if (Interop.Alive(_eyeL)) _eyeL.localRotation = _eyeLRest;
            if (Interop.Alive(_eyeR)) _eyeR.localRotation = _eyeRRest;
            RestoreFace();
            _retarget.Apply();
            HangLegs(dt);
            IdleMotion();
            var near = Nearness();
            BiteBody(dt);
            LookAtPlayer(dt);
            Face(dt, near);
            if (_hands != null)
            {
                try { _hands.Update(dt); }
                catch (Exception e) { SkinManager.LogOnce("grip " + _skin.Name, e); }
            }
            foreach (var (bone, to, rot, off) in _weapons)
            {
                if (!Interop.Alive(bone) || !Interop.Alive(to)) continue;
                bone.SetPositionAndRotation(to.TransformPoint(off), to.rotation * rot);
            }
            if (_springs != null)
            {
                try { _springs.Simulate(dt); }
                catch (Exception e) { SkinManager.LogOnce("springs " + _skin.Name, e); }
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
            BuildSprings();
            BuildGrip();
            _retarget = _retarget ?? new PoseRetargeter();
            _captureNote = _retarget.Build(_source, _instance, _skin.Manifest);
            _ready = _retarget.LinkCount > 0;
            _lastEnemyPos = _enemy.position;
            Core.Log.Msg($"Skin `{_skin.Name}` on `{SkinManager.CleanName(Interop.Name(_enemy))}`: {_captureNote}; {_scaleNote}.");
            Report.Line($"- **{_skin.Name}** pose copy: {_captureNote}; {_scaleNote}; paired: {_retarget.Describe()}");
            Report.Line($"- **{_skin.Name}** springs: {_springNote}; look: {_lookNote}; grip: {(_hands == null ? "off" : _hands.JointCount + " finger joint(s)")}; face: {_faceNote}");
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

        /// <summary>
        /// Spring chains: the manifest's own (PhysBones captured by the exporter) plus the
        /// sidecar's. Built at rest, after the scale fit, because the solver measures its bone
        /// lengths in world space. A sidecar chain's last bone swings toward a virtual tip one
        /// segment further on.
        /// </summary>
        private void BuildSprings()
        {
            var chains = new List<AvatarManifest.ChainInfo>();
            if (_skin.Manifest.dynamics?.chains != null) chains.AddRange(_skin.Manifest.dynamics.chains);
            var missing = new List<string>();
            foreach (var sp in _skin.Def.springs ?? new List<SkinLibrary.Spring>())
            {
                if (sp?.bones == null || sp.bones.Count == 0) continue;
                var ts = new List<Transform>();
                foreach (var b in sp.bones)
                {
                    var t = Bone(b);
                    if (!Interop.Alive(t)) { missing.Add(b); ts.Clear(); break; }
                    ts.Add(t);
                }
                if (ts.Count == 0) continue;
                var paths = new List<string>();
                foreach (var t in ts) paths.Add(RelPath(t));
                List<float> end = null;
                var last = ts[ts.Count - 1];
                var prev = ts.Count >= 2 ? ts[ts.Count - 2] : last.parent;
                if (Interop.Alive(prev))
                {
                    var tip = last.InverseTransformPoint(last.position + (last.position - prev.position));
                    end = new List<float> { tip.x, tip.y, tip.z };
                }
                chains.Add(new AvatarManifest.ChainInfo
                {
                    name = sp.name ?? sp.bones[0], source = "enemy.json", bones = paths,
                    stiffness = sp.stiffness, pull = sp.stiffness, spring = sp.spring, gravity = sp.gravity, immobile = sp.immobile,
                    limitType = sp.maxAngle > 0f ? "Angle" : "None", maxAngleX = sp.maxAngle, maxAngleZ = sp.maxAngle,
                    endpointPosition = end,
                });
            }
            if (chains.Count == 0) { _springs = null; _springNote = "none listed"; return; }
            var manifest = new AvatarManifest
            {
                dynamics = new AvatarManifest.DynamicsInfo { chains = chains, colliders = Colliders(missing) },
            };
            _springs = new SpringBones();
            var note = _springs.Build(_instance, manifest);
            _springs.Reset();
            _springNote = $"{_springs.ChainCount} chain(s), {_springs.BoneCount} bone(s), {_springs.ColliderCount} collider(s)" +
                          (missing.Count > 0 ? $"; bones not found: {string.Join(", ", missing)}" : "") +
                          (string.IsNullOrEmpty(note) ? "" : $" ({note})");
            if (_springs.ChainCount == 0) _springs = null;
        }

        /// <summary>Fixed finger curls through CustomAvatars' HandPoser (its palm detection finds each finger's bend axis).</summary>
        private void BuildGrip()
        {
            _hands = null;
            var g = _skin.Def.grip;
            if (g == null || g.Length == 0) return;
            var hp = new HandPoser { RemoteDriven = true };
            var note = hp.Build(_instance, _skin.Manifest);
            if (hp.JointCount == 0) { Core.Log.Warning($"Skin `{_skin.Name}` grip: {note}"); return; }
            var curls = new float[10];
            for (var i = 0; i < 5; i++) curls[i] = curls[5 + i] = Mathf.Clamp01(g[Mathf.Min(i, g.Length - 1)]);
            hp.SetRemoteCurls(curls);
            _hands = hp;
        }

        /// <summary>
        /// Which way the face points, from the bind pose: the horizontal direction from the head
        /// bone to the midpoint of the eyes (an MMD head bone sits at the top of the neck, behind
        /// and below the eyes), kept in each bone's own frame.
        /// </summary>
        private void LookSetup(Transform head)
        {
            _eyeL = Bone("LeftEye"); _eyeR = Bone("RightEye");
            if (Interop.Alive(_eyeL)) _eyeLRest = _eyeL.localRotation;
            if (Interop.Alive(_eyeR)) _eyeRRest = _eyeR.localRotation;
            if (_skin.Def.look == null || !Interop.Alive(head)) { _lookNote = "off"; return; }
            Vector3 fwd;
            string from;
            if (Interop.Alive(_eyeL) && Interop.Alive(_eyeR))
            {
                fwd = (_eyeL.position + _eyeR.position) * 0.5f - head.position;
                fwd.y = 0f;
                from = "eyes in front of the head";
            }
            else { fwd = Vector3.zero; from = ""; }
            if (fwd.sqrMagnitude < 1e-6f) { fwd = _instance.transform.forward; from = "the model's forward"; }
            fwd.Normalize();
            _headFwd = head.InverseTransformDirection(fwd);
            _headUp = head.InverseTransformDirection(Vector3.up);
            if (Interop.Alive(_eyeL)) { _eyeLFwd = _eyeL.InverseTransformDirection(fwd); _eyeLUp = _eyeL.InverseTransformDirection(Vector3.up); }
            if (Interop.Alive(_eyeR)) { _eyeRFwd = _eyeR.InverseTransformDirection(fwd); _eyeRUp = _eyeR.InverseTransformDirection(Vector3.up); }
            _lookNote = $"head{(Interop.Alive(_eyeL) && Interop.Alive(_eyeR) ? " and eyes" : "")}, face direction from {from}";
        }

        /// <summary>Head (shared with the neck), then eyes, toward the local camera, clamped and smoothed.</summary>
        private void LookAtPlayer(float dt)
        {
            var look = _skin.Def.look;
            if (look == null || !Interop.Alive(_dstHead)) return;
            var cam = CameraFinder.Get();
            var want = Vector2.zero;
            var on = false;
            if (Interop.Alive(cam))
            {
                var target = cam.transform.position;
                var a = Angles(_dstHead, _headFwd, _headUp, target);
                // Beyond range, or well behind the head's reach: back to the copied pose.
                if ((target - _dstHead.position).sqrMagnitude < look.range * look.range && Mathf.Abs(a.x) < look.headYaw + 50f)
                {
                    want = new Vector2(Mathf.Clamp(a.x, -look.headYaw, look.headYaw), Mathf.Clamp(a.y, -look.headPitch, look.headPitch));
                    on = true;
                }
            }
            var k = 1f - Mathf.Exp(-dt / Mathf.Max(0.02f, look.speed));
            _look = Vector2.Lerp(_look, want, k);

            var neck = Interop.Alive(_neck) ? _neck : null;
            var share = neck != null ? Mathf.Clamp01(look.neckShare) : 0f;
            // Axes from the head's current frame, so the turn is relative to the copied pose.
            var up = _dstHead.TransformDirection(_headUp);
            var fwd = _dstHead.TransformDirection(_headFwd);
            var right = Vector3.Cross(up, fwd).normalized;
            if (neck != null) neck.rotation = Turn(_look * share, up, right) * neck.rotation;
            up = _dstHead.TransformDirection(_headUp);
            fwd = _dstHead.TransformDirection(_headFwd);
            right = Vector3.Cross(up, fwd).normalized;
            _dstHead.rotation = Turn(_look * (1f - share), up, right) * _dstHead.rotation;

            // Eyes: what the head did not cover, quicker.
            var ke = 1f - Mathf.Exp(-dt / Mathf.Max(0.01f, look.speed * 0.25f));
            var eyeWant = Vector2.zero;
            if (on && Interop.Alive(cam) && Interop.Alive(_eyeL))
            {
                var e = Angles(_eyeL, _eyeLFwd, _eyeLUp, cam.transform.position);
                eyeWant = new Vector2(Mathf.Clamp(e.x, -look.eyeYaw, look.eyeYaw), Mathf.Clamp(e.y, -look.eyePitch, look.eyePitch));
            }
            _lookEye = Vector2.Lerp(_lookEye, eyeWant, ke);
            Eye(_eyeL, _eyeLFwd, _eyeLUp);
            Eye(_eyeR, _eyeRFwd, _eyeRUp);
        }

        private void Eye(Transform eye, Vector3 fwdLocal, Vector3 upLocal)
        {
            if (!Interop.Alive(eye)) return;
            var up = eye.TransformDirection(upLocal);
            var fwd = eye.TransformDirection(fwdLocal);
            eye.rotation = Turn(_lookEye, up, Vector3.Cross(up, fwd).normalized) * eye.rotation;
        }

        /// <summary>Yaw (right positive) and pitch (up positive) of a target from a bone's face direction, degrees.</summary>
        private static Vector2 Angles(Transform bone, Vector3 fwdLocal, Vector3 upLocal, Vector3 target)
        {
            var up = bone.TransformDirection(upLocal).normalized;
            var fwd = bone.TransformDirection(fwdLocal).normalized;
            var right = Vector3.Cross(up, fwd).normalized;
            var d = (target - bone.position).normalized;
            var x = Vector3.Dot(d, right); var y = Vector3.Dot(d, up); var z = Vector3.Dot(d, fwd);
            return new Vector2(Mathf.Atan2(x, z) * Mathf.Rad2Deg, Mathf.Atan2(y, Mathf.Sqrt(x * x + z * z)) * Mathf.Rad2Deg);
        }

        private static Quaternion Turn(Vector2 yawPitch, Vector3 up, Vector3 right) =>
            Quaternion.AngleAxis(yawPitch.x, up) * Quaternion.AngleAxis(-yawPitch.y, right);

        private List<AvatarManifest.ColliderInfo> Colliders(List<string> missing)
        {
            var list = new List<AvatarManifest.ColliderInfo>();
            if (_skin.Manifest.dynamics?.colliders != null) list.AddRange(_skin.Manifest.dynamics.colliders);
            foreach (var c in _skin.Def.colliders ?? new List<SkinLibrary.SpringCollider>())
            {
                var t = Bone(c?.bone);
                if (!Interop.Alive(t)) { missing.Add(c?.bone ?? "?"); continue; }
                var o = Vec(c.offset, Vector3.zero);
                list.Add(new AvatarManifest.ColliderInfo { path = RelPath(t), shape = "Sphere", radius = c.radius, position = new List<float> { o.x, o.y, o.z } });
            }
            return list;
        }

        private string RelPath(Transform t)
        {
            var root = _instance.transform;
            var s = t.name;
            for (var p = t.parent; Interop.Alive(p) && p != root; p = p.parent) s = p.name + "/" + s;
            return s;
        }

        /// <summary>Breathing in the chest and a slow wander of the head, on top of the copied pose.</summary>
        private void IdleMotion()
        {
            var idle = _skin.Def.idle;
            if (idle == null) return;
            var root = _root.transform;
            var t = Time.time + _phase;
            if (idle.breath > 0f)
            {
                var b = Mathf.Sin(t * Mathf.PI * 2f / Mathf.Max(0.5f, idle.breathPeriod)) * idle.breath;
                var chest = Interop.Alive(_upperChest) ? _upperChest : _chest;
                if (Interop.Alive(chest)) chest.rotation = Quaternion.AngleAxis(-b, root.right) * chest.rotation;
                if (Interop.Alive(_neck)) _neck.rotation = Quaternion.AngleAxis(b * 0.6f, root.right) * _neck.rotation;
            }
            if (idle.head > 0f && Interop.Alive(_dstHead))
            {
                var w = Mathf.PI * 2f / Mathf.Max(0.5f, idle.headPeriod);
                var yaw = (Mathf.Sin(t * w) + 0.5f * Mathf.Sin(t * w * 2.3f + 1.1f)) / 1.5f * idle.head;
                var pitch = Mathf.Sin(t * w * 0.7f + 2.0f) * idle.head * 0.5f;
                var roll = Mathf.Sin(t * w * 0.45f + 0.4f) * idle.head * 0.6f;
                _dstHead.rotation = Quaternion.AngleAxis(yaw, root.up) * Quaternion.AngleAxis(pitch, root.right) * Quaternion.AngleAxis(roll, root.forward) * _dstHead.rotation;
            }
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
            // Feet and toes held, not copied: the enemy's feet twitch under its robe, and on a
            // skin whose legs hang that reads as paws flitting sideways (2026-11-18).
            Foot(_footL, _footLRest, _toesL, _toesLRest, legs, root);
            Foot(_footR, _footRRest, _toesR, _toesRRest, legs, root);
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

        private static void Foot(Transform foot, Quaternion rest, Transform toes, Quaternion toesRest, SkinLibrary.Legs legs, Transform root)
        {
            if (Interop.Alive(foot)) foot.rotation = Quaternion.AngleAxis(legs.footPoint, root.right) * (foot.parent.rotation * rest);
            if (Interop.Alive(toes)) toes.localRotation = toesRest;
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
                   $"pose overwritten by something else {(over < 0 ? "n/a" : over.ToString("0.0") + "°")} (the look-at turns the head after the copy)" +
                   (_skin.Def.look == null ? "" : $"; look yaw {_look.x:0} pitch {_look.y:0}, eyes {_lookEye.x:0}/{_lookEye.y:0}");
        }

        public string Describe() =>
            $"bundle `{_skin.Name}` ({_renderers.Count} renderer(s), {_gameShader.Count} material(s) on the enemy's shader, " +
            $"{_weapons.Count} weapon(s), {_halos?.Count ?? 0} eye halo(s), legs {(_skin.Def.legs == null ? "copied" : "hanging")}, " +
            $"{_skin.Def.springs?.Count ?? 0} spring chain(s) listed, idle {(_skin.Def.idle == null ? "off" : "on")}), {_captureNote}";

        public void SetVisible(float visible)
        {
            if (_halos != null) _halos.Visible = visible;
        }

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
