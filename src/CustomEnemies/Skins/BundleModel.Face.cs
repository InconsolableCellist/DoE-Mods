using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Random = UnityEngine.Random;

namespace CustomEnemies.Skins
{
    /// <summary>
    /// The face and the bite: ear flicks, the growl as the local player comes near (jaw, blend
    /// shapes, ears back, eyes brighter with white pupils), and a bite played whenever the enemy's
    /// Animator enters its attack state. All of it runs after the pose copy, on bones the copy
    /// doesn't drive (ears, jaw, eyes), which are put back to their bind pose every frame first.
    /// </summary>
    public sealed partial class BundleModel
    {
        private sealed class Ear
        {
            public Transform Bone;
            public Quaternion Rest;
            public float Side;        // +1 the model's left, -1 its right (sign of the outward tilt)
            public float Next;        // time of the next flick
            public float Start = -10f;
            public bool Double;
        }

        private readonly List<Ear> _ears = new List<Ear>();
        private Transform _jaw;
        private Quaternion _jawRest;
        private readonly List<(SkinnedMeshRenderer r, int index, string name)> _shapes = new List<(SkinnedMeshRenderer, int, string)>();
        private readonly Dictionary<string, float> _shapeWant = new Dictionary<string, float>();
        private Material _eyeMat;
        private Texture _eyeTex, _eyeFury;
        private bool _furyOn;
        private float _biteT = -1f, _shakeSeed;
        private bool _inAttack;
        private int _bites;
        private float _near;
        private string _faceNote = "off";

        private void FaceSetup()
        {
            var parts = new List<string>();
            var ears = _skin.Def.ears;
            if (ears?.bones != null)
            {
                foreach (var n in ears.bones)
                {
                    var b = Bone(n);
                    if (!Interop.Alive(b)) continue;
                    var side = Interop.Alive(_dstHead) ? -Mathf.Sign(Vector3.Dot(b.position - _dstHead.position, Right(_dstHead))) : 1f;
                    _ears.Add(new Ear { Bone = b, Rest = b.localRotation, Side = side == 0f ? 1f : side, Next = Time.time + Random.Range(1f, ears.flickMax) });
                }
                parts.Add($"{_ears.Count} ear(s)");
            }
            _jaw = Bone("Jaw");
            if (Interop.Alive(_jaw)) { _jawRest = _jaw.localRotation; parts.Add("jaw"); }

            var names = new HashSet<string>();
            if (_skin.Def.close?.shapes != null) foreach (var k in _skin.Def.close.shapes.Keys) names.Add(k);
            if (_skin.Def.bite?.shapes != null) foreach (var k in _skin.Def.bite.shapes.Keys) names.Add(k);
            foreach (var r in _renderers)
            {
                var smr = r.TryCast<SkinnedMeshRenderer>();
                if (smr == null || !Interop.Alive(smr.sharedMesh)) continue;
                foreach (var n in names)
                {
                    var i = smr.sharedMesh.GetBlendShapeIndex(n);
                    if (i >= 0) _shapes.Add((smr, i, n));
                }
            }
            if (names.Count > 0)
            {
                var found = new HashSet<string>();
                foreach (var s in _shapes) found.Add(s.name);
                var miss = new List<string>();
                foreach (var n in names) if (!found.Contains(n)) miss.Add(n);
                parts.Add($"{found.Count} blend shape(s)" + (miss.Count > 0 ? $" (not in the mesh: {string.Join(", ", miss)})" : ""));
            }

            var g = _skin.Def.eyeGlow;
            if (g != null)
            {
                foreach (var m in _gameShader)
                    if (Interop.Alive(m) && string.Equals(m.name, "CustomEnemies " + g.material, StringComparison.OrdinalIgnoreCase)) { _eyeMat = m; break; }
                _eyeTex = LoadTexture(g.texture);
                _eyeFury = LoadTexture(g.furyTexture) ?? _eyeTex;
                if (_eyeMat == null) parts.Add($"eye glow: no material `{g.material}`");
                else if (_eyeTex == null) parts.Add($"eye glow: texture `{g.texture}` not loaded");
                else { SetEyes(_eyeTex, g.intensity); parts.Add("eye glow"); }
            }
            if (_skin.Def.bite != null) parts.Add($"bite on `{_skin.Def.bite.state}`");
            _faceNote = parts.Count == 0 ? "off" : string.Join(", ", parts);
        }

        private void FaceReset()
        {
            _biteT = -1f; _inAttack = false; _near = 0f;
            foreach (var e in _ears) { e.Start = -10f; e.Next = Time.time + Random.Range(1f, 4f); }
        }

