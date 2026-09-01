using System;
using UnityEngine;
using Il2Cpp;

namespace CustomAvatars.Recon
{
    /// <summary>
    /// Dumps the visual half of a player: the <c>CharacterPrefab</c> and its model hierarchy.
    ///
    /// The v0.1.0 pass missed all of this because it only walked the AvatarPlayer transform —
    /// and the model turns out to be a **root-level GameObject** (`Model_&lt;nick&gt;`), not a
    /// child of `Player_&lt;nick&gt;`. Everything Phase 2 needs — the merged SkinnedMeshRenderer,
    /// the bone names, the character shader, VRIK, the jaw bone — lives over there.
    /// </summary>
    public static class ModelRecon
    {
        /// <summary>Dump a CharacterPrefab: its own serialized settings, then its whole subtree.</summary>
        public static void DumpCharacterPrefab(string label, CharacterPrefab cp)
        {
            if (!Interop.Alive(cp)) { ReconLog.KeyValue(label, "<null>"); return; }

            ReconLog.Line();
            ReconLog.Line($"### {label} — CharacterPrefab `{Interop.Name(cp)}`");
            DumpAnchoring(cp.transform);

            ReconLog.Line();
            ReconLog.Line("#### Serialized settings");
            ReconLog.TryKeyValue("scale", () => cp.scale);
            // The two shaders Phase 2 has to co-exist with: the character shader our custom
            // avatars sit beside, and the dissolve shader we're initially skipping.
            ReconLog.TryKeyValue("sourceShader", () => ShaderName(cp.sourceShader));
            ReconLog.TryKeyValue("dissolveShader", () => ShaderName(cp.dissolveShader));
            ReconLog.TryKeyValue("visibleDistance", () => cp.visibleDistance);
            ReconLog.TryKeyValue("jaw angles (closed/open)", () => $"{cp.closedJawAngle} / {cp.openedJawAngle}");
            ReconLog.TryKeyValue("voiceEnergyOverride", () => cp.voiceEnergyOverride);
            ReconLog.TryKeyValue("blinkTime", () => cp.blinkTime);
            ReconLog.TryKeyValue("audioTap (VivoxParticipantTap)", () => Describe(cp.audioTap));
            ReconLog.TryKeyValue("glancer", () => Describe(cp.glancer));

            // FinalIK: PLAN 2b wants to reuse the game's own VRIK/GrounderIK rather than
            // bundling FinalIK, so confirm the live instances and where they sit.
            ReconLog.TryKeyValue("VRIK (ik)", () => Describe(cp.ik));
            ReconLog.TryKeyValue("GrounderIK (grounderPrefab)", () => Describe(cp.grounderPrefab));

            ReconLog.Try("defaultBlendShapes", () =>
            {
                var shapes = cp.defaultBlendShapes;
                if (shapes == null) { ReconLog.KeyValue("defaultBlendShapes", "null"); return; }
                ReconLog.KeyValue("defaultBlendShapes", $"{shapes.Length} entries");
                for (var i = 0; i < shapes.Length; i++)
                {
                    var bs = shapes[i];
                    if (!Interop.Alive(bs)) continue;
                    ReconLog.Line($"  - `{bs.name}` = {bs.weight}");
                }
            });

            // The merged mesh itself — private field on CharacterPrefab, and the single most
            // important object in the whole recon pass.
            ReconLog.Try("characterMesh", () =>
            {
                var smr = cp.characterMesh;
                if (!Interop.Alive(smr)) { ReconLog.KeyValue("characterMesh", "<null — mesh not built yet?>"); return; }
                ReconLog.KeyValue("characterMesh", Interop.ScenePath(smr.transform));
                HierarchyDump.SkinnedMesh(smr);
            });

            ReconLog.Line();
            ReconLog.Line($"#### All renderers under `{Interop.Name(cp)}`");
            RendererList(cp.transform);

            ReconLog.Line();
            ReconLog.Line($"#### All SkinnedMeshRenderers under `{Interop.Name(cp)}`");
            HierarchyDump.SkinnedMeshes(cp.transform);

            ReconLog.Line();
            ReconLog.Line($"#### Hierarchy of `{Interop.Name(cp)}`");
            HierarchyDump.Tree(cp.transform);
        }

