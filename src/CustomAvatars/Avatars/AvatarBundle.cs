using System;
using System.Collections.Generic;
using CustomAvatars.Recon;
using UnityEngine;

namespace CustomAvatars.Avatars
{
    /// <summary>
    /// Wraps one loaded <c>.avatar</c> bundle and hands out the prefab.
    ///
    /// Uses MelonLoader's <c>Il2CppAssetBundle</c> rather than Unity's <c>AssetBundle</c>:
    /// <c>AssetBundle.LoadAsset&lt;T&gt;</c> is a generic native method that Il2CppInterop
    /// can't dispatch cleanly, and MelonLoader ships this wrapper precisely for that.
    /// </summary>
    public class AvatarBundle : IDisposable
    {
        // Unity refuses to load a bundle file that is already loaded — LoadFromFile just
        // returns null. So the preview (F6) and the swap (F4) must share one load rather than
        // each opening the file, which is exactly what they were doing.
        private static readonly Dictionary<string, AvatarBundle> Loaded =
            new Dictionary<string, AvatarBundle>(StringComparer.OrdinalIgnoreCase);

        private Il2CppAssetBundle _bundle;
        private string _key;
        private int _refCount;

        public AvatarManifest Manifest { get; }
        public GameObject Prefab { get; private set; }

        private AvatarBundle(AvatarManifest manifest) { Manifest = manifest; }

        /// <summary>
        /// Load, or take a share of an existing load. Balance every call with
        /// <see cref="Release"/> — the file is only unloaded when the last holder lets go.
        /// </summary>
        public static AvatarBundle Acquire(AvatarManifest manifest, out string error)
        {
            error = null;
            var key = NormalizeKey(manifest.BundlePath);

            if (Loaded.TryGetValue(key, out var existing))
            {
                // Check the prefab is still there before handing it back. Unity can unload the
                // asset on a scene change — our reference to it is an interop proxy, which does
                // not count as a Unity reference keeping it alive — leaving a cache entry whose
                // prefab is destroyed. Instantiating that throws a bare NullReferenceException
                // from inside the engine, which says nothing about the real cause.
                if (Interop.Alive(existing.Prefab))
                {
                    existing._refCount++;
                    Core.Log.Msg($"Reusing already-loaded bundle `{manifest.name}` (holders: {existing._refCount}).");
                    return existing;
                }

                Core.Log.Warning($"Cached bundle `{manifest.name}` lost its prefab (unloaded on a scene change) — reloading.");
                Loaded.Remove(key);
                existing.UnloadNow();
            }

            var self = new AvatarBundle(manifest) { _key = key };
            try
            {
                self._bundle = Il2CppAssetBundleManager.LoadFromFile(manifest.BundlePath);
                if (self._bundle == null)
                {
                    error = "LoadFromFile returned null. Most often this means the bundle is already " +
                            "loaded by something else; otherwise it's a wrong Unity version, wrong " +
                            "build target, or a corrupt file";
                    return null;
                }

                self.Prefab = self.FindPrefab(out error);
                if (!Interop.Alive(self.Prefab)) { self.UnloadNow(); return null; }

                self._refCount = 1;
                Loaded[key] = self;
                return self;
            }
            catch (Exception e)
            {
                error = $"{e.GetType().Name}: {e.Message}";
                self.UnloadNow();
                return null;
            }
        }

        private static string NormalizeKey(string path)
        {
            try { return System.IO.Path.GetFullPath(path); } catch { return path ?? ""; }
        }

