using System;
using System.Collections.Generic;
using System.Text;
using CustomAvatars.Recon;
using Il2Cpp;
using UnityEngine;
using Interop = CustomAvatars.Recon.Interop;

namespace CustomAvatars.Avatars
{
    /// <summary>
    /// The circle of faces on the loading screen shows custom avatars, wearing the expression
    /// each player picked as their load face.
    ///
    /// The game draws that circle in `UILoadScreen`: `UpdateAvatar(idx, avatar)` fills slot
    /// `players[idx]` with the player's name, level and `AvatarPlayer.CharacterThumbnail`, a
    /// headshot of the vanilla character the game snapshots through `CharacterPrefab.thumbnailRig`.
    /// The expression in that snapshot is the "load face" cosmetic (`CharacterPrefab.avatarModules.loadFace`),
    /// a `CosmeticModuleContainer` whose `blendShapeDefs` are weights on the vanilla face's own
    /// shapes (relaxed, idle, happy, angry, sad, surprise, pain, tired, the blinks and ten
    /// visemes) and whose `boneRotationDefs` can open the jaw.
    ///
    /// After `UpdateAvatar`, for anyone wearing a custom avatar, we render our own headshot and
    /// put it where the vanilla one was. The avatar is a throwaway copy built at a far-off
    /// point on a layer only our camera draws, posed with the load face translated into Unified
    /// Expressions, rendered once, and destroyed in the same frame. The picture is kept per
    /// player until their avatar or load face changes.
    ///
    /// Gating comes for free: <see cref="AvatarSwapManager.AvatarNameFor"/> only names an
    /// avatar for a peer once the gate has put it on them, so a public room or a vanilla player
    /// keeps the game's own pictures. Local and cosmetic; nothing is sent.
    /// </summary>
    public class LoadScreenFaces
    {
        private const int RenderLayer = 31;
        private const int DefaultSize = 512;
        private static readonly Vector3 StagePosition = new Vector3(0f, -4000f, 0f);

        private class Cached
        {
            public string Key;
            public RenderTexture Texture;
        }

        private static LoadScreenFaces _instance;

        private readonly AvatarLibrary _library;
        private readonly AvatarSwapManager _swaps;
        private readonly Dictionary<int, Cached> _cache = new Dictionary<int, Cached>();
        private readonly HashSet<string> _reported = new HashSet<string>();

        public LoadScreenFaces(AvatarLibrary library, AvatarSwapManager swaps)
        {
            _library = library;
            _swaps = swaps;
            _instance = this;
        }

        public static void Install(HarmonyLib.Harmony harmony)
        {
            try
            {
                // UpdateAvatar has its own body (RVA 0x9DD240 in the 1.3 build, once in dump.cs),
                // so this postfix lands on nothing else.
                var target = HarmonyLib.AccessTools.Method(typeof(UILoadScreen), "UpdateAvatar");
                if (target == null)
                {
                    Core.Log.Warning("Load screen: UILoadScreen.UpdateAvatar not found — the loading screen keeps the game's faces.");
                    return;
                }
                harmony.Patch(target, postfix: new HarmonyLib.HarmonyMethod(
                    HarmonyLib.AccessTools.Method(typeof(LoadScreenFaces), nameof(UpdateAvatar_Postfix))));
                Core.Log.Msg("Load screen: UILoadScreen.UpdateAvatar hooked — custom avatars' faces go in the loading-screen circle.");
            }
            catch (Exception e)
            {
                Core.Log.Warning($"Load screen: could not hook UpdateAvatar ({e.GetType().Name}: {e.Message}).");
            }
        }

        private static void UpdateAvatar_Postfix(UILoadScreen __instance, int idx, AvatarPlayer avatar)
        {
            // Runs inside the game's load sequence. Never throw out of here.
            try { _instance?.OnSlotFilled(__instance, idx, avatar); }
            catch (Exception e) { Core.Log.Warning($"Load screen: face swap failed: {e.GetType().Name}: {e.Message}"); }
        }

