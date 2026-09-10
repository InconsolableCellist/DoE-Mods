using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppPhoton.Pun;
using Il2CppOthergate.Audio;
using LootOverhaul.Recon;
using Interop = LootOverhaul.Recon.Interop;
using UnityEngine;

namespace LootOverhaul.Loot
{
    /// <summary>One tagged loot object on the floor, as this client knows it.</summary>
    public class LootTag
    {
        public int ViewId;
        public LootItem Item;
        public GameObject Object;   // may be null on a client that only heard the broadcast
        public GameObject Beam;
        public GameObject Label;
        public GameObject Sparkle;
        public bool ClaimPending;
        public bool Claimed;
        /// <summary>Actor the master granted the item to, or -1.</summary>
        public int ClaimedBy = -1;
        /// <summary>A resting pose sent by the master to a late joiner, applied once the object is here.</summary>
        public Vector3? PosePosition;
        public Quaternion PoseRotation;
        public bool Outlined;
        public float NextOutlineAt;
        public bool SoundPlayed;
        /// <summary>The object has been alive on this client at least once; if it is gone now, the game destroyed it.</summary>
        public bool Seen;
    }

    /// <summary>
    /// Which networked objects in the room are loot. Keyed by PhotonView ID, which is the
    /// same number on every client. The master spawns and broadcasts; everyone tags; the
    /// pickup prefix consults this to decide "bag it" versus "let the game wield it".
    /// Cleared on every scene change; the master destroys what it still owns on the way
    /// out, because the pooled objects would otherwise outlive the dungeon (0.9.10).
    /// </summary>
    public static class LootRegistry
    {
        private static readonly Dictionary<int, LootTag> Tags = new Dictionary<int, LootTag>();

        public static int Count => Tags.Count;
        public static IEnumerable<LootTag> All => Tags.Values;

        public static bool TryGet(int viewId, out LootTag tag) => Tags.TryGetValue(viewId, out tag);

        public static LootTag Add(int viewId, LootItem item, GameObject obj)
        {
            if (Tags.TryGetValue(viewId, out var existing)) { existing.Item = item; if (obj != null) existing.Object = obj; return existing; }
            var tag = new LootTag { ViewId = viewId, Item = item, Object = obj };
            Tags[viewId] = tag;
            if (obj == null) tag.Object = FindObject(viewId);
            Decorate(tag);
            return tag;
        }

        private static void Decorate(LootTag tag)
        {
            if (!Interop.Alive(tag.Object)) return;
            tag.Seen = true;
            ApplyPose(tag);
            if (ModConfig.DropBeams.Value && tag.Beam == null && tag.Item.WeaponClass >= ModConfig.BeamMinClass.Value) tag.Beam = DropBeam.Attach(tag);
            JunkTint.Apply(tag);
            if (ModConfig.DropLabels.Value && tag.Label == null) tag.Label = DropLabel.Attach(tag);
            if (ModConfig.DropSparkles.Value && tag.Sparkle == null) tag.Sparkle = DropSparkle.Attach(tag);
            DropOutline.Refresh(tag);
            DropSound.OnFound(tag);
        }

