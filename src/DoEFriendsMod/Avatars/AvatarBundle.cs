using System;
using System.Collections.Generic;
using UnityEngine;

namespace DoEFriendsMod.Avatars
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
                existing._refCount++;
                Core.Log.Msg($"Reusing already-loaded bundle `{manifest.name}` (holders: {existing._refCount}).");
                return existing;
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
                if (self.Prefab == null) { self.UnloadNow(); return null; }

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
            if (_key != null) Loaded.Remove(_key);
            UnloadNow();
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