        /// <summary>
        /// The exporter builds with an explicit asset path, so the in-bundle name is a full
        /// lowercase "assets/doeexport_temp/&lt;name&gt;.prefab". Try what the manifest says,
        /// then fall back to scanning — a bundle with one GameObject in it is unambiguous.
        /// </summary>
        private GameObject FindPrefab(out string error)
        {
            error = null;

            foreach (var candidate in Candidates())
            {
                if (string.IsNullOrEmpty(candidate)) continue;
                try
                {
                    var go = _bundle.LoadAsset<GameObject>(candidate);
                    if (go != null) return go;
                }
                catch { /* try the next candidate */ }
            }

            try
            {
                var names = _bundle.AllAssetNames();
                if (names != null)
                    foreach (var n in names)
                    {
                        if (n == null || !n.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) continue;
                        var go = _bundle.LoadAsset<GameObject>(n);
                        if (go != null) return go;
                    }

                error = "no GameObject found in the bundle" +
                        (names != null ? $" (contains: {string.Join(", ", names)})" : "");
            }
            catch (Exception e)
            {
                error = $"prefab lookup failed: {e.GetType().Name}: {e.Message}";
            }
            return null;
        }

        private IEnumerable<string> Candidates()
        {
            yield return Manifest.prefabPath;
            yield return Manifest.prefab;
            yield return Manifest.name;
            if (!string.IsNullOrEmpty(Manifest.prefab))
                yield return $"assets/doeexport_temp/{Manifest.prefab}.prefab".ToLowerInvariant();
        }

        /// <summary>Give up one share. The file unloads when the last holder releases it.</summary>
        public void Release()
        {
            if (_refCount > 0) _refCount--;
            if (_refCount > 0) return;
            // Only evict the cache entry if it is still THIS object. A handle that lost its
            // prefab on a scene change is replaced in the cache by a fresh load; when the old
            // handle's last holder let go it removed the NEW entry's key, the file was still
            // loaded by the new one, and every swap for the next two seconds failed with
            // "LoadFromFile returned null" until the mannequins happened to release it.
            if (_key != null && Loaded.TryGetValue(_key, out var current) && ReferenceEquals(current, this))
                Loaded.Remove(_key);
            UnloadNow();
        }

        /// <summary>
        /// Switch off every Animator on an instance we have just made. The exporter keeps the
        /// avatar's Animator for its humanoid map, and if the author left a controller on it —
        /// a VRChat FX or locomotion layer is common — it plays that controller in every copy
        /// we spawn. The worn avatar hides it, because VRIK and the retargeter overwrite the
        /// bones and the root is re-anchored every frame. A mannequin has nobody re-anchoring
        /// it, so the controller's idle drove its hips and root around the pedestal. Nothing
        /// in the mod reads the avatar's own Animator; every pose here is ours.
        /// </summary>
        public static string QuietAnimators(GameObject instance)
        {
            if (!Interop.Alive(instance)) return "no instance";
            var count = 0;
            var controllers = new List<string>();
            var rootMotion = false;
            try
            {
                foreach (var animator in instance.GetComponentsInChildren<Animator>(true))
                {
                    if (!Interop.Alive(animator)) continue;
                    count++;
                    try
                    {
                        var controller = animator.runtimeAnimatorController;
                        if (Interop.Alive(controller)) controllers.Add(controller.name);
                        if (animator.applyRootMotion) rootMotion = true;
                    }
                    catch { }
                    try { animator.enabled = false; } catch { }
                }
            }
            catch (Exception e) { return $"could not inspect: {e.Message}"; }

            if (count == 0) return "none on the prefab";
            var what = controllers.Count == 0
                ? "no controller"
                : $"controller `{string.Join("`, `", controllers)}` still attached (harmless now; re-export to drop it)";
            return $"{count} disabled — {what}{(rootMotion ? ", root motion was on" : "")}";
        }

        /// <summary>
        /// Unload the bundle but NOT the objects loaded from it — instances already in the
        /// scene are destroyed separately, and pulling their assets out from under them gives
        /// you an avatar that renders as nothing at all.
        /// </summary>
        private void UnloadNow()
        {
            try { _bundle?.Unload(false); }
            catch (Exception e) { Core.Log.Warning($"Bundle unload failed: {e.Message}"); }
            _bundle = null;
            Prefab = null;
        }

        public void Dispose() => Release();
    }
}
