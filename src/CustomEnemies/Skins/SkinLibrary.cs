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
            /// <summary>Feet pointed down this many degrees from the shin, held there (not copied from the enemy).</summary>
            public float footPoint = 25f;
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

        /// <summary>Head and eyes turn toward the local player's camera, within limits (degrees).</summary>
        public sealed class Look
        {
            public float headYaw = 60f, headPitch = 30f;
            /// <summary>Share of the head turn taken by the neck.</summary>
            public float neckShare = 0.35f;
            public float eyeYaw = 25f, eyePitch = 15f;
            /// <summary>Only within this distance, metres; beyond it the head goes back to the copied pose.</summary>
            public float range = 15f;
            /// <summary>Seconds to turn about two thirds of the way (head; the eyes are four times quicker).</summary>
            public float speed = 0.3f;
        }

        /// <summary>Ears that flick now and then, and pin back when the enemy is close or biting.</summary>
        public sealed class Ears
        {
            public List<string> bones;
            /// <summary>Seconds between flicks, picked at random in this range.</summary>
            public float flickMin = 2.5f, flickMax = 8f;
            /// <summary>A flick: degrees back and degrees out.</summary>
            public float flick = 30f, twist = 22f;
            /// <summary>Degrees back when fully pinned (close range, bite).</summary>
            public float pinBack = 45f;
        }

        /// <summary>The growl as the player comes near: 0 beyond range, full at half of it.</summary>
        public sealed class Close
        {
            public float range = 4f;
            /// <summary>Jaw open, degrees, and a tremble on top.</summary>
            public float jaw = 7f, tremble = 1.5f;
            /// <summary>Blend shapes (0..100) at full growl.</summary>
            public Dictionary<string, float> shapes;
            /// <summary>Eye halos grow by this factor at full growl.</summary>
            public float haloScale = 1.8f;
            /// <summary>How pinned the ears are at full growl, 0..1.</summary>
            public float earsBack = 0.6f;
        }

        /// <summary>Eye glow from the skin's own emission map (loaded from this folder), brighter as the player comes near.</summary>
        public sealed class EyeGlow
        {
            /// <summary>The skin material the eyes use (as exported).</summary>
            public string material = "Eyes";
            public string texture;
            /// <summary>Swapped in at full growl and in a bite (the white pupils).</summary>
            public string furyTexture;
            public float intensity = 1.5f, furyIntensity = 5f;
            /// <summary>Emission colour far away and at full growl (the texture is a mask the shader tints); the eye halos follow it.</summary>
            public float[] colour = { 1f, 0.12f, 0.04f };
            public float[] furyColour = { 1f, 1f, 1f };
            /// <summary>Growl level (0..1) at which the fury texture takes over.</summary>
            public float furyAt = 0.6f;
        }

        /// <summary>A bite, played whenever the enemy's Animator enters the given state.</summary>
        public sealed class Bite
        {
            public string state = "Attack";
            /// <summary>Jaw open at the top of the wind-up, degrees.</summary>
            public float open = 38f;
            /// <summary>Seconds: wind-up (jaws open, rear back), snap (lunge and chomp), hold (shake), release.</summary>
            public float windup = 0.28f, snap = 0.09f, hold = 0.3f, release = 0.45f;
            /// <summary>Degrees the chest pitches forward in the lunge (a third of it backward in the wind-up).</summary>
            public float lunge = 18f;
            /// <summary>Head shake in the hold, degrees.</summary>
            public float shake = 5f;
            public Dictionary<string, float> shapes;
        }

        /// <summary>A sphere the spring chains stay out of: a bone, a radius in model metres, and an offset in the bone's frame.</summary>
        public sealed class SpringCollider
        {
            public string bone;
            public float radius = 0.1f;
            public float[] offset;
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
            public Look look;
            public Ears ears;
            public Close close;
            public EyeGlow eyeGlow;
            public Bite bite;
            /// <summary>Blend shapes held at a fixed weight (0..100), under whatever the growl and bite add.</summary>
            public Dictionary<string, float> shapes;
            public List<SpringCollider> colliders;
            /// <summary>Fixed finger curls, 0..1, thumb index middle ring little (CustomAvatars' HandPoser); null = hands as exported.</summary>
            public float[] grip;
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
