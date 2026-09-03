using System;
using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppTMPro;
using LootOverhaul.Recon;
using UnityEngine;
using UnityEngine.Events;
using Interop = LootOverhaul.Recon.Interop;

namespace LootOverhaul.Loot
{
    /// <summary>
    /// The small set of building blocks the bag panel and the booth are made of, all borrowed
    /// from the game so the pointer, the fonts and the look match: a cloned
    /// <c>InteractableButton</c> with its inherited listeners switched off (the 0.1 coin
    /// lesson), a TextMeshPro label using the game's own font, and a weapon mesh preview
    /// from the generator. Templates are captured in the lobby and kept alive across scene
    /// loads (DontDestroyOnLoad survives; recon 2026-09-02), so the panel also opens in a
    /// dungeon where no fabricator exists.
    /// </summary>
    public static class UiKit
    {
        private static GameObject _buttonTemplate;
        private static TMP_FontAsset _font;
        private static Material _fontMaterial;
        private static GameObject _templateRoot;
        /// <summary>World size of the button template at scale 1, measured at capture. Layouts use it.</summary>
        public static Vector2 ButtonSize = new Vector2(0.52f, 0.06f);

        public static bool Ready => Interop.Alive(_buttonTemplate) && Interop.Alive(_font);

        /// <summary>Try to capture templates from whatever the current scene offers. Safe to call often.</summary>
        public static void CaptureTemplates()
        {
            if (!Interop.Alive(_templateRoot))
            {
                _templateRoot = new GameObject("LootOverhaul_Templates");
                UnityEngine.Object.DontDestroyOnLoad(_templateRoot);
                _templateRoot.SetActive(false);
            }

            if (!Interop.Alive(_buttonTemplate))
            {
                InteractableButton source = null;
                try
                {
                    foreach (var f in UnityEngine.Object.FindObjectsOfType<Fabricator>())
                        if (Interop.Alive(f) && Interop.Alive(f.fabricateButton)) { source = f.fabricateButton; break; }
                    if (source == null)
                        foreach (var b in UnityEngine.Object.FindObjectsOfType<InteractableButton>())
                            if (Interop.Alive(b)) { source = b; break; }
                }
                catch (Exception e) { Core.Log.Warning($"Button template search failed: {e.GetType().Name}"); }

                if (source != null)
                {
                    try
                    {
                        var clone = UnityEngine.Object.Instantiate(source.gameObject, _templateRoot.transform);
                        clone.name = "ButtonTemplate";
                        var btn = clone.GetComponent<InteractableButton>();
                        var stripped = StripListeners(btn);
                        if (stripped < 0) { UnityEngine.Object.Destroy(clone); }
                        else
                        {
                            _buttonTemplate = clone;
                            try
                            {
                                // Measure on the live source (the template root is inactive, so bounds there are empty).
                                var rs = source.GetComponentsInChildren<Renderer>();
                                var b = new Bounds(source.transform.position, Vector3.zero);
                                var first = true;
                                foreach (var r in rs) { if (!Interop.Alive(r)) continue; if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }
                                // The renderers under the button are the label, not the glowing frame: the
                                // 2026-09-02 run measured 0.05 m for a button that draws ~0.5 m wide. Trust the
                                // measurement only if it is plausible; otherwise keep the known good default.
                                if (!first && b.size.x > 0.25f && b.size.x < 1.5f) ButtonSize = new Vector2(b.size.x, Mathf.Max(0.03f, b.size.y));
                            }
                            catch { }
                            Core.Log.Msg($"UI: button template captured from `{Interop.ScenePath(source.transform)}` ({stripped} inherited listener(s) off), size {ButtonSize.x:0.00}×{ButtonSize.y:0.00} m.");
                        }
                    }
                    catch (Exception e) { Core.Log.Warning($"Button template clone failed: {e.GetType().Name}: {e.Message}"); }
                }
            }

            if (!Interop.Alive(_font))
            {
                try
                {
                    foreach (var t in UnityEngine.Object.FindObjectsOfType<TextMeshPro>())
                    {
                        if (!Interop.Alive(t) || !Interop.Alive(t.font)) continue;
                        _font = t.font;
                        _fontMaterial = t.fontSharedMaterial;
                        Core.Log.Msg($"UI: font template captured from `{Interop.ScenePath(t.transform)}` ({_font.name}).");
                        break;
                    }
                }
                catch (Exception e) { Core.Log.Warning($"Font template search failed: {e.GetType().Name}"); }
            }
        }

