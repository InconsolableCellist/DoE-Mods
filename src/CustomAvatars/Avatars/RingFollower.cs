using System;
using System.Collections.Generic;
using CustomAvatars.Recon;
using UnityEngine;
using Il2Cpp;
using Interop = CustomAvatars.Recon.Interop;

namespace CustomAvatars.Avatars
{
    /// <summary>
    /// Puts a worn ring on the custom avatar's finger.
    ///
    /// Rings arrived with the 2026-09-27 update (`WeaponFactory.EquippableType.*Ring`, looted
    /// from the dark crypts). A worn ring is an `Equippable` (prefab `Ring_Gen1`, a PhotonView
    /// for RPCs and no transform sync, its mesh generated at runtime) parented straight onto a
    /// vanilla finger bone, at identity:
    ///
    ///   - on your own client, `EquippableHolster.InitHolsterContents` spawns it with
    ///     `PhotonNetwork.Instantiate` and parents it to the holster's `attachPoint`, which in
    ///     the scene data is `VR Controller/FPS-Arms-Model/.../hand_l/ring_01_l` (holsterType
    ///     LeftRing, slot RingL; the right one likewise) — the first-person arms' ring finger;
    ///   - `Equippable.Net_Holster` then sends `Remote_Holster` to everyone else (RpcTarget
    ///     Others), where `AvatarHolster.HolsterEquippable` parents it to that player's
    ///     `Model_&lt;nick&gt;/.../ring_01_l/Holster RingLeft` — an `AvatarHolster` whose `bone` is
    ///     `HumanBodyBones.LeftRingProximal` (48, RightRingProximal, on the right), bound with
    ///     `resetTransformOnBind`, so it too sits exactly on the proximal ring bone;
    ///   - the equipment-room mannequin gets the same through `Hologram_Holster`.
    ///
    /// None of that sees our avatar, and the ring's renderer is its own, not the vanilla mesh
    /// we hide, so it stayed visible on a finger that is nowhere near the avatar's. This moves
    /// it. Each frame, after the fingers are posed, the ring is placed on the avatar's bone for
    /// the same `HumanBodyBones`, as it sat on the vanilla one: the same fraction of the way
    /// along the segment, the same roll relative to the back of the hand, and sized to the
    /// avatar's finger. Nothing is reparented — the ring belongs to the game and to Photon,
    /// and is destroyed and re-holstered by them — so it is written in world space every frame
    /// and handed back its local pose when the avatar comes off.
    ///
    /// Every measurement is the avatar's own. Each finger is a tube: the segment from the
    /// bone to the next joint, a frame from that segment and the index-to-little knuckle line
    /// (bone axis conventions differ per rig, see ArmIK.HandFromTargetFrame), and a radius and
    /// centre measured from the vertices skinned to that bone. Where a mesh can't be read
    /// (not readable, no weights) both sides fall back to a radius proportional to segment
    /// length, so the ring scales with the finger rather than not at all. An avatar with no
    /// bone for that finger gets the ring hidden (scaled to zero) rather than left floating
    /// where the vanilla finger is; it comes back on revert.
    ///
    /// Your own rings are then shown per your settings for that hand (`Look`: off until you
    /// turn it on, then size, up, slide and three angles on top of the fit); other players'
    /// rings get the fit alone, behind one switch. All local: it only changes what this client
    /// draws. Size and angles pivot on the band's centre, so the ring stays on the finger.
    ///
    /// Grep the log for `ring:`.
    /// </summary>
    public class RingFollower
    {
        /// <summary>Where a ring can turn up: a transform it gets parented to, and the finger that means.</summary>
        public class Site
        {
            public Transform Anchor;          // the ring's parent: a holster, or the attach point itself
            public HumanBodyBones Bone;       // the finger bone, in humanoid terms
            public Transform VanillaBone;     // that bone on the rig the ring is on
            public Transform VanillaNext, VanillaIndex, VanillaLittle, VanillaHand;
            public List<SkinnedMeshRenderer> VanillaSkins = new List<SkinnedMeshRenderer>();
            /// <summary>The same finger on another rig with the same proportions, whose mesh can stand in.</summary>
            public Transform ShapeBone, ShapeNext, ShapeIndex, ShapeLittle;
            public List<SkinnedMeshRenderer> ShapeSkins = new List<SkinnedMeshRenderer>();
            public string Where;
        }

        /// <summary>A finger segment, in its bone's local space so it rides along with the bone.</summary>
        private class Tube
        {
            public Transform Bone;
            public Quaternion Frame;   // local: Z along the finger, Y from the knuckle line
            public float Length;       // local units, to the next joint
            public bool FromMesh;
            public float RadiusPerLength;
            public Vector2 CentrePerLength;  // cross-section centre off the bone axis, frame X/Y
            public string Note;
        }

        private class Ring
        {
            public Transform T;
            public Transform Parent;
            public Vector3 LocalPos, LocalScale;
            public Quaternion LocalRot;
            public string Name;
            public Site Site;
            public Tube Avatar;
            public bool UseMesh;
            public float Along;         // band centre, fraction of the segment
            public Vector2 Across;      // band centre off the finger's centre, in radii
            public Quaternion Rot;      // ring rotation in the finger frame
            public float SizePerRadius; // ring lossy scale per world finger radius
            public Vector3 CentreInRing;
            public bool Hidden;
            public bool LeftAlone;      // nothing to measure against; the game's placement stands
        }

        // Only the ratio matters when a mesh can't be read: the same constant on both sides
        // cancels, leaving the ring scaled by the ratio of segment lengths. 0.2 is a typical
        // proximal phalanx (about 4.5 cm long, 0.9 cm radius), so the logged radii read sensibly.
        private const float FallbackRadiusPerLength = 0.2f;
        private const float ScanSeconds = 0.5f;
        private const float SiteSeconds = 2f;
        // The band's hole against the finger's measured radius (a median of its surface), so
        // the ring sits on the skin rather than in it.
        private const float FitSlack = 1.1f;
        private const float MinSize = 0.5f, MaxSize = 4f;

