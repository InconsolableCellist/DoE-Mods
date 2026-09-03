using System;
using System.Collections.Generic;
using Il2Cpp;
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
    }

    /// <summary>
    /// Which networked objects in the room are loot. Keyed by PhotonView ID, which is the
    /// same number on every client. The master spawns and broadcasts; everyone tags; the
    /// pickup prefix consults this to decide "bag it" versus "let the game wield it".
    /// Cleared on every scene change, because the objects die with the scene anyway.
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
            if (ModConfig.DropBeams.Value && tag.Beam == null) tag.Beam = DropBeam.Attach(tag);
            if (ModConfig.DropLabels.Value && tag.Label == null) tag.Label = DropLabel.Attach(tag);
            if (ModConfig.DropSparkles.Value && tag.Sparkle == null) tag.Sparkle = DropSparkle.Attach(tag);
        }

        public static void Remove(int viewId)
        {
            if (!Tags.TryGetValue(viewId, out var tag)) return;
            Tags.Remove(viewId);
            DropBeam.Detach(tag);
            DropLabel.Detach(tag);
            DropSparkle.Detach(tag);
        }

        public static void Clear(string why)
        {
            foreach (var tag in Tags.Values) { DropBeam.Detach(tag); DropLabel.Detach(tag); DropSparkle.Detach(tag); }
            if (Tags.Count > 0) Core.Log.Msg($"Loot registry cleared ({Tags.Count} tag(s)): {why}");
            Tags.Clear();
        }

        /// <summary>Resolve a view id to its object on this client, or null if not (yet) here.</summary>
        public static GameObject FindObject(int viewId)
        {
            try
            {
                var pv = Il2CppPhoton.Pun.PhotonView.Find(viewId);
                return Interop.Alive(pv) ? pv.gameObject : null;
            }
            catch { return null; }
        }

        /// <summary>Retry object lookup for tags whose object had not replicated when the broadcast arrived.</summary>
        public static void Tick()
        {
            foreach (var tag in Tags.Values)
            {
                if (tag.Claimed) continue;
                if (Interop.Alive(tag.Object)) { DropBeam.Follow(tag); DropLabel.Follow(tag); DropSparkle.Follow(tag); continue; }
                tag.Object = FindObject(tag.ViewId);
                if (tag.Object != null) Decorate(tag);
            }
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
                var width = tag.Item.WeaponClass >= 3 ? 0.08f : 0.045f;
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
                Vector3 eye;
                try { eye = AvatarPlayer.LocalAvatar.Head.position; } catch { eye = pos + Vector3.forward; }
                var toEye = pos - eye; toEye.y = 0f;
                if (toEye.sqrMagnitude > 0.0001f) label.transform.rotation = Quaternion.LookRotation(toEye, Vector3.up);
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
