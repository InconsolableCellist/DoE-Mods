// DoE Friends-Mod — enemy skin import + export, for CustomEnemies.
//
// An enemy skin is an ordinary avatar export (Export Selected Avatar: <name>.avatar +
// <name>.manifest.json) plus a hand-written <name>.enemy.json sidecar, all three in the
// game's UserData/CustomEnemies/. This file only automates getting a rigged FBX to that point:
// import it as a Humanoid (Unity maps the bones by name, so name them as Unity's humanoid
// bones: Hips, Spine, Chest, UpperChest, Neck, Head, LeftUpperArm, ...), find its textures,
// then run the avatar exporter on it.
//
// From the editor:  Tools ▸ Foxipso ▸ DoE Avatar Export ▸ Import Selected FBX As Enemy Skin
// From a shell:     Unity.exe -batchmode -projectPath <project> -quit
//                     -executeMethod DoEMod.Export.DoEEnemySkinBatch.Run -fbx Assets/<path>.fbx
// Textures: put them in a folder named Textures next to the FBX; Unity matches them by file name.

#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DoEMod.Export
{
    public static class DoEEnemySkinBatch
    {
        [MenuItem("Tools/Foxipso/DoE Avatar Export/Import Selected FBX As Enemy Skin", priority = 40)]
        static void FromMenu()
        {
            var path = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
            {
                EditorUtility.DisplayDialog("DoE Export", "Select an .fbx in the Project window first.", "OK");
                return;
            }
            ImportAndExport(path);
        }

        /// <summary>Batch entry point: -executeMethod DoEMod.Export.DoEEnemySkinBatch.Run -fbx Assets/...fbx</summary>
        public static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, "-fbx");
            if (i < 0 || i + 1 >= args.Length) { Debug.LogError("DoE enemy skin: pass -fbx Assets/<path>.fbx"); EditorApplication.Exit(2); return; }
            var ok = ImportAndExport(args[i + 1]);
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        static bool ImportAndExport(string fbx)
        {
            AssetDatabase.Refresh();
            var imp = AssetImporter.GetAtPath(fbx) as ModelImporter;
            if (imp == null) { Debug.LogError($"DoE enemy skin: {fbx} is not a model asset."); return false; }

            imp.animationType = ModelImporterAnimationType.Human;
            imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            imp.importAnimation = false;
            imp.importBlendShapes = true;
            imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            imp.materialLocation = ModelImporterMaterialLocation.InPrefab;
            imp.materialSearch = ModelImporterMaterialSearch.Local;
            imp.materialName = ModelImporterMaterialName.BasedOnMaterialName;
            imp.maxBonesPerVertex = 4;          // the game skins with FourBones
            imp.isReadable = false;
            imp.SaveAndReimport();

            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            var anim = asset != null ? asset.GetComponent<Animator>() : null;
            if (anim == null || anim.avatar == null || !anim.avatar.isValid || !anim.avatar.isHuman)
            {
                Debug.LogError($"DoE enemy skin: {fbx} did not import as a valid Humanoid. Name the bones as Unity's humanoid bones, " +
                               "or open the FBX's Rig tab, Configure, and map them by hand.");
                return false;
            }

            var mapped = imp.humanDescription.human?.Length ?? 0;
            Debug.Log($"DoE enemy skin: {fbx} imported as Humanoid, {mapped} bones mapped: " +
                      string.Join(", ", imp.humanDescription.human.Select(h => $"{h.humanName}={h.boneName}")));

            var name = Path.GetFileNameWithoutExtension(fbx);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            inst.name = name;
            try
            {
                Selection.activeGameObject = inst;
                DoEAvatarExporter.ExportSelected();
            }
            finally { UnityEngine.Object.DestroyImmediate(inst); }
            // The exporter names its files after the (sanitised) object name.
            var outDir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "DoEExport");
            var ok = Directory.Exists(outDir) && Directory.GetFiles(outDir, "*.avatar")
                .Any(f => string.Equals(Path.GetFileNameWithoutExtension(f), name, StringComparison.OrdinalIgnoreCase));
            Debug.Log($"DoE enemy skin: export {(ok ? "written to " + outDir : "did not produce a bundle; see the report above")}.");
            return ok;
        }
    }
}
#endif
