// DoE Friends-Mod — face override generator.
//
// Reads the ranges a VRChat avatar's face-tracking controllers were authored with, and writes
// them out as <avatar>.overrides.json for the mod to apply.
//
// The mod deliberately does not run the avatar's FX controller (see docs/FACE-TRACKING.md),
// which means losing the artist's tuning: eyelids that only close to 70%, a shape held at half
// strength. That tuning is not guesswork — it is written down in the controller, and this reads
// it back out.
//
// Scope, deliberately narrow: **Direct Blend Trees**. The face-tracking templates drive
// blendshapes through a DBT whose children each carry a `directBlendParameter` — the Unified
// Expressions parameter name — and a clip. The clip's peak blendshape weight is the range the
// artist chose. That is a 1:1, unambiguous mapping. General blend-tree analysis is not
// attempted: 1D and 2D trees encode input remapping rather than output range, they are used for
// quite different things across avatars, and guessing at them would produce confident nonsense.
// Whatever isn't understood is reported rather than invented.

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace DoEMod.Export
{
    public static class FaceOverrideGenerator
    {
        private class Finding
        {
            public string Parameter;
            public string Shape;
            public float Peak;         // 0..100, the clip's highest blendshape weight
            public string Controller;
        }

        public static void Generate(GameObject avatar)
        {
            var report = new StringBuilder($"=== DoE face override scan: {avatar.name} ===\n");

            var controllers = FindControllers(avatar, report);
            if (controllers.Count == 0)
            {
                report.AppendLine("No animator controllers found on this avatar or its VRCFury components.");
                report.AppendLine("Nothing to read ranges from — write the overrides file by hand instead.");
                Debug.Log(report.ToString());
                return;
            }

            var findings = new List<Finding>();
            var trees = 0;
            var unreadable = new List<string>();

            foreach (var controller in controllers)
            {
                foreach (var layer in controller.layers)
                {
                    if (layer?.stateMachine == null) continue;
                    foreach (var state in AllStates(layer.stateMachine))
                        Walk(state.motion, controller.name, findings, ref trees, unreadable, 0);
                }
            }

            report.AppendLine($"Scanned {controllers.Count} controller(s), found {trees} direct blend tree(s) " +
                              $"and {findings.Count} parameter→blendshape link(s).");

            // Only ranges that actually differ from full are worth writing: an override saying
            // "use the whole range" is the same as no override, and a file full of them makes
            // the interesting entries hard to see.
            var interesting = findings
                .Where(f => f.Peak < 99.5f && f.Peak > 0.01f)
                .GroupBy(f => f.Parameter)
                .Select(g => g.OrderBy(f => f.Peak).First())
                .OrderBy(f => f.Parameter)
                .ToList();

            if (interesting.Count == 0)
            {
                report.AppendLine("Every clip drives its blendshape to full, so there is nothing to override.");
                report.AppendLine("That is a perfectly normal result — it means the avatar's ranges are already 0..1.");
            }
            else
            {
                report.AppendLine($"{interesting.Count} parameter(s) are authored below full range:");
                foreach (var f in interesting)
                    report.AppendLine($"    {f.Parameter,-28} → {f.Shape,-28} peaks at {f.Peak:0.#}%  ({f.Controller})");
            }

            if (unreadable.Count > 0)
            {
                report.AppendLine($"\nNot read ({unreadable.Count}) — these use blend trees this tool does not");
                report.AppendLine("interpret, and are left for you to tune by hand if they matter:");
                foreach (var u in unreadable.Distinct().Take(20)) report.AppendLine($"    {u}");
            }

            WriteFile(avatar, interesting, report);
            Debug.Log(report.ToString());
        }

        private static void WriteFile(GameObject avatar, List<Finding> interesting, StringBuilder report)
        {
            var outDir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "DoEExport");
            Directory.CreateDirectory(outDir);
            var path = Path.Combine(outDir, Sanitize(avatar.name) + ".overrides.json");

            // Never clobber hand tuning. This file is the one place a person's own adjustments
            // live, and it is not regenerated by the exporter for exactly that reason.
            if (File.Exists(path))
            {
                report.AppendLine($"\n`{Path.GetFileName(path)}` already exists and was NOT overwritten.");
                report.AppendLine("Delete it first if you want it regenerated, or merge the values above by hand.");
                return;
            }

            var shapes = new Dictionary<string, object>();
            foreach (var f in interesting)
                shapes[f.Parameter] = new Dictionary<string, object>
                {
                    {"min", 0.0},
                    {"max", Math.Round(f.Peak / 100.0, 4)},
                };

            var root = new Dictionary<string, object> { {"shapes", shapes} };
            File.WriteAllText(path, MiniJson.Serialize(root));

            report.AppendLine($"\nWrote {path}");
            report.AppendLine("Copy it next to the .avatar and .manifest.json in the game's Avatars folder.");
            report.AppendLine("The exporter never rewrites this file, so anything you change by hand survives.");
        }

        /// <summary>Controllers on the avatar's Animators, plus any referenced by VRCFury components.</summary>
        private static List<AnimatorController> FindControllers(GameObject avatar, StringBuilder report)
        {
            var found = new List<AnimatorController>();

            void Add(RuntimeAnimatorController rac, string where)
            {
                var ac = rac as AnimatorController;
                if (ac == null || found.Contains(ac)) return;
                found.Add(ac);
                report.AppendLine($"    controller: {AssetDatabase.GetAssetPath(ac)}  ({where})");
            }

            report.AppendLine("Looking for controllers:");

            foreach (var animator in avatar.GetComponentsInChildren<Animator>(true))
                if (animator.runtimeAnimatorController != null)
                    Add(animator.runtimeAnimatorController, $"Animator on {animator.name}");

            // VRCFury components hold their controllers in nested serialized objects, and the
            // shape of that varies by feature and version. Reflection over the object graph is
            // uglier than a typed reference, but it survives VRCFury updates and needs no
            // compile-time dependency on it.
            foreach (var component in avatar.GetComponentsInChildren<Component>(true))
            {
                if (component == null) continue;
                var typeName = component.GetType().Name;
                if (typeName.IndexOf("VRCFury", StringComparison.OrdinalIgnoreCase) < 0) continue;
                CollectControllers(component, found, rac => Add(rac, $"{typeName} on {component.name}"), 0);
            }

            return found;
        }

        private static void CollectControllers(object target, List<AnimatorController> found,
                                               Action<RuntimeAnimatorController> add, int depth)
        {
            if (target == null || depth > 5) return;

            const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            foreach (var field in target.GetType().GetFields(F))
            {
                object value;
                try { value = field.GetValue(target); } catch { continue; }
                if (value == null) continue;

                switch (value)
                {
                    case RuntimeAnimatorController rac:
                        add(rac);
                        break;
                    case string _:
                    case UnityEngine.Object _ when !(value is ScriptableObject):
                        break;
                    case System.Collections.IEnumerable list:
                        foreach (var item in list)
                        {
                            if (item is RuntimeAnimatorController itemRac) add(itemRac);
                            else if (item != null && !(item is string)) CollectControllers(item, found, add, depth + 1);
                        }
                        break;
                    default:
                        if (value.GetType().IsClass) CollectControllers(value, found, add, depth + 1);
                        break;
                }
            }
        }

        private static IEnumerable<AnimatorState> AllStates(AnimatorStateMachine machine)
        {
            foreach (var child in machine.states) yield return child.state;
            foreach (var sub in machine.stateMachines)
                foreach (var s in AllStates(sub.stateMachine)) yield return s;
        }

        private static void Walk(Motion motion, string controllerName, List<Finding> findings,
                                 ref int trees, List<string> unreadable, int depth)
        {
            if (motion == null || depth > 6) return;

            if (!(motion is BlendTree tree)) return;

            if (tree.blendType == BlendTreeType.Direct)
            {
                trees++;
                foreach (var child in tree.children)
                {
                    if (string.IsNullOrEmpty(child.directBlendParameter)) continue;

                    if (child.motion is BlendTree nested)
                    {
                        Walk(nested, controllerName, findings, ref trees, unreadable, depth + 1);
                        continue;
                    }
                    if (!(child.motion is AnimationClip clip)) continue;

                    foreach (var (shape, peak) in PeakBlendShapes(clip))
                        findings.Add(new Finding
                        {
                            Parameter = StripPrefix(child.directBlendParameter),
                            Shape = shape,
                            Peak = peak,
                            Controller = controllerName,
                        });
                }
                return;
            }

            // 1D and 2D trees encode input remapping, not output range, and are used for very
            // different things across avatars. Record and move on rather than guess.
            unreadable.Add($"{controllerName}: {tree.blendType} tree on `{tree.blendParameter}`");
            foreach (var child in tree.children)
                if (child.motion is BlendTree nested)
                    Walk(nested, controllerName, findings, ref trees, unreadable, depth + 1);
        }

        /// <summary>Highest weight each blendshape reaches in a clip.</summary>
        private static IEnumerable<(string shape, float peak)> PeakBlendShapes(AnimationClip clip)
        {
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (!binding.propertyName.StartsWith("blendShape.", StringComparison.Ordinal)) continue;
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null || curve.keys.Length == 0) continue;

                var peak = 0f;
                foreach (var key in curve.keys) if (key.value > peak) peak = key.value;
                yield return (binding.propertyName.Substring("blendShape.".Length), peak);
            }
        }

        /// <summary>`FT/v2/JawOpen` and `v2/JawOpen` both mean `JawOpen` to the mod.</summary>
        private static string StripPrefix(string parameter)
        {
            var index = parameter.LastIndexOf('/');
            return index >= 0 && index < parameter.Length - 1 ? parameter.Substring(index + 1) : parameter;
        }

        private static string Sanitize(string s) =>
            new string(s.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-').ToArray());
    }
}
#endif