        /// <summary>
        /// How one of your own rings is shown, on top of the automatic fit. Read from
        /// `[CustomAvatars] RingLeft*` / `RingRight*`. Everything here is local: it changes only
        /// what this client draws.
        ///   Visible: off by default, so a ring that would be placed badly never shows until
        ///     its owner turns it on (and tunes it).
        ///   Size: a multiplier on the fitted size.
        ///   Offset: up from the back of the hand, in finger radii.
        ///   Slide: toward the fingertip, as a share of the finger bone.
        ///   Tilt / Turn / Roll: degrees in the hand's frame. Tilt tips the top of the ring
        ///     toward the fingertip, turn points it toward the thumb, roll turns its top toward
        ///     the little finger. Turn and roll are mirrored on the right hand, so the same
        ///     numbers look the same on both hands.
        /// </summary>
        public class Look
        {
            public bool Visible;
            public float Size = 1f, Offset, Slide, Tilt, Turn, Roll;

            public override string ToString() =>
                $"{(Visible ? "on" : "off")} x{Size:0.00} up {Offset:+0.0;-0.0;0} slide {Slide:+0.00;-0.00;0} " +
                $"tilt/turn/roll {Tilt:0}/{Turn:0}/{Roll:0}°";
        }

        public static readonly Look Left = new Look(), Right = new Look();

        /// <summary>
        /// Other players' rings: the automatic fit alone (your tuning is for your fingers, not
        /// theirs), shown only when `RingsOthersVisible` is on. Off by default.
        /// </summary>
        public static readonly Look Others = new Look();

        private static float _nextRefreshAt;

        /// <summary>
        /// Re-reads the ring settings. Called at load, on F3, and twice a second from the
        /// followers, so a change made in game (LootOverhaul's menu writing the entries) shows
        /// at once with no F3. Reading fifteen cached entries twice a second costs nothing.
        /// </summary>
        public static void RefreshSize()
        {
            _nextRefreshAt = Time.unscaledTime + ScanSeconds;
            ReadLook(Left, ModConfig.RingLeft);
            ReadLook(Right, ModConfig.RingRight);
            try { Others.Visible = ModConfig.RingsOthersVisible.Value; } catch { Others.Visible = false; }
        }

        private static void ReadLook(Look look, ModConfig.RingEntries e)
        {
            if (e == null) return;
            try { look.Visible = e.Visible.Value; } catch { look.Visible = false; }
            var size = Read(e.Size, MinSize, MaxSize, 1f);
            look.Size = size;
            look.Offset = Read(e.Offset, -5f, 5f, 0f);
            look.Slide = Read(e.Slide, -1f, 1f, 0f);
            look.Tilt = Read(e.Tilt, -180f, 180f, 0f);
            look.Turn = Read(e.Turn, -180f, 180f, 0f);
            look.Roll = Read(e.Roll, -180f, 180f, 0f);
        }

        private static float Read(MelonLoader.MelonPreferences_Entry<float> entry, float min, float max, float fallback)
        {
            var v = fallback;
            try { v = entry.Value; } catch { }
            return float.IsFinite(v) ? Mathf.Clamp(v, min, max) : fallback;
        }

        private readonly string _who;
        private readonly bool _self;
        private readonly GameObject _model;
        private readonly AvatarManifest _manifest;
        private readonly Func<List<Site>> _findSites;
        private readonly Dictionary<IntPtr, Ring> _rings = new Dictionary<IntPtr, Ring>();
        private readonly Dictionary<int, Tube> _avatarTubes = new Dictionary<int, Tube>();
        private readonly HashSet<int> _avatarMissing = new HashSet<int>();
        private readonly Dictionary<IntPtr, float> _pending = new Dictionary<IntPtr, float>();
        private const float PendingSeconds = 3f;
        private List<Site> _sites = new List<Site>();
        private float _nextScanAt, _nextSitesAt;
        private bool _failed;

        /// <param name="who">For the log: whose avatar this is.</param>
        /// <param name="self">Your own avatar (or the mannequin showing it): your ring settings apply.</param>
        /// <param name="findSites">Enumerates where rings can appear; called every couple of seconds.</param>
        public RingFollower(string who, bool self, GameObject model, AvatarManifest manifest, Func<List<Site>> findSites)
        {
            _who = who;
            _self = self;
            _model = model;
            _manifest = manifest;
            _findSites = findSites;
        }

        public int Count => _rings.Count;

        /// <summary>Every frame, after the avatar's fingers are posed.</summary>
        public void Apply()
        {
            if (_failed || !Interop.Alive(_model)) return;
            var now = Time.unscaledTime;
            try
            {
                if (now >= _nextRefreshAt) RefreshSize();
                if (now >= _nextSitesAt)
                {
                    _nextSitesAt = now + SiteSeconds;
                    _sites = _findSites?.Invoke() ?? new List<Site>();
                }
                if (now >= _nextScanAt)
                {
                    _nextScanAt = now + ScanSeconds;
                    Scan();
                }
                foreach (var ring in _rings.Values) Place(ring);
            }
            catch (Exception e)
            {
                _failed = true;
                Core.Log.Warning($"ring: follower for {_who} failed, rings left where the game put them: {e.GetType().Name}: {e.Message}");
                Release("follower failed");
            }
        }

        /// <summary>Give every ring back its own local pose.</summary>
        public void Release(string why)
        {
            var n = 0;
            foreach (var ring in _rings.Values)
            {
                if (!Interop.Alive(ring.T)) continue;
                try
                {
                    if (ring.LeftAlone) continue;
                    if (!SameObject(ring.T.parent, ring.Parent)) continue;   // the game has moved it on
                    ring.T.localPosition = ring.LocalPos;
                    ring.T.localRotation = ring.LocalRot;
                    ring.T.localScale = ring.LocalScale;
                    n++;
                }
                catch { }
            }
            if (n > 0) Core.Log.Msg($"ring: {n} ring(s) on {_who} handed back to the vanilla finger ({why}).");
            _rings.Clear();
            _pending.Clear();
        }