        private void OnSlotFilled(UILoadScreen screen, int idx, AvatarPlayer avatar)
        {
            if (!Interop.Alive(screen) || !Interop.Alive(avatar)) return;

            var actor = avatar.ActorNumber;
            var avatarName = _swaps.AvatarNameFor(actor);
            if (avatarName == null) return;

            var players = screen.players;
            if (players == null || idx < 0 || idx >= players.Count) return;
            var slot = players[idx];
            var renderer = slot?.thumbnail;
            if (!Interop.Alive(renderer)) return;

            Texture2D vanilla = null;
            try { vanilla = avatar.CharacterThumbnail; } catch { }

            var face = ReadLoadFace(avatar, out var faceDesc);
            var key = $"{avatarName}|{faceDesc}";

            if (!_cache.TryGetValue(actor, out var cached) || cached.Key != key || !Interop.Alive(cached.Texture))
            {
                if (cached != null && Interop.Alive(cached.Texture))
                {
                    cached.Texture.Release();
                    UnityEngine.Object.Destroy(cached.Texture);
                }
                var texture = Render(avatarName, face, vanilla, out var how);
                if (texture == null) return;
                cached = new Cached { Key = key, Texture = texture };
                _cache[actor] = cached;
                Core.Log.Msg($"Load screen: `{avatarName}` headshot for {Who(avatar)} — load face {faceDesc}; {how}.");
            }

            var placed = Place(renderer, vanilla, cached.Texture);
            // On some headsets the slot is drawn as a compositor overlay rather than through the
            // renderer; if the picture doesn't change in the headset, this is the first suspect.
            try
            {
                var overlay = slot.thumbnailOverlay;
                if (Interop.Alive(overlay) && overlay.enabled) placed += "; the slot also has an active OVROverlay";
            }
            catch { }
            var report = $"{actor}|{placed}";
            if (_reported.Add(report))
                Core.Log.Msg($"Load screen: slot {idx} ({Who(avatar)}) {placed}.");
        }

        private static string Who(AvatarPlayer avatar)
        {
            try
            {
                var local = AvatarPlayer.LocalAvatar;
                return Interop.Alive(local) && local.ActorNumber == avatar.ActorNumber ? "you" : $"actor {avatar.ActorNumber}";
            }
            catch { return "a player"; }
        }

        // ---- the load face --------------------------------------------------------------

        /// <summary>
        /// What each vanilla face shape is in Unified Expressions, at full weight. The vanilla
        /// mesh has no ARKit or UE shapes (docs/GAME-INTERNALS.md), so this is a translation
        /// of each emotion into the facial movements that make it up, not a rename. Values
        /// combine by weighted sum and clamp at 1.
        /// </summary>
        private static readonly Dictionary<string, (string shape, float value)[]> Recipes =
            new Dictionary<string, (string, float)[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["idle"] = Array.Empty<(string, float)>(),
            ["relaxed"] = Both(("MouthCornerPull", 0.3f), ("EyeSquint", 0.15f)),
            ["happy"] = Both(("MouthCornerPull", 1f), ("CheekSquint", 0.6f), ("EyeSquint", 0.4f)),
            ["angry"] = Both(("BrowLowerer", 1f), ("BrowPinch", 0.7f), ("NoseSneer", 0.5f), ("MouthPress", 0.4f), ("EyeSquint", 0.3f)),
            ["sad"] = Both(("BrowInnerUp", 1f), ("BrowPinch", 0.3f), ("MouthFrown", 0.8f)),
            ["surprise"] = Concat(Both(("EyeWide", 1f), ("BrowInnerUp", 0.8f), ("BrowOuterUp", 1f)), ("JawOpen", 0.5f)),
            ["pain"] = Both(("EyeSquint", 0.8f), ("EyeClosed", 0.4f), ("BrowLowerer", 0.6f), ("BrowPinch", 0.6f), ("MouthStretch", 0.6f), ("NoseSneer", 0.4f)),
            ["tired"] = Both(("EyeClosed", 0.5f), ("BrowInnerUp", 0.2f), ("MouthFrown", 0.2f)),
            ["blinkL"] = new[] { ("EyeClosedLeft", 1f) },
            ["blinkR"] = new[] { ("EyeClosedRight", 1f) },
            // Visemes: the mouth shapes of speech.
            ["sill"] = Array.Empty<(string, float)>(),
            ["PP"] = Both(("MouthPress", 0.6f)),
            ["FF"] = Both(("LipSuckLower", 0.6f)),
            ["TH"] = new[] { ("TongueOut", 0.3f), ("JawOpen", 0.2f) },
            ["DD"] = new[] { ("JawOpen", 0.3f) },
            ["kk"] = new[] { ("JawOpen", 0.35f) },
            ["CH"] = Concat(Both(("LipFunnelUpper", 0.5f), ("LipFunnelLower", 0.5f)), ("JawOpen", 0.2f)),
            ["SS"] = Concat(Both(("MouthStretch", 0.4f)), ("JawOpen", 0.1f)),
            ["nn"] = new[] { ("JawOpen", 0.2f) },
            ["ih"] = Concat(Both(("MouthStretch", 0.3f)), ("JawOpen", 0.25f)),
        };

