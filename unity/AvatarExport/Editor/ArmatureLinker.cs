// DoE Friends-Mod — VRCFury Armature Link baker.
//
// The exporter strips VRCFury and ships whatever the scene holds, which is the right model for
// toggles and outfit swaps: the author enables what they want, then exports. Armature Link is
// the exception. It is not a runtime choice — it is the static statement "this garment's
// armature IS the avatar's armature", and unapplied it leaves clothing skinned to its own
// bones, sitting in avatar-local space, not following the body.
//
// So this bakes that one feature and nothing else. No toggles, no Full Controller, no mesh
// merging: those are animator-and-parameter machinery we deliberately don't want. Armature Link
// is pure hierarchy and skinning, and it can be applied by reading the rule and doing what it
// says.
//
// What it does per rule: align the prop armature root onto its target, walk both trees matching
// bones by name (minus the author's suffix), repoint every SkinnedMeshRenderer bone reference
// from the prop bone to the avatar bone, carry non-matching children (skirt bones, jiggle
// chains) across, and delete the emptied prop bones.
//
// Runs BEFORE the dynamics capture, deliberately. Dynamics are recorded as paths relative to
// the avatar root, so a skirt bone captured pre-link is stored at a path that no longer exists
// post-link. Linking first means the captured paths are the shipped ones.
//
// VRCFury is read reflectively — it has reshaped ArmatureLink's serialised fields more than
// once (`linkTo` was a path string before it was a list; `linkMode` and `removeBoneSuffix` came
// and went). Probing field names and falling back survives that and needs no compile-time
// dependency, the same trade FaceOverrideGenerator makes for controllers.

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace DoEMod.Export
{
    public static class ArmatureLinker
    {
        // A matched pair whose world transforms disagree by more than this will deform: the mesh
        // was bound against the prop bone's pose, and we are handing it the avatar's. Roughly a
        // millimetre, and a degree — below authoring noise, above nothing.
        const float PositionTolerance = 0.001f;
        const float AngleTolerance = 1.0f;
        const float ScaleTolerance = 0.005f;

        const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        // Face-tracking debug panels carry links of their own, aimed at marker transforms that
        // get stripped a few steps below — merging those into the armature is work done on
        // something already on its way out.
        //
        // Deliberately NARROWER than the exporter's ScaffoldingMarkers, which also matches the
        // substring "VRCFury". That is right for the blendshape scan, where a debug panel would
        // hijack the gaze shapes, and wrong here: `VRCFury Items` is VRCFury's own generated
        // holder and is exactly where a real garment's Armature Link normally lives. Matching
        // on it would silently skip the clothing this pass exists to fix.
        static readonly string[] DebugMarkers = { "FT_Debug", "Face Tracking UE Debug" };

        static bool IsDebugPath(string path) =>
            path != null && DebugMarkers.Any(m => path.IndexOf(m, StringComparison.OrdinalIgnoreCase) >= 0);

        class Rule
        {
            public Component Owner;          // the VRCFury MonoBehaviour holding the feature
            public object Feature;           // the ArmatureLink model object itself
            public Transform Prop;           // garment armature root
            public Transform Target;         // avatar bone it links onto
            public string Suffix = "";
            public bool KeepOffsets;
            public bool RecursiveMatch = true;
            public string LinkMode = "";
            public string TargetNote = "";   // how Target was resolved, for the report
        }

        /// <summary>
        /// Applies every VRCFury Armature Link on the clone. Returns the number of rules applied;
        /// everything interesting, including refusals, goes into <paramref name="report"/>.
        /// </summary>
        public static int Apply(GameObject clone, bool skipInactive, StringBuilder report)
        {
            var animator = clone.GetComponent<Animator>();
            var rules = Collect(clone, animator, skipInactive, report);
            if (rules.Count == 0) return 0;

            int applied = 0;
            foreach (var rule in rules)
                if (ApplyRule(clone, rule, report))
                    applied++;

            report.AppendLine($"Armature links: applied {applied} of {rules.Count} rule(s).");
            return applied;
        }

        // --- Reading the rules ---------------------------------------------------

        static List<Rule> Collect(GameObject clone, Animator animator, bool skipInactive, StringBuilder report)
        {
            var rules = new List<Rule>();
            var skipped = new List<string>();

            var seen = new HashSet<object>(ReferenceComparer.Instance);
            foreach (var component in clone.GetComponentsInChildren<Component>(true))
            {
                if (component == null) continue;
                foreach (var feature in Features(component))
                {
                    if (!seen.Add(feature)) continue;
                    if (feature.GetType().Name.IndexOf("ArmatureLink", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    var ownerPath = Path(clone.transform, component.transform);

                    // "What is active is what ships" — a link on a disabled outfit would merge
                    // bones into the armature that the inactive strip is about to orphan.
                    if (skipInactive && !component.gameObject.activeInHierarchy)
                    {
                        skipped.Add($"`{ownerPath}` (inactive — enable it if this is clothing you want)");
                        continue;
                    }

                    var propPath = Path(clone.transform, AsTransform(GetMember(feature, "propBone")));
                    if (IsDebugPath(ownerPath) || IsDebugPath(propPath))
                    {
                        skipped.Add($"`{ownerPath}` -> prop `{propPath ?? "(unset)"}` (face-tracking debug)");
                        continue;
                    }

                    var rule = Build(component, feature, clone, animator, report);
                    if (rule != null) rules.Add(rule);
                }
            }

            if (rules.Count == 0 && skipped.Count == 0) return rules;

            report.AppendLine($"Armature links: found {rules.Count} rule(s)" +
                              (skipped.Count > 0 ? $", skipped {skipped.Count}" : "") + ".");
            // Named, not counted: a garment that didn't move in game is usually a link that was
            // skipped here, and a bare number gives you nothing to go and look at.
            foreach (var skip in skipped) report.AppendLine($"    skipped {skip}");
            return rules;
        }

        /// <summary>
        /// The feature model objects a component might carry. Current VRCFury puts one model in
        /// `content` on a `VRCFury` behaviour; older builds used a `config.features` list, and
        /// older still a dedicated per-feature component.
        /// </summary>
        static IEnumerable<object> Features(Component component)
        {
            var typeName = component.GetType().Name;
            if (typeName.IndexOf("VRCFury", StringComparison.OrdinalIgnoreCase) < 0 &&
                typeName.IndexOf("ArmatureLink", StringComparison.OrdinalIgnoreCase) < 0)
                yield break;

            yield return component;

            var content = GetMember(component, "content");
            if (content != null) yield return content;

            var config = GetMember(component, "config");
            if (GetMember(config, "features") is System.Collections.IEnumerable features)
                foreach (var f in features)
                    if (f != null) yield return f;
        }

        static Rule Build(Component owner, object feature, GameObject clone, Animator animator, StringBuilder report)
        {
            var prop = AsTransform(GetMember(feature, "propBone"));
            if (prop == null)
            {
                report.AppendLine($"    SKIPPED link on `{Path(clone.transform, owner.transform)}`: no propBone set.");
                return null;
            }

            var rule = new Rule
            {
                Owner = owner,
                Feature = feature,
                Prop = prop,
                RecursiveMatch = GetMember(feature, "recursiveMatch") as bool? ?? true,
                LinkMode = GetMember(feature, "linkMode")?.ToString() ?? "",
                KeepOffsets = ReadKeepOffsets(feature),
            };

            rule.Target = ResolveTarget(feature, clone, animator, out var note);
            rule.TargetNote = note;
            if (rule.Target == null)
            {
                report.AppendLine($"    SKIPPED link on `{Path(clone.transform, owner.transform)}`: " +
                                  $"could not resolve its target ({note}).");
                return null;
            }

            rule.Suffix = ReadSuffix(feature, rule.Prop, rule.Target);
            return rule;
        }

        /// <summary>
        /// The avatar-side bone. Modern VRCFury stores a list of candidates, each either a
        /// humanoid bone or an object, plus a path offset; it uses the first that resolves.
        /// Older versions stored a bare path string.
        /// </summary>
        static Transform ResolveTarget(object feature, GameObject clone, Animator animator, out string note)
        {
            note = "no linkTo field";
            var linkTo = GetMember(feature, "linkTo");

            if (linkTo is System.Collections.IEnumerable entries && !(linkTo is string))
            {
                var tried = new List<string>();
                foreach (var entry in entries)
                {
                    if (entry == null) continue;
                    var resolved = ResolveEntry(entry, clone, animator, out var how);
                    tried.Add(how);
                    if (resolved != null) { note = how; return resolved; }
                }
                note = tried.Count > 0 ? "tried " + string.Join("; ", tried) : "linkTo list empty";
                return null;
            }

            if (linkTo is string path && path.Length > 0)
                return ResolvePath(path, clone, animator, out note);

            // No linkTo at all: VRCFury's default target is the humanoid Hips.
            var hips = Bone(animator, HumanBodyBones.Hips);
            note = hips != null ? "defaulted to humanoid Hips" : "no linkTo, and no humanoid Hips";
            return hips;
        }

        static Transform ResolveEntry(object entry, GameObject clone, Animator animator, out string how)
        {
            var offset = GetMember(entry, "offset") as string ?? "";
            Transform baseBone = null;
            how = "";

            var useObj = GetMember(entry, "useObj") as bool? ?? false;
            if (useObj)
            {
                baseBone = AsTransform(GetMember(entry, "obj"));
                how = baseBone != null ? $"object `{Path(clone.transform, baseBone)}`" : "object (unset)";
            }
            else
            {
                var bone = ToHumanBone(GetMember(entry, "bone"));
                baseBone = Bone(animator, bone);
                how = baseBone != null ? $"humanoid {bone}" : $"humanoid {bone} (not mapped)";
            }

            if (baseBone == null) return null;
            if (offset.Length == 0) return baseBone;

            var child = baseBone.Find(offset);
            how += $" + offset `{offset}`" + (child == null ? " (not found)" : "");
            return child;
        }

        /// <summary>
        /// A bare path string, from an older VRCFury. It was written relative to the avatar root
        /// in some versions and to the Hips in others, so try both and say which one answered
        /// rather than silently picking.
        /// </summary>
        static Transform ResolvePath(string path, GameObject clone, Animator animator, out string note)
        {
            var fromRoot = clone.transform.Find(path);
            if (fromRoot != null) { note = $"path `{path}` from avatar root"; return fromRoot; }

            var hips = Bone(animator, HumanBodyBones.Hips);
            var fromHips = hips != null ? hips.Find(path) : null;
            if (fromHips != null) { note = $"path `{path}` from Hips"; return fromHips; }

            note = $"path `{path}` matched neither the avatar root nor the Hips";
            return null;
        }

        /// <summary>
        /// The suffix the garment's bones carry, e.g. "Hips (MyAvatar)" links to "Hips". Authors
        /// often leave the field blank, in which case VRCFury infers it from the prop root's
        /// name against its target — do the same.
        /// </summary>
        static string ReadSuffix(object feature, Transform prop, Transform target)
        {
            if (GetMember(feature, "removeBoneSuffix") is string s && s.Length > 0) return s;
            if (prop.name.Length > target.name.Length && prop.name.StartsWith(target.name, StringComparison.Ordinal))
                return prop.name.Substring(target.name.Length);
            return "";
        }

        /// <summary>
        /// Whether to preserve the garment armature's own placement. VRCFury spells this as a
        /// tri-state (Auto/Yes/No) whose Auto means "no" for the linking modes we support, and
        /// as a plain bool in older builds.
        /// </summary>
        static bool ReadKeepOffsets(object feature)
        {
            var v = GetMember(feature, "keepBoneOffsets");
            if (v == null) return false;
            if (v is bool b) return b;
            var name = v.ToString();
            return name.Equals("Yes", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("True", StringComparison.OrdinalIgnoreCase);
        }

        // --- Applying one rule ---------------------------------------------------

        static bool ApplyRule(GameObject clone, Rule rule, StringBuilder report)
        {
            var propPath = Path(clone.transform, rule.Prop);
            var targetPath = Path(clone.transform, rule.Target);
            report.AppendLine($"    link `{propPath}` -> `{targetPath}`  ({rule.TargetNote}" +
                              (rule.Suffix.Length > 0 ? $", suffix `{rule.Suffix}`" : ", no suffix") + ")");

            if (rule.Prop == rule.Target || rule.Target.IsChildOf(rule.Prop))
            {
                report.AppendLine("      SKIPPED: the target is the prop bone or lives inside it.");
                return false;
            }

            // Unsupported modes are named, not guessed at. Bone Constraint keeps two skeletons
            // and constrains one to the other, which needs constraint components we strip anyway.
            if (rule.LinkMode.IndexOf("Constraint", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                report.AppendLine($"      SKIPPED: linkMode `{rule.LinkMode}` is constraint-based, which this " +
                                  "exporter can't ship. Re-author the link as a skin rewrite / merge.");
                return false;
            }

            // Align first: the rewrite below hands the mesh the avatar's bone in place of the one
            // its bindposes were baked against, so the two have to be in the same place.
            if (!rule.KeepOffsets)
            {
                rule.Prop.position = rule.Target.position;
                rule.Prop.rotation = rule.Target.rotation;
                rule.Prop.localScale = MatchLossyScale(rule.Prop, rule.Target);
            }

            var map = new Dictionary<Transform, Transform>();
            var moves = new List<KeyValuePair<Transform, Transform>>();   // unmatched child -> its prop parent
            var mismatches = new List<string>();

            Match(rule, rule.Prop, rule.Target, map, moves, mismatches, clone);

            // A merged bone carrying anything else — a PhysBone, a light, a mesh — is not ours to
            // delete; it survives, reparented onto the avatar bone. Decide that before moving
            // anything, because it changes where the unmatched children belong: a PhysBone on the
            // garment's chest wants its breast bones still under it, not hoisted to the avatar's.
            var kept = new HashSet<Transform>(
                map.Keys.Where(t => t.GetComponents<Component>().Length > 1));

            foreach (var move in moves)
            {
                var propParent = move.Value;
                var newParent = kept.Contains(propParent) ? propParent : map[propParent];
                if (move.Key.parent != newParent) move.Key.SetParent(newParent, worldPositionStays: true);
            }

            int rewrittenBones = 0, rewrittenRoots = 0;
            foreach (var smr in clone.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var bones = smr.bones;
                if (bones != null)
                {
                    var changed = false;
                    for (int i = 0; i < bones.Length; i++)
                        if (bones[i] != null && map.TryGetValue(bones[i], out var replacement))
                        {
                            bones[i] = replacement; changed = true; rewrittenBones++;
                        }
                    if (changed) smr.bones = bones;
                }

                if (smr.rootBone != null && map.TryGetValue(smr.rootBone, out var newRoot))
                {
                    smr.rootBone = newRoot; rewrittenRoots++;
                }
            }

            // Deepest-first, so a kept bone is lifted onto the avatar before the parent it was
            // hanging from is deleted, and a parent's removal never strands a queued child.
            int removed = 0;
            foreach (var prop in map.Keys.OrderByDescending(Depth).ToList())
            {
                if (prop == null) continue;
                if (kept.Contains(prop) || prop.childCount > 0)
                {
                    prop.SetParent(map[prop], worldPositionStays: true);
                    kept.Add(prop);
                    continue;
                }
                UnityEngine.Object.DestroyImmediate(prop.gameObject);
                removed++;
            }

            report.AppendLine($"      merged {map.Count} bone(s), moved {moves.Count} unmatched child(ren), " +
                              $"rewrote {rewrittenBones} skin reference(s) and {rewrittenRoots} root bone(s), " +
                              $"deleted {removed} emptied bone(s)" +
                              (kept.Count > 0 ? $", kept {kept.Count} carrying components or children" : "") + ".");

            if (mismatches.Count > 0)
            {
                report.AppendLine("      WARNING: matched bones whose poses disagree. The mesh was bound against the");
                report.AppendLine("               garment's bone and now follows the avatar's, so these will deform:");
                foreach (var m in mismatches.Take(12)) report.AppendLine($"                 {m}");
                if (mismatches.Count > 12)
                    report.AppendLine($"                 ... and {mismatches.Count - 12} more.");
            }

            return true;
        }

        static void Match(Rule rule, Transform prop, Transform target,
                          Dictionary<Transform, Transform> map,
                          List<KeyValuePair<Transform, Transform>> moves,
                          List<string> mismatches, GameObject clone)
        {
            map[prop] = target;

            if (Differs(prop, target))
                mismatches.Add($"`{prop.name}` -> `{target.name}`: {Delta(prop, target)}");

            foreach (var child in prop.Cast<Transform>().ToList())
            {
                var wanted = StripSuffix(child.name, rule.Suffix);
                var match = rule.RecursiveMatch ? FindChild(target, wanted) : null;
                if (match != null) Match(rule, child, match, map, moves, mismatches, clone);
                else moves.Add(new KeyValuePair<Transform, Transform>(child, prop));
            }
        }

        static Transform FindChild(Transform parent, string name)
        {
            foreach (Transform child in parent)
                if (string.Equals(child.name, name, StringComparison.Ordinal)) return child;
            // Unity's own duplicate-name suffix, and case differences between a garment authored
            // against one export of the base body and another, are common enough to be worth a
            // second pass rather than a silently unmatched bone.
            foreach (Transform child in parent)
                if (string.Equals(child.name, name, StringComparison.OrdinalIgnoreCase)) return child;
            return null;
        }

        static string StripSuffix(string name, string suffix) =>
            suffix.Length > 0 && name.Length > suffix.Length && name.EndsWith(suffix, StringComparison.Ordinal)
                ? name.Substring(0, name.Length - suffix.Length)
                : name;

        // --- Small helpers -------------------------------------------------------

        static bool Differs(Transform a, Transform b) =>
            Vector3.Distance(a.position, b.position) > PositionTolerance ||
            Quaternion.Angle(a.rotation, b.rotation) > AngleTolerance ||
            (a.lossyScale - b.lossyScale).magnitude > ScaleTolerance;

        static string Delta(Transform a, Transform b) =>
            $"{Vector3.Distance(a.position, b.position) * 100f:0.##} cm, " +
            $"{Quaternion.Angle(a.rotation, b.rotation):0.#}°, " +
            $"scale {a.lossyScale.x:0.###} vs {b.lossyScale.x:0.###}";

        /// <summary>The local scale that gives <paramref name="t"/> the same world scale as the target.</summary>
        static Vector3 MatchLossyScale(Transform t, Transform target)
        {
            var parent = t.parent;
            if (parent == null) return target.lossyScale;
            var p = parent.lossyScale;
            return new Vector3(
                Mathf.Approximately(p.x, 0f) ? t.localScale.x : target.lossyScale.x / p.x,
                Mathf.Approximately(p.y, 0f) ? t.localScale.y : target.lossyScale.y / p.y,
                Mathf.Approximately(p.z, 0f) ? t.localScale.z : target.lossyScale.z / p.z);
        }

        static int Depth(Transform t)
        {
            int d = 0;
            for (var cur = t; cur != null; cur = cur.parent) d++;
            return d;
        }

        static Transform Bone(Animator animator, HumanBodyBones bone) =>
            animator != null && animator.avatar != null && animator.avatar.isHuman
                ? animator.GetBoneTransform(bone)
                : null;

        static HumanBodyBones ToHumanBone(object o)
        {
            if (o == null) return HumanBodyBones.Hips;
            try { return (HumanBodyBones)Convert.ToInt32(o); }
            catch { return HumanBodyBones.Hips; }
        }

        static Transform AsTransform(object o)
        {
            switch (o)
            {
                case Transform t: return t != null ? t : null;
                case GameObject g: return g != null ? g.transform : null;
                case Component c: return c != null ? c.transform : null;
                default: return null;
            }
        }

        static object GetMember(object target, string name)
        {
            if (target == null) return null;
            var type = target.GetType();
            try
            {
                var field = type.GetField(name, F);
                if (field != null) return field.GetValue(target);
                var prop = type.GetProperty(name, F);
                if (prop != null) return prop.GetValue(target);
            }
            catch { }
            return null;
        }

        /// <summary>Identity, not Equals — two distinct feature models can compare equal by value.</summary>
        class ReferenceComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceComparer Instance = new ReferenceComparer();
            public new bool Equals(object a, object b) => ReferenceEquals(a, b);
            public int GetHashCode(object o) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o);
        }

        static string Path(Transform root, Transform t)
        {
            if (t == null) return null;
            if (t == root) return "";
            var parts = new List<string>();
            for (var cur = t; cur != null && cur != root; cur = cur.parent) parts.Add(cur.name);
            parts.Reverse();
            return string.Join("/", parts);
        }
    }
}
#endif