        // ---- finding rings ------------------------------------------------------------------

        private void Scan()
        {
            List<IntPtr> gone = null;
            foreach (var kv in _rings)
            {
                var ring = kv.Value;
                string reason = null;
                if (!Interop.Alive(ring.T)) reason = "taken off";
                else if (!SameObject(ring.T.parent, ring.Parent)) reason = "moved by the game";
                if (reason == null) continue;
                (gone ??= new List<IntPtr>()).Add(kv.Key);
                Core.Log.Msg($"ring: `{ring.Name}` on {_who}'s {Describe(ring.Site.Bone)} {reason}.");
            }
            if (gone != null) foreach (var id in gone) _rings.Remove(id);

            foreach (var site in _sites)
            {
                if (!Interop.Alive(site.Anchor)) continue;
                var count = site.Anchor.childCount;
                for (var i = 0; i < count; i++)
                {
                    var child = site.Anchor.GetChild(i);
                    if (!Interop.Alive(child) || _rings.ContainsKey(child.Pointer)) continue;
                    Equippable equippable = null;
                    try { equippable = child.GetComponent<Equippable>(); } catch { }
                    if (!Interop.Alive(equippable)) continue;

                    // The band's mesh is generated after the ring is spawned; measuring before
                    // it exists would put the joint, not the band, on the avatar's finger. Wait
                    // for it, but not forever.
                    var now = Time.unscaledTime;
                    if (!_pending.TryGetValue(child.Pointer, out var since)) _pending[child.Pointer] = since = now;
                    if (!HasBandMesh(child) && now - since < PendingSeconds) continue;
                    _pending.Remove(child.Pointer);

                    var ring = Capture(child, site);
                    _rings[child.Pointer] = ring;
                }
            }
        }

        private Ring Capture(Transform t, Site site)
        {
            var ring = new Ring
            {
                T = t, Parent = t.parent, Site = site, Name = Interop.Name(t),
                LocalPos = t.localPosition, LocalRot = t.localRotation, LocalScale = t.localScale,
            };

            var avatar = AvatarTube(site);
            if (avatar == null)
            {
                ring.Hidden = true;
                t.localScale = Vector3.zero;
                Core.Log.Msg($"ring: `{ring.Name}` on {_who}'s {Describe(site.Bone)} ({site.Where}) hidden — " +
                             $"the avatar has no {site.Bone} bone to put it on.");
                return ring;
            }

            var vanilla = MeasureTube(site.VanillaBone, site.VanillaNext, site.VanillaIndex, site.VanillaLittle,
                                      site.VanillaHand, site.VanillaSkins);
            if (vanilla == null)
            {
                ring.LeftAlone = true;
                Core.Log.Msg($"ring: `{ring.Name}` on {_who}'s {Describe(site.Bone)} ({site.Where}) left alone — " +
                             "no usable vanilla finger to measure it against.");
                return ring;
            }
            if (!vanilla.FromMesh && Interop.Alive(site.ShapeBone))
            {
                // The first-person arms' mesh may not be readable; the body is the same
                // character, so its finger's proportions stand in.
                var shape = MeasureTube(site.ShapeBone, site.ShapeNext, site.ShapeIndex, site.ShapeLittle, null, site.ShapeSkins);
                if (shape != null && shape.FromMesh)
                {
                    vanilla.FromMesh = true;
                    vanilla.RadiusPerLength = shape.RadiusPerLength;
                    vanilla.CentrePerLength = shape.CentrePerLength;
                    vanilla.Note = shape.Note + " (from the body)";
                }
            }

            ring.Avatar = avatar;
            ring.UseMesh = vanilla.FromMesh && avatar.FromMesh;

            // The band's centre, not the ring's origin: the origin is the joint, the band is
            // further along, and it is the band that has to land on the avatar's finger.
            var centre = BandCentre(t);
            var bone = vanilla.Bone;
            var rPer = ring.UseMesh ? vanilla.RadiusPerLength : FallbackRadiusPerLength;
            var cPer = ring.UseMesh ? vanilla.CentrePerLength : Vector2.zero;
            var radius = vanilla.Length * rPer;
            var d = Quaternion.Inverse(vanilla.Frame) * bone.InverseTransformPoint(centre);
            ring.Along = d.z / vanilla.Length;
            ring.Across = new Vector2(d.x / radius - cPer.x / rPer, d.y / radius - cPer.y / rPer);
            ring.Rot = Quaternion.Inverse(vanilla.Frame) * (Quaternion.Inverse(bone.rotation) * t.rotation);
            if (!(ring.Along > -0.5f && ring.Along < 1.5f) || !(ring.Across.magnitude < 4f))
            {
                // The ring is nowhere near the finger it was measured against: copying that
                // would put it metres away. The middle of the segment, centred, instead.
                Core.Log.Warning($"ring: `{ring.Name}` on {_who}'s {Describe(site.Bone)} ({site.Where}) measured " +
                                 $"{ring.Along * 100f:0}% along and {ring.Across.magnitude:0.0} radii off the vanilla finger — " +
                                 "not believed, placed mid-segment.");
                ring.Along = 0.5f;
                ring.Across = Vector2.zero;
            }
            var radiusWorld = WorldRadius(vanilla, rPer);
            ring.SizePerRadius = radiusWorld > 1e-6f ? Mathf.Abs(t.lossyScale.x) / radiusWorld : 1f;
            ring.CentreInRing = t.InverseTransformPoint(centre);

            // The fit. Copying the game's placement copies its slack too: its band sits loosely
            // and off-centre on a thin finger, which on a thicker one leaves the band sunk in.
            // Where the avatar's finger was measured from its mesh, and the band's hole can be
            // measured from the ring's, centre the band on the finger and size it so the hole
            // is just around it. Along the finger and the roll stay as the game had them.
            string fit = null;
            if (avatar.FromMesh)
            {
                var axis = Quaternion.Inverse(ring.Rot) * Vector3.forward;   // the finger, in the ring's space
                var hole = HoleRadius(t, ring.CentreInRing, axis, out var holeCentre, out var holeWhy);
                if (hole > 1e-6f)
                {
                    ring.UseMesh = true;
                    ring.CentreInRing = holeCentre;
                    ring.Across = Vector2.zero;
                    ring.SizePerRadius = FitSlack / hole;
                    fit = $"hole fitted around the finger ({FitSlack:0.00}x its radius" + (holeWhy != null ? $", {holeWhy}" : "") + ")";
                }
                else fit = $"ring hole not measurable ({holeWhy}), game's fit copied";
            }

            Place(ring);
            var avatarRadius = WorldRadius(avatar, ring.UseMesh ? avatar.RadiusPerLength : FallbackRadiusPerLength);
            Core.Log.Msg($"ring: `{ring.Name}` on {_who}'s {Describe(site.Bone)} ({site.Where}) re-homed from " +
                         $"`{Interop.Name(site.VanillaBone)}` to the avatar's `{Interop.Name(avatar.Bone)}` — " +
                         $"{ring.Along * 100f:0}% along the segment, finger radius " +
                         $"{radiusWorld * 100f:0.00} cm → {avatarRadius * 100f:0.00} cm " +
                         $"(x{(radiusWorld > 1e-6f ? avatarRadius / radiusWorld : 1f):0.00}, " +
                         (ring.UseMesh ? "measured from both meshes" : $"by segment length: vanilla {vanilla.Note}, avatar {avatar.Note}") +
                         (fit != null ? $"; {fit}" : "") + ").");
            return ring;
        }