        /// <summary>
        /// A model root that reports no parent is a root-level object; say so explicitly
        /// rather than leaving it to be inferred from a path with no slashes in it.
        /// </summary>
        public static void DumpAnchoring(Transform t)
        {
            if (!Interop.Alive(t)) { ReconLog.KeyValue("transform", "<null>"); return; }
            ReconLog.TryKeyValue("path", () => Interop.ScenePath(t));
            ReconLog.TryKeyValue("parent", () => Interop.Alive(t.parent)
                ? Interop.ScenePath(t.parent)
                : "<NONE — this is a scene-root object>");
            ReconLog.TryKeyValue("scene", () => t.gameObject.scene.name);
            ReconLog.TryKeyValue("world position", () => Interop.Vec(t.position));
            ReconLog.TryKeyValue("lossyScale", () => Interop.Vec(t.lossyScale));
        }

        /// <summary>Depth-limited dump of an arbitrary subtree, e.g. the SteamVR camera rig.</summary>
        public static void DumpSubtree(string label, Transform t, int maxDepth)
        {
            if (!Interop.Alive(t)) { ReconLog.KeyValue(label, "<null>"); return; }
            ReconLog.Line();
            ReconLog.Line($"### {label}");
            DumpAnchoring(t);
            ReconLog.Line();
            RendererList(t);
            ReconLog.Line();
            HierarchyDump.Tree(t, maxDepth);
        }

        /// <summary>Walk to the scene root of a transform, so we can dump the whole rig it belongs to.</summary>
        public static Transform RootOf(Transform t)
        {
            if (!Interop.Alive(t)) return null;
            var guard = 0;
            while (Interop.Alive(t.parent) && guard++ < 64) t = t.parent;
            return t;
        }

        private static void RendererList(Transform root)
        {
            ReconLog.Try("renderers", () =>
            {
                var renderers = root.GetComponentsInChildren<Renderer>(true);
                if (renderers == null || renderers.Length == 0) { ReconLog.Line("- _no renderers_"); return; }
                var visible = 0;
                for (var i = 0; i < renderers.Length; i++)
                {
                    var r = renderers[i];
                    if (!Interop.Alive(r)) continue;
                    var on = false;
                    try { on = r.enabled && r.gameObject.activeInHierarchy; } catch { }
                    if (on) visible++;
                    ReconLog.Line($"- {(on ? "ON " : "off")} `{Interop.ScenePath(r.transform)}` <{HierarchyDump.TypeName(r)}>");
                }
                ReconLog.KeyValue("renderer total", $"{renderers.Length} ({visible} visible)");
            });
        }

        private static string ShaderName(Shader s) => Interop.Alive(s) ? s.name : "<null>";

        /// <summary>
        /// Not everything on CharacterPrefab is a Component — Glancer, for one, is a plain
        /// class — so describe by interop base type and only reach for a transform when the
        /// object actually has one.
        /// </summary>
        private static string Describe(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase o)
        {
            if (!Interop.Alive(o)) return "<null>";
            var type = HierarchyDump.TypeName(o);
            return o is Component c ? $"{type} on `{Interop.ScenePath(c.transform)}`" : type;
        }

        /// <summary>
        /// Closes the last GAME-INTERNALS open question: the prefab-name → prefab table that
        /// every PhotonNetwork.Instantiate resolves through, and Phase 4b's injection point.
        /// </summary>
        public static void DumpNetworkObjectPool()
        {
            ReconLog.Section("NetworkObjectPool prefab table");
            ReconLog.Try("NetworkObjectPool", () =>
            {
                var pool = NetworkObjectPool.Instance;
                if (!Interop.Alive(pool)) { ReconLog.Line("_NetworkObjectPool.Instance is null (not in a room yet?)_"); return; }
                ReconLog.KeyValue("instance", Interop.ScenePath(pool.transform));

                var unpooled = NetworkObjectPool.UnpooledPrefabs;
                if (ReferenceEquals(unpooled, null)) { ReconLog.Line("_UnpooledPrefabs is null_"); return; }

                ReconLog.KeyValue("UnpooledPrefabs count", unpooled.Count);
                ReconLog.Line("```");
                var e = unpooled.GetEnumerator();
                var n = 0;
                while (e.MoveNext() && n++ < 500)
                {
                    var kv = e.Current;
                    ReconLog.Line($"{kv.Key}  ->  {(Interop.Alive(kv.Value) ? kv.Value.name : "<null>")}");
                }
                ReconLog.Line("```");
            });
        }
    }
}
