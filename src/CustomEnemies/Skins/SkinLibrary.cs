using System;
using System.Collections.Generic;
using System.IO;
using CustomAvatars.Avatars;
using MelonLoader.Utils;
using Newtonsoft.Json;

namespace CustomEnemies.Skins
{
    /// <summary>
    /// The skins on disk. A skin is what the avatar exporter writes (Tools ▸ Foxipso ▸ DoE Avatar
    /// Export ▸ Export Selected Avatar): <c>&lt;name&gt;.avatar</c> and
    /// <c>&lt;name&gt;.manifest.json</c>, the same files CustomAvatars wears, plus one sidecar,
    /// <c>&lt;name&gt;.enemy.json</c>, that says which enemy it replaces and how it sits on it.
    /// All three go in <c>UserData/CustomEnemies/</c>. A manifest without a sidecar is ignored, so
    /// an avatar copied here by mistake never turns up on an enemy.
    /// </summary>
    public static class SkinLibrary
    {
        public sealed class Weapon
        {
            /// <summary>The skin's weapon bone (it is moved, with everything skinned to it).</summary>
            public string bone;
            /// <summary>The skin's bone it is held by, e.g. a hand or a "hand item" bone.</summary>
            public string to;
            public float[] euler;
            public float[] offset;
        }

        public sealed class Legs
        {
            /// <summary>Thighs swung forward from hanging straight down, in degrees.</summary>
            public float thighPitch = 50f;
            /// <summary>Knees bent back, in degrees.</summary>
            public float kneeBend = 70f;
            /// <summary>Slow sway either side of that, in degrees.</summary>
            public float sway = 6f;
            public float swayPeriod = 2.6f;
            /// <summary>Degrees the legs trail behind per metre per second of travel, capped at 35.</summary>
            public float lag = 18f;
        }

        /// <summary>A spring chain (tail, robe): bones parent first; the solver is CustomAvatars' own.</summary>
        public sealed class Spring
        {
            public string name;
            public List<string> bones;
            /// <summary>Pull back toward the animated pose, 0..1.</summary>
            public float stiffness = 0.2f;
            /// <summary>Bounce (less drag), 0..1.</summary>
            public float spring = 0.3f;
            public float gravity = 0.3f;
            /// <summary>How much the chain ignores the body's own travel, 0..1.</summary>
            public float immobile;
            /// <summary>Cone limit in degrees from the animated direction; 0 = the solver's default.</summary>
            public float maxAngle;
        }

        /// <summary>Small motion on top of the enemy's pose, so a floating enemy doesn't look carved.</summary>
        public sealed class Idle
        {
            /// <summary>Chest pitch, degrees, and its period in seconds.</summary>
            public float breath = 2f;
            public float breathPeriod = 3.6f;
            /// <summary>Head wander, degrees, and its period in seconds.</summary>
            public float head = 4f;
            public float headPeriod = 5.3f;
        }

        public sealed class Sidecar
        {
            public List<string> replaces;
            /// <summary>On top of the automatic fit (the skin's head at the enemy's head height).</summary>
            public float scale = 1f;
            public float yaw;
            public float[] offset;
            /// <summary>Draw the skin with the enemy's own shader (lit like the enemy) using the skin's base colour textures.</summary>
            public bool gameShader = true;
            /// <summary>Materials (by name) left exactly as exported.</summary>
            public List<string> keepMaterials;
            /// <summary>Bones the eye halos sit on.</summary>
            public List<string> eyes;
            public float haloSize = 0.12f;
            public float[] haloColour;
            public List<Weapon> weapons;
            /// <summary>Hang the legs (glide); null leaves them copying the enemy's legs.</summary>
            public Legs legs;
            public List<Spring> springs;
            public Idle idle;
            /// <summary>Shader keywords switched off on the skin's copies of the enemy material; null = the default (the X-ray outline).</summary>
            public List<string> keywordsOff;
            /// <summary>
            /// Texture slots of the enemy material, by property name, and what the skin puts there:
            /// "keep" (the enemy's own), "none", "black", "white", "grey", "bump" (flat normal), or
            /// "skin:&lt;property&gt;" (that texture of the skin's own exported material).
            /// Slots not listed are guessed from their names; the report lists every slot.
            /// </summary>
            public Dictionary<string, string> textures;
            /// <summary>Who made the model and its licence; logged and kept with the skin.</summary>
            public string credit;
        }

        public sealed class Skin
        {
            public string Name;
            public AvatarManifest Manifest;
            public Sidecar Def;
            public override string ToString() => Name;
        }

        private static readonly List<Skin> Skins = new List<Skin>();
        private static bool _scanned;

        public static string Folder => Path.Combine(MelonEnvironment.UserDataDirectory, "CustomEnemies");

        public static IReadOnlyList<Skin> All
        {
            get { if (!_scanned) Scan(); return Skins; }
        }

        public static void Rescan() { _scanned = false; Scan(); }

        private static void Scan()
        {
            _scanned = true;
            Skins.Clear();
            try
            {
                Directory.CreateDirectory(Folder);
                foreach (var side in Directory.GetFiles(Folder, "*.enemy.json"))
                {
                    var name = Path.GetFileName(side);
                    name = name.Substring(0, name.Length - ".enemy.json".Length);
                    var manifestPath = Path.Combine(Folder, name + ".manifest.json");
                    if (!File.Exists(manifestPath)) { Core.Log.Warning($"Skin `{name}`: {name}.enemy.json has no {name}.manifest.json beside it."); continue; }
                    Sidecar def;
                    try { def = JsonConvert.DeserializeObject<Sidecar>(File.ReadAllText(side)); }
                    catch (Exception e) { Core.Log.Warning($"Skin `{name}`: {name}.enemy.json does not parse: {e.Message}"); continue; }
                    if (def?.replaces == null || def.replaces.Count == 0) { Core.Log.Warning($"Skin `{name}`: `replaces` is empty, so it would never be used."); continue; }
                    var m = AvatarManifest.Load(manifestPath, out var err);
                    if (m == null) { Core.Log.Warning($"Skin `{name}`: manifest: {err}"); continue; }
                    if (!File.Exists(m.BundlePath)) { Core.Log.Warning($"Skin `{name}`: bundle {Path.GetFileName(m.BundlePath)} is missing."); continue; }
                    if (m.rig?.humanoidBones == null) { Core.Log.Warning($"Skin `{name}`: the manifest has no humanoid bones; re-export it as a humanoid."); continue; }
                    Skins.Add(new Skin { Name = name, Manifest = m, Def = def });
                    Core.Log.Msg($"Skin `{name}` replaces {string.Join(", ", def.replaces)}" +
                                 (string.IsNullOrEmpty(def.credit) ? "" : $". Model: {def.credit}"));
                }
            }
            catch (Exception e) { Core.Log.Warning($"Skin folder scan failed: {e.GetType().Name}: {e.Message}"); }
            if (Skins.Count == 0) Core.Log.Msg($"No skins in {Folder} (a skin is <name>.avatar + <name>.manifest.json + <name>.enemy.json).");
        }

        /// <summary>The first skin that replaces this enemy, by name or Sauron ID.</summary>
        public static Skin For(string name, string id)
        {
            foreach (var s in All)
                foreach (var r in s.Def.replaces)
                    if (string.Equals(r, name, StringComparison.OrdinalIgnoreCase) ||
                        (id != null && string.Equals(r, id, StringComparison.OrdinalIgnoreCase))) return s;
            return null;
        }
    }
}
