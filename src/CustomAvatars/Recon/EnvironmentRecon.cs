using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.XR;

namespace CustomAvatars.Recon
{
    /// <summary>
    /// Closes the first Open Question in docs/GAME-INTERNALS.md: stereo rendering mode and
    /// the graphics/XR stack, which together dictate what a custom avatar's shaders must
    /// support. Single-Pass Instanced is the expected answer; this is how we stop expecting
    /// and start knowing.
    /// </summary>
    public static class EnvironmentRecon
    {
        private static bool _done;

        public static void DumpOnce(bool force = false)
        {
            if (_done && !force) return;
            _done = true;

            ReconLog.Section("Environment / XR / graphics");

            ReconLog.TryKeyValue("Unity version", () => Application.unityVersion);
            ReconLog.TryKeyValue("Game version", () => Application.version);
            ReconLog.TryKeyValue("Platform", () => Application.platform);
            ReconLog.TryKeyValue("Target frame rate", () => Application.targetFrameRate);

            ReconLog.Line();
            ReconLog.Line("### XR");
            // THE question for the avatar shader constraint. SinglePassInstanced means every
            // custom avatar shader must be SPS-I aware (liltoon / locked Poiyomi / Standard).
            ReconLog.TryKeyValue("XRSettings.enabled", () => XRSettings.enabled);
            ReconLog.TryKeyValue("XRSettings.isDeviceActive", () => XRSettings.isDeviceActive);
            ReconLog.TryKeyValue("XRSettings.loadedDeviceName", () => XRSettings.loadedDeviceName);
            ReconLog.TryKeyValue("XRSettings.stereoRenderingMode", () => XRSettings.stereoRenderingMode);
            ReconLog.TryKeyValue("XRSettings.eyeTexture", () => $"{XRSettings.eyeTextureWidth}x{XRSettings.eyeTextureHeight}");
            ReconLog.TryKeyValue("XRSettings.renderViewportScale", () => XRSettings.renderViewportScale);
            ReconLog.TryKeyValue("XRSettings.useOcclusionMesh", () => XRSettings.useOcclusionMesh);
            ReconLog.Try("active XR loader", () => ReconLog.KeyValue("Active XR loader", ProbeActiveXrLoader()));

            ReconLog.Line();
            ReconLog.Line("### Graphics");
            ReconLog.TryKeyValue("Graphics API", () => SystemInfo.graphicsDeviceType);
            ReconLog.TryKeyValue("GPU", () => SystemInfo.graphicsDeviceName);
            ReconLog.TryKeyValue("Driver", () => SystemInfo.graphicsDeviceVersion);
            ReconLog.TryKeyValue("Shader level", () => SystemInfo.graphicsShaderLevel);
            ReconLog.TryKeyValue("Supports instancing", () => SystemInfo.supportsInstancing);
            ReconLog.TryKeyValue("Max texture size", () => SystemInfo.maxTextureSize);
            ReconLog.TryKeyValue("Graphics memory (MB)", () => SystemInfo.graphicsMemorySize);

            ReconLog.Line();
            ReconLog.Line("### Quality");
            ReconLog.Try("quality level", () =>
            {
                var level = QualitySettings.GetQualityLevel();
                var names = QualitySettings.names;
                var label = (names != null && level >= 0 && level < names.Length) ? names[level] : "?";
                ReconLog.KeyValue("Quality level", $"{level} ({label})");
            });
            // skinWeights caps bones-per-vertex: VRC avatars routinely author for 4, and a
            // game running at TwoBones will visibly deform them wrong.
            ReconLog.TryKeyValue("QualitySettings.skinWeights", () => QualitySettings.skinWeights);
            ReconLog.TryKeyValue("Anti-aliasing", () => QualitySettings.antiAliasing);
            ReconLog.TryKeyValue("Shadow distance", () => QualitySettings.shadowDistance);
            ReconLog.TryKeyValue("Pixel light count", () => QualitySettings.pixelLightCount);
            ReconLog.TryKeyValue("LOD bias", () => QualitySettings.lodBias);
            ReconLog.TryKeyValue("vSync count", () => QualitySettings.vSyncCount);

            ReconLog.Headline($"Environment dump written to {ReconLog.CurrentFile}");
        }

        /// <summary>
        /// Unity.XR.Management may or may not be present as a generated interop assembly, and
        /// its type names shift between XR plugin versions — so this walks loaded assemblies
        /// by name instead of taking a compile-time reference we might not have.
        /// </summary>
        private static string ProbeActiveXrLoader()
        {
            var settingsType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(SafeTypes)
                .FirstOrDefault(t => t.Name == "XRGeneralSettings");

            if (settingsType == null) return "<XRGeneralSettings type not found>";

            var instance = GetMember(settingsType, null, "Instance");
            if (instance == null) return "<XRGeneralSettings.Instance null>";

            var manager = GetMember(instance.GetType(), instance, "Manager")
                       ?? GetMember(instance.GetType(), instance, "m_LoaderManagerInstance");
            if (manager == null) return "<XRManagerSettings null>";

            var loader = GetMember(manager.GetType(), manager, "activeLoader");
            if (loader == null) return "<no active loader>";

            var loaderName = GetMember(loader.GetType(), loader, "name");
            return $"{loader.GetType().Name} (name: {loaderName ?? "?"})";
        }

        private static Type[] SafeTypes(Assembly a)
        {
            try { return a.GetTypes(); }
            catch { return Array.Empty<Type>(); }
        }

        private static object GetMember(Type type, object target, string name)
        {
            const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic
                                     | BindingFlags.Static | BindingFlags.Instance;
            try
            {
                var prop = type.GetProperty(name, Flags);
                if (prop != null) return prop.GetValue(target);
                var field = type.GetField(name, Flags);
                if (field != null) return field.GetValue(target);
            }
            catch { /* interop reflection is best-effort; caller reports the miss */ }
            return null;
        }
    }
}