        /// <summary>Switch every persistent listener on every event of a button off. Returns -1 if any stayed on.</summary>
        public static int StripListeners(InteractableButton btn)
        {
            if (!Interop.Alive(btn)) return -1;
            var n = 0;
            n += Strip(btn.onPressed); n += Strip(btn.onHover); n += Strip(btn.onStartHolding);
            n += Strip(btn.onHolding); n += Strip(btn.onInterrupted);
            btn.tooltipHandler = null;
            btn.enableTooltip = false;
            btn.holdToPress = false;
            for (var i = 0; i < btn.onPressed.GetPersistentEventCount(); i++)
                if (btn.onPressed.GetPersistentListenerState(i) != UnityEventCallState.Off) return -1;
            return n;
        }

        private static int Strip(UnityEventBase ev)
        {
            if (ev == null) return 0;
            var n = 0;
            try
            {
                for (var i = 0; i < ev.GetPersistentEventCount(); i++) { ev.SetPersistentListenerState(i, UnityEventCallState.Off); n++; }
                ev.RemoveAllListeners();
            }
            catch { }
            return n;
        }

        public static InteractableButton Button(Transform parent, Vector3 localPos, string label, Action onPressed, float scale = 1f)
        {
            if (!Interop.Alive(_buttonTemplate)) return null;
            try
            {
                var go = UnityEngine.Object.Instantiate(_buttonTemplate, parent);
                go.name = $"Btn_{label}";
                go.transform.localPosition = localPos;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = _buttonTemplate.transform.localScale * scale;
                go.SetActive(true);
                var btn = go.GetComponent<InteractableButton>();
                try { btn.ShowButton(true, label, null); } catch { }
                try { btn.SetLabel(label); } catch { }
                if (onPressed != null)
                    btn.onPressed.AddListener(DelegateSupport.ConvertDelegate<UnityAction>(onPressed));
                return btn;
            }
            catch (Exception e)
            {
                Core.Log.Warning($"Button `{label}` failed: {e.GetType().Name}: {e.Message}");
                return null;
            }
        }

        public static TextMeshPro Text(Transform parent, Vector3 localPos, float width, float height, float size, string text,
                                       TextAlignmentOptions align = TextAlignmentOptions.Left)
        {
            try
            {
                var go = new GameObject("Text");
                go.transform.SetParent(parent, false);
                go.transform.localPosition = localPos;
                go.transform.localRotation = Quaternion.identity;
                var tmp = go.AddComponent(Il2CppType.Of<TextMeshPro>()).TryCast<TextMeshPro>();
                if (tmp == null) { UnityEngine.Object.Destroy(go); return null; }
                if (Interop.Alive(_font)) tmp.font = _font;
                if (Interop.Alive(_fontMaterial)) tmp.fontSharedMaterial = _fontMaterial;
                // 3D TextMeshPro: fontSize 1 ≈ 0.1 m glyphs. 0.35 is a readable 3.5 cm line at arm's length.
                tmp.fontSize = size;
                tmp.alignment = align;
                tmp.enableWordWrapping = false;
                // Truncate to the rect: a line that overflows runs under the buttons to its right.
                tmp.overflowMode = TextOverflowModes.Truncate;
                tmp.richText = true;
                tmp.rectTransform.sizeDelta = new Vector2(width, Mathf.Max(height, size * 0.14f));
                tmp.rectTransform.pivot = new Vector2(align == TextAlignmentOptions.Center ? 0.5f : 0f, 0.5f);
                tmp.text = text;
                return tmp;
            }
            catch (Exception e)
            {
                Core.Log.Warning($"Text failed: {e.GetType().Name}: {e.Message}");
                return null;
            }
        }

        /// <summary>A flat dark backdrop. The default primitive material tinted; good enough until the booth gets art.</summary>
        public static GameObject Backdrop(Transform parent, Vector3 localPos, float width, float height, Color color)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Backdrop";
            try { UnityEngine.Object.Destroy(quad.GetComponent<Collider>()); } catch { }
            quad.transform.SetParent(parent, false);
            quad.transform.localPosition = localPos;
            quad.transform.localRotation = Quaternion.identity;
            quad.transform.localScale = new Vector3(width, height, 1f);
            try
            {
                var r = quad.GetComponent<Renderer>();
                r.material.color = color;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            catch { }
            if (ModConfig.PanelLaser.Value) LaserCatcher(parent, localPos + new Vector3(0f, 0f, 0.005f), width, height);
            return quad;
        }