        private void RestoreFace()
        {
            foreach (var e in _ears) if (Interop.Alive(e.Bone)) e.Bone.localRotation = e.Rest;
            if (Interop.Alive(_jaw)) _jaw.localRotation = _jawRest;
        }

        /// <summary>0 beyond the growl range, 1 at half of it and closer.</summary>
        private float Nearness()
        {
            var c = _skin.Def.close;
            var cam = CameraFinder.Get();
            if (c == null || !Interop.Alive(cam) || !Interop.Alive(_dstHead)) return 0f;
            var d = Vector3.Distance(cam.transform.position, _dstHead.position);
            var r = Mathf.Max(0.5f, c.range);
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(r, r * 0.5f, d));
        }

        /// <summary>The bite's timeline: the wind-up, snap, hold and release, each 0..1 within its stretch.</summary>
        private void BiteBody(float dt)
        {
            var b = _skin.Def.bite;
            if (b == null) return;
            var attack = false;
            float len = 0f;
            try { var st = _source.GetCurrentAnimatorStateInfo(0); attack = st.IsName(b.state); len = st.length; } catch { }
            if (attack && !_inAttack && _biteT < 0f)
            {
                _biteT = 0f; _bites++; _shakeSeed = Random.value * 10f;
                Report.Line($"- `{_skin.Name}` bite {_bites} (the enemy's `{b.state}` state is {len:0.00} s long)");
            }
            _inAttack = attack;
            if (_biteT < 0f) return;
            _biteT += dt;
            if (_biteT > b.windup + b.snap + b.hold + b.release) { _biteT = -1f; return; }

            // Chest: rear back a little, then lunge forward.
            var lunge = BiteCurve(b, out _, out _);
            var chest = Interop.Alive(_upperChest) ? _upperChest : _chest;
            if (Interop.Alive(chest)) chest.rotation = Quaternion.AngleAxis(lunge * b.lunge, _root.transform.right) * chest.rotation;
        }

        /// <summary>Returns the lunge (-1/3 .. 1); out: jaw open 0..1 (negative = clenched past shut), and head shake degrees.</summary>
        private float BiteCurve(SkinLibrary.Bite b, out float jaw, out float shake)
        {
            var t = _biteT;
            shake = 0f;
            if (t < b.windup)
            {
                var k = Ease(t / b.windup);
                jaw = k; return -k / 3f;
            }
            t -= b.windup;
            if (t < b.snap)
            {
                var k = t / b.snap; k *= k;   // accelerating: the chomp
                jaw = Mathf.Lerp(1f, -0.08f, k); return Mathf.Lerp(-1f / 3f, 1f, Ease(t / b.snap));
            }
            t -= b.snap;
            if (t < b.hold)
            {
                var k = t / b.hold;
                jaw = Mathf.Lerp(-0.08f, 0f, k);
                shake = Mathf.Sin((t + _shakeSeed) * 38f) * b.shake * (1f - k);
                return 1f;
            }
            t -= b.hold;
            jaw = 0f;
            return 1f - Ease(Mathf.Clamp01(t / b.release));
        }

