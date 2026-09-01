using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace DoEFriendsMod.Avatars
{
    /// <summary>
    /// The `<name>.manifest.json` written by the Unity exporter
    /// (`unity/AvatarExport/Editor/DoEAvatarExporter.cs`). Deliberately tolerant: every field
    /// is optional at parse time and validated afterwards, so a manifest from a newer or older
    /// exporter produces a clear complaint rather than a deserialisation exception.
    /// </summary>
    public class AvatarManifest
    {
        public int schema;
        public string name;
        public string bundle;
        public string sha256;
        public string prefab;
        public string prefabPath;
        public string unity;
        public string exportedAt;

        public RigInfo rig;
        public CostInfo cost;
        public List<string> shaders;
        public List<string> shadersUnvouchedForSpsi;
        public FaceTrackingInfo faceTracking;
        public DynamicsInfo dynamics;

        /// <summary>Absolute path of the manifest file, filled in by the loader.</summary>
        [JsonIgnore] public string ManifestPath;
        /// <summary>Absolute path of the sibling bundle, filled in by the loader.</summary>
        [JsonIgnore] public string BundlePath;

        public class RigInfo
        {
            public float height;
            public float headHeight;
            public float humanScale;
            /// <summary>Multiplier that puts this avatar's head at the game's 1.5 m viewpoint.</summary>
            public float suggestedScale = 1f;
            public float gameHeadHeight;
            public int maxBoneInfluences;
            public Dictionary<string, string> humanoidBones;
            public string jawBone;
        }

        /// <summary>VRCPhysBone configuration, captured by the exporter before it strips the SDK.</summary>
        public class DynamicsInfo
        {
            public List<ChainInfo> chains;
            public List<ColliderInfo> colliders;
        }

        public class ChainInfo
        {
            public string name;
            /// <summary>Which system it came from — VRCPhysBone, DynamicBone, … — for diagnosis.</summary>
            public string source;
            public List<string> bones;
            /// <summary>How strongly the chain returns to its animated pose (VRC `pull`, 0..1).</summary>
            public float pull = 0.2f;
            public float spring = 0.2f;
            /// <summary>Resistance to bending away from the rest direction (VRC `stiffness`, 0..1).</summary>
            public float stiffness = 0.2f;
            /// <summary>Fraction of world gravity applied (VRC `gravity`, 0..1).</summary>
            public float gravity;
            public float gravityFalloff;
            /// <summary>How much the chain ignores the wearer's own motion (VRC `immobile`, 0..1).</summary>
            public float immobile;
            public float radius;
            /// <summary>VRChat's PhysBone limit: None, Angle, Hinge or Polar.</summary>
            public string limitType;
            public float maxAngleX;
            public float maxAngleZ;
            /// <summary>Euler angles defining the frame the limit is measured against.</summary>
            public List<float> limitRotation;
            public List<float> endpointPosition;
        }

        public class ColliderInfo
        {
            public string path;
            public string shape;
            public float radius = 0.1f;
            public float height;
            public List<float> position;
            public List<float> rotation;
        }

        public class CostInfo
        {
            public int skinnedMeshes;
            public int vertices;
            public int submeshes;
            public int uniqueTextures;
            public long textureBytes;
        }

        public class ShapeRef
        {
            public string renderer;
            public int index;
            /// <summary>Set when the exporter resolved this through its alias table.</summary>
            public string via;
        }

        public class FaceTrackingInfo
        {
            public bool hasFaceTracking;
            public Dictionary<string, ShapeRef> shapes;
            public Dictionary<string, ShapeRef> visemes;
            public bool eyeUseBones;
            public string eyeBoneLeft;
            public string eyeBoneRight;
            public Dictionary<string, float> eyeMaxDegrees;
        }

        public static AvatarManifest Load(string path, out string error)
        {
            error = null;
            try
            {
                var json = File.ReadAllText(path);
                var m = JsonConvert.DeserializeObject<AvatarManifest>(json);
                if (m == null) { error = "manifest deserialised to null"; return null; }

                m.ManifestPath = path;
                if (string.IsNullOrEmpty(m.name)) { error = "manifest has no `name`"; return null; }
                if (string.IsNullOrEmpty(m.bundle)) { error = "manifest has no `bundle`"; return null; }
                if (string.IsNullOrEmpty(m.sha256)) { error = "manifest has no `sha256`"; return null; }

                m.BundlePath = Path.Combine(Path.GetDirectoryName(path) ?? ".", m.bundle);
                m.rig ??= new RigInfo();
                if (m.rig.suggestedScale <= 0.01f || m.rig.suggestedScale > 100f) m.rig.suggestedScale = 1f;
                m.faceTracking ??= new FaceTrackingInfo();

                return m;
            }
            catch (Exception e)
            {
                error = $"{e.GetType().Name}: {e.Message}";
                return null;
            }
        }

        public string Describe()
        {
            var ft = faceTracking == null ? "none"
                : $"{(faceTracking.shapes?.Count ?? 0)} UE shapes, {(faceTracking.visemes?.Count ?? 0)} visemes" +
                  $"{(faceTracking.eyeUseBones ? ", eye bones" : "")}";
            var geo = cost == null ? "?" : $"{cost.vertices:N0} verts / {cost.skinnedMeshes} mesh(es)";
            return $"`{name}` — {geo}, scale x{rig.suggestedScale:0.###}, face: {ft}";
        }
    }
}