        /// <summary>
        /// The radius of the band's hole, in the ring's own units: how close the band's vertices
        /// come to the hole's centre line, read from the generated mesh. A low percentile, not
        /// the minimum, so a stray vertex (a seam, the setting of a stone) can't decide it.
        /// -1 when there is no readable band or the answer is implausible.
        /// </summary>
        private static float HoleRadius(Transform ring, Vector3 centre, Vector3 axis, out Vector3 holeCentre, out string why)
        {
            holeCentre = centre; why = null;
            try
            {
                if (axis.sqrMagnitude < 1e-12f) { why = "no finger axis"; return -1f; }
                axis.Normalize();
                MeshFilter band = null;
                var best = -1f;
                foreach (var mf in ring.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (!Interop.Alive(mf) || !Interop.Alive(mf.sharedMesh)) continue;
                    var size = mf.sharedMesh.bounds.size.sqrMagnitude;
                    if (size > best) { best = size; band = mf; }
                }
                if (band == null) { why = "no band mesh"; return -1f; }
                var mesh = band.sharedMesh;
                if (!mesh.isReadable) return BoundsHole(ring, band, centre, axis, "band mesh not readable", out holeCentre, out why);
                var vertices = mesh.vertices;
                if (vertices == null || vertices.Length < 8) { why = $"{vertices?.Length ?? 0} band vertices"; return -1f; }

                var toRing = ring.worldToLocalMatrix * band.transform.localToWorldMatrix;
                var flat = new List<Vector3>(vertices.Length);   // across the hole, relative to centre
                foreach (var v in vertices)
                {
                    var p = toRing.MultiplyPoint3x4(v) - centre;
                    flat.Add(p - Vector3.Dot(p, axis) * axis);
                }

                // The bounds centre leans toward a stone or setting on top. The hole's own centre
                // is the middle of the band's inner surface: the nearer vertices, whose extent
                // is a full circle. Two passes settle it.
                var shift = Vector3.zero;
                var distances = new List<float>(flat.Count);
                for (var pass = 0; pass < 2; pass++)
                {
                    distances.Clear();
                    foreach (var p in flat) distances.Add((p - shift).magnitude);
                    var sorted = new List<float>(distances);
                    sorted.Sort();
                    var cut = sorted[sorted.Count * 2 / 5];
                    Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
                    var n = 0;
                    for (var i = 0; i < flat.Count; i++)
                    {
                        if (distances[i] > cut) continue;
                        min = Vector3.Min(min, flat[i]);
                        max = Vector3.Max(max, flat[i]);
                        n++;
                    }
                    if (n < 8) break;
                    var mid = (min + max) * 0.5f;
                    shift = mid - Vector3.Dot(mid, axis) * axis;
                }

                distances.Clear();
                foreach (var p in flat) distances.Add((p - shift).magnitude);
                distances.Sort();
                var inner = distances[distances.Count / 20];
                var outer = distances[distances.Count - 1];
                // A hole that is nearly nothing, or as wide as the whole ring, is not a band.
                if (!(inner > 1e-6f) || !(inner < outer * 0.98f) || inner < outer * 0.2f) return BoundsHole(ring, band, centre, axis, $"hole {inner:0.0000} against outside {outer:0.0000} from the vertices", out holeCentre, out why);
                holeCentre = centre + shift;
                return inner;
            }
            catch (Exception e) { why = e.GetType().Name; return -1f; }
        }

        // A band's hole against its outside, for when only the bounds can be read: a typical
        // band is about a quarter as thick as it is wide.
        private const float HoleOfOutside = 0.75f;