        /// <summary>Move this client's copy to the pose the master sent (late join: the cached spawn puts it in the air, upright).</summary>
        public static void ApplyPose(LootTag tag)
        {
            if (!tag.PosePosition.HasValue || tag.Claimed || !Interop.Alive(tag.Object)) return;
            try
            {
                var t = tag.Object.transform;
                t.position = tag.PosePosition.Value;
                t.rotation = tag.PoseRotation;
                var rb = tag.Object.GetComponent<Rigidbody>();
                if (Interop.Alive(rb))
                {
                    if (!rb.isKinematic) { rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
                    rb.position = tag.PosePosition.Value;
                    rb.rotation = tag.PoseRotation;
                    try { rb.Sleep(); } catch { }
                }
                ReconLog.Line($"loot view {tag.ViewId} moved to the master's resting pose {Interop.Vec(tag.PosePosition.Value)}");
            }
            catch (Exception e) { Core.Log.Warning($"Apply pose failed for view {tag.ViewId}: {e.GetType().Name}"); }
            tag.PosePosition = null;
        }

        public static void Remove(int viewId)
        {
            if (!Tags.TryGetValue(viewId, out var tag)) return;
            Tags.Remove(viewId);
            Undecorate(tag);
        }

        private static void Undecorate(LootTag tag)
        {
            DropBeam.Detach(tag);
            DropLabel.Detach(tag);
            DropSparkle.Detach(tag);
            DropOutline.Stop(tag);
        }

        /// <summary>
        /// Forget every tag. With <paramref name="destroyOwned"/> the objects this client
        /// controls (the master's room objects) are network-destroyed first, so a dungeon's
        /// floor loot does not turn up in the next one or in a late joiner's room.
        /// </summary>
        public static void Clear(string why, bool destroyOwned = false)
        {
            var destroyed = 0;
            foreach (var tag in Tags.Values)
            {
                Undecorate(tag);
                if (!destroyOwned || tag.Claimed) continue;
                try
                {
                    var obj = Interop.Alive(tag.Object) ? tag.Object : FindObject(tag.ViewId);
                    if (obj == null) continue;
                    var pv = obj.GetComponent<PhotonView>();
                    if (!Interop.Alive(pv) || !pv.IsMine) continue;
                    PhotonNetwork.Destroy(obj);
                    destroyed++;
                }
                catch (Exception e) { Core.Log.Warning($"Destroying floor loot view {tag.ViewId} failed: {e.GetType().Name}: {e.Message}"); }
            }
            if (Tags.Count > 0) Core.Log.Msg($"Loot registry cleared ({Tags.Count} tag(s), {destroyed} destroyed): {why}");
            Tags.Clear();
        }

        /// <summary>Resolve a view id to its object on this client, or null if not (yet) here.</summary>
        public static GameObject FindObject(int viewId)
        {
            try
            {
                var pv = PhotonView.Find(viewId);
                return Interop.Alive(pv) ? pv.gameObject : null;
            }
            catch { return null; }
        }

        /// <summary>
        /// Retry object lookup for tags whose object had not replicated when the broadcast
        /// arrived; keep the decorations on the item. An object that was here and is gone
        /// without a claim was destroyed by the game (the sandbox despawns its enemies' bodies,
        /// 2026-09-08): its tag goes too, so the sparkle and label do not hang in the air.
        /// </summary>
        public static void Tick()
        {
            List<int> gone = null;
            foreach (var tag in Tags.Values)
            {
                if (tag.Claimed) continue;
                if (Interop.Alive(tag.Object))
                {
                    DropBeam.Follow(tag); DropLabel.Follow(tag); DropSparkle.Follow(tag); DropOutline.Refresh(tag);
                    continue;
                }
                tag.Object = FindObject(tag.ViewId);
                if (tag.Object != null) { Decorate(tag); continue; }
                if (tag.Seen) (gone ??= new List<int>()).Add(tag.ViewId);
            }
            if (gone == null) return;
            foreach (var id in gone)
            {
                if (Tags.TryGetValue(id, out var t)) ReconLog.Line($"loot view {id} ({t.Item.Name}) vanished without a claim; forgetting it");
                Remove(id);
            }
        }
    }

    /// <summary>
    /// The game's own pickup glow (<c>Prop.Outline</c>, the FXOutline the coins and the
    /// vanilla drops carry), kept lit on every tagged floor item until it is taken, in the
    /// item's rarity colour. The game's own colour is not rarity at all: <c>Weapon.outlineColor</c>
    /// is a constant cyan for every weapon (read from the assembly 2026-09-08, the "all blue"
    /// report), and the FXOutline glow colour is whatever the prefab was saved with. Both are
    /// set here: the glow via <c>FXOutline.SetGlowColor</c>, and the shader parameter the prop
    /// writes into its renderers' property blocks (<c>Prop.outlineParameter</c>).
    /// </summary>
    public static class DropOutline
    {
        private const int Frames = 120;
        private const float Every = 0.5f;
        private static bool _colorLogged;

        public static void Refresh(LootTag tag)
        {
            if (!ModConfig.DropOutline.Value || !Interop.Alive(tag.Object)) return;
            var now = Time.unscaledTime;
            if (tag.Outlined && now < tag.NextOutlineAt) return;
            try
            {
                var prop = tag.Object.GetComponent<Prop>();
                if (!Interop.Alive(prop)) return;
                if (!tag.Outlined)
                {
                    // Some bodies (mugs, dice) ship with an always-on-top glow that shows through
                    // walls; loot should glow like the spoon does, only in sight.
                    try { var fx = prop.fxOutline; if (Interop.Alive(fx)) fx.glowVisibility = FXOutline.Visibility.Normal; } catch { }
                    Colorize(tag, prop);
                }
                prop.Outline(Frames);
                tag.Outlined = true;
                tag.NextOutlineAt = now + Every;
            }
            catch (Exception e) { if (!tag.Outlined) Core.Log.Warning($"Outline failed for view {tag.ViewId}: {e.GetType().Name}"); tag.Outlined = true; tag.NextOutlineAt = now + 5f; }
        }

