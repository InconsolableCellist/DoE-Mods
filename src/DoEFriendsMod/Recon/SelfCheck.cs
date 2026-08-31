using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using MelonLoader;

namespace DoEFriendsMod.Recon
{
    /// <summary>
    /// Phase 1 groundwork: the mod hashes its own DLL so peers can compare builds. Running
    /// it already in Phase 0 means the recon transcript always says which build produced it
    /// — worth having the first time two logs disagree.
    /// </summary>
    public static class SelfCheck
    {
        /// <summary>Full lowercase SHA-256 of the loaded mod assembly, or null if unreadable.</summary>
        public static string ModHash { get; private set; }

        /// <summary>First 16 hex chars — what Phase 1 will advertise as <c>dfm.sha</c>.</summary>
        public static string ShortHash => ModHash == null ? "unknown" : ModHash.Substring(0, 16);

        public static void LogSelfHash(MelonLogger.Instance log)
        {
            try
            {
                var path = Assembly.GetExecutingAssembly().Location;
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    log.Warning("Self-hash unavailable: assembly has no on-disk location.");
                    return;
                }

                using var sha = SHA256.Create();
                using var fs = File.OpenRead(path);
                ModHash = Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
                log.Msg($"Mod DLL SHA-256: {ShortHash}… ({Path.GetFileName(path)})");
            }
            catch (Exception e)
            {
                log.Warning($"Self-hash failed: {e.GetType().Name}: {e.Message}");
            }
        }
    }
}
