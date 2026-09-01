// DoE Friends-Mod — avatar bundle exporter.
// Drop this folder (AvatarExport/) into the Assets/ of your ALCOM/VCC avatar project
// (Unity 2022.3.x). Select the avatar prefab or scene instance, then:
//   Tools ▸ DoE Mod ▸ Export Selected Avatar
// Output: <project>/DoEExport/<name>.avatar + <name>.manifest.json
//
// What it does:
//   1. Clones the avatar, bakes any VRCFury Armature Links (clothing whose armature is
//      declared to be the avatar's — see ArmatureLinker.cs), then strips every VRC SDK
//      component (PhysBones, descriptors, constraints, contacts, stations...) and
//      missing-script stubs.
//   2. Validates: humanoid Animator, head bone, at least one SkinnedMeshRenderer.
//   3. Scans all renderers for Unified Expressions blendshapes (exact name or with
//      common prefixes: "UE.", "ft.", "v2/", "FT/"), VRC visemes (vrc.v_*), and
//      humanoid eye bones. Warns on shader names it can't vouch for.
//   4. Saves the stripped clone as a temp prefab, builds a StandaloneWindows64
//      AssetBundle (LZ4/chunked), renames it to <name>.avatar, writes the manifest
//      (including sha256 of the bundle) that the mod's AvatarSwapper consumes.
//
// Target constraints, measured in-game 2026-08-31 (docs/GAME-INTERNALS.md):
//   Unity 2022.3.62f2 · DX11 · stereo = SinglePassInstanced · skinWeights = FourBones
//   90 Hz target · game rig is humanoid, 98 bones, UE4 naming, with eyeL/eyeR/jaw bones.

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace DoEMod.Export
{
    public static class UEShapes
    {
        // Canonical Unified Expressions order — MUST stay byte-identical to the mod's
        // UEShapes.cs (it defines network shape IDs). Append only, never reorder.
        public static readonly string[] Canonical =
        {
            // Eye gaze (8)
            "EyeLookOutRight","EyeLookInRight","EyeLookUpRight","EyeLookDownRight",
            "EyeLookOutLeft","EyeLookInLeft","EyeLookUpLeft","EyeLookDownLeft",
            // Eyelids / pupils (10)
            "EyeClosedRight","EyeClosedLeft","EyeSquintRight","EyeSquintLeft",
            "EyeWideRight","EyeWideLeft","EyeDilationRight","EyeDilationLeft",
            "EyeConstrictRight","EyeConstrictLeft",
            // Brow (8)
            "BrowPinchRight","BrowPinchLeft","BrowLowererRight","BrowLowererLeft",
            "BrowInnerUpRight","BrowInnerUpLeft","BrowOuterUpRight","BrowOuterUpLeft",
            // Nose (6)
            "NoseSneerRight","NoseSneerLeft","NasalDilationRight","NasalDilationLeft",
            "NasalConstrictRight","NasalConstrictLeft",
            // Cheek (6)
            "CheekSquintRight","CheekSquintLeft","CheekPuffRight","CheekPuffLeft",
            "CheekSuckRight","CheekSuckLeft",
            // Jaw (8)
            "JawOpen","MouthClosed","JawRight","JawLeft","JawForward","JawBackward",
            "JawClench","JawMandibleRaise",
            // Lips: suck/funnel/pucker (14)
            "LipSuckUpperRight","LipSuckUpperLeft","LipSuckLowerRight","LipSuckLowerLeft",
            "LipSuckCornerRight","LipSuckCornerLeft",
            "LipFunnelUpperRight","LipFunnelUpperLeft","LipFunnelLowerRight","LipFunnelLowerLeft",
            "LipPuckerUpperRight","LipPuckerUpperLeft","LipPuckerLowerRight","LipPuckerLowerLeft",
            // Mouth (26)
            "MouthUpperUpRight","MouthUpperUpLeft","MouthLowerDownRight","MouthLowerDownLeft",
            "MouthUpperDeepenRight","MouthUpperDeepenLeft",
            "MouthUpperRight","MouthUpperLeft","MouthLowerRight","MouthLowerLeft",
            "MouthCornerPullRight","MouthCornerPullLeft",
            "MouthCornerSlantRight","MouthCornerSlantLeft",
            "MouthFrownRight","MouthFrownLeft","MouthStretchRight","MouthStretchLeft",
            "MouthDimpleRight","MouthDimpleLeft","MouthRaiserUpper","MouthRaiserLower",
            "MouthPressRight","MouthPressLeft","MouthTightenerRight","MouthTightenerLeft",
            // Tongue (12)
            "TongueOut","TongueUp","TongueDown","TongueRight","TongueLeft","TongueRoll",
            "TongueBendDown","TongueCurlUp","TongueSquish","TongueFlat",
            "TongueTwistRight","TongueTwistLeft",
        };

        public static readonly string[] MeshPrefixes = { "", "UE.", "ft.", "FT.", "v2/", "FT/", "v2." };

        public enum AliasKind
        {
            /// <summary>Same motion, different naming convention. Always safe.</summary>
            Rename,
            /// <summary>Source is coarser; the targets are its components. Sharing one index is correct.</summary>
            Split,
            /// <summary>A different motion faked with a near neighbour. Off by default — a
            /// plausible-but-wrong mapping is worse than a shape that simply doesn't move.</summary>
            Approximate,
        }

        /// <summary>
        /// Fallback source names per canonical UE shape, tried in order and only when the
        /// canonical name is absent. Covers ARKit naming and the older, coarser UE names that
        /// most existing avatars were actually authored against.
        ///
        /// <see cref="AliasKind.Split"/> entries deliberately point several UE shapes at one
        /// source (ARKit `browDownLeft` feeds both BrowLowererLeft and BrowPinchLeft; one
        /// `mouthPucker` feeds all four pucker shapes) — that is the documented VRCFT
        /// correspondence, and it means the runtime must COMBINE those with max() rather than
        /// last-write-wins. The exporter flags every collision it creates.
        /// </summary>
        public static readonly Dictionary<string, (string src, AliasKind kind)[]> Aliases =
            new Dictionary<string, (string, AliasKind)[]>
        {
            // Gaze — only relevant for avatars without eye bones; bones are preferred.
            {"EyeLookOutRight",   new[]{("eyeLookOutRight", AliasKind.Rename)}},
            {"EyeLookInRight",    new[]{("eyeLookInRight", AliasKind.Rename)}},
            {"EyeLookUpRight",    new[]{("eyeLookUpRight", AliasKind.Rename)}},
            {"EyeLookDownRight",  new[]{("eyeLookDownRight", AliasKind.Rename)}},
            {"EyeLookOutLeft",    new[]{("eyeLookOutLeft", AliasKind.Rename)}},
            {"EyeLookInLeft",     new[]{("eyeLookInLeft", AliasKind.Rename)}},
            {"EyeLookUpLeft",     new[]{("eyeLookUpLeft", AliasKind.Rename)}},
            {"EyeLookDownLeft",   new[]{("eyeLookDownLeft", AliasKind.Rename)}},

            {"EyeClosedRight",    new[]{("eyeBlinkRight", AliasKind.Rename), ("vrc.blink_right", AliasKind.Rename)}},
            {"EyeClosedLeft",     new[]{("eyeBlinkLeft", AliasKind.Rename), ("vrc.blink_left", AliasKind.Rename)}},

            // ARKit browDown is documented as feeding both the lowerer and the pinch.
            {"BrowLowererRight",  new[]{("BrowDownRight", AliasKind.Rename), ("browDownRight", AliasKind.Split)}},
            {"BrowLowererLeft",   new[]{("BrowDownLeft", AliasKind.Rename), ("browDownLeft", AliasKind.Split)}},
            {"BrowPinchRight",    new[]{("browDownRight", AliasKind.Split), ("BrowDownRight", AliasKind.Split)}},
            {"BrowPinchLeft",     new[]{("browDownLeft", AliasKind.Split), ("BrowDownLeft", AliasKind.Split)}},
            {"BrowInnerUpRight",  new[]{("browInnerUp", AliasKind.Split), ("BrowInnerUp", AliasKind.Split)}},
            {"BrowInnerUpLeft",   new[]{("browInnerUp", AliasKind.Split), ("BrowInnerUp", AliasKind.Split)}},
            {"BrowOuterUpRight",  new[]{("browOuterUpRight", AliasKind.Rename)}},
            {"BrowOuterUpLeft",   new[]{("browOuterUpLeft", AliasKind.Rename)}},

            // MouthCornerPull IS the UE name for a smile — MouthSmile* is the same motion
            // under the older name, so this is a rename, not an approximation.
            {"MouthCornerPullRight",  new[]{("MouthSmileRight", AliasKind.Rename), ("mouthSmileRight", AliasKind.Rename)}},
            {"MouthCornerPullLeft",   new[]{("MouthSmileLeft", AliasKind.Rename), ("mouthSmileLeft", AliasKind.Rename)}},
            // Slant is a distinct smirk, not a smile. Faking it from the smile makes every
            // smirk read as a grin — available, but opt-in.
            {"MouthCornerSlantRight", new[]{("MouthSmileSharpRight", AliasKind.Rename), ("MouthSmileRight", AliasKind.Approximate)}},
            {"MouthCornerSlantLeft",  new[]{("MouthSmileSharpLeft", AliasKind.Rename), ("MouthSmileLeft", AliasKind.Approximate)}},
            // Deepen and UpperUp are genuinely different motions; doubling them onto one
            // shape just makes the lip raise stronger than intended.
            {"MouthUpperDeepenRight", new[]{("MouthUpperUpRight", AliasKind.Approximate)}},
            {"MouthUpperDeepenLeft",  new[]{("MouthUpperUpLeft", AliasKind.Approximate)}},

            {"MouthRaiserUpper",      new[]{("mouthShrugUpper", AliasKind.Rename), ("MouthShrugUpper", AliasKind.Rename)}},
            {"MouthRaiserLower",      new[]{("mouthShrugLower", AliasKind.Rename), ("MouthShrugLower", AliasKind.Rename)}},

            // Pucker / funnel / suck: one coarse source, four UE components.
            {"LipPuckerUpperRight", new[]{("LipPuckerRight", AliasKind.Split), ("mouthPucker", AliasKind.Split)}},
            {"LipPuckerUpperLeft",  new[]{("LipPuckerLeft", AliasKind.Split), ("mouthPucker", AliasKind.Split)}},
            {"LipPuckerLowerRight", new[]{("LipPuckerRight", AliasKind.Split), ("mouthPucker", AliasKind.Split)}},
            {"LipPuckerLowerLeft",  new[]{("LipPuckerLeft", AliasKind.Split), ("mouthPucker", AliasKind.Split)}},
            {"LipFunnelUpperRight", new[]{("LipFunnelUpper", AliasKind.Split), ("mouthFunnel", AliasKind.Split)}},
            {"LipFunnelUpperLeft",  new[]{("LipFunnelUpper", AliasKind.Split), ("mouthFunnel", AliasKind.Split)}},
            {"LipFunnelLowerRight", new[]{("LipFunnelLower", AliasKind.Split), ("mouthFunnel", AliasKind.Split)}},
            {"LipFunnelLowerLeft",  new[]{("LipFunnelLower", AliasKind.Split), ("mouthFunnel", AliasKind.Split)}},
            {"LipSuckUpperRight",   new[]{("LipSuckUpper", AliasKind.Split), ("mouthRollUpper", AliasKind.Split)}},
            {"LipSuckUpperLeft",    new[]{("LipSuckUpper", AliasKind.Split), ("mouthRollUpper", AliasKind.Split)}},
            {"LipSuckLowerRight",   new[]{("LipSuckLower", AliasKind.Split), ("mouthRollLower", AliasKind.Split)}},
            {"LipSuckLowerLeft",    new[]{("LipSuckLower", AliasKind.Split), ("mouthRollLower", AliasKind.Split)}},

            {"CheekPuffRight",  new[]{("cheekPuff", AliasKind.Split), ("CheekPuff", AliasKind.Split)}},
            {"CheekPuffLeft",   new[]{("cheekPuff", AliasKind.Split), ("CheekPuff", AliasKind.Split)}},
            {"CheekSquintRight",new[]{("cheekSquintRight", AliasKind.Rename)}},
            {"CheekSquintLeft", new[]{("cheekSquintLeft", AliasKind.Rename)}},

            {"NoseSneerRight",  new[]{("noseSneerRight", AliasKind.Rename)}},
            {"NoseSneerLeft",   new[]{("noseSneerLeft", AliasKind.Rename)}},

            {"JawForward",  new[]{("jawForward", AliasKind.Rename)}},
            {"JawRight",    new[]{("jawRight", AliasKind.Rename)}},
            {"JawLeft",     new[]{("jawLeft", AliasKind.Rename)}},
            {"JawOpen",     new[]{("jawOpen", AliasKind.Rename)}},
            {"MouthClosed", new[]{("mouthClose", AliasKind.Rename), ("MouthClose", AliasKind.Rename)}},
        };
    }

    public class DoEAvatarExporter : EditorWindow
    {
        const string ExportDirName = "DoEExport";
        const string TempAssetDir = "Assets/DoEExport_Temp";
        static readonly string[] Visemes =
            { "sil","pp","ff","th","dd","kk","ch","ss","nn","rr","aa","e","ih","oh","ou" };

        // Shaders known to render correctly under Single Pass Instanced, which the game uses.
        // Anything outside this list isn't necessarily broken — it just hasn't been vouched
        // for, and a shader that isn't SPS-I aware renders in one eye only.
        static readonly string[] SpsiKnownGood =
        {
            "lilToon", "Poiyomi", ".poiyomi", "Standard", "Unlit/", "UnityChanToonShader",
            "Universal Render Pipeline/", "Mobile/", "Sprites/", "TextMeshPro",
        };

        // The game runs QualitySettings.skinWeights = FourBones. Vertices weighted to more
        // than four bones lose their extra influences at runtime and deform wrong.
        const int GameBoneWeightLimit = 4;

        // GameObjects whose subtree we never take blendshapes from: VRCFury feature holders
        // and face-tracking debug displays. A real avatar's FT debug panel carries the whole
        // UE shape set, and a first-match scan will happily map your gaze onto a floating
        // debug window instead of your face.
        static readonly string[] ScaffoldingMarkers = { "VRCFury", "FT_Debug", "Face Tracking UE Debug" };

        /// <summary>True for a hierarchy path that lives inside feature-holder or debug scaffolding.</summary>
        static bool IsScaffoldingPath(string path) =>
            ScaffoldingMarkers.Any(m => path.IndexOf(m, StringComparison.OrdinalIgnoreCase) >= 0);

        const string StripInactivePref = "DoEMod.Export.StripInactive";
        static bool StripInactive
        {
            get => EditorPrefs.GetBool(StripInactivePref, true);
            set => EditorPrefs.SetBool(StripInactivePref, value);
        }

        [MenuItem("Tools/Foxipso/DoE Avatar Export/Export Selected Avatar", priority = 0)]
        public static void ExportSelected() => Run(buildBundle: true);

        [MenuItem("Tools/Foxipso/DoE Avatar Export/Analyze Selected Avatar (no bundle)", priority = 1)]
        public static void AnalyzeSelected() => Run(buildBundle: false);

        const string ApproxAliasPref = "DoEMod.Export.ApproxAliases";
        static bool AllowApproximateAliases
        {
            get => EditorPrefs.GetBool(ApproxAliasPref, false);
            set => EditorPrefs.SetBool(ApproxAliasPref, value);
        }

        [MenuItem("Tools/Foxipso/DoE Avatar Export/Allow Approximate Shape Aliases", priority = 21)]
        static void ToggleApproxAliases() => AllowApproximateAliases = !AllowApproximateAliases;

        [MenuItem("Tools/Foxipso/DoE Avatar Export/Allow Approximate Shape Aliases", true)]
        static bool ToggleApproxAliasesValidate()
        {
            Menu.SetChecked("Tools/Foxipso/DoE Avatar Export/Allow Approximate Shape Aliases", AllowApproximateAliases);
            return true;
        }

        [MenuItem("Tools/Foxipso/DoE Avatar Export/Generate Face Overrides", priority = 2)]
        public static void GenerateFaceOverrides()
        {
            var src = Selection.activeGameObject;
            if (src == null)
            {
                EditorUtility.DisplayDialog("DoE Export", "Select the avatar first.", "OK");
                return;
            }
            try { FaceOverrideGenerator.Generate(src); }
            catch (Exception e) { Debug.LogError($"DoE face override scan failed: {e}"); }
        }

        // Armature Link is the one VRCFury feature worth applying at export: it is a static
        // statement about the rig rather than a runtime choice, and skipping it leaves clothing
        // skinned to its own armature, not following the body. Toggles, Full Controller and
        // mesh merging stay unapplied by design — enable what you want, then export.
        const string ArmatureLinkPref = "DoEMod.Export.ArmatureLinks";
        static bool ApplyArmatureLinks
        {
            get => EditorPrefs.GetBool(ArmatureLinkPref, true);
            set => EditorPrefs.SetBool(ArmatureLinkPref, value);
        }

        [MenuItem("Tools/Foxipso/DoE Avatar Export/Apply VRCFury Armature Links", priority = 22)]
        static void ToggleArmatureLinks() => ApplyArmatureLinks = !ApplyArmatureLinks;

        [MenuItem("Tools/Foxipso/DoE Avatar Export/Apply VRCFury Armature Links", true)]
        static bool ToggleArmatureLinksValidate()
        {
            Menu.SetChecked("Tools/Foxipso/DoE Avatar Export/Apply VRCFury Armature Links", ApplyArmatureLinks);
            return true;
        }

        [MenuItem("Tools/Foxipso/DoE Avatar Export/Strip Inactive Objects", priority = 20)]
        static void ToggleStripInactive() => StripInactive = !StripInactive;

        [MenuItem("Tools/Foxipso/DoE Avatar Export/Strip Inactive Objects", true)]
        static bool ToggleStripInactiveValidate()
        {
            Menu.SetChecked("Tools/Foxipso/DoE Avatar Export/Strip Inactive Objects", StripInactive);
            return true;
        }

        static void Run(bool buildBundle)
        {
            var src = Selection.activeGameObject;
            if (src == null) { EditorUtility.DisplayDialog("DoE Export", "Select the avatar prefab or scene instance first.", "OK"); return; }
            try { Export(src, buildBundle); }
            finally { Cleanup(); }
        }

        static void Export(GameObject src, bool buildBundle)
        {
            string avatarName = Sanitize(src.name);
            var report = new StringBuilder(
                $"=== DoE avatar {(buildBundle ? "export" : "analysis")}: {avatarName} ===\n");

            // --- 1. Clone & strip -------------------------------------------------
            var clone = UnityEngine.Object.Instantiate(src);
            clone.name = avatarName;
            int removedMissing = 0, removedVrc = 0;

            // --- 1a. Bake Armature Links BEFORE anything reads the hierarchy -------
            // Order matters twice over. Dynamics below are recorded as paths relative to the
            // avatar root, so a skirt bone captured before the link is stored at a path that
            // won't exist after it. And the inactive-object strip protects bones by walking
            // the renderers' bone arrays — which only names the avatar's bones once the
            // garment's have been merged into them.
            int linksApplied = 0;
            if (ApplyArmatureLinks)
                linksApplied = ArmatureLinker.Apply(clone, StripInactive, report);

            // --- 1b. Capture PhysBone dynamics BEFORE stripping --------------------
            // The strip pass destroys VRCPhysBone/VRCPhysBoneCollider, which is correct — they
            // would be missing-script stubs in game. But the *configuration* is the only record
            // of which bones are supposed to swing, so read it out first. Reflection rather than
            // an SDK reference, so this keeps working across SDK versions and in projects that
            // don't have PhysBones at all.
            var dynChains = new List<object>();
            var dynColliders = new List<object>();
            CaptureDynamics(clone, dynChains, dynColliders, report);

            foreach (var t in clone.GetComponentsInChildren<Transform>(true))
                removedMissing += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);

            // Loop: RequireComponent chains (e.g. PhysBone colliders) need repeat passes.
            for (int pass = 0; pass < 8; pass++)
            {
                var vrc = clone.GetComponentsInChildren<Component>(true)
                    .Where(c => c != null && IsVrcComponent(c.GetType()))
                    .ToArray();
                if (vrc.Length == 0) break;
                foreach (var c in vrc)
                {
                    try { UnityEngine.Object.DestroyImmediate(c); removedVrc++; }
                    catch { /* dependency ordering — retry next pass */ }
                }
            }
            // Also strip pipeline/bake leftovers that serialize editor-only refs.
            foreach (var c in clone.GetComponentsInChildren<Component>(true).Where(c =>
                     c != null && (c.GetType().Name == "PipelineSaver" || c.GetType().Name == "PipelineManager")))
                UnityEngine.Object.DestroyImmediate(c);

            report.AppendLine($"Stripped {removedVrc} VRC/VRCFury components, {removedMissing} missing scripts.");
            report.AppendLine("NOTE: VRCFury components are build-time directives. Armature links are baked in above");
            report.AppendLine($"      ({linksApplied} applied), because they describe the rig rather than a choice. Everything");
            report.AppendLine("      else is not: toggles and outfit swaps ship exactly as the scene has them right now.");
            report.AppendLine("      Set the avatar up the way you want it, then export.");

            // --- 1c. Strip inactive objects ---------------------------------------
            // A representative VRC avatar carries every outfit variant and toggle target in
            // the hierarchy, disabled. They cost bundle size and load time for something no
            // one will ever see. Bones are protected: deleting an inactive bone would break
            // the rig, and some rigs legitimately disable bones.
            int strippedInactive = 0;
            var strippedNames = new List<string>();
            if (StripInactive)
            {
                var protectedSet = new HashSet<Transform>();
                void Protect(Transform t)
                {
                    for (var cur = t; cur != null; cur = cur.parent) protectedSet.Add(cur);
                }

                var animatorForBones = clone.GetComponent<Animator>();
                if (animatorForBones != null && animatorForBones.avatar != null && animatorForBones.avatar.isHuman)
                    foreach (HumanBodyBones hb in Enum.GetValues(typeof(HumanBodyBones)))
                    {
                        if (hb == HumanBodyBones.LastBone) continue;
                        var b = animatorForBones.GetBoneTransform(hb);
                        if (b != null) Protect(b);
                    }

                foreach (var smr in clone.GetComponentsInChildren<SkinnedMeshRenderer>(false))
                {
                    if (smr.rootBone != null) Protect(smr.rootBone);
                    var bs = smr.bones;
                    if (bs == null) continue;
                    foreach (var b in bs) if (b != null) Protect(b);
                }

                // Deepest-first, so removing a parent doesn't invalidate a queued child.
                var candidates = clone.GetComponentsInChildren<Transform>(true)
                    .Where(t => t != null && t != clone.transform && !t.gameObject.activeSelf
                                && !protectedSet.Contains(t))
                    .OrderByDescending(t => Depth(t))
                    .ToList();

                foreach (var t in candidates)
                {
                    if (t == null) continue;
                    if (strippedNames.Count < 40) strippedNames.Add(RelPath(clone.transform, t));
                    UnityEngine.Object.DestroyImmediate(t.gameObject);
                    strippedInactive++;
                }

                report.AppendLine($"Stripped {strippedInactive} inactive GameObject(s) " +
                                  "(Tools > Foxipso > DoE Avatar Export > Strip Inactive Objects to turn off).");
                foreach (var n in strippedNames) report.AppendLine($"    - {n}");
                if (strippedInactive > strippedNames.Count)
                    report.AppendLine($"    … and {strippedInactive - strippedNames.Count} more");
            }
            else
            {
                report.AppendLine("Inactive-object stripping is OFF — every disabled outfit and toggle target ships.");
            }

            // --- 2. Validate ------------------------------------------------------
            var animator = clone.GetComponent<Animator>();
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
                throw new Exception("Avatar must have an Animator with a valid Humanoid avatar.");
            var head = animator.GetBoneTransform(HumanBodyBones.Head);
            if (head == null) throw new Exception("Humanoid rig has no Head bone mapped.");
            var renderers = clone.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (renderers.Length == 0) throw new Exception("No SkinnedMeshRenderers found.");

            // Height from ACTIVE skinned meshes only, seeded from the first one rather than
            // from the avatar origin. The old "encapsulate every renderer starting at the
            // root" measure reported 3.87 m for a normal-sized avatar because it swept in
            // particle systems and effect quads parked far off the body.
            float height = 0f;
            var activeSkinned = clone.GetComponentsInChildren<SkinnedMeshRenderer>(false)
                                     .Where(r => r.sharedMesh != null).ToList();
            if (activeSkinned.Count > 0)
            {
                var bounds = activeSkinned[0].bounds;
                foreach (var r in activeSkinned) bounds.Encapsulate(r.bounds);
                height = bounds.size.y;
            }
            float headHeight = head.position.y - clone.transform.position.y;
            float humanScale = animator.humanScale;

            // The game spawns players with AvatarPlayer.headYHeight = 1.5 m. This is the
            // factor the mod will need to make a custom avatar's eyes land at the game's
            // viewpoint; recorded so the runtime doesn't have to rediscover it.
            const float GameHeadHeight = 1.5f;
            float suggestedScale = headHeight > 0.01f ? GameHeadHeight / headHeight : 1f;

            // --- 3. Scan blendshapes / bones / shaders ----------------------------
            var shapes  = new Dictionary<string, object>();
            var visemes = new Dictionary<string, object>();
            var shapeSources = new Dictionary<string, int>();
            var skippedScaffolding = new List<string>();

            // Scan the richest mesh first. A first-match-wins scan over an arbitrary renderer
            // order is how gaze ends up mapped to a face-tracking debug panel instead of the
            // face — the debug mesh carries the full UE set too, and may simply come first.
            var scanOrder = renderers
                .Where(r => r.sharedMesh != null)
                .Where(r =>
                {
                    string p = RelPath(clone.transform, r.transform);
                    bool scaffolding = IsScaffoldingPath(p);
                    if (scaffolding) skippedScaffolding.Add(p);
                    return !scaffolding;
                })
                .OrderByDescending(r => UeShapeCount(r.sharedMesh))
                .ToList();

            var aliasUsed = new List<string>();
            var maps = new List<(SkinnedMeshRenderer r, string path, Dictionary<string, int> byName)>();

            foreach (var r in scanOrder)
            {
                var mesh = r.sharedMesh;
                string path = RelPath(clone.transform, r.transform);
                var byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < mesh.blendShapeCount; i++)
                    byName[mesh.GetBlendShapeName(i)] = i;
                maps.Add((r, path, byName));
            }

            void Claim(string ue, string path, int idx, string via)
            {
                var entry = new Dictionary<string, object> { {"renderer", path}, {"index", idx} };
                if (via != null) entry["via"] = via;
                shapes[ue] = entry;
                shapeSources[path] = shapeSources.TryGetValue(path, out var n) ? n + 1 : 1;
                if (via != null) aliasUsed.Add($"{ue} <- `{via}`");
            }

            // Pass 1: canonical UE names, with the accepted prefixes.
            foreach (var (r, path, byName) in maps)
            {
                foreach (var ue in UEShapes.Canonical)
                {
                    if (shapes.ContainsKey(ue)) continue;
                    foreach (var pre in UEShapes.MeshPrefixes)
                        if (byName.TryGetValue(pre + ue, out int idx)) { Claim(ue, path, idx, null); break; }
                }
                foreach (var v in Visemes)
                {
                    if (visemes.ContainsKey(v)) continue;
                    if (byName.TryGetValue("vrc.v_" + v, out int idx))
                        visemes[v] = new Dictionary<string, object> { {"renderer", path}, {"index", idx} };
                }
            }

            // Pass 2: aliases, only for shapes pass 1 couldn't find. Recovers avatars authored
            // against ARKit or the older, coarser UE naming — which is most of them.
            var approxSkipped = new List<string>();
            foreach (var (r, path, byName) in maps)
            {
                foreach (var ue in UEShapes.Canonical)
                {
                    if (shapes.ContainsKey(ue)) continue;
                    if (!UEShapes.Aliases.TryGetValue(ue, out var candidates)) continue;
                    foreach (var (cand, kind) in candidates)
                    {
                        var hit = false;
                        foreach (var pre in UEShapes.MeshPrefixes)
                        {
                            if (!byName.TryGetValue(pre + cand, out int idx)) continue;
                            if (kind == UEShapes.AliasKind.Approximate && !AllowApproximateAliases)
                            {
                                approxSkipped.Add($"{ue} <- `{cand}`");
                                hit = true; // candidate found but declined; don't fall further down the list
                                break;
                            }
                            Claim(ue, path, idx, $"{cand} ({kind})");
                            hit = true;
                            break;
                        }
                        if (hit) break;
                    }
                }
            }

            // Which UE parameters ended up sharing one blendshape — the runtime has to combine
            // those with max() rather than letting the last one written win.
            var collisions = shapes
                .GroupBy(kv => $"{((Dictionary<string, object>)kv.Value)["renderer"]}#{((Dictionary<string, object>)kv.Value)["index"]}")
                .Where(g => g.Count() > 1)
                .Select(g => string.Join(" + ", g.Select(kv => kv.Key)))
                .ToList();

            // Everything on the face mesh we didn't consume. This is the evidence for growing
            // the alias table: if a shape is "missing", its real name is in here.
            var unconsumed = new List<string>();
            if (maps.Count > 0)
            {
                // Visemes live in their own map but are absolutely "consumed" — listing
                // vrc.v_* as unused was pure noise in the first run of this report.
                var claimed = new HashSet<int>(shapes.Values.Concat(visemes.Values)
                    .Select(v => (Dictionary<string, object>)v)
                    .Where(d => (string)d["renderer"] == maps[0].path)
                    .Select(d => (int)d["index"]));
                var faceMesh = maps[0].r.sharedMesh;
                for (int i = 0; i < faceMesh.blendShapeCount; i++)
                    if (!claimed.Contains(i)) unconsumed.Add(faceMesh.GetBlendShapeName(i));
            }

            var leftEye  = animator.GetBoneTransform(HumanBodyBones.LeftEye);
            var rightEye = animator.GetBoneTransform(HumanBodyBones.RightEye);
            // Capture as strings NOW. The clone is destroyed before the manifest is written,
            // and a Transform into a destroyed hierarchy evaluates as null — which is how
            // eyeBoneLeft/Right silently came out null in the first real export while
            // humanoidBones.LeftEye was correctly populated.
            string leftEyePath  = leftEye  != null ? RelPath(clone.transform, leftEye)  : null;
            string rightEyePath = rightEye != null ? RelPath(clone.transform, rightEye) : null;
            bool hasEyeBones = leftEye != null && rightEye != null;

            var shaderNames = renderers.SelectMany(r => r.sharedMaterials)
                .Where(m => m != null && m.shader != null)
                .Select(m => m.shader.name).Distinct().OrderBy(s => s).ToList();

            var jaw = animator.GetBoneTransform(HumanBodyBones.Jaw);
            string jawPath = jaw != null ? RelPath(clone.transform, jaw) : null;
            bool hasJawOpenShape = shapes.ContainsKey("JawOpen");

            // Full humanoid bone map, so the mod can wire VRIK and the face rig by lookup
            // instead of guessing at names. The game's own skeleton is UE4-named
            // (root/pelvis/spine_01/...), ours will be whatever the artist used — the
            // humanoid mapping is the only reliable bridge between the two.
            var bones = new Dictionary<string, object>();
            foreach (HumanBodyBones hb in Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (hb == HumanBodyBones.LastBone) continue;
                var t = animator.GetBoneTransform(hb);
                if (t != null) bones[hb.ToString()] = RelPath(clone.transform, t);
            }

            // Bone-weight budget: the game clamps to four influences per vertex.
            int maxInfluences = 0;
            string worstMesh = null;
            string boneWeightNote = null;
            foreach (var r in renderers)
            {
                var mesh = r.sharedMesh; if (mesh == null) continue;
                try
                {
                    // Read-only view into mesh data (Allocator.None) — do NOT Dispose it.
                    // Throws outright if the mesh has Read/Write disabled.
                    var perVertex = mesh.GetBonesPerVertex();
                    for (int i = 0; i < perVertex.Length; i++)
                        if (perVertex[i] > maxInfluences) { maxInfluences = perVertex[i]; worstMesh = mesh.name; }
                }
                catch (Exception e)
                {
                    boneWeightNote = $"could not read bone weights on `{mesh.name}` ({e.GetType().Name}) " +
                                     "— likely Read/Write disabled on the model importer";
                }
            }

            var unvouched = shaderNames
                .Where(n => !SpsiKnownGood.Any(k => n.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0))
                .ToList();

            report.AppendLine($"UE blendshapes found: {shapes.Count}/{UEShapes.Canonical.Length}");
            report.AppendLine($"Visemes found: {visemes.Count}/15");
            report.AppendLine($"Humanoid bones mapped: {bones.Count}");
            if (skippedScaffolding.Count > 0)
            {
                report.AppendLine($"Skipped {skippedScaffolding.Count} scaffolding/debug renderer(s) when scanning blendshapes:");
                foreach (var p2 in skippedScaffolding.Distinct().Take(10)) report.AppendLine($"    - {p2}");
            }
            if (shapeSources.Count > 0)
            {
                report.AppendLine("UE shapes sourced from:");
                foreach (var kv in shapeSources.OrderByDescending(k => k.Value))
                    report.AppendLine($"    - {kv.Value,3} from `{(kv.Key == "" ? "<root>" : kv.Key)}`");
                if (shapeSources.Count > 1)
                {
                    report.AppendLine("    NOTE: shapes came from more than one mesh. Normal for a split head/body,");
                    report.AppendLine("          worth a look if you expected them all on one face mesh.");
                }
            }
            if (aliasUsed.Count > 0)
            {
                report.AppendLine($"Resolved {aliasUsed.Count} shape(s) through the ARKit/legacy-UE alias table:");
                foreach (var chunk in Chunk(aliasUsed, 3)) report.AppendLine("    " + string.Join(",  ", chunk));
            }
            if (approxSkipped.Count > 0)
            {
                report.AppendLine($"Declined {approxSkipped.Count} approximate alias(es) — a different motion faked with a");
                report.AppendLine("near neighbour. Enable via Tools > Foxipso > DoE Avatar Export > Allow Approximate Shape Aliases:");
                foreach (var chunk in Chunk(approxSkipped, 3)) report.AppendLine("    " + string.Join(",  ", chunk));
            }
            if (collisions.Count > 0)
            {
                report.AppendLine($"{collisions.Count} blendshape(s) are driven by more than one UE parameter:");
                foreach (var c in collisions.Take(12)) report.AppendLine($"    - {c}");
                report.AppendLine("    (Expected where UE splits one ARKit shape. The mod combines these with max().)");
            }

            var missing = UEShapes.Canonical.Where(u => !shapes.ContainsKey(u)).ToList();
            var missingGaze = missing.Where(m => m.StartsWith("EyeLook", StringComparison.Ordinal)).ToList();
            var missingOther = missing.Except(missingGaze).ToList();

            if (missingGaze.Count > 0 && hasEyeBones)
                report.AppendLine($"Gaze: no EyeLook* blendshapes, but eye BONES are present — " +
                                  "gaze will be driven by bone rotation, which is the preferred path. Nothing lost.");
            else if (missingGaze.Count > 0)
                report.AppendLine("WARNING: no EyeLook* blendshapes AND no eye bones — eye gaze will not animate.");

            if (missingOther.Count > 0)
            {
                report.AppendLine($"Missing UE shapes ({missingOther.Count}) — these won't animate:");
                foreach (var chunk in Chunk(missingOther, 6)) report.AppendLine("    " + string.Join(", ", chunk));
            }
            if (unconsumed.Count > 0)
            {
                report.AppendLine($"Unused blendshapes on the face mesh ({unconsumed.Count}) — if something above is");
                report.AppendLine("\"missing\", its real name is probably in here; tell the mod author to alias it:");
                foreach (var chunk in Chunk(unconsumed.Take(60).ToList(), 5)) report.AppendLine("    " + string.Join(", ", chunk));
                if (unconsumed.Count > 60) report.AppendLine($"    … and {unconsumed.Count - 60} more");
            }
            report.AppendLine($"Eye bones: L={(leftEye ? leftEye.name : "none")} R={(rightEye ? rightEye.name : "none")}");
            report.AppendLine($"Jaw bone: {(jaw ? jaw.name : "none")}");
            report.AppendLine($"Bounds height: {height:F2}m (renderer AABB — ears/hair/tail inflate this), " +
                              $"head bone: {headHeight:F2}m, humanScale: {humanScale:F3}");
            report.AppendLine($"Suggested runtime scale: x{suggestedScale:F3}  (to put the head at the game's {GameHeadHeight}m)");
            if (maxInfluences == 0)
                report.AppendLine($"Max bone influences/vertex: unknown" +
                                  (boneWeightNote != null ? $" — {boneWeightNote}" : ""));
            else
                report.AppendLine($"Max bone influences/vertex: {maxInfluences}" +
                                  (maxInfluences > GameBoneWeightLimit
                                      ? $"  <-- WARNING: game clamps to {GameBoneWeightLimit} (FourBones); `{worstMesh}` will deform differently in-game"
                                      : "  (within the game's FourBones limit)"));

            // A broken material is a different problem from an unvouched one, and it will be a
            // visible magenta mesh in-game. Name the offenders rather than filing it under
            // "shader we can't vouch for".
            var broken = new List<string>();
            foreach (var r in renderers)
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null) { broken.Add($"`{RelPath(clone.transform, r.transform)}` material[{i}] is NULL"); continue; }
                    if (m.shader == null || m.shader.name == "Hidden/InternalErrorShader")
                        broken.Add($"`{RelPath(clone.transform, r.transform)}` material[{i}] `{m.name}` has a missing/failed shader");
                }
            }

            report.AppendLine("Shaders:");
            foreach (var s in shaderNames)
                report.AppendLine($"  - {s}{(unvouched.Contains(s) ? "   <-- not vouched for under Single Pass Instanced" : "")}");

            if (broken.Count > 0)
            {
                report.AppendLine($"ERROR: {broken.Count} material(s) have a missing or failed shader. These render magenta in-game:");
                foreach (var b in broken.Take(20)) report.AppendLine($"    - {b}");
                report.AppendLine("       Usually a shader that isn't imported in this project, or a Poiyomi lock that");
                report.AppendLine("       failed. Fix or delete the offending mesh, then re-export.");
            }

            var realUnvouched = unvouched.Where(n => n != "Hidden/InternalErrorShader").ToList();
            if (realUnvouched.Count > 0)
                report.AppendLine("WARNING: the game renders Single Pass Instanced. A shader that isn't SPS-I aware\n" +
                                  "         draws in one eye only. Poiyomi must be LOCKED before export.");
            else if (shaderNames.Any(n => n.StartsWith("Hidden/Locked/.poiyomi", StringComparison.OrdinalIgnoreCase)))
                report.AppendLine("Poiyomi shaders are locked — good, that's what the game needs.");

            // Four of these can be in a dungeon at 90 Hz. Worth seeing the number before a
            // friend reports the framerate rather than after.
            int totalVerts = 0, totalSubmeshes = 0;
            foreach (var r in renderers)
                if (r.sharedMesh != null) { totalVerts += r.sharedMesh.vertexCount; totalSubmeshes += r.sharedMesh.subMeshCount; }
            report.AppendLine($"Cost: {renderers.Length} skinned mesh(es), {totalVerts:N0} verts, " +
                              $"{totalSubmeshes} submeshes, {shaderNames.Count} distinct shaders.");

            // Textures are almost always where an avatar's bundle size actually goes — mesh
            // data rarely is. Name the biggest ones so the size problem has an address.
            var seenTex = new HashSet<int>();
            var texList = new List<(string name, int w, int h, string fmt, long bytes)>();
            long texBytes = 0;
            foreach (var r in renderers)
            foreach (var m in r.sharedMaterials)
            {
                if (m == null) continue;
                foreach (var prop in m.GetTexturePropertyNames())
                {
                    var tex = m.GetTexture(prop) as Texture;
                    if (tex == null || !seenTex.Add(tex.GetInstanceID())) continue;
                    long bytes = 0;
                    try { bytes = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(tex); } catch { }
                    texBytes += bytes;
                    var t2d = tex as Texture2D;
                    texList.Add((tex.name, tex.width, tex.height, t2d != null ? t2d.format.ToString() : tex.GetType().Name, bytes));
                }
            }
            report.AppendLine($"Textures: {texList.Count} unique, {texBytes / 1048576.0:F1} MB uncompressed in memory.");
            foreach (var t in texList.OrderByDescending(t => t.bytes).Take(10))
                report.AppendLine($"    {t.bytes / 1048576.0,6:F1} MB  {t.w}x{t.h}  {t.fmt,-12} {t.name}");
            if (texList.Count > 10) report.AppendLine($"    … and {texList.Count - 10} more");

            // Shader variants for stereo instancing get stripped at bundle-build time if the
            // exporting project never asks for them. PlayerSettings.stereoRenderingPath is the
            // *legacy* setting and doesn't necessarily reflect XR Plug-in Management, so report
            // it as a hint rather than a verdict — a false alarm here would be worse than none.
            var stereo = PlayerSettings.stereoRenderingPath;
            report.AppendLine($"Project stereoRenderingPath (legacy setting): {stereo}");
            report.AppendLine("NOTE: the game renders Single Pass Instanced. If the avatar turns out to render in\n" +
                              "      one eye only, this is the cause: check Project Settings > XR Plug-in Management >\n" +
                              "      (your provider) > Stereo Rendering Mode = Single Pass Instanced, then re-export.\n" +
                              "      A VRChat SDK project is normally already configured this way.");

            if (shapes.Count == 0) report.AppendLine("WARNING: no Unified Expressions shapes — face tracking will be voice-jaw only.");
            if (jaw == null && !shapes.ContainsKey("JawOpen"))
                report.AppendLine("WARNING: no Jaw bone and no JawOpen shape — the Vivox voice-energy jaw fallback\n" +
                                  "         (what non-face-tracked peers see) will have nothing to drive.");

            // Analysis mode stops here: no prefab, no bundle, no files touched. This is the
            // loop you want while fixing a broken material or trimming an avatar down —
            // a 37 MB bundle build per iteration is not.
            if (!buildBundle)
            {
                report.AppendLine("\n(Analysis only — no bundle built, nothing written.)");
                UnityEngine.Object.DestroyImmediate(clone);
                Debug.Log(report.ToString());
                return;
            }

            // --- 4. Temp prefab + bundle build ------------------------------------
            if (!AssetDatabase.IsValidFolder(TempAssetDir))
                AssetDatabase.CreateFolder("Assets", "DoEExport_Temp");
            string prefabPath = $"{TempAssetDir}/{avatarName}.prefab";
            PrefabUtility.SaveAsPrefabAsset(clone, prefabPath, out bool ok);
            if (!ok) throw new Exception("Failed to save temp prefab.");
            UnityEngine.Object.DestroyImmediate(clone);

            string bundleName = avatarName.ToLowerInvariant() + ".avatar";
            string outDir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, ExportDirName);
            Directory.CreateDirectory(outDir);

            // Build exactly this one bundle, by explicit AssetBundleBuild. The parameterless
            // overload builds every bundle assigned anywhere in the project — in a real VRC
            // project that can be a long, surprising detour through someone else's assets.
            var builds = new[]
            {
                new AssetBundleBuild
                {
                    assetBundleName = bundleName,
                    assetNames = new[] { prefabPath },
                }
            };
            var manifest = BuildPipeline.BuildAssetBundles(outDir, builds,
                BuildAssetBundleOptions.ChunkBasedCompression |
                BuildAssetBundleOptions.DeterministicAssetBundle |
                BuildAssetBundleOptions.StrictMode,
                BuildTarget.StandaloneWindows64);
            if (manifest == null) throw new Exception("AssetBundle build failed (see console).");

            // --- 5. Manifest ------------------------------------------------------
            string bundlePath = Path.Combine(outDir, bundleName);
            string sha256;
            using (var sha = SHA256.Create())
            using (var fs = File.OpenRead(bundlePath))
                sha256 = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();

            var manifestObj = new Dictionary<string, object>
            {
                {"schema", 1},
                {"name", avatarName},
                {"bundle", bundleName},
                {"sha256", sha256},
                {"prefab", avatarName},                 // asset name
                // Explicit in-bundle asset path. The bundle is built from an explicit
                // AssetBundleBuild, so the runtime name is this lowercased path — recording it
                // saves the mod guessing, and it keeps a fallback scan as a fallback.
                {"prefabPath", prefabPath.ToLowerInvariant()},
                {"unity", Application.unityVersion},
                {"exportedAt", DateTime.UtcNow.ToString("o")},
                {"rig", new Dictionary<string, object> {
                    {"height", Math.Round(height, 3)},
                    {"headHeight", Math.Round(headHeight, 3)},
                    {"humanScale", Math.Round(humanScale, 4)},
                    {"suggestedScale", Math.Round(suggestedScale, 4)},
                    {"gameHeadHeight", GameHeadHeight},
                    {"maxBoneInfluences", maxInfluences},
                    {"humanoidBones", bones},
                    {"jawBone", jawPath},
                }},
                {"cost", new Dictionary<string, object> {
                    {"skinnedMeshes", renderers.Length},
                    {"vertices", totalVerts},
                    {"submeshes", totalSubmeshes},
                    {"uniqueTextures", texList.Count},
                    {"textureBytes", texBytes},
                }},
                {"dynamics", new Dictionary<string, object> {
                    {"chains", dynChains},
                    {"colliders", dynColliders},
                }},
                {"shaders", shaderNames},
                {"shadersUnvouchedForSpsi", unvouched},
                {"projectStereoRenderingPath", stereo.ToString()},
                {"faceTracking", new Dictionary<string, object> {
                    {"hasFaceTracking", shapes.Count > 0},
                    {"shapes", shapes},
                    {"visemes", visemes},
                    {"eyeUseBones", hasEyeBones},
                    {"eyeBoneLeft",  leftEyePath},
                    {"eyeBoneRight", rightEyePath},
                    {"eyeMaxDegrees", new Dictionary<string, object> { {"x", 25}, {"y", 20} }},
                }},
            };
            string manifestPath = Path.Combine(outDir, avatarName + ".manifest.json");
            File.WriteAllText(manifestPath, MiniJson.Serialize(manifestObj));

            // Remove Unity's side manifests to keep the folder clean.
            foreach (var f in new[] { bundlePath + ".manifest",
                     Path.Combine(outDir, ExportDirName), Path.Combine(outDir, ExportDirName + ".manifest") })
                if (File.Exists(f)) File.Delete(f);

            long bundleBytes = new FileInfo(bundlePath).Length;
            double bundleMb = bundleBytes / 1048576.0;
            report.AppendLine($"\nBundle:   {bundlePath}");
            report.AppendLine($"Size:     {bundleMb:F1} MB");
            report.AppendLine($"Manifest: {manifestPath}");
            report.AppendLine($"sha256:   {sha256}");
            if (bundleMb > 25)
                report.AppendLine($"WARNING: {bundleMb:F0} MB is heavy. Up to four of these load at once in a dungeon at\n" +
                                  "         90 Hz, and every friend needs the file on disk before they can see you.\n" +
                                  "         Biggest wins are usually texture resolution and unused outfit variants.");
            report.AppendLine("\nInstall: copy both files to <game>/UserData/CustomAvatars/Avatars/ on every friend's machine.");
            Debug.Log(report.ToString());
            EditorUtility.RevealInFinder(bundlePath);
        }

        /// <summary>How many canonical UE shapes a mesh carries — used to find the real face mesh.</summary>
        static int UeShapeCount(Mesh mesh)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < mesh.blendShapeCount; i++) names.Add(mesh.GetBlendShapeName(i));
            int n = 0;
            foreach (var ue in UEShapes.Canonical)
                foreach (var pre in UEShapes.MeshPrefixes)
                    if (names.Contains(pre + ue)) { n++; break; }
            return n;
        }

        /// <summary>
        /// Reads VRCPhysBone / VRCPhysBoneCollider configuration off the avatar by reflection
        /// and flattens it into serialisable chains the mod can rebuild at runtime.
        ///
        /// A PhysBone covers a whole subtree, which may branch (hair strands). A runtime spring
        /// solver wants linear chains, so each root-to-leaf path becomes one chain — that's why
        /// one PhysBone can produce several entries.
        /// </summary>
        static void CaptureDynamics(GameObject clone, List<object> chains, List<object> colliders, StringBuilder report)
        {
            int colliderCount = 0;
            var seenTypes = new Dictionary<string, int>();
            var notes = new List<string>();

            foreach (var c in clone.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                var typeName = c.GetType().Name;

                // Inventory anything dynamics-shaped, so a zero result says what WAS there.
                if (typeName.IndexOf("PhysBone", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    typeName.IndexOf("DynamicBone", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    typeName.IndexOf("SpringBone", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    typeName.IndexOf("Magica", StringComparison.OrdinalIgnoreCase) >= 0)
                    seenTypes[typeName] = seenTypes.TryGetValue(typeName, out var n) ? n + 1 : 1;

                if (typeName.IndexOf("Collider", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    (typeName.IndexOf("PhysBone", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     typeName.IndexOf("DynamicBone", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    colliderCount++;
                    var ct = AsTransform(GetMember(c, "rootTransform"))
                          ?? AsTransform(GetMember(c, "m_Center"))
                          ?? c.transform;
                    colliders.Add(new Dictionary<string, object>
                    {
                        {"path", RelPath(clone.transform, ct)},
                        {"shape", GetMember(c, "shapeType")?.ToString() ?? "Sphere"},
                        {"radius", ToFloat(GetMember(c, "radius") ?? GetMember(c, "m_Radius"), 0.1f)},
                        {"height", ToFloat(GetMember(c, "height") ?? GetMember(c, "m_Height"), 0f)},
                        {"position", VecToList(GetMember(c, "position") ?? GetMember(c, "m_Center"))},
                        {"rotation", QuatToList(GetMember(c, "rotation"))},
                    });
                    continue;
                }

                var isPhysBone = typeName == "VRCPhysBone";
                // The older Dynamic Bone asset is still on a lot of avatars, and its fields
                // are all m_-prefixed with a different parameter model.
                var isDynamicBone = typeName == "DynamicBone" || typeName == "DynamicBone_Ver02";
                if (!isPhysBone && !isDynamicBone) continue;

                var ownerPath = RelPath(clone.transform, c.transform);
                // Both VRCPhysBone and DynamicBone document the same default: an unassigned
                // root means the GameObject the component sits on. Emulate that exactly.
                var root = (isPhysBone
                              ? AsTransform(GetMember(c, "rootTransform"))
                              : AsTransform(GetMember(c, "m_Root")))
                           ?? c.transform;

                var ignore = new HashSet<Transform>();
                var ignoreField = isPhysBone ? "ignoreTransforms" : "m_Exclusions";
                if (GetMember(c, ignoreField) is System.Collections.IEnumerable ig)
                    foreach (var o in ig) if (o is Transform it && it != null) ignore.Add(it);

                float pull, spring, stiffness, gravity, gravityFalloff, immobile, radius, maxAngleX;
                float maxAngleZ;
                string limitType;
                List<object> endpoint, limitRotation;

                if (isPhysBone)
                {
                    pull      = ToFloat(GetMember(c, "pull"), 0.2f);
                    spring    = ToFloat(GetMember(c, "spring"), 0.2f);
                    stiffness = ToFloat(GetMember(c, "stiffness"), 0.2f);
                    gravity   = ToFloat(GetMember(c, "gravity"), 0f);
                    gravityFalloff = ToFloat(GetMember(c, "gravityFalloff"), 0f);
                    immobile  = ToFloat(GetMember(c, "immobile"), 0f);
                    radius    = ToFloat(GetMember(c, "radius"), 0f);
                    maxAngleX = ToFloat(GetMember(c, "maxAngleX"), 0f);
                    // Polar limits use a second angle, and limitRotation is the frame the whole
                    // limit is measured against — without it a cone is centred on the wrong axis.
                    maxAngleZ = ToFloat(GetMember(c, "maxAngleZ"), 0f);
                    limitRotation = VecToList(GetMember(c, "limitRotation"));
                    limitType = GetMember(c, "limitType")?.ToString() ?? "None";
                    endpoint  = VecToList(GetMember(c, "endpointPosition"));
                }
                else
                {
                    // Dynamic Bone: elasticity restores toward the pose, damping resists motion,
                    // inert is "ignore the wearer's movement" — the same roles under other names.
                    pull      = ToFloat(GetMember(c, "m_Elasticity"), 0.1f);
                    spring    = pull;
                    stiffness = ToFloat(GetMember(c, "m_Stiffness"), 0.2f);
                    // Dynamic Bone's gravity is a world-space vector; ours is a 0..1 fraction.
                    gravity   = GetMember(c, "m_Gravity") is Vector3 gv ? Mathf.Clamp01(gv.magnitude / 9.81f) : 0f;
                    gravityFalloff = 0f;
                    immobile  = ToFloat(GetMember(c, "m_Inert"), 0f);
                    radius    = ToFloat(GetMember(c, "m_Radius"), 0f);
                    maxAngleX = 0f;
                    maxAngleZ = 0f;
                    limitRotation = null;
                    limitType = "None";
                    endpoint  = null;
                }

                var produced = 0;
                foreach (var leafPath in LeafChains(root, ignore))
                {
                    if (leafPath.Count < 2) continue;   // a single bone can't swing
                    produced++;
                    chains.Add(new Dictionary<string, object>
                    {
                        {"name", root.name},
                        {"source", typeName},
                        {"bones", leafPath.Select(t => RelPath(clone.transform, t)).Cast<object>().ToList()},
                        {"pull", pull}, {"spring", spring}, {"stiffness", stiffness},
                        {"gravity", gravity}, {"gravityFalloff", gravityFalloff},
                        {"immobile", immobile}, {"radius", radius},
                        {"limitType", limitType}, {"maxAngleX", maxAngleX},
                        {"maxAngleZ", maxAngleZ}, {"limitRotation", limitRotation},
                        {"endpointPosition", endpoint},
                    });
                }

                var childCount = root.childCount;
                var rootIsDefault = root == c.transform;
                notes.Add($"    - {typeName} on `{ownerPath}` → root `{RelPath(clone.transform, root)}`" +
                          (rootIsDefault ? " (unassigned, defaulted to own transform)" : "") +
                          (limitType != "None" ? $" [limit {limitType} x{maxAngleX:0}° z{maxAngleZ:0}°]" : "") + " " +
                          $"({childCount} child(ren)) → {produced} chain(s)" +
                          (produced == 0
                              ? childCount == 0
                                  ? "  <-- root has no children; nothing to swing"
                                  : "  <-- children exist but every path was length 1 or ignored"
                              : ""));
            }

            report.AppendLine($"Dynamics: captured {chains.Count} chain(s) and {colliderCount} collider(s) before stripping.");
            foreach (var n in notes) report.AppendLine(n);
            if (seenTypes.Count > 0)
                report.AppendLine("    components seen: " +
                                  string.Join(", ", seenTypes.Select(kv => $"{kv.Key} x{kv.Value}")));

            if (chains.Count == 0)
            {
                report.AppendLine("    NOTHING WILL SWING. Common causes, in order of likelihood:");
                report.AppendLine("      1. VRCFury generates the PhysBones at upload time, so at edit time they");
                report.AppendLine("         don't exist yet. Add a plain VRCPhysBone on the tail/ear root for export.");
                report.AppendLine("      2. The PhysBones present are on VRCFury holder objects with no bone children.");
                report.AppendLine("      3. The avatar uses a dynamics asset this exporter doesn't read (see the list above).");
            }
        }

        /// <summary>Every root-to-leaf path under <paramref name="root"/>, skipping ignored branches.</summary>
        static IEnumerable<List<Transform>> LeafChains(Transform root, HashSet<Transform> ignore)
        {
            var results = new List<List<Transform>>();
            void Walk(Transform t, List<Transform> path)
            {
                if (ignore.Contains(t)) return;
                path.Add(t);

                var kids = new List<Transform>();
                for (int i = 0; i < t.childCount; i++)
                {
                    var k = t.GetChild(i);
                    if (!ignore.Contains(k)) kids.Add(k);
                }

                if (kids.Count == 0) results.Add(new List<Transform>(path));
                else foreach (var k in kids) Walk(k, path);

                path.RemoveAt(path.Count - 1);
            }
            Walk(root, new List<Transform>());
            return results;
        }

        /// <summary>
        /// Collapses Unity's fake-null to a real C# null.
        ///
        /// An unassigned serialized reference is NOT C# null — it's a live object whose
        /// overloaded `==` reports null. So `GetMember(...) as Transform ?? fallback` never
        /// takes the fallback, and the next member access throws UnassignedReferenceException.
        /// Everything that reads a Transform out of a component by reflection must come
        /// through here.
        /// </summary>
        static Transform AsTransform(object o)
        {
            var t = o as Transform;
            return t != null ? t : null;   // `!=` is Unity's overload; the result is real null
        }

        static object GetMember(object target, string name)
        {
            if (target == null) return null;
            var t = target.GetType();
            const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            try
            {
                var f = t.GetField(name, F);
                if (f != null) return f.GetValue(target);
                var p2 = t.GetProperty(name, F);
                if (p2 != null) return p2.GetValue(target);
            }
            catch { }
            return null;
        }

        static float ToFloat(object o, float fallback)
        {
            if (o == null) return fallback;
            try { return Convert.ToSingle(o, System.Globalization.CultureInfo.InvariantCulture); }
            catch { return fallback; }
        }

        static List<object> VecToList(object o) =>
            o is Vector3 v ? new List<object> { (double)v.x, (double)v.y, (double)v.z } : null;

        static List<object> QuatToList(object o) =>
            o is Quaternion q ? new List<object> { (double)q.x, (double)q.y, (double)q.z, (double)q.w } : null;

        static IEnumerable<List<string>> Chunk(List<string> items, int size)
        {
            for (int i = 0; i < items.Count; i += size)
                yield return items.GetRange(i, Math.Min(size, items.Count - i));
        }

        static int Depth(Transform t)
        {
            int d = 0;
            for (var cur = t; cur != null; cur = cur.parent) d++;
            return d;
        }

        static bool IsVrcComponent(Type t)
        {
            for (var cur = t; cur != null && cur != typeof(Component); cur = cur.BaseType)
            {
                string ns = cur.Namespace ?? "";
                if (ns.StartsWith("VRC") || cur.Name.StartsWith("VRC") ||
                    cur.Assembly.GetName().Name.Contains("VRCSDK") ||
                    cur.Assembly.GetName().Name.StartsWith("VRC."))
                    return true;
            }
            return false;
        }

        static string RelPath(Transform root, Transform t)
        {
            if (t == root) return "";
            var parts = new List<string>();
            for (var cur = t; cur != null && cur != root; cur = cur.parent) parts.Add(cur.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        static string RelPathBone(Animator a, Transform bone) => RelPath(a.transform, bone);

        static string Sanitize(string s) =>
            new string(s.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-').ToArray());

        static void Cleanup()
        {
            if (AssetDatabase.IsValidFolder(TempAssetDir))
                AssetDatabase.DeleteAsset(TempAssetDir);
            AssetDatabase.RemoveUnusedAssetBundleNames();
        }
    }

    // Minimal JSON writer (no external deps in an editor script).
    static class MiniJson
    {
        public static string Serialize(object o)
        {
            var sb = new StringBuilder(); Write(sb, o, 0); return sb.ToString();
        }
        static void Write(StringBuilder sb, object o, int depth)
        {
            string pad = new string(' ', depth * 2), pad2 = new string(' ', (depth + 1) * 2);
            switch (o)
            {
                case null: sb.Append("null"); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case string s: sb.Append('"').Append(Escape(s)).Append('"'); break;
                case int or long or float or double or decimal:
                    sb.Append(Convert.ToString(o, System.Globalization.CultureInfo.InvariantCulture)); break;
                case IDictionary<string, object> d:
                    sb.Append("{\n");
                    bool first = true;
                    foreach (var kv in d)
                    {
                        if (!first) sb.Append(",\n"); first = false;
                        sb.Append(pad2).Append('"').Append(Escape(kv.Key)).Append("\": ");
                        Write(sb, kv.Value, depth + 1);
                    }
                    sb.Append('\n').Append(pad).Append('}');
                    break;
                case System.Collections.IEnumerable e:
                    sb.Append('[');
                    bool f2 = true;
                    foreach (var item in e)
                    {
                        if (!f2) sb.Append(", "); f2 = false;
                        Write(sb, item, depth + 1);
                    }
                    sb.Append(']');
                    break;
                default: sb.Append('"').Append(Escape(o.ToString())).Append('"'); break;
            }
        }
        static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                                           .Replace("\n", "\\n").Replace("\r", "\\r");
    }
}
#endif
