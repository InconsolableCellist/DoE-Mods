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
                            Core.Log.Msg($"UI: button template captured from `{Interop.ScenePath(source.transform)}` ({stripped} inherited listener(s) off).");
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
                tmp.fontSize = size;
                tmp.alignment = align;
                tmp.enableWordWrapping = false;
                tmp.overflowMode = TextOverflowModes.Truncate;
                tmp.richText = true;
                tmp.rectTransform.sizeDelta = new Vector2(width, height);
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
            return quad;
        }

        /// <summary>The weapon's generated mesh and material, as a small static preview.</summary>
        public static GameObject WeaponPreview(Transform parent, Vector3 localPos, LootItem item, float scale)
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
                go.transform.localScale = Vector3.one * scale;
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
