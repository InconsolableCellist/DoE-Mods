// Skin check 3: render two exported bundles (.avatar) the same way and measure how far apart they
// look. For proving an optimisation (merged materials, smaller textures) left a skin looking the
// same: it loads the files that ship, not the source.
//
//   (geometry and emission checks: unity/SkinCheck/fbx_compare.py, in Blender)
//   Unity.exe -batchmode -projectPath <project> -executeMethod DoEMod.Export.DoESkinCompare.Run
//     -a OLD.avatar -b NEW.avatar -out <dir> [-shapes "L Scythe=100,R Scythe=100"]
//   (no -nographics: it needs the GPU)
//
// For each eye state (emission red, emission white) and each view (front, three-quarters, sides,
// back, face, hand) it renders A and B under the same light, then writes:
//   <dir>/<state>-<view>-a.png, -b.png, -diff.png   (diff: |A-B| x 8, so 1/255 shows)
//   <dir>/sheet-<state>.png                          (A | B | diff, one row per view)
//   <dir>/report.md                                  (PSNR, SSIM, worst pixel, changed pixels, worst block)
// Exit code 0 when every view clears the bar (PSNR >= 45 dB, SSIM >= 0.995, worst 16x16 block mean
// difference <= 4/255), 1 otherwise. Unity renders with the bundle's own Standard materials; the
// in-game skin wears the game's shader, but what an optimisation can break (which texels each face
// shows, which faces glow) is the same under either.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace DoEMod.Export
{
    public static class DoESkinCompare
    {
        const int Size = 768;
        const double PsnrBar = 45.0, SsimBar = 0.995, BlockBar = 4.0 / 255.0;

        struct View { public string Name; public Vector3 Dir; public Func<Rig, (Vector3 c, float r)> Frame; }
        class Rig { public GameObject Go; public Bounds B; public Transform Head, EyeL, EyeR, HandR; public Vector3 Fwd; }

        public static void Run()
        {
            var a = Arg("-a"); var b = Arg("-b"); var outDir = Arg("-out");
            if (a == null || b == null || outDir == null) { Debug.LogError("DoESkinCompare: pass -a, -b and -out"); EditorApplication.Exit(2); return; }
            var shapes = ParseShapes(Arg("-shapes"));
            Directory.CreateDirectory(outDir);
            var ok = Compare(a, b, outDir, shapes);
            EditorApplication.Exit(ok ? 0 : 1);
        }

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        static Dictionary<string, float> ParseShapes(string s)
        {
            var d = new Dictionary<string, float>();
            if (string.IsNullOrEmpty(s)) return d;
            foreach (var part in s.Split(','))
            {
                var kv = part.Split('=');
                if (kv.Length == 2 && float.TryParse(kv[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var w)) d[kv[0].Trim()] = w;
            }
            return d;
        }

        static readonly (string name, Color colour)[] States =
        {
            ("red", new Color(1f, 0.12f, 0.04f) * 1.5f),
            ("white", Color.white * 4f),
        };

        static bool Compare(string pathA, string pathB, string outDir, Dictionary<string, float> shapes)
        {
            var views = new List<View>
            {
                new View { Name = "front", Dir = Vector3.zero },
                new View { Name = "front-left", Dir = new Vector3(0, -45, 0) },
                new View { Name = "left", Dir = new Vector3(0, -90, 0) },
                new View { Name = "back", Dir = new Vector3(0, 180, 0) },
                new View { Name = "right", Dir = new Vector3(0, 90, 0) },
                new View { Name = "front-right", Dir = new Vector3(0, 45, 0) },
                new View { Name = "face", Dir = new Vector3(8, 0, 0), Frame = r => r.Head ? (Mid(r) , Dist(r) * 1.6f) : (r.B.center, r.B.extents.magnitude) },
                new View { Name = "hand", Dir = new Vector3(15, 30, 0), Frame = r => r.HandR ? (r.HandR.position, r.B.extents.magnitude * 0.45f) : (r.B.center, r.B.extents.magnitude) },
            };

            var shots = new Dictionary<string, Color32[]>();   // key: side|state|view
            foreach (var (side, path) in new[] { ("a", pathA), ("b", pathB) })
            {
                var bundle = AssetBundle.LoadFromFile(path);
                if (bundle == null) { Debug.LogError($"DoESkinCompare: cannot load {path}"); return false; }
                try
                {
                    var prefab = bundle.LoadAllAssets<GameObject>()
                        .OrderByDescending(g => g.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length).FirstOrDefault();
                    if (prefab == null) { Debug.LogError($"DoESkinCompare: no prefab in {path}"); return false; }
                    var rig = Spawn(prefab, shapes);
                    try
                    {
                        foreach (var (state, colour) in States)
                        {
                            Emission(rig.Go, colour);
                            foreach (var v in views) shots[$"{side}|{state}|{v.Name}"] = Render(rig, v);
                        }
                    }
                    finally { UnityEngine.Object.DestroyImmediate(rig.Go); }
                }
                finally { bundle.Unload(true); }
            }

            var report = new StringBuilder();
            report.AppendLine($"# Skin comparison\n\nA: `{pathA}`  \nB: `{pathB}`  \n{Size}x{Size}, Standard materials, one directional light + flat ambient.\n");
            report.AppendLine($"Bar: PSNR >= {PsnrBar} dB, SSIM >= {SsimBar}, worst 16x16 block mean <= {BlockBar * 255:0.#}/255.\n");
            report.AppendLine("| state | view | PSNR dB | SSIM | worst pixel /255 | pixels > 2/255 | worst block /255 | |");
            report.AppendLine("|---|---|---|---|---|---|---|---|");
            var allOk = true;
            foreach (var (state, _) in States)
            {
                var rows = new List<(Color32[] a, Color32[] b, Color32[] d)>();
                foreach (var v in views)
                {
                    var A = shots[$"a|{state}|{v.Name}"]; var B = shots[$"b|{state}|{v.Name}"];
                    var m = Metrics(A, B, out var diff);
                    var pass = m.psnr >= PsnrBar && m.ssim >= SsimBar && m.block <= BlockBar;
                    allOk &= pass;
                    report.AppendLine($"| {state} | {v.Name} | {(double.IsInfinity(m.psnr) ? "identical" : m.psnr.ToString("0.0"))} | {m.ssim:0.0000} | {m.worst * 255:0} | {m.changed:0.000%} | {m.block * 255:0.0} | {(pass ? "pass" : "**FAIL**")} |");
                    Save(Path.Combine(outDir, $"{state}-{v.Name}-a.png"), A, Size, Size);
                    Save(Path.Combine(outDir, $"{state}-{v.Name}-b.png"), B, Size, Size);
                    Save(Path.Combine(outDir, $"{state}-{v.Name}-diff.png"), diff, Size, Size);
                    rows.Add((A, B, diff));
                }
                Sheet(Path.Combine(outDir, $"sheet-{state}.png"), rows);
            }
            report.AppendLine($"\n**{(allOk ? "PASS" : "FAIL")}**");
            File.WriteAllText(Path.Combine(outDir, "report.md"), report.ToString());
            Debug.Log("DoESkinCompare:\n" + report);
            return allOk;
        }

        static Vector3 Mid(Rig r) => r.EyeL && r.EyeR ? (r.EyeL.position + r.EyeR.position) * 0.5f : r.Head.position;
        static float Dist(Rig r) => r.EyeL && r.EyeR ? Mathf.Max(0.05f, Vector3.Distance(r.EyeL.position, r.EyeR.position)) * 2.5f : 0.3f;

        static Rig Spawn(GameObject prefab, Dictionary<string, float> shapes)
        {
            var go = UnityEngine.Object.Instantiate(prefab);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            foreach (var an in go.GetComponentsInChildren<Animator>(true)) an.enabled = false;
            var bounds = new Bounds(); var first = true;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                // Bundled shaders are player builds; the editor's own copy of the same shader renders reliably.
                var mats = r.sharedMaterials;
                foreach (var m in mats) if (m && m.shader) { var s = Shader.Find(m.shader.name); if (s) m.shader = s; }
                if (r is SkinnedMeshRenderer smr)
                {
                    smr.updateWhenOffscreen = true;
                    foreach (var kv in shapes) { var i = smr.sharedMesh ? smr.sharedMesh.GetBlendShapeIndex(kv.Key) : -1; if (i >= 0) smr.SetBlendShapeWeight(i, kv.Value); }
                }
                if (first) { bounds = r.bounds; first = false; } else bounds.Encapsulate(r.bounds);
            }
            var rig = new Rig { Go = go, B = bounds };
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "Head") rig.Head = t;
                else if (t.name == "LeftEye") rig.EyeL = t;
                else if (t.name == "RightEye") rig.EyeR = t;
                else if (t.name == "RightHand") rig.HandR = t;
            }
            // The face points from the head toward the eyes; views are measured from there.
            var f = rig.Head && rig.EyeL && rig.EyeR ? Mid(rig) - rig.Head.position : Vector3.forward;
            f.y = 0; rig.Fwd = f.sqrMagnitude > 1e-6f ? f.normalized : Vector3.forward;
            return rig;
        }

        static void Emission(GameObject go, Color c)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                {
                    if (!m || !m.HasProperty("_EmissionMap") || !m.GetTexture("_EmissionMap")) continue;
                    m.EnableKeyword("_EMISSION");
                    m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                    m.SetColor("_EmissionColor", c);
                }
        }

        static Color32[] Render(Rig rig, View v)
        {
            // Whole-body views frame the height (the scythes make the bounds wide, not tall).
            var (centre, radius) = v.Frame != null ? v.Frame(rig) : (rig.B.center, rig.B.extents.y * 1.08f);
            var look = Quaternion.LookRotation(-rig.Fwd, Vector3.up) * Quaternion.Euler(v.Dir);   // camera looks at the face from the front
            var camGo = new GameObject("compare cam");
            var lightGo = new GameObject("compare light");
            var rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
            try
            {
                var cam = camGo.AddComponent<Camera>();
                cam.fieldOfView = 30f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.22f, 0.22f, 0.24f);
                cam.nearClipPlane = 0.01f; cam.farClipPlane = 100f;
                var dist = radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
                cam.transform.rotation = look;
                cam.transform.position = centre - look * Vector3.forward * dist;
                cam.targetTexture = rt;

                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.1f;
                light.shadows = LightShadows.None;
                lightGo.transform.rotation = Quaternion.LookRotation(-rig.Fwd, Vector3.up) * Quaternion.Euler(35, -30, 0);
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.35f, 0.35f, 0.38f);
                RenderSettings.fog = false;

                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                var px = tex.GetPixels32();
                UnityEngine.Object.DestroyImmediate(tex);
                return px;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(camGo);
                UnityEngine.Object.DestroyImmediate(lightGo);
                rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
            }
        }

        static (double psnr, double ssim, double worst, double changed, double block) Metrics(Color32[] a, Color32[] b, out Color32[] diff)
        {
            diff = new Color32[a.Length];
            double se = 0; int worst = 0, changed = 0;
            var la = new double[a.Length]; var lb = new double[a.Length]; var dd = new double[a.Length];
            for (var i = 0; i < a.Length; i++)
            {
                int dr = Math.Abs(a[i].r - b[i].r), dg = Math.Abs(a[i].g - b[i].g), db = Math.Abs(a[i].b - b[i].b);
                se += dr * dr + dg * dg + db * db;
                var m = Math.Max(dr, Math.Max(dg, db));
                worst = Math.Max(worst, m);
                if (m > 2) changed++;
                dd[i] = (dr + dg + db) / (3.0 * 255.0);
                la[i] = (0.299 * a[i].r + 0.587 * a[i].g + 0.114 * a[i].b) / 255.0;
                lb[i] = (0.299 * b[i].r + 0.587 * b[i].g + 0.114 * b[i].b) / 255.0;
                var v = (byte)Math.Min(255, m * 8);
                diff[i] = new Color32(v, (byte)(v / 4), 0, 255);
            }
            var mse = se / (a.Length * 3.0);
            var psnr = mse <= 0 ? double.PositiveInfinity : 10 * Math.Log10(255.0 * 255.0 / mse);

            // SSIM on luminance over 8x8 windows (non-overlapping), and the worst 16x16 block of mean difference.
            double ssimSum = 0; int n = 0; double blockWorst = 0;
            const double c1 = 0.01 * 0.01, c2 = 0.03 * 0.03;
            for (var y = 0; y + 8 <= Size; y += 8)
                for (var x = 0; x + 8 <= Size; x += 8)
                {
                    double ma = 0, mb = 0;
                    for (var j = 0; j < 8; j++) for (var i = 0; i < 8; i++) { var k = (y + j) * Size + x + i; ma += la[k]; mb += lb[k]; }
                    ma /= 64; mb /= 64;
                    double va = 0, vb = 0, cov = 0;
                    for (var j = 0; j < 8; j++) for (var i = 0; i < 8; i++) { var k = (y + j) * Size + x + i; va += (la[k] - ma) * (la[k] - ma); vb += (lb[k] - mb) * (lb[k] - mb); cov += (la[k] - ma) * (lb[k] - mb); }
                    va /= 63; vb /= 63; cov /= 63;
                    ssimSum += ((2 * ma * mb + c1) * (2 * cov + c2)) / ((ma * ma + mb * mb + c1) * (va + vb + c2));
                    n++;
                }
            for (var y = 0; y + 16 <= Size; y += 16)
                for (var x = 0; x + 16 <= Size; x += 16)
                {
                    double s = 0;
                    for (var j = 0; j < 16; j++) for (var i = 0; i < 16; i++) s += dd[(y + j) * Size + x + i];
                    blockWorst = Math.Max(blockWorst, s / 256);
                }
            return (psnr, ssimSum / n, worst / 255.0, changed / (double)a.Length, blockWorst);
        }

        static void Save(string path, Color32[] px, int w, int h)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            t.SetPixels32(px); t.Apply();
            File.WriteAllBytes(path, t.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(t);
        }

        /// <summary>A | B | diff per row, each cell a quarter of the render.</summary>
        static void Sheet(string path, List<(Color32[] a, Color32[] b, Color32[] d)> rows)
        {
            const int cell = Size / 4;
            var w = cell * 3; var h = cell * rows.Count;
            var px = new Color32[w * h];
            for (var r = 0; r < rows.Count; r++)
            {
                var cols = new[] { rows[r].a, rows[r].b, rows[r].d };
                var y0 = (rows.Count - 1 - r) * cell;   // first row at the top
                for (var c = 0; c < 3; c++)
                    for (var y = 0; y < cell; y++)
                        for (var x = 0; x < cell; x++)
                            px[(y0 + y) * w + c * cell + x] = cols[c][(y * 4) * Size + x * 4];
            }
            Save(path, px, w, h);
        }
    }
}
#endif