        /// <summary>
        /// An invisible pointable the size of the panel: a button clone with its renderers off
        /// and no action, so the game's laser shows wherever you aim on the window, not only
        /// over the buttons. Real buttons sit in front of it and win the raycast.
        /// </summary>
        private static void LaserCatcher(Transform parent, Vector3 localPos, float width, float height)
        {
            if (!Interop.Alive(_buttonTemplate)) return;
            try
            {
                var go = UnityEngine.Object.Instantiate(_buttonTemplate, parent);
                go.name = "LaserCatcher";
                go.transform.localPosition = localPos;
                go.transform.localRotation = Quaternion.identity;
                var baseScale = _buttonTemplate.transform.localScale;
                go.transform.localScale = new Vector3(baseScale.x * width / ButtonSize.x, baseScale.y * height / ButtonSize.y, baseScale.z);
                go.SetActive(true);
                foreach (var r in go.GetComponentsInChildren<Renderer>(true)) if (Interop.Alive(r)) r.enabled = false;
                var btn = go.GetComponent<InteractableButton>();
                if (Interop.Alive(btn)) { btn.onPressedSound = null; btn.onHoverSound = null; btn.scaleOnHoverAndPress = false; }
            }
            catch (Exception e) { Core.Log.Warning($"Laser catcher failed: {e.GetType().Name}: {e.Message}"); }
        }

        /// <summary>
        /// A preview that fits in <paramref name="fit"/> metres whatever the item: the weapon's
        /// generated mesh scaled by its bounds, or a text glyph for junk.
        /// </summary>
        public static GameObject Preview(Transform parent, Vector3 localPos, LootItem item, float fit)
        {
            if (!item.IsWeapon)
            {
                var glyph = item.IsBuff ? "○" : item.WeaponClass >= 2 ? "✦" : item.WeaponClass == 1 ? "◆" : "•";
                var color = item.IsBuff ? "#7FD8FF" : LootTables.JunkColor(item.WeaponClass);
                var t = Text(parent, localPos + new Vector3(-fit * 0.25f, 0f, 0f), fit, fit, fit * 1.6f, $"<color={color}>{glyph}</color>", TextAlignmentOptions.Center);
                return t == null ? null : t.gameObject;
            }
            return WeaponPreview(parent, localPos, item, fit);
        }

        /// <summary>The weapon's generated mesh and material, scaled so its longest side is <paramref name="fit"/> metres.</summary>
        public static GameObject WeaponPreview(Transform parent, Vector3 localPos, LootItem item, float fit)
        {
            try
            {
                var wm = WeaponCodec.ToModule(item);
                var tuple = WeaponFactory.GenerateLootWeapon(wm);
                var mesh = tuple.Item1;
                var mat = tuple.Item2;
                if (!Interop.Alive(mesh)) return null;
                var go = new GameObject("Preview");
                go.transform.SetParent(parent, false);
                go.transform.localPosition = localPos;
                go.transform.localRotation = Quaternion.Euler(0f, 0f, -35f);
                var longest = 1f;
                try { var b = mesh.bounds.size; longest = Mathf.Max(b.x, Mathf.Max(b.y, b.z)); } catch { }
                // Some generated meshes report tiny bounds; a bad number here drew a spear across the whole booth.
                if (longest < 0.15f || longest > 5f) longest = 1f;
                var factor = Mathf.Clamp(fit / longest, 0.04f, 0.5f);
                go.transform.localScale = Vector3.one * factor;
                // Centre the mesh on the anchor rather than on its origin (a spear's origin is at the grip).
                try { go.transform.localPosition = localPos - go.transform.localRotation * (mesh.bounds.center * factor); } catch { }
                var mf = go.AddComponent(Il2CppType.Of<MeshFilter>()).TryCast<MeshFilter>();
                var mr = go.AddComponent(Il2CppType.Of<MeshRenderer>()).TryCast<MeshRenderer>();
                if (mf == null || mr == null) { UnityEngine.Object.Destroy(go); return null; }
                mf.sharedMesh = mesh;
                if (Interop.Alive(mat)) mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                return go;
            }
            catch (Exception e)
            {
                Core.Log.Warning($"Weapon preview failed for {item.Name}: {e.GetType().Name}: {e.Message}");
                return null;
            }
        }

        public static void DestroyChildren(Transform t)
        {
            if (!Interop.Alive(t)) return;
            for (var i = t.childCount - 1; i >= 0; i--)
            {
                try { UnityEngine.Object.Destroy(t.GetChild(i).gameObject); } catch { }
            }
        }
    }
}