        /// <summary>The rarity colour onto the glow and the outline shader parameter. Junk keeps its tier colour.</summary>
        private static void Colorize(LootTag tag, Prop prop)
        {
            var color = ColorFor(tag.Item);
            var glow = false; var param = 0; string paramName = null;
            try
            {
                var fx = prop.fxOutline;
                if (Interop.Alive(fx)) { fx.SetGlowColor(color); glow = true; }
            }
            catch (Exception e) { if (!_colorLogged) Core.Log.Warning($"Outline glow colour failed: {e.GetType().Name}: {e.Message}"); }
            try
            {
                paramName = prop.outlineParameter;
                if (!string.IsNullOrEmpty(paramName))
                {
                    var block = new MaterialPropertyBlock();
                    foreach (var r in prop.GetComponentsInChildren<Renderer>(true))
                    {
                        if (!Interop.Alive(r)) continue;
                        try
                        {
                            r.GetPropertyBlock(block);
                            block.SetVector(paramName, new Vector4(color.r, color.g, color.b, color.a));
                            r.SetPropertyBlock(block);
                            param++;
                        }
                        catch { }
                    }
                }
            }
            catch (Exception e) { if (!_colorLogged) Core.Log.Warning($"Outline parameter colour failed: {e.GetType().Name}: {e.Message}"); }
            if (!_colorLogged)
            {
                _colorLogged = true;
                ReconLog.Line($"outline colour for {tag.Item.Name}: {color} glow={glow} param=`{paramName}` on {param} renderer(s)");
            }
        }

        /// <summary>
        /// The game's own rarity colour, read from the colour tag the game put on the display
        /// name; the mod's beam palette when there is none (junk carries its tier colour).
        /// </summary>
        public static Color ColorFor(LootItem item)
        {
            if (!item.IsWeapon && !item.IsArmor)
            {
                if (ColorUtility.TryParseHtmlString(LootTables.JunkColor(item.WeaponClass), out var jc)) return jc;
            }
            var tagged = TagColor(item.ColoredName);
            if (tagged.HasValue) return tagged.Value;
            if (item.IsArmor && ColorUtility.TryParseHtmlString(Armor.ClassColor(item.WeaponClass), out var ac)) return ac;
            return DropBeam.RarityColor(item.WeaponClass);
        }

        /// <summary>The first <c>&lt;color=…&gt;</c> in a rich-text string, as a colour, or null.</summary>
        public static Color? TagColor(string rich)
        {
            if (string.IsNullOrEmpty(rich)) return null;
            var i = rich.IndexOf("<color=", StringComparison.OrdinalIgnoreCase);
            if (i < 0) return null;
            var j = rich.IndexOf('>', i);
            if (j < 0) return null;
            var spec = rich.Substring(i + 7, j - i - 7).Trim().Trim('"');
            return ColorUtility.TryParseHtmlString(spec, out var c) ? c : (Color?)null;
        }

        public static void Stop(LootTag tag)
        {
            if (!tag.Outlined) return;
            tag.Outlined = false;
            try
            {
                if (!Interop.Alive(tag.Object)) return;
                var prop = tag.Object.GetComponent<Prop>();
                if (Interop.Alive(prop)) prop.StopOutline();
            }
            catch { }
        }
    }

    /// <summary>
    /// Sounds for a drop. The spawner plays the prop's own throw/spin audio, which the game
    /// networks itself; every client adds the coin pile's chime for weapons and armor when
    /// the object turns up. The chime is one of the coin pile's three sound refs, chosen by
    /// <c>DropChime</c> (start / finish / collected / off).
    /// </summary>
    public static class DropSound
    {
        private static SoundFXRef _chime;
        private static bool _searched;

        public static void OnFound(LootTag tag)
        {
            if (tag.SoundPlayed || !ModConfig.DropSounds.Value) return;
            tag.SoundPlayed = true;
            if (!tag.Item.IsWeapon && !tag.Item.IsArmor) return;
            var chime = Chime();
            if (chime == null) return;
            try
            {
                var pitch = tag.Item.WeaponClass >= 3 ? 0.85f : tag.Item.WeaponClass == 2 ? 0.95f : 1.05f;
                chime.PlaySoundAt(tag.Object.transform.position, 0f, 0.8f, pitch, false);
            }
            catch (Exception e) { Core.Log.Warning($"Drop chime failed: {e.GetType().Name}: {e.Message}"); }
        }