        private static (string, float)[] Both(params (string shape, float value)[] pairs)
        {
            var list = new List<(string, float)>(pairs.Length * 2);
            foreach (var p in pairs) { list.Add((p.shape + "Left", p.value)); list.Add((p.shape + "Right", p.value)); }
            return list.ToArray();
        }

        private static (string, float)[] Concat((string, float)[] a, params (string, float)[] b)
        {
            var list = new List<(string, float)>(a);
            list.AddRange(b);
            return list.ToArray();
        }

        /// <summary>
        /// The player's load face as UE values, and a short description of it for the log and
        /// the cache key. An unknown vanilla shape is named in the description and otherwise
        /// skipped, so a load face nobody has seen yet still renders, just without that part.
        /// </summary>
        private static Dictionary<string, float> ReadLoadFace(AvatarPlayer avatar, out string description)
        {
            var values = new Dictionary<string, float>(StringComparer.Ordinal);
            var parts = new StringBuilder();
            try
            {
                var container = avatar.FullBody?.avatarModules?.loadFace;
                var shapes = container?.blendShapeDefs;
                if (shapes != null)
                {
                    for (var i = 0; i < shapes.Length; i++)
                    {
                        var def = shapes[i];
                        if (def == null || string.IsNullOrEmpty(def.name) || def.weight <= 0f) continue;
                        var weight = Mathf.Clamp01(def.weight / 100f);
                        var known = Recipes.TryGetValue(def.name, out var recipe);
                        if (parts.Length > 0) parts.Append(", ");
                        parts.Append(def.name).Append(' ').Append(def.weight.ToString("0"));
                        if (!known) { parts.Append(" (no translation)"); continue; }
                        foreach (var (shape, value) in recipe)
                            values[shape] = Mathf.Clamp01((values.TryGetValue(shape, out var v) ? v : 0f) + value * weight);
                    }
                }

                var bones = container?.boneRotationDefs;
                if (bones != null)
                {
                    for (var i = 0; i < bones.Length; i++)
                    {
                        var def = bones[i];
                        if (def == null || string.IsNullOrEmpty(def.bone)) continue;
                        if (parts.Length > 0) parts.Append(", ");
                        parts.Append($"bone {def.bone} {Interop.Vec(def.rotation)}");
                        // A jaw rotation opens the mouth; about 25° is wide open on the vanilla rig.
                        if (def.bone.IndexOf("jaw", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            var open = Mathf.Clamp01(Mathf.Max(Mathf.Abs(def.rotation.x), Mathf.Abs(def.rotation.z)) / 25f);
                            values["JawOpen"] = Mathf.Max(values.TryGetValue("JawOpen", out var j) ? j : 0f, open);
                        }
                    }
                }
            }
            catch (Exception e) { parts.Append($"unreadable ({e.GetType().Name})"); }

            description = parts.Length == 0 ? "neutral" : parts.ToString();
            return values;
        }

        // ---- the picture ----------------------------------------------------------------

        private RenderTexture Render(string avatarName, Dictionary<string, float> face, Texture2D vanilla, out string how)
        {
            how = null;
            var manifest = _library.Get(avatarName);
            if (manifest == null) { Core.Log.Warning($"Load screen: `{avatarName}` is not in the library."); return null; }

            var bundle = AvatarBundle.Acquire(manifest, out var error);
            if (bundle == null) { Core.Log.Warning($"Load screen: could not load `{avatarName}`: {error}"); return null; }

            GameObject model = null;
            GameObject rig = null;
            try
            {
                if (!Interop.Alive(bundle.Prefab)) { Core.Log.Warning($"Load screen: `{avatarName}` prefab is not loaded."); return null; }

                model = UnityEngine.Object.Instantiate(bundle.Prefab);
                model.name = $"DFM_LoadFace_{avatarName}";
                model.SetActive(false);
                AvatarBundle.QuietAnimators(model);
                model.transform.SetPositionAndRotation(StagePosition, Quaternion.identity);
                model.transform.localScale = AvatarBundle.RootScale(model, "load screen") * manifest.rig.suggestedScale;
                foreach (var t in model.GetComponentsInChildren<Transform>(true))
                    if (Interop.Alive(t)) t.gameObject.layer = RenderLayer;
                foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if (Interop.Alive(smr)) smr.updateWhenOffscreen = true;
                model.SetActive(true);

                // The face first, while the model is posed at rest.
                var driver = new Face.FaceDriver();
                var faceResult = driver.Build(model, manifest);
                var applied = 0;
                if (driver.TargetCount > 0)
                {
                    var state = new Face.FaceState();
                    foreach (var kv in face) { state.Set("/avatar/parameters/v2/" + kv.Key, kv.Value); applied++; }
                    // A long step snaps the driver's smoothing straight to the target values.
                    driver.Apply(state, 10f);
                }

                var head = FindHead(model, manifest);
                if (!Interop.Alive(head)) { Core.Log.Warning($"Load screen: `{avatarName}` has no head bone — keeping the game's picture."); return null; }

                // Size everything by how high the head sits: a 1.55 m head bone is an ordinary
                // adult, and a small or huge avatar is framed the same way.
                var up = model.transform.up;
                var forward = model.transform.forward;
                var headHeight = Vector3.Dot(head.position - model.transform.position, up);
                var s = Mathf.Clamp(headHeight / 1.55f, 0.1f, 10f);
                var target = head.position + up * (0.07f * s);

                int width = DefaultSize, height = DefaultSize;
                if (Interop.Alive(vanilla) && vanilla.width > 0 && vanilla.height > 0) { width = vanilla.width; height = vanilla.height; }

                var texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4, name = $"DFM_LoadFace_{avatarName}" };
                texture.Create();

                rig = new GameObject("DFM_LoadFaceCamera");
                rig.transform.position = target + forward * (0.6f * s);
                rig.transform.LookAt(target, up);
                var camera = rig.AddComponent<Camera>();
                camera.enabled = false;
                camera.cullingMask = 1 << RenderLayer;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Background(vanilla);
                camera.fieldOfView = 28f;
                camera.nearClipPlane = 0.01f * s;
                camera.farClipPlane = 5f * s;
                camera.targetTexture = texture;
                camera.aspect = width / (float)height;

                // A key light over the camera's shoulder, so the face reads whatever scene we are leaving.
                var lightGo = new GameObject("DFM_LoadFaceLight");
                lightGo.transform.SetParent(rig.transform, false);
                lightGo.transform.localRotation = Quaternion.Euler(20f, -25f, 0f);
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.cullingMask = 1 << RenderLayer;
                light.intensity = 1.1f;

                camera.Render();
                camera.targetTexture = null;

                how = $"{width}x{height}, face {faceResult}, {applied} expression value(s) set";
                return texture;
            }
            catch (Exception e)
            {
                Core.Log.Warning($"Load screen: rendering `{avatarName}` failed: {e.GetType().Name}: {e.Message}");
                return null;
            }
            finally
            {
                if (Interop.Alive(model)) { model.SetActive(false); UnityEngine.Object.Destroy(model); }
                if (Interop.Alive(rig)) UnityEngine.Object.Destroy(rig);
                bundle.Release();
            }
        }