        /// <summary>
        /// The hole estimated from the band mesh's bounds, which are readable on any mesh. Across
        /// the finger the band is a circle, so its narrower extent there is the outside diameter
        /// (a stone only adds to the other one), and the middle of that extent is the hole's
        /// centre along it.
        /// </summary>
        private static float BoundsHole(Transform ring, MeshFilter band, Vector3 centre, Vector3 axis, string because,
                                        out Vector3 holeCentre, out string why)
        {
            holeCentre = centre;
            why = because;
            try
            {
                var u = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
                var v = Vector3.Cross(axis, u);
                var b = band.sharedMesh.bounds;
                var toRing = ring.worldToLocalMatrix * band.transform.localToWorldMatrix;
                float uMin = float.MaxValue, uMax = float.MinValue, vMin = float.MaxValue, vMax = float.MinValue;
                for (var i = 0; i < 8; i++)
                {
                    var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = toRing.MultiplyPoint3x4(corner) - centre;
                    var pu = Vector3.Dot(p, u);
                    var pv = Vector3.Dot(p, v);
                    uMin = Mathf.Min(uMin, pu); uMax = Mathf.Max(uMax, pu);
                    vMin = Mathf.Min(vMin, pv); vMax = Mathf.Max(vMax, pv);
                }
                var halfU = (uMax - uMin) * 0.5f;
                var halfV = (vMax - vMin) * 0.5f;
                var outer = Mathf.Min(halfU, halfV);
                if (!(outer > 1e-6f)) { why = because + ", bounds empty"; return -1f; }
                // Centre along the narrow extent; along the wide one, the hole is the narrow
                // extent's size measured from the end without the stone, which can't be told
                // apart, so the middle stands.
                var shift = halfU <= halfV ? u * ((uMin + uMax) * 0.5f) : v * ((vMin + vMax) * 0.5f);
                holeCentre = centre + shift;
                why = because + ", estimated from the band's bounds";
                return outer * HoleOfOutside;
            }
            catch (Exception e) { why = because + ", " + e.GetType().Name; return -1f; }
        }

        // ---- placing ------------------------------------------------------------------------

        private void Place(Ring ring)
        {
            if (!Interop.Alive(ring.T) || ring.LeftAlone) return;
            if (ring.Hidden)
            {
                if (ring.T.localScale != Vector3.zero) ring.T.localScale = Vector3.zero;
                return;
            }
            var tube = ring.Avatar;
            if (tube == null || !Interop.Alive(tube.Bone)) return;
            var isLeft = ring.Site.Bone <= HumanBodyBones.LeftLittleDistal;
            var look = !_self ? Others : isLeft ? Left : Right;
            if (!look.Visible)
            {
                if (ring.T.localScale != Vector3.zero) ring.T.localScale = Vector3.zero;
                return;
            }
            // Someone else's ring: the fit alone, none of your tuning.
            var size = _self ? look.Size : 1f;
            var up = _self ? look.Offset : 0f;
            var slide = _self ? look.Slide : 0f;
            var tiltDeg = _self ? look.Tilt : 0f;
            var turnDeg = _self ? look.Turn : 0f;
            var rollDeg = _self ? look.Roll : 0f;

            var rPer = ring.UseMesh ? tube.RadiusPerLength : FallbackRadiusPerLength;
            var cPer = ring.UseMesh ? tube.CentrePerLength : Vector2.zero;
            var radius = tube.Length * rPer;
            // The frame's Y is along (index - little) x (toward the tip), which mirrors with the
            // hand: the back of the hand on the left, the palm on the right. Up means the back of
            // the hand on both.
            var lift = isLeft ? up : -up;
            var local = new Vector3(cPer.x * tube.Length + ring.Across.x * radius,
                                    cPer.y * tube.Length + (ring.Across.y + lift) * radius,
                                    (ring.Along + slide) * tube.Length);
            var bone = tube.Bone;
            var target = bone.TransformPoint(tube.Frame * local);
            var rotation = bone.rotation * tube.Frame * ring.Rot;
            if (tiltDeg != 0f || turnDeg != 0f || rollDeg != 0f)
            {
                // In the hand's frame (Y out of the back of the hand on both hands: the finger
                // frame turned half round the finger on the right), then back into the finger
                // frame, before the ring's own rotation. The pivot is the band's centre, because
                // the position below is solved after the rotation.
                var hand = isLeft ? Quaternion.identity : Quaternion.AngleAxis(180f, Vector3.forward);
                var mirror = isLeft ? 1f : -1f;
                var tilt = Quaternion.AngleAxis(turnDeg * mirror, Vector3.up) *
                           Quaternion.AngleAxis(tiltDeg, Vector3.right) *
                           Quaternion.AngleAxis(rollDeg * mirror, Vector3.forward);
                rotation = bone.rotation * tube.Frame * hand * tilt * Quaternion.Inverse(hand) * ring.Rot;
            }

            var wantLossy = ring.SizePerRadius * WorldRadius(tube, rPer) * size;
            var parentLossy = 1f;
            var parent = ring.T.parent;
            if (Interop.Alive(parent)) parentLossy = Mathf.Abs(parent.lossyScale.x);
            var baseX = Mathf.Abs(ring.LocalScale.x);
            if (parentLossy > 1e-6f && baseX > 1e-6f && IsFinite(wantLossy))
                ring.T.localScale = ring.LocalScale * (wantLossy / (parentLossy * baseX));

            ring.T.rotation = rotation;
            var offset = ring.T.TransformPoint(ring.CentreInRing) - ring.T.position;
            var position = target - offset;
            if (IsFinite(position)) ring.T.position = position;
        }

        private static float WorldRadius(Tube tube, float radiusPerLength)
        {
            try { return tube.Bone.TransformVector(tube.Frame * (Vector3.right * tube.Length * radiusPerLength)).magnitude; }
            catch { return 0f; }
        }

        private static bool HasBandMesh(Transform ring)
        {
            try
            {
                foreach (var mf in ring.GetComponentsInChildren<MeshFilter>(true))
                    if (Interop.Alive(mf) && Interop.Alive(mf.sharedMesh) && mf.sharedMesh.vertexCount > 0) return true;
            }
            catch { }
            return false;
        }