        /// <summary>Called by the spawner right after the kick: the game's own throw whoosh and spin loop, sent to everyone by the game.</summary>
        public static void OnSpawned(GameObject go, Vector3 velocity, Vector3 angular)
        {
            if (!ModConfig.DropSounds.Value) return;
            try
            {
                var prop = go.GetComponent<Prop>();
                if (Interop.Alive(prop)) prop.PlayThrowAudio(velocity, angular);
            }
            catch (Exception e) { Core.Log.Warning($"Throw audio failed: {e.GetType().Name}: {e.Message}"); }
        }

        private static SoundFXRef Chime()
        {
            var which = (ModConfig.DropChime.Value ?? "").Trim().ToLowerInvariant();
            if (which == "off" || which == "") return null;
            if (_chime != null) return _chime;
            if (_searched) return null;
            try
            {
                Coins coins = null; string from = null;
                var pool = NetworkObjectPool.UnpooledPrefabs;
                if (pool != null)
                    foreach (var kv in pool)
                    {
                        if (kv.Key == null || !kv.Key.StartsWith("Coin_Pile") || !Interop.Alive(kv.Value)) continue;
                        var c = kv.Value.GetComponent<Coins>();
                        if (Interop.Alive(c)) { coins = c; from = kv.Key; break; }
                    }
                if (coins == null)
                    foreach (var c in UnityEngine.Object.FindObjectsOfType<Coins>())
                        if (Interop.Alive(c)) { coins = c; from = c.name; break; }
                if (coins == null) return null;   // look again on a later drop
                _searched = true;
                var start = coins.startFlightSoundFX; var finish = coins.finishFlightSoundFX; var collected = coins.collectedSoundFX;
                _chime = which == "finish" ? finish : which == "collected" ? collected : start;
                Core.Log.Msg($"Drop chime: `{Name(_chime)}` ({which}) from `{from}`; the coin pile's refs are start `{Name(start)}`, finish `{Name(finish)}`, collected `{Name(collected)}`.");
                if (_chime == null || !_chime.IsValid) { Core.Log.Msg("Drop chime: that sound ref is not valid; chime off."); _chime = null; }
                return _chime;
            }
            catch (Exception e) { Core.Log.Warning($"Drop chime lookup failed: {e.GetType().Name}: {e.Message}"); _searched = true; return null; }
        }

        private static string Name(SoundFXRef r) { try { return r == null ? "null" : r.name; } catch { return "?"; } }
    }

    /// <summary>
    /// The bone body (Wolf_Treat) is the game's own dog treat and other drops use it too;
    /// junk on it is tinted by tier so a marrow bone reads as loot: brown, ivory, gold.
    /// </summary>
    public static class JunkTint
    {
        public static void Apply(LootTag tag)
        {
            if (tag.Item.IsWeapon || tag.Item.IsArmor || tag.Item.IsBuff || tag.Item.PrefabName != "Wolf_Treat") return;
            if (!Interop.Alive(tag.Object)) return;
            var color = tag.Item.WeaponClass switch { 0 => new Color(0.55f, 0.42f, 0.30f), 1 => new Color(0.97f, 0.94f, 0.82f), _ => new Color(1.0f, 0.78f, 0.25f) };
            try
            {
                foreach (var r in tag.Object.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (!Interop.Alive(r)) continue;
                    try { r.material.color = color; } catch { }
                    try { r.material.SetColor("_EmissionColor", color * (tag.Item.WeaponClass >= 2 ? 0.35f : 0.05f)); } catch { }
                }
            }
            catch (Exception e) { Core.Log.Warning($"Junk tint failed: {e.GetType().Name}"); }
        }
    }

    /// <summary>
    /// The Diablo beam: a thin glowing column over the item, in the game's own rarity
    /// hologram material so it matches the chest holograms. Purely local, per client.
    /// </summary>
    public static class DropBeam
    {
        private static float Height(LootItem item) => item.WeaponClass >= 3 ? 3.0f : item.WeaponClass == 2 ? 2.2f : 1.5f;

