using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace CustomAvatars.Face
{
    /// <summary>
    /// Per-avatar tuning for face tracking, read from `&lt;avatar&gt;.overrides.json` beside the
    /// avatar files.
    ///
    /// A VRChat avatar's FX controller normally carries this: eyelids that only ever close to
    /// 70%, a smile that needs easing, a shape the artist wants at half strength. We deliberately
    /// don't run that controller, so the tuning has to live somewhere — and it has to survive a
    /// re-export, which rules out the manifest, since the exporter overwrites that every time.
    /// A separate file next to it is never touched by the exporter.
    ///
    /// <code>
    /// {
    ///   "shapes": {
    ///     "EyeClosedLeft":  { "min": 0.0, "max": 0.7 },
    ///     "EyeClosedRight": { "min": 0.0, "max": 0.7 },
    ///     "JawOpen":        { "max": 0.8, "gamma": 1.5 },
    ///     "TongueOut":      { "enabled": false }
    ///   }
    /// }
    /// </code>
    ///
    /// `min`/`max` remap the 0..1 input onto that output range. `gamma` above 1 makes the shape
    /// slower to come on, below 1 quicker. `enabled: false` switches a shape off entirely.
    /// </summary>
    public class FaceOverrides
    {
        public class ShapeOverride
        {
            public float min = 0f;
            public float max = 1f;
            public float gamma = 1f;
            public bool enabled = true;
        }

        [JsonProperty("shapes")]
        public Dictionary<string, ShapeOverride> Shapes { get; set; }

        [JsonIgnore] public int Count => Shapes?.Count ?? 0;

        public static FaceOverrides Load(string avatarsDir, string avatarName, out string summary)
        {
            summary = "no overrides file";
            if (string.IsNullOrEmpty(avatarName)) return null;

            var path = Path.Combine(avatarsDir, avatarName + ".overrides.json");
            if (!File.Exists(path)) return null;

            try
            {
                var overrides = JsonConvert.DeserializeObject<FaceOverrides>(File.ReadAllText(path));
                if (overrides?.Shapes == null || overrides.Shapes.Count == 0)
                {
                    summary = $"`{Path.GetFileName(path)}` has no shapes";
                    return null;
                }
                summary = $"{overrides.Shapes.Count} override(s) from `{Path.GetFileName(path)}`";
                return overrides;
            }
            catch (Exception e)
            {
                summary = $"`{Path.GetFileName(path)}` could not be read — {e.Message}";
                return null;
            }
        }

        /// <summary>Remap one raw 0..1 tracking value for one UE shape.</summary>
        public float Apply(string ueName, float value)
        {
            if (Shapes == null || !Shapes.TryGetValue(ueName, out var o) || o == null) return value;
            if (!o.enabled) return 0f;

            var shaped = value;
            if (o.gamma > 0.001f && Math.Abs(o.gamma - 1f) > 0.001f)
                shaped = (float)Math.Pow(Math.Max(0f, Math.Min(1f, value)), o.gamma);

            return o.min + (o.max - o.min) * shaped;
        }
    }
}
