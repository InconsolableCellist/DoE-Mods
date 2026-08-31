using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using MelonLoader.Utils;

namespace DoEFriendsMod.Avatars
{
    /// <summary>
    /// Scans <c>UserData/DoEFriendsMod/Avatars/</c> for exporter output and validates it.
    ///
    /// **A bundle whose SHA-256 doesn't match its manifest is refused outright**, not
    /// best-effort loaded. Two friends silently running different bytes under the same avatar
    /// name is exactly the class of confusion the Phase 1 gate exists to prevent, and it would
    /// be undone here if a truncated download were allowed through.
    /// </summary>
    public class AvatarLibrary
    {
        private readonly Dictionary<string, AvatarManifest> _byName =
            new Dictionary<string, AvatarManifest>(StringComparer.OrdinalIgnoreCase);

        public static string AvatarsDir =>
            Path.Combine(MelonEnvironment.UserDataDirectory, "DoEFriendsMod", "Avatars");

        public IReadOnlyDictionary<string, AvatarManifest> Avatars => _byName;

        public AvatarManifest Get(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            return _byName.TryGetValue(name, out var m) ? m : null;
        }

        /// <summary>First avatar alphabetically — what the preview uses when none is named.</summary>
        public AvatarManifest First()
        {
            AvatarManifest best = null;
            foreach (var kv in _byName)
                if (best == null || string.CompareOrdinal(kv.Value.name, best.name) < 0) best = kv.Value;
            return best;
        }

        public void Rescan()
        {
            _byName.Clear();

            try { Directory.CreateDirectory(AvatarsDir); }
            catch (Exception e)
            {
                Core.Log.Warning($"Could not create {AvatarsDir}: {e.Message}");
                return;
            }

            var files = Directory.GetFiles(AvatarsDir, "*.manifest.json", SearchOption.TopDirectoryOnly);
            if (files.Length == 0)
            {
                Core.Log.Msg($"No avatars found. Drop <name>.avatar + <name>.manifest.json into {AvatarsDir}");
                return;
            }

            foreach (var file in files)
            {
                var manifest = AvatarManifest.Load(file, out var error);
                if (manifest == null)
                {
                    Core.Log.Warning($"Skipped `{Path.GetFileName(file)}`: {error}");
                    continue;
                }

                if (!File.Exists(manifest.BundlePath))
                {
                    Core.Log.Warning($"Skipped `{manifest.name}`: bundle `{manifest.bundle}` is missing " +
                                     "(copy BOTH files from the exporter's DoEExport folder).");
                    continue;
                }

                var actual = Sha256(manifest.BundlePath, out var hashError);
                if (actual == null)
                {
                    Core.Log.Warning($"Skipped `{manifest.name}`: could not hash bundle — {hashError}");
                    continue;
                }

                if (!string.Equals(actual, manifest.sha256, StringComparison.OrdinalIgnoreCase))
                {
                    Core.Log.Error($"REFUSED `{manifest.name}`: bundle SHA-256 does not match its manifest.");
                    Core.Log.Error($"  manifest: {manifest.sha256}");
                    Core.Log.Error($"  bundle:   {actual}");
                    Core.Log.Error("  The file is truncated, edited, or paired with the wrong manifest. Re-copy both.");
                    continue;
                }

                if (_byName.ContainsKey(manifest.name))
                {
                    Core.Log.Warning($"Duplicate avatar name `{manifest.name}` — keeping the first, ignoring {Path.GetFileName(file)}.");
                    continue;
                }

                _byName[manifest.name] = manifest;
                Core.Log.Msg($"Avatar OK: {manifest.Describe()}");

                if (manifest.shadersUnvouchedForSpsi != null && manifest.shadersUnvouchedForSpsi.Count > 0)
                    Core.Log.Warning($"  `{manifest.name}` carries {manifest.shadersUnvouchedForSpsi.Count} shader(s) " +
                                     "not vouched for under Single Pass Instanced — may render in one eye only.");
            }

            Core.Log.Msg($"Avatar library: {_byName.Count} usable of {files.Length} manifest(s) in {AvatarsDir}");
        }

        private static string Sha256(string path, out string error)
        {
            error = null;
            try
            {
                using var sha = SHA256.Create();
                using var fs = File.OpenRead(path);
                return Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
            }
            catch (Exception e)
            {
                error = $"{e.GetType().Name}: {e.Message}";
                return null;
            }
        }
    }
}