        private static Transform FindHead(GameObject model, AvatarManifest manifest)
        {
            try
            {
                if (manifest.rig?.humanoidBones != null &&
                    manifest.rig.humanoidBones.TryGetValue("Head", out var path) && !string.IsNullOrEmpty(path))
                {
                    var head = model.transform.Find(path);
                    if (Interop.Alive(head)) return head;
                }
            }
            catch { }
            try
            {
                var animator = model.GetComponentInChildren<Animator>(true);
                if (Interop.Alive(animator) && animator.isHuman) return animator.GetBoneTransform(HumanBodyBones.Head);
            }
            catch { }
            return null;
        }

        /// <summary>
        /// The vanilla picture's own background, read from its corner, so the headshot sits in
        /// the colour the player chose for their load screen. Dark grey if it can't be read.
        /// </summary>
        private static Color Background(Texture2D vanilla)
        {
            try
            {
                if (Interop.Alive(vanilla) && vanilla.isReadable)
                {
                    var c = vanilla.GetPixel(2, 2);
                    c.a = 1f;
                    return c;
                }
            }
            catch { }
            return new Color(0.12f, 0.12f, 0.14f, 1f);
        }

        /// <summary>
        /// Put our picture wherever the vanilla one went. We don't know which property
        /// `UpdateAvatar` set, so every texture slot on the slot's material that holds the
        /// vanilla thumbnail is replaced; failing that, the main texture.
        /// </summary>
        private static string Place(Renderer renderer, Texture2D vanilla, RenderTexture ours)
        {
            var material = renderer.material;
            if (!Interop.Alive(material)) return "has no material";

            var replaced = new List<string>();
            if (Interop.Alive(vanilla))
            {
                var names = material.GetTexturePropertyNames();
                for (var i = 0; i < names.Length; i++)
                {
                    var name = names[i];
                    Texture current = null;
                    try { current = material.GetTexture(name); } catch { }
                    if (Interop.Alive(current) && current.Pointer == vanilla.Pointer)
                    {
                        material.SetTexture(name, ours);
                        replaced.Add(name);
                    }
                }
            }

            // The game may set the picture through a property block instead, which overrides
            // the material; replace it there too.
            try
            {
                var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block);
                if (!block.isEmpty && Interop.Alive(vanilla))
                {
                    var names = material.GetTexturePropertyNames();
                    var changed = false;
                    for (var i = 0; i < names.Length; i++)
                    {
                        var name = names[i];
                        if (!block.HasTexture(name)) continue;
                        var current = block.GetTexture(name);
                        if (Interop.Alive(current) && current.Pointer == vanilla.Pointer)
                        {
                            block.SetTexture(name, ours);
                            replaced.Add(name + " (property block)");
                            changed = true;
                        }
                    }
                    if (changed) renderer.SetPropertyBlock(block);
                }
            }
            catch { }

            if (replaced.Count > 0) return $"picture replaced in {string.Join(", ", replaced)} on `{material.shader?.name}`";

            material.mainTexture = ours;
            return $"vanilla picture not found on `{material.shader?.name}`; set as its main texture";
        }
    }
}