        private static float Ease(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

        private void Face(float dt, float near)
        {
            _near = near;
            var c = _skin.Def.close;
            var b = _skin.Def.bite;
            var biting = b != null && _biteT >= 0f;
            float biteJaw = 0f, shake = 0f;
            if (biting) BiteCurve(b, out biteJaw, out shake);
            var biteAmt = biting ? Mathf.Clamp01(Mathf.Max(biteJaw, _biteT < b.windup + b.snap + b.hold ? 1f : 0f)) : 0f;

            var right = Interop.Alive(_dstHead) ? Right(_dstHead) : _root.transform.right;
            var fwd = Interop.Alive(_dstHead) ? _dstHead.TransformDirection(_headFwd) : _root.transform.forward;

            // Head shake in the hold.
            if (Interop.Alive(_dstHead) && shake != 0f) _dstHead.rotation = Quaternion.AngleAxis(shake, fwd) * _dstHead.rotation;

            // Jaw: the growl's parted lips with a tremble, or the bite's.
            if (Interop.Alive(_jaw))
            {
                var deg = 0f;
                if (c != null && near > 0f)
                    deg = near * (c.jaw + (Mathf.PerlinNoise(Time.time * 18f, _phase) - 0.5f) * 2f * c.tremble);
                if (biting) deg = Mathf.Max(deg, biteJaw * b.open) + Mathf.Min(0f, biteJaw * b.open);
                if (deg != 0f) _jaw.rotation = Quaternion.AngleAxis(deg, right) * _jaw.rotation;
            }

            // Blend shapes: the larger of growl and bite.
            _shapeWant.Clear();
            if (c?.shapes != null) foreach (var kv in c.shapes) _shapeWant[kv.Key] = kv.Value * near;
            if (biting && b.shapes != null)
                foreach (var kv in b.shapes)
                    _shapeWant[kv.Key] = Mathf.Max(_shapeWant.TryGetValue(kv.Key, out var v) ? v : 0f, kv.Value * biteAmt);
            foreach (var (r, i, n) in _shapes)
                if (Interop.Alive(r)) r.SetBlendShapeWeight(i, _shapeWant.TryGetValue(n, out var w) ? Mathf.Clamp(w, 0f, 100f) : 0f);

            Ears(right, fwd, Mathf.Max(c != null ? near * c.earsBack : 0f, biteAmt));

            // Eyes: halos swell, the iris brightens, and the white pupils come in at full growl.
            if (_halos != null) _halos.Boost = 1f + ((c?.haloScale ?? 1f) - 1f) * Mathf.Max(near, biteAmt);
            var g = _skin.Def.eyeGlow;
            if (g != null && _eyeMat != null && _eyeTex != null)
            {
                var fury = Mathf.Max(near, biteAmt) >= g.furyAt;
                if (fury != _furyOn) { _furyOn = fury; _eyeMat.SetTexture("_Emission", fury ? _eyeFury : _eyeTex); }
                SetEyes(null, Mathf.Lerp(g.intensity, g.furyIntensity, Mathf.Max(near, biteAmt)));
            }
        }

        private void Ears(Vector3 right, Vector3 fwd, float pinned)
        {
            var e = _skin.Def.ears;
            if (e == null) return;
            var now = Time.time;
            foreach (var ear in _ears)
            {
                if (!Interop.Alive(ear.Bone)) continue;
                if (now >= ear.Next)
                {
                    ear.Start = now;
                    ear.Double = Random.value < 0.3f;
                    // Irritable when the player is near: flicks come quicker.
                    ear.Next = now + Random.Range(e.flickMin, e.flickMax) * Mathf.Lerp(1f, 0.4f, _near);
                }
                var t = now - ear.Start;
                var f = Flick(t) + (ear.Double ? Flick(t - 0.22f) * 0.8f : 0f);
                var back = -(e.flick * f + e.pinBack * pinned);
                var outward = e.twist * f * ear.Side;
                ear.Bone.rotation = Quaternion.AngleAxis(outward, fwd) * Quaternion.AngleAxis(back, right) * ear.Bone.rotation;
            }
        }

        /// <summary>One flick: snap out in 60 ms, hold 50 ms, ease back over 350 ms.</summary>
        private static float Flick(float t)
        {
            if (t < 0f || t > 0.46f) return 0f;
            if (t < 0.06f) return t / 0.06f;
            if (t < 0.11f) return 1f;
            return 1f - Ease((t - 0.11f) / 0.35f);
        }

        private Vector3 Right(Transform head)
        {
            var up = head.TransformDirection(_headUp);
            var fwd = head.TransformDirection(_headFwd);
            var r = Vector3.Cross(up, fwd);
            return r.sqrMagnitude > 1e-8f ? r.normalized : _root.transform.right;
        }

        private void SetEyes(Texture tex, float intensity)
        {
            if (tex != null) _eyeMat.SetTexture("_Emission", tex);
            if (_eyeMat.HasProperty("_EmissionColor")) _eyeMat.SetColor("_EmissionColor", Color.white);
            if (_eyeMat.HasProperty("_EmissionIntensity")) _eyeMat.SetFloat("_EmissionIntensity", intensity);
        }

        private static readonly Dictionary<string, Texture2D> Loaded = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);

        /// <summary>A PNG or JPG from the skins folder, loaded once per session.</summary>
        private static Texture LoadTexture(string file)
        {
            if (string.IsNullOrEmpty(file)) return null;
            if (Loaded.TryGetValue(file, out var t) && Interop.Alive(t)) return t;
            var path = Path.Combine(SkinLibrary.Folder, file);
            if (!File.Exists(path)) { Core.Log.Warning($"Skin texture `{path}` not found."); return null; }
            try
            {
                t = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = "CustomEnemies " + file, hideFlags = HideFlags.DontUnloadUnusedAsset };
                if (!ImageConversion.LoadImage(t, File.ReadAllBytes(path), false)) { Core.Log.Warning($"Skin texture `{file}` could not be decoded."); return null; }
                Loaded[file] = t;
                return t;
            }
            catch (Exception e) { Core.Log.Warning($"Skin texture `{file}`: {e.GetType().Name}: {e.Message}"); return null; }
        }
    }
}
