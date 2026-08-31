using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace DoEFriendsMod.Recon
{
    /// <summary>
    /// GameObject-tree and renderer printers. The parallel-rig avatar plan (PLAN.md 2b) lives
    /// or dies on knowing the real runtime hierarchy and the exact bone names the merged
    /// SkinnedMeshRenderer binds to, so these dumps are deliberately verbose.
    /// </summary>
    public static class HierarchyDump
    {
        public static void Tree(Transform root, int maxDepth = -1)
        {
            if (!Interop.Alive(root)) { ReconLog.Line("_(null root)_"); return; }
            if (maxDepth < 0) maxDepth = ModConfig.HierarchyMaxDepth.Value;

            ReconLog.Line("```");
            WalkTree(root, 0, maxDepth);
            ReconLog.Line("```");
        }

        private static void WalkTree(Transform t, int depth, int maxDepth)
        {
            if (!Interop.Alive(t)) return;

            var indent = new string(' ', depth * 2);
            var flags = new StringBuilder();
            try
            {
                if (!t.gameObject.activeSelf) flags.Append(" [inactive]");
                if (t.gameObject.layer != 0) flags.Append($" [layer {t.gameObject.layer}]");
            }
            catch { /* keep walking */ }

            ReconLog.Line($"{indent}{SafeName(t)}{flags}  {ComponentList(t)}");

            if (depth >= maxDepth)
            {
                try
                {
                    if (t.childCount > 0)
                        ReconLog.Line($"{indent}  … {t.childCount} more children (depth limit)");
                }
                catch { }
                return;
            }

            try
            {
                for (var i = 0; i < t.childCount; i++)
                    WalkTree(t.GetChild(i), depth + 1, maxDepth);
            }
            catch (Exception e) { ReconLog.Line($"{indent}  <children unavailable: {e.Message}>"); }
        }

        private static string SafeName(Transform t)
        {
            try { return t.name; } catch { return "<unnamed>"; }
        }

        /// <summary>Component type names in angle brackets, e.g. &lt;AvatarPlayer, PhotonView&gt;.</summary>
        public static string ComponentList(Transform t)
        {
            try
            {
                var comps = t.GetComponents<Component>();
                if (comps == null || comps.Length == 0) return "";
                var names = new List<string>(comps.Length);
                for (var i = 0; i < comps.Length; i++)
                {
                    var c = comps[i];
                    names.Add(!Interop.Alive(c) ? "<missing script>" : TypeName(c));
                }
                return "<" + string.Join(", ", names) + ">";
            }
            catch (Exception e) { return $"<components unavailable: {e.GetType().Name}>"; }
        }

        /// <summary>
        /// Interop proxy types keep an "Il2Cpp" namespace prefix that adds nothing to a
        /// transcript we're reading against dump.cs, so strip it.
        /// </summary>
        public static string TypeName(object o)
        {
            if (o == null) return "null";
            var n = o.GetType().FullName ?? o.GetType().Name;
            if (n.StartsWith("Il2Cpp", StringComparison.Ordinal)) n = n.Substring("Il2Cpp".Length).TrimStart('.');
            return n;
        }

        /// <summary>Every SkinnedMeshRenderer under <paramref name="root"/>, in full detail.</summary>
        public static void SkinnedMeshes(Transform root)
        {
            SkinnedMeshRenderer[] smrs;
            try { smrs = root.GetComponentsInChildren<SkinnedMeshRenderer>(true); }
            catch (Exception e) { ReconLog.Error("GetComponentsInChildren<SkinnedMeshRenderer>", e); return; }

            if (smrs == null || smrs.Length == 0) { ReconLog.Line("_(no SkinnedMeshRenderers)_"); return; }

            for (var i = 0; i < smrs.Length; i++)
                SkinnedMesh(smrs[i]);
        }

        public static void SkinnedMesh(SkinnedMeshRenderer smr)
        {
            if (!Interop.Alive(smr)) return;

            ReconLog.Line();
            ReconLog.Line($"#### SkinnedMeshRenderer `{Interop.Name(smr)}`");
            ReconLog.TryKeyValue("path", () => Interop.ScenePath(smr.transform));
            ReconLog.TryKeyValue("enabled", () => $"{smr.enabled} (gameObject active: {smr.gameObject.activeInHierarchy})");
            ReconLog.TryKeyValue("quality", () => smr.quality);
            ReconLog.TryKeyValue("updateWhenOffscreen", () => smr.updateWhenOffscreen);
            ReconLog.TryKeyValue("rootBone", () => Interop.ScenePath(smr.rootBone));
            ReconLog.TryKeyValue("bounds", () => $"center {Interop.Vec(smr.bounds.center)} size {Interop.Vec(smr.bounds.size)}");

            ReconLog.Try("mesh", () =>
            {
                var mesh = smr.sharedMesh;
                if (!Interop.Alive(mesh)) { ReconLog.KeyValue("sharedMesh", "null"); return; }
                ReconLog.KeyValue("sharedMesh", $"{Interop.Name(mesh)} ({mesh.vertexCount} verts, {mesh.subMeshCount} submeshes)");

                var count = mesh.blendShapeCount;
                ReconLog.KeyValue("blendShapeCount", count);
                if (count > 0)
                {
                    var cap = Math.Min(count, ModConfig.MaxBlendShapesLogged.Value);
                    ReconLog.Line();
                    ReconLog.Line("Blend shapes:");
                    ReconLog.Line("```");
                    for (var i = 0; i < cap; i++)
                        ReconLog.Line($"{i,4}  {mesh.GetBlendShapeName(i)}");
                    if (cap < count) ReconLog.Line($"… {count - cap} more (raise MaxBlendShapesLogged)");
                    ReconLog.Line("```");
                }
            });

            ReconLog.Try("bones", () =>
            {
                var bones = smr.bones;
                if (bones == null) { ReconLog.KeyValue("bones", "null"); return; }
                ReconLog.KeyValue("bone count", bones.Length);
                ReconLog.Line();
                ReconLog.Line("Bones:");
                ReconLog.Line("```");
                for (var i = 0; i < bones.Length; i++)
                    ReconLog.Line($"{i,4}  {(Interop.Alive(bones[i]) ? Interop.ScenePath(bones[i]) : "<null>")}");
                ReconLog.Line("```");
            });

            ReconLog.Try("materials", () => Materials(smr));
        }

        /// <summary>
        /// Shader name + keywords per material. This is how we learn what the character shader
        /// actually is (CharacterPrefab.sourceShader) and whether it is instancing-aware.
        /// </summary>
        public static void Materials(Renderer r)
        {
            var mats = r.sharedMaterials;
            if (mats == null || mats.Length == 0) { ReconLog.KeyValue("materials", "none"); return; }

            ReconLog.Line();
            ReconLog.Line("Materials:");
            for (var i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (!Interop.Alive(m)) { ReconLog.Line($"- [{i}] <null>"); continue; }
                var shaderName = Interop.Alive(m.shader) ? m.shader.name : "<null shader>";
                ReconLog.Line($"- [{i}] `{Interop.Name(m)}` — shader `{shaderName}` " +
                              $"(renderQueue {m.renderQueue}, GPU instancing {SafeInstancing(m)})");
                try
                {
                    var kw = m.shaderKeywords;
                    if (kw != null && kw.Length > 0)
                        ReconLog.Line($"  - keywords: {string.Join(" ", kw)}");
                }
                catch { }
            }
        }

        private static string SafeInstancing(Material m)
        {
            try { return m.enableInstancing.ToString(); } catch { return "?"; }
        }
    }
}