        public static GameObject Attach(LootTag tag)
        {
            if (!Interop.Alive(tag.Object)) return null;
            try
            {
                // Not parented: a weapon tumbles as it lands and a child beam would tumble
                // with it (0.2.0 looked like a tube around the knife). Follow() keeps it
                // upright over the item every frame instead.
                var beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                beam.name = $"LootBeam_{tag.ViewId}";
                try { UnityEngine.Object.Destroy(beam.GetComponent<Collider>()); } catch { }
                var height = Height(tag.Item);
                var width = tag.Item.WeaponClass >= 3 ? 0.035f : 0.02f;
                beam.transform.localScale = new Vector3(width, height * 0.5f, width);
                beam.transform.rotation = Quaternion.identity;
                beam.transform.position = tag.Object.transform.position + Vector3.up * (height * 0.5f + 0.1f);

                var r = beam.GetComponent<Renderer>();
                if (Interop.Alive(r))
                {
                    Material mat = null;
                    try
                    {
                        var wf = WeaponFactory.Instance;
                        if (Interop.Alive(wf))
                            mat = tag.Item.WeaponClass switch
                            {
                                0 => wf.commonHologramMaterial,
                                1 => wf.uniqueHologramMaterial,
                                2 => wf.rareHologramMaterial,
                                _ => wf.legendaryHologramMaterial,
                            };
                    }
                    catch { }
                    if (Interop.Alive(mat)) r.sharedMaterial = mat;
                    else r.material.color = RarityColor(tag.Item.WeaponClass);
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    r.receiveShadows = false;
                }
                return beam;
            }
            catch (Exception e)
            {
                Core.Log.Warning($"Drop beam failed: {e.GetType().Name}: {e.Message}");
                return null;
            }
        }

        /// <summary>Keep the beam upright over the item. Called every frame for every tag.</summary>
        public static void Follow(LootTag tag)
        {
            if (!Interop.Alive(tag.Beam)) return;
            if (!Interop.Alive(tag.Object)) { Detach(tag); return; }
            try
            {
                var height = Height(tag.Item);
                tag.Beam.transform.position = tag.Object.transform.position + Vector3.up * (height * 0.5f + 0.1f);
                tag.Beam.transform.rotation = Quaternion.identity;
            }
            catch { }
        }

        public static void Detach(LootTag tag)
        {
            try { if (Interop.Alive(tag.Beam)) UnityEngine.Object.Destroy(tag.Beam); } catch { }
            tag.Beam = null;
        }

        public static Color RarityColor(int weaponClass) => weaponClass switch
        {
            0 => new Color(0.31f, 0.64f, 1f),
            1 => new Color(0.19f, 0.74f, 0.18f),
            2 => new Color(0.06f, 0.13f, 1f),
            _ => new Color(0.25f, 0.06f, 1f),
        };
    }

    /// <summary>
    /// The item's name floating over it in its rarity colour, always facing the player. The
    /// quiet alternative to the beam: readable when you walk up, ignorable mid-fight.
    /// </summary>
    public static class DropLabel
    {
        public static GameObject Attach(LootTag tag)
        {
            if (!Interop.Alive(tag.Object)) return null;
            try
            {
                var go = new GameObject($"LootLabel_{tag.ViewId}");
                var text = tag.Item.ColoredName;
                if (!tag.Item.IsWeapon) text += $"  <size=70%><color=#9A9A9A>{LootTables.JunkTierName(tag.Item.WeaponClass)}</color></size>";
                var tmp = UiKit.Text(go.transform, Vector3.zero, 1.2f, 0.08f, 0.32f, text, Il2CppTMPro.TextAlignmentOptions.Center);
                if (tmp == null) { UnityEngine.Object.Destroy(go); return null; }
                Follow(tag, go);
                return go;
            }
            catch (Exception e)
            {
                Core.Log.Warning($"Drop label failed: {e.GetType().Name}: {e.Message}");
                return null;
            }
        }

        public static void Follow(LootTag tag) => Follow(tag, tag.Label);

