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