        private static Vector3 BandCentre(Transform ring)
        {
            // The generated band is the biggest mesh under the ring; its bounds centre is the
            // middle of the hole. Mesh.bounds is readable on any mesh.
            try
            {
                var best = -1f;
                var centre = ring.position;
                foreach (var mf in ring.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (!Interop.Alive(mf)) continue;
                    var mesh = mf.sharedMesh;
                    if (!Interop.Alive(mesh)) continue;
                    var b = mesh.bounds;
                    var size = b.size.sqrMagnitude;
                    if (size <= best) continue;
                    best = size;
                    centre = mf.transform.TransformPoint(b.center);
                }
                if (best < 0f)
                {
                    foreach (var r in ring.GetComponentsInChildren<Renderer>(true))
                        if (Interop.Alive(r)) { centre = r.bounds.center; break; }
                }
                return centre;
            }
            catch { return ring.position; }
        }

        // ---- measuring fingers --------------------------------------------------------------

        private Tube AvatarTube(Site site)
        {
            var key = (int)site.Bone;
            if (_avatarTubes.TryGetValue(key, out var cached)) return cached;
            if (_avatarMissing.Contains(key)) return null;

            var bone = AvatarBone(site.Bone);
            Tube tube = null;
            if (Interop.Alive(bone))
            {
                var isLeft = key <= (int)HumanBodyBones.LeftLittleDistal;
                var next = NextJoint(site.Bone);
                var index = AvatarBone(isLeft ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal);
                var little = AvatarBone(isLeft ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal);
                var hand = AvatarBone(isLeft ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                var skins = new List<SkinnedMeshRenderer>();
                try { foreach (var s in _model.GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (Interop.Alive(s)) skins.Add(s); }
                catch { }
                tube = MeasureTube(bone, next, index, little, hand, skins);
            }

            if (tube == null) _avatarMissing.Add(key);
            else _avatarTubes[key] = tube;
            return tube;
        }

        private Transform NextJoint(HumanBodyBones bone)
        {
            // Proximal → intermediate → distal. A rig that skips the middle one still has a
            // direction for the finger in the one after it.
            var b = (int)bone;
            var inFinger = (b - (int)HumanBodyBones.LeftThumbProximal) % 3;
            for (var step = 1; inFinger + step <= 2; step++)
            {
                var t = AvatarBone((HumanBodyBones)(b + step));
                if (Interop.Alive(t)) return t;
            }
            return null;
        }

        private Transform AvatarBone(HumanBodyBones bone)
        {
            var map = _manifest?.rig?.humanoidBones;
            if (map == null || !map.TryGetValue(bone.ToString(), out var path) || string.IsNullOrEmpty(path)) return null;
            try
            {
                var t = _model.transform.Find(path);
                return Interop.Alive(t) ? t : null;
            }
            catch { return null; }
        }

        /// <summary>
        /// A finger segment's frame, length and, when a skinned mesh can be read, cross-section.
        /// Null only when there is no bone. The frame is built from geometry, never from the
        /// bone's own axes: Z toward the next joint, Y perpendicular to it and to the knuckle
        /// line (index minus little), the same construction on every rig, so the same answer
        /// means the same side of the finger.
        /// </summary>
        private static Tube MeasureTube(Transform bone, Transform next, Transform index, Transform little,
                                        Transform hand, List<SkinnedMeshRenderer> skins)
        {
            if (!Interop.Alive(bone)) return null;
            var tube = new Tube { Bone = bone };
            var notes = new List<string>();

            // Along the finger.
            Vector3 along;
            if (Interop.Alive(next)) along = next.position - bone.position;
            else
            {
                Transform child = null;
                for (var i = 0; i < bone.childCount; i++)
                {
                    var c = bone.GetChild(i);
                    if (!Interop.Alive(c) || Interop.Alive(c.GetComponent<Equippable>())) continue;
                    if ((c.position - bone.position).sqrMagnitude > 1e-10f) { child = c; break; }
                }
                if (child != null) { along = child.position - bone.position; notes.Add("no next joint, used its child"); }
                else if (Interop.Alive(hand))
                {
                    // Nothing beyond it: carry on the line from the wrist, half as long again.
                    along = (bone.position - hand.position) * 0.5f;
                    notes.Add("no next joint, extended from the wrist");
                }
                else return null;
            }
            if (along.sqrMagnitude < 1e-12f) return null;

            // Across the hand.
            Vector3 lateral;
            if (Interop.Alive(index) && Interop.Alive(little) && (index.position - little.position).sqrMagnitude > 1e-12f)
                lateral = index.position - little.position;
            else if (Interop.Alive(index) && !SameObject(index, bone)) lateral = index.position - bone.position;
            else if (Interop.Alive(little) && !SameObject(little, bone)) lateral = bone.position - little.position;
            else { lateral = bone.right; notes.Add("no knuckle line, roll from the bone's axes"); }

            var up = Vector3.Cross(along, lateral);
            if (up.sqrMagnitude < 1e-12f) up = Vector3.Cross(along, bone.up);
            var frameWorld = Quaternion.LookRotation(along, up);
            tube.Frame = Quaternion.Inverse(bone.rotation) * frameWorld;
            tube.Length = bone.InverseTransformVector(along).magnitude;
            if (tube.Length < 1e-6f) return null;

            if (MeasureCrossSection(bone, tube, skins, out var r, out var centreOff, out var meshNote))
            {
                tube.FromMesh = true;
                tube.RadiusPerLength = r;
                tube.CentrePerLength = centreOff;
            }
            notes.Add(meshNote);
            tube.Note = string.Join(", ", notes);
            return tube;
        }

        /// <summary>
        /// The finger's thickness from the vertices skinned to its bone. In bind space each
        /// vertex's bone-local position is `bindposes[b] * vertex`, which is exactly the bone's
        /// current local space — skinning is rigid per bone — so this needs no pose at all.
        /// Samples from the middle of the segment only, away from both knuckles' creases.
        /// </summary>
        private static bool MeasureCrossSection(Transform bone, Tube tube, List<SkinnedMeshRenderer> skins,
                                                out float radiusPerLength, out Vector2 centrePerLength, out string note)
        {
            radiusPerLength = 0f;
            centrePerLength = Vector2.zero;
            note = "no skinned mesh";
            if (skins == null || skins.Count == 0) return false;

            var inverse = Quaternion.Inverse(tube.Frame);
            List<Vector2> best = null;
            string bestName = null;
            var unreadable = 0;

            foreach (var skin in skins)
            {
                try
                {
                    if (!Interop.Alive(skin)) continue;
                    var bones = skin.bones;
                    if (bones == null) continue;
                    var b = -1;
                    for (var i = 0; i < bones.Length; i++)
                        if (SameObject(bones[i], bone)) { b = i; break; }
                    if (b < 0) continue;

                    var mesh = skin.sharedMesh;
                    if (!Interop.Alive(mesh)) continue;
                    if (!mesh.isReadable) { unreadable++; continue; }

                    var bindposes = mesh.bindposes;
                    if (bindposes == null || b >= bindposes.Length) continue;
                    var vertices = mesh.vertices;
                    var weights = mesh.boneWeights;
                    if (vertices == null || weights == null || weights.Length != vertices.Length) continue;

                    var bind = bindposes[b];
                    var samples = new List<Vector2>();
                    var insane = 0;
                    for (var v = 0; v < vertices.Length; v++)
                    {
                        var w = weights[v];
                        var weight = 0f;
                        if (w.boneIndex0 == b) weight += w.weight0;
                        if (w.boneIndex1 == b) weight += w.weight1;
                        if (w.boneIndex2 == b) weight += w.weight2;
                        if (w.boneIndex3 == b) weight += w.weight3;
                        if (weight > 1.01f || weight < 0f) { insane++; continue; }
                        if (weight < 0.5f) continue;

                        var p = inverse * bind.MultiplyPoint3x4(vertices[v]);
                        var t = p.z / tube.Length;
                        if (t < 0.15f || t > 0.85f) continue;
                        samples.Add(new Vector2(p.x, p.y));
                    }
                    // Implausible weights mean the struct array is being read wrong, which
                    // has happened in this game before: trust nothing from that mesh.
                    if (insane > vertices.Length / 10) continue;
                    if (best == null || samples.Count > best.Count) { best = samples; bestName = Interop.Name(skin); }
                }
                catch { }
            }

            if (best == null || best.Count < 8)
            {
                note = unreadable > 0 ? "mesh not readable" : $"{best?.Count ?? 0} vertices on the finger";
                return false;
            }

            var centre = Vector2.zero;
            foreach (var s in best) centre += s;
            centre /= best.Count;
            var distances = new List<float>(best.Count);
            foreach (var s in best) distances.Add((s - centre).magnitude);
            distances.Sort();
            var radius = distances[distances.Count / 2];

            radiusPerLength = radius / tube.Length;
            centrePerLength = centre / tube.Length;
            if (!(radiusPerLength > 0.03f && radiusPerLength < 1.5f))
            {
                note = $"measured radius {radiusPerLength:0.00} of the segment, not believed";
                return false;
            }
            note = $"{best.Count} vertices of `{bestName}`";
            return true;
        }

        // ---- the sites on a body ------------------------------------------------------------

        /// <summary>
        /// The ring holsters on a rig driven by <paramref name="animator"/>: every `AvatarHolster`
        /// under <paramref name="root"/> whose bone is a finger. That is the two ring holsters
        /// today, and whatever else is ever worn on a finger tomorrow.
        /// </summary>
        public static void AddHolsterSites(List<Site> into, Transform root, Animator animator,
                                           SkinnedMeshRenderer skin, string where)
        {
            if (!Interop.Alive(root) || !Interop.Alive(animator)) return;
            AvatarHolster[] holsters;
            try { holsters = root.GetComponentsInChildren<AvatarHolster>(true); }
            catch { return; }
            if (holsters == null) return;
            foreach (var holster in holsters)
            {
                if (!Interop.Alive(holster)) continue;
                HumanBodyBones bone;
                try { bone = holster.bone; } catch { continue; }
                if (!IsFinger(bone)) continue;

                var site = new Site { Anchor = holster.transform, Bone = bone, Where = where };
                FillFromAnimator(animator, bone, out site.VanillaBone, out site.VanillaNext,
                                 out site.VanillaIndex, out site.VanillaLittle, out site.VanillaHand);
                // The holster is parented to the bone by Initiate; before that it is elsewhere
                // and the ring would be too.
                if (!Interop.Alive(site.VanillaBone)) site.VanillaBone = holster.transform.parent;
                // The animator may drive a different copy of the skeleton than the one the
                // holster hangs on (the mannequin: its rings were measured against a finger
                // hundreds of segment lengths away). The finger the ring is really on is the
                // holster's parent; take its neighbours from the same rig, by name.
                var parent = holster.transform.parent;
                if (Interop.Alive(parent) && !SameObject(parent, site.VanillaBone) && parent.name == Interop.Name(site.VanillaBone))
                {
                    var hand = parent.parent;
                    var next = FindChildNamed(parent, Interop.Name(site.VanillaNext));
                    var index = FindChildNamed(hand, Interop.Name(site.VanillaIndex));
                    var little = FindChildNamed(hand, Interop.Name(site.VanillaLittle));
                    site.VanillaBone = parent;
                    site.VanillaHand = hand;
                    site.VanillaNext = next;
                    site.VanillaIndex = index;
                    site.VanillaLittle = little;
                }
                if (Interop.Alive(skin)) site.VanillaSkins.Add(skin);
                into.Add(site);
            }
        }

        /// <summary>
        /// Your own rings, on the first-person arms. `EquippableHolster.attachPoint` is a bone
        /// of the FPS arm rig, which has no humanoid Animator of its own; its finger is named
        /// after the body's (the two are the same skeleton), so the humanoid bone comes from the
        /// body holster of the same `holsterType`, and the neighbouring joints by name.
        /// </summary>
        public static void AddFirstPersonSites(List<Site> into, Transform vrRoot, Transform bodyRoot, Animator body,
                                               SkinnedMeshRenderer bodySkin)
        {
            if (!Interop.Alive(vrRoot) || !Interop.Alive(body)) return;
            EquippableHolster[] holsters;
            try { holsters = vrRoot.GetComponentsInChildren<EquippableHolster>(true); }
            catch { return; }
            if (holsters == null || holsters.Length == 0) return;

            var bones = new Dictionary<int, HumanBodyBones>();
            try
            {
                foreach (var h in bodyRoot.GetComponentsInChildren<AvatarHolster>(true))
                    if (Interop.Alive(h) && IsFinger(h.bone)) bones[(int)h.holsterType] = h.bone;
            }
            catch { }

            foreach (var holster in holsters)
            {
                if (!Interop.Alive(holster)) continue;
                Transform attach;
                int type;
                try { attach = holster.attachPoint; type = (int)holster.holsterType; } catch { continue; }
                if (!Interop.Alive(attach)) continue;

                if (!bones.TryGetValue(type, out var bone) && !BoneByName(body, attach, out bone)) continue;

                FillFromAnimator(body, bone, out var bBone, out var bNext, out var bIndex, out var bLittle, out var bHand);
                var site = new Site { Anchor = attach, Bone = bone, Where = "first-person arms" };
                // The attach point is the finger bone in today's data; if it is ever a child
                // of it instead, walk up to the one that carries the body's name.
                site.VanillaBone = attach;
                var name = Interop.Name(bBone);
                for (var t = attach; Interop.Alive(t); t = t.parent)
                    if (t.name == name) { site.VanillaBone = t; break; }
                var hand = site.VanillaBone.parent;
                site.VanillaHand = hand;
                site.VanillaNext = FindChildNamed(site.VanillaBone, Interop.Name(bNext));
                site.VanillaIndex = FindChildNamed(hand, Interop.Name(bIndex));
                site.VanillaLittle = FindChildNamed(hand, Interop.Name(bLittle));
                try
                {
                    foreach (var s in AvatarSwapper.RootOf(attach).GetComponentsInChildren<SkinnedMeshRenderer>(true))
                        if (Interop.Alive(s)) site.VanillaSkins.Add(s);
                }
                catch { }
                site.ShapeBone = bBone; site.ShapeNext = bNext; site.ShapeIndex = bIndex; site.ShapeLittle = bLittle;
                if (Interop.Alive(bodySkin)) site.ShapeSkins.Add(bodySkin);
                into.Add(site);
            }
        }

        private static void FillFromAnimator(Animator animator, HumanBodyBones bone, out Transform b,
                                             out Transform next, out Transform index, out Transform little, out Transform hand)
        {
            b = next = index = little = hand = null;
            try
            {
                var isLeft = (int)bone <= (int)HumanBodyBones.LeftLittleDistal;
                b = animator.GetBoneTransform(bone);
                var inFinger = ((int)bone - (int)HumanBodyBones.LeftThumbProximal) % 3;
                for (var step = 1; inFinger + step <= 2 && !Interop.Alive(next); step++)
                    next = animator.GetBoneTransform((HumanBodyBones)((int)bone + step));
                index = animator.GetBoneTransform(isLeft ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal);
                little = animator.GetBoneTransform(isLeft ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal);
                hand = animator.GetBoneTransform(isLeft ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            }
            catch { }
        }

        private static bool BoneByName(Animator body, Transform attach, out HumanBodyBones bone)
        {
            bone = HumanBodyBones.LastBone;
            for (var b = (int)HumanBodyBones.LeftThumbProximal; b <= (int)HumanBodyBones.RightLittleDistal; b++)
            {
                try
                {
                    var t = body.GetBoneTransform((HumanBodyBones)b);
                    if (Interop.Alive(t) && t.name == attach.name) { bone = (HumanBodyBones)b; return true; }
                }
                catch { }
            }
            return false;
        }

        private static Transform FindChildNamed(Transform root, string name)
        {
            if (!Interop.Alive(root) || string.IsNullOrEmpty(name) || name == "<null>") return null;
            try
            {
                var direct = root.Find(name);
                if (Interop.Alive(direct)) return direct;
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (Interop.Alive(t) && t.name == name) return t;
            }
            catch { }
            return null;
        }

        private static bool IsFinger(HumanBodyBones bone) =>
            (int)bone >= (int)HumanBodyBones.LeftThumbProximal && (int)bone <= (int)HumanBodyBones.RightLittleDistal;

        private static string Describe(HumanBodyBones bone)
        {
            var b = (int)bone;
            if (!IsFinger(bone)) return bone.ToString();
            var side = b <= (int)HumanBodyBones.LeftLittleDistal ? "left" : "right";
            var rel = (b - (int)HumanBodyBones.LeftThumbProximal) % 15;
            var finger = new[] { "thumb", "index finger", "middle finger", "ring finger", "little finger" }[rel / 3];
            return $"{side} {finger}";
        }

        private static bool SameObject(Transform a, Transform b)
        {
            var aa = Interop.Alive(a);
            var bb = Interop.Alive(b);
            if (!aa || !bb) return aa == bb;
            return a.Pointer == b.Pointer;
        }

        private static bool IsFinite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
        private static bool IsFinite(Vector3 v) => IsFinite(v.x) && IsFinite(v.y) && IsFinite(v.z);
    }
}