        private static void Follow(LootTag tag, GameObject label)
        {
            if (!Interop.Alive(label)) return;
            if (!Interop.Alive(tag.Object)) { Detach(tag); return; }
            try
            {
                var pos = tag.Object.transform.position + Vector3.up * 0.35f;
                label.transform.position = pos;
                Vector3 eye, fwd; Vector3? hand = null;
                try
                {
                    var head = AvatarPlayer.LocalAvatar.Head;
                    eye = head.position; fwd = head.forward;
                    try { var h = AvatarPlayer.LocalAvatar.RightHand; if (Interop.Alive(h)) hand = h.position; } catch { }
                }
                catch { eye = pos + Vector3.forward; fwd = -Vector3.forward; }
                var toEye = pos - eye; toEye.y = 0f;
                if (toEye.sqrMagnitude > 0.0001f) label.transform.rotation = Quaternion.LookRotation(toEye, Vector3.up);

                // Hover-only: visible while you look roughly at it within a few metres, or a hand is near it.
                if (ModConfig.DropLabelsOnHover.Value)
                {
                    var toItem = tag.Object.transform.position - eye;
                    var dist = toItem.magnitude;
                    var looking = dist < 4f && dist > 0.01f && Vector3.Angle(fwd, toItem) < Mathf.Lerp(18f, 8f, dist / 4f);
                    var reaching = hand.HasValue && (hand.Value - tag.Object.transform.position).sqrMagnitude < 0.6f * 0.6f;
                    var show = looking || reaching;
                    if (label.activeSelf != show) label.SetActive(show);
                }
                else if (!label.activeSelf) label.SetActive(true);
            }
            catch { }
        }

        public static void Detach(LootTag tag)
        {
            try { if (Interop.Alive(tag.Label)) UnityEngine.Object.Destroy(tag.Label); } catch { }
            tag.Label = null;
        }
    }

    /// <summary>
    /// The coin pile's own particle sparkle, cloned locally over each loot item. Found by
    /// looking for a particle system on any `Coin_Pile*` prefab in the game's networked
    /// pool, or on a live coin pile in the scene; the template is kept across scenes.
    /// </summary>
    public static class DropSparkle
    {
        private static GameObject _template;
        private static bool _searched;

        private static GameObject Template()
        {
            if (Interop.Alive(_template)) return _template;
            try
            {
                ParticleSystem found = null; string from = null;
                try
                {
                    var pool = NetworkObjectPool.UnpooledPrefabs;
                    if (pool != null)
                        foreach (var kv in pool)
                        {
                            if (kv.Key == null || !kv.Key.StartsWith("Coin_Pile") || !Interop.Alive(kv.Value)) continue;
                            var ps = kv.Value.GetComponentsInChildren<ParticleSystem>(true);
                            if (ps != null && ps.Length > 0) { found = ps[0]; from = kv.Key; break; }
                        }
                }
                catch { }
                if (found == null)
                {
                    foreach (var c in UnityEngine.Object.FindObjectsOfType<Coins>())
                    {
                        if (!Interop.Alive(c)) continue;
                        var ps = c.GetComponentsInChildren<ParticleSystem>(true);
                        if (ps != null && ps.Length > 0) { found = ps[0]; from = c.name; break; }
                    }
                }
                if (found == null)
                {
                    if (!_searched) { _searched = true; Core.Log.Msg("Drop sparkle: no coin-pile particle found yet; will look again later."); }
                    return null;
                }
                var root = new GameObject("LootOverhaul_SparkleTemplate");
                UnityEngine.Object.DontDestroyOnLoad(root);
                root.SetActive(false);
                var clone = UnityEngine.Object.Instantiate(found.gameObject, root.transform);
                clone.name = "Sparkle";
                _template = root;
                Core.Log.Msg($"Drop sparkle: borrowed `{found.name}` from `{from}`.");
                return _template;
            }
            catch (Exception e)
            {
                Core.Log.Warning($"Drop sparkle template failed: {e.GetType().Name}: {e.Message}");
                return null;
            }
        }

        public static GameObject Attach(LootTag tag)
        {
            if (!Interop.Alive(tag.Object)) return null;
            var template = Template();
            if (template == null) return null;
            try
            {
                var src = template.transform.GetChild(0).gameObject;
                var go = UnityEngine.Object.Instantiate(src);
                go.name = $"LootSparkle_{tag.ViewId}";
                go.transform.position = tag.Object.transform.position + Vector3.up * 0.15f;
                go.SetActive(true);
                foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true)) { try { ps.Play(true); } catch { } }
                return go;
            }
            catch (Exception e)
            {
                Core.Log.Warning($"Drop sparkle failed: {e.GetType().Name}: {e.Message}");
                return null;
            }
        }

        public static void Follow(LootTag tag)
        {
            if (!Interop.Alive(tag.Sparkle)) return;
            if (!Interop.Alive(tag.Object)) { Detach(tag); return; }
            try { tag.Sparkle.transform.position = tag.Object.transform.position + Vector3.up * 0.15f; } catch { }
        }

        public static void Detach(LootTag tag)
        {
            try { if (Interop.Alive(tag.Sparkle)) UnityEngine.Object.Destroy(tag.Sparkle); } catch { }
            tag.Sparkle = null;
        }
    }
}
