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
        // One tinted material per (source material, colour, emission), shared by every element
        // that asks for it (0.10.8). Renderer.material made a fresh copy per element on every
        // panel rebuild, and destroying the element does not free that copy, so each click on
        // the bag or booth leaked a handful of materials.
        private static readonly System.Collections.Generic.Dictionary<(int, Color, Color, bool), Material> Tints =
            new System.Collections.Generic.Dictionary<(int, Color, Color, bool), Material>();

        /// <summary>Gives the renderer a shared material: its current one tinted <paramref name="color"/>, with optional emission.</summary>
        public static void Tint(Renderer r, Color color, Color? emission = null, bool enableEmission = false)
        {
            var src = r.sharedMaterial;
            if (!Interop.Alive(src)) { r.material.color = color; return; }
            var key = (src.GetInstanceID(), color, emission ?? Color.clear, enableEmission);
            if (!Tints.TryGetValue(key, out var mat) || !Interop.Alive(mat))
            {
                mat = new Material(src) { hideFlags = HideFlags.DontUnloadUnusedAsset };
                mat.color = color;
                if (emission.HasValue)
                {
                    try { if (enableEmission) mat.EnableKeyword("_EMISSION"); mat.SetColor("_EmissionColor", emission.Value); } catch { }
                }
                Tints[key] = mat;
            }
            r.sharedMaterial = mat;
        }

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


        /// <summary>
        /// A label. With <paramref name="fit"/> the font shrinks (to 80% of <paramref name="size"/>, never below UiStyle.Floor)
        /// until the line fits the rect, so a long stats line is read in full instead of cut off
        /// under the buttons (report 2026-09-08: two-perk weapons). With <paramref name="lines"/>
        /// above 1 the text wraps onto that many lines first and shrinks only after that; the
        /// block is centred vertically on <paramref name="localPos"/>.
        /// </summary>
        public static TextMeshPro Text(Transform parent, Vector3 localPos, float width, float height, float size, string text,
                                       TextAlignmentOptions align = TextAlignmentOptions.Left, bool fit = false, int lines = 1,
                                       float? minSize = null, TextOverflowModes overflow = TextOverflowModes.Truncate)
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
                if (fit)
                {
                    tmp.enableAutoSizing = true;
                    tmp.fontSizeMax = size;
                    // 0.13.0: shrink to 80% at most, and never below the legibility floor (0.30).
                    tmp.fontSizeMin = minSize.HasValue ? Mathf.Min(size, minSize.Value) : Mathf.Max(size * UiStyle.FitShare, Mathf.Min(size, UiStyle.Floor));
                }
                tmp.alignment = align;
                tmp.enableWordWrapping = lines > 1;
                // Truncate to the rect: a line that overflows runs under the buttons to its right.
                // 0.13.2: list rows use Ellipsis, so a cut name ends in "…" instead of mid-word.
                tmp.overflowMode = overflow;
                tmp.richText = true;
                tmp.rectTransform.sizeDelta = new Vector2(width, Mathf.Max(height, size * 0.14f * Mathf.Max(1, lines)));
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

        /// <summary>
        /// The game's stats text on one short line: <c>GetStatsText()</c> returns lines like
        /// "18 &lt;color=grey&gt;&lt;/color&gt;Damage" and "+ &lt;color=orange&gt;Criticals&lt;/color&gt;"; empty colour
        /// tags go, lines are joined with a dot.
        /// </summary>
        public static string StatsLine(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            var s = System.Text.RegularExpressions.Regex.Replace(raw, @"<color=[^>]*>\s*</color>", "");
            var parts = new System.Collections.Generic.List<string>();
            foreach (var line in s.Replace("\r", "").Split(new[] { '\n', '|' }))
            {
                var t = System.Text.RegularExpressions.Regex.Replace(line, @"\s+", " ").Trim();
                if (t.Length > 0) parts.Add(t);
            }
            return string.Join(" · ", parts);
        }

        /// <summary>Row layout, from the row's left edge: icon, then the type tag, then the text.</summary>
        public const float TagX = 0.17f, TextX = 0.245f;

        /// <summary>
        /// The small type tag between a row's icon and its name, centred on two lines:
        /// "Sword" over "T7" for a weapon, "Head" over "armor", "Tonic", "Junk". It replaces the
        /// "Sword t7" that used to lead the stats line, so the line starts with the numbers.
        /// </summary>
        public static void TypeTag(Transform row, Vector3 localPos, LootItem item, string chip = null)
        {
            string text;
            // 0.13.2: a status word (EQUIPPED, WORN, ARMORY) takes the tag's second line instead of
            // sitting in front of the stats, where it took room the name and stats needed.
            var second = chip != null ? Grey(chip) : null;
            if (item.IsWeapon) text = $"{LootTables.TypeName(item.PropType)}\n{second ?? Grey($"T{item.WeaponTier + 1}")}";
            else if (item.IsArmor) text = $"{Armor.SlotNames[item.ArmorSlot]}\n{second ?? Grey("armor")}";
            else if (item.IsBuff) text = second == null ? "Tonic" : $"Tonic\n{second}";
            else text = second == null ? "Junk" : $"Junk\n{second}";
            Text(row, localPos, UiStyle.TagWidth, 0.1f, UiStyle.Body, Paint("#D7DCE4", text), TextAlignmentOptions.Center, fit: true, lines: 2);
        }

        /// <summary>
        /// A bright rectangular frame: four thin bars around <paramref name="center"/> in the
        /// parent's plane, unlit-looking (colour plus emission) so it reads in a dark room.
        /// </summary>
        public static GameObject Frame(Transform parent, Vector3 center, float width, float height, float thickness, Color color, float depth = 0.004f, float emission = UiStyle.FrameGlow)
        {
            var root = new GameObject("Frame");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = center;
            root.transform.localRotation = Quaternion.identity;
            var hw = width * 0.5f; var hh = height * 0.5f; var t = thickness;
            var bars = new (Vector3 pos, Vector3 size)[]
            {
                (new Vector3(0f, hh - t * 0.5f, 0f), new Vector3(width, t, depth)),
                (new Vector3(0f, -hh + t * 0.5f, 0f), new Vector3(width, t, depth)),
                (new Vector3(-hw + t * 0.5f, 0f, 0f), new Vector3(t, height, depth)),
                (new Vector3(hw - t * 0.5f, 0f, 0f), new Vector3(t, height, depth)),
            };
            foreach (var (pos, size) in bars)
            {
                var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                bar.name = "Bar";
                try { UnityEngine.Object.Destroy(bar.GetComponent<Collider>()); } catch { }
                bar.transform.SetParent(root.transform, false);
                bar.transform.localPosition = pos;
                bar.transform.localRotation = Quaternion.identity;
                bar.transform.localScale = size;
                try
                {
                    var r = bar.GetComponent<Renderer>();
                    Tint(r, color, color * emission, enableEmission: true);
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    r.receiveShadows = false;
                }
                catch { }
            }
            return root;
        }

        /// <summary>
        /// A lit-from-within flat strip (row stripe, divider, active-tab underline). Emissive so it
        /// shows in a dark room (the plain tinted backdrop renders near black there); no laser
        /// catcher, so it never steals the pointer from the buttons on top of it.
        /// </summary>
        public static GameObject Bar(Transform parent, Vector3 localPos, float width, float height, Color color)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Bar";
            try { UnityEngine.Object.Destroy(quad.GetComponent<Collider>()); } catch { }
            quad.transform.SetParent(parent, false);
            quad.transform.localPosition = localPos;
            quad.transform.localRotation = Quaternion.identity;
            quad.transform.localScale = new Vector3(width, height, 1f);
            try
            {
                var r = quad.GetComponent<Renderer>();
                Tint(r, color, color * UiStyle.PlateGlow, enableEmission: true);
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            catch { }
            return quad;
        }

        /// <summary>
        /// A pointable with nothing drawn: a button clone with its renderers off, sized to
        /// <paramref name="width"/> × <paramref name="height"/> metres, that runs
        /// <paramref name="onPressed"/>. The caller draws whatever it should look like.
        /// </summary>
        public static InteractableButton HitBox(Transform parent, Vector3 localPos, float width, float height, Action onPressed)
        {
            if (!Interop.Alive(_buttonTemplate)) return null;
            try
            {
                var go = UnityEngine.Object.Instantiate(_buttonTemplate, parent);
                go.name = "HitBox";
                go.transform.localPosition = localPos;
                go.transform.localRotation = Quaternion.identity;
                var baseScale = _buttonTemplate.transform.localScale;
                go.transform.localScale = new Vector3(baseScale.x * width / ButtonSize.x, baseScale.y * height / ButtonSize.y, baseScale.z);
                go.SetActive(true);
                FitColliders(go, parent, localPos, width * HitShare, height * HitShare);
                foreach (var r in go.GetComponentsInChildren<Renderer>(true)) if (Interop.Alive(r)) r.enabled = false;
                var btn = go.GetComponent<InteractableButton>();
                if (!Interop.Alive(btn)) return null;
                btn.scaleOnHoverAndPress = false;
                if (onPressed != null) btn.onPressed.AddListener(DelegateSupport.ConvertDelegate<UnityAction>(onPressed));
                return btn;
            }
            catch (Exception e)
            {
                Core.Log.Warning($"Hit box failed: {e.GetType().Name}: {e.Message}");
                return null;
            }
        }


        /// <summary>A hit box is this share of the drawn button, so two buttons a Gap apart never touch.</summary>
        public const float HitShare = 0.9f;
        private static bool _colliderLogged;

        /// <summary>
        /// 0.13.2: make the pointable area exactly <paramref name="width"/> × <paramref name="height"/>
        /// (in <paramref name="parent"/>'s units) centred on the button. Until now the clone was only
        /// scaled by width / <see cref="ButtonSize"/>, and ButtonSize is a guess (the measured
        /// renderers are the label, so the 0.52 × 0.06 default is used). Where the game button's
        /// collider is bigger than that guess (it covers the glow), a hit box is bigger than its
        /// plate and reaches its neighbours: on the ARMOR page WEAR / SELL sit 0.02 m apart with the
        /// &lt; &gt; arrows just above, the laser hops between overlapping boxes every frame (the
        /// hover sound over and over) and sticks to the middle of the panel. The same overlap was
        /// the WEAR / SELL flicker of 0.10.4. Box colliders are set to the size asked for; any
        /// other collider is left as scaled and named in the log once. The first fit logs the
        /// collider's old size, so a playtest log shows how far off it was.
        /// </summary>
        private static void FitColliders(GameObject go, Transform parent, Vector3 localPos, float width, float height)
        {
            try
            {
                var center = parent.TransformPoint(localPos);
                var worldW = parent.TransformVector(new Vector3(width, 0f, 0f)).magnitude;
                var worldH = parent.TransformVector(new Vector3(0f, height, 0f)).magnitude;
                var others = "";
                foreach (var c in go.GetComponentsInChildren<Collider>(true))
                {
                    if (!Interop.Alive(c)) continue;
                    var box = c.TryCast<BoxCollider>();
                    if (box == null) { others += c.GetIl2CppType().Name + " "; continue; }
                    var t = box.transform;
                    var ls = t.lossyScale;
                    var sx = Mathf.Max(1e-5f, Mathf.Abs(ls.x)); var sy = Mathf.Max(1e-5f, Mathf.Abs(ls.y));
                    if (!_colliderLogged)
                    {
                        _colliderLogged = true;
                        var was = Vector3.Scale(box.size, new Vector3(sx, sy, Mathf.Abs(ls.z)));
                        Core.Log.Msg($"UI: hit box collider was {was.x:0.000}×{was.y:0.000} m for a {worldW:0.000}×{worldH:0.000} m button; fitted.");
                    }
                    box.size = new Vector3(worldW / sx, worldH / sy, box.size.z);
                    var c0 = t.InverseTransformPoint(center);
                    box.center = new Vector3(c0.x, c0.y, box.center.z);
                }
                if (others.Length > 0 && !_colliderLogged) { _colliderLogged = true; Core.Log.Warning($"UI: hit box has colliders that can't be fitted: {others.Trim()}"); }
            }
            catch (Exception e) { Core.Log.Warning($"Hit box fit failed: {e.GetType().Name}: {e.Message}"); }
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
                Tint(r, color);
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            catch { }
            LaserCatcher(parent, localPos + new Vector3(0f, 0f, 0.005f), width, height);
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
                FitColliders(go, parent, localPos, width, height);
                foreach (var r in go.GetComponentsInChildren<Renderer>(true)) if (Interop.Alive(r)) r.enabled = false;
                var btn = go.GetComponent<InteractableButton>();
                if (Interop.Alive(btn)) { btn.onPressedSound = null; btn.onHoverSound = null; btn.scaleOnHoverAndPress = false; }
            }
            catch (Exception e) { Core.Log.Warning($"Laser catcher failed: {e.GetType().Name}: {e.Message}"); }
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

        // =====================================================================================
        // 0.13.0: the shared components. Every panel of the stall and the bag is built only
        // from these and from UiStyle, so all of them look and behave alike.
        // =====================================================================================

        public static string HexOf(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);
        public static string Paint(string hex, string text) => $"<color={hex}>{text}</color>";
        public static string Grey(string text) => Paint(UiStyle.Text2Hex, text);

        /// <summary>
        /// An item's coloured name for the panels. Armor rolled before 0.13.0 keeps the old dark
        /// Rare/Legendary blues in its saved name, which are hard to read on the dark panel; they
        /// are shown in today's colours (the saved name is not changed).
        /// </summary>
        public static string NameOf(LootItem item)
        {
            var n = item?.ColoredName ?? "";
            // 0.13.2: weapons too. The game names them in the same dark Rare / Legendary blues, so a
            // Legendary weapon read as plain dark text on the enchant page.
            if (item != null && (item.IsArmor || item.IsWeapon))
                n = n.Replace("#1020FF", UiStyle.ArmorHex[2]).Replace("#4010FF", UiStyle.ArmorHex[3]).Replace("#31BC2F", UiStyle.ArmorHex[1]);
            if (item != null && item.IsWeapon && !n.Contains("<color"))
                n = Paint(UiStyle.ArmorHex[Math.Max(0, Math.Min(3, item.WeaponClass))], n);
            return n;
        }

        /// <summary>"1,240": the one way a token amount is written.</summary>
        public static string Number(long n) => n.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
        /// <summary>"1,240 tokens": the one way tokens are written in sentences and toasts.</summary>
        public static string Money(long n) => $"{Number(n)} tokens";
        /// <summary>"1 item", "3 items".</summary>
        public static string Plural(int n, string word) => $"{n} {(n == 1 ? word : word + "s")}";

        /// <summary>
        /// A label of one size and colour (rich text inside still works). <paramref name="x"/> is the
        /// left edge of its box for every alignment. 0.13.2: a centred label was drawn half its width
        /// too far left, because <see cref="Text"/> puts a centred text's pivot in the middle (the
        /// enchant cards' titles at the card's left edge, "none" under SELL ALL off its button,
        /// "locked, can't be sold" over the SELL button, the page number on top of the &lt; button).
        /// </summary>
        public static TextMeshPro Label(Transform parent, float x, float y, float width, float height, float size, string text, Color color,
                                        TextAlignmentOptions align = TextAlignmentOptions.Left, int lines = 1)
            => Text(parent, new Vector3(align == TextAlignmentOptions.Center ? x + width * 0.5f : x, y, 0f), width, height, size,
                    Paint(HexOf(color), text), align, fit: true, lines: lines);

        /// <summary>A divider across the panel.</summary>
        public static void Rule(Transform parent, float y)
            => Bar(parent, new Vector3(0f, y, 0.004f), UiStyle.PanelWidth - 0.04f, 0.004f, UiStyle.Rule);

        /// <summary>The lit band behind a row: every other row, or always when a colour is given.</summary>
        public static void RowStripe(Transform row, int i, float pitch, Color? color = null)
        {
            if (color.HasValue || i % 2 == 0)
                Bar(row, new Vector3(0f, 0f, 0.006f), UiStyle.PanelWidth - 0.03f, pitch - 0.008f, color ?? UiStyle.Stripe);
        }

        /// <summary>
        /// The one button: a lit plate, a frame, a bold label, and an invisible game button on top
        /// for the pointer. The plate lights up while the pointer is on it (the game button's own
        /// hover event). Disabled: dim plate and frame, same label, no hit box.
        /// </summary>
        public static InteractableButton Btn(Transform parent, Vector3 pos, Vector2 size, string label, Action onPressed,
                                             UiStyle.Variant variant = UiStyle.Variant.Primary, UiStyle.State state = UiStyle.State.Normal,
                                             float labelSize = UiStyle.Label)
        {
            var look = UiStyle.ButtonLook(variant, state);
            var plate = Bar(parent, pos + new Vector3(0f, 0f, 0.002f), size.x, size.y, look.Plate);
            Frame(parent, pos, size.x, size.y, UiStyle.FrameWidth, look.Frame);
            if (!string.IsNullOrEmpty(label))
                Text(parent, pos + new Vector3(0f, 0f, -0.004f), size.x - 0.016f, size.y, labelSize,
                    Paint(HexOf(look.Label), $"<b>{(label == "<" || label == ">" ? $"<noparse>{label}</noparse>" : label)}</b>"), TextAlignmentOptions.Center, fit: true);
            if (state == UiStyle.State.Disabled || onPressed == null) return null;
            var hit = HitBox(parent, pos, size.x, size.y, onPressed);
            Hover(hit, plate, look.Plate, look.Hover);
            return hit;
        }

        /// <summary>Lights <paramref name="plate"/> while the pointer is on <paramref name="hit"/>.</summary>
        private static void Hover(InteractableButton hit, GameObject plate, Color normal, Color lit)
        {
            if (!Interop.Alive(hit) || !Interop.Alive(plate)) return;
            try
            {
                var r = plate.GetComponent<Renderer>();
                Action<bool> onHover = on =>
                {
                    try { if (Interop.Alive(r)) { var c = on ? lit : normal; Tint(r, c, c * UiStyle.PlateGlow, enableEmission: true); } } catch { }
                };
                hit.onHover.AddListener(DelegateSupport.ConvertDelegate<UnityAction<bool>>(onHover));
            }
            catch (Exception e) { Core.Log.Warning($"Button hover hook failed: {e.GetType().Name}: {e.Message}"); }
        }

        public static InteractableButton ActionBtn(Transform parent, float x, float y, string label, Action onPressed,
                                                   UiStyle.Variant v = UiStyle.Variant.Primary, UiStyle.State s = UiStyle.State.Normal)
            => Btn(parent, new Vector3(x, y, 0f), UiStyle.Action, label, onPressed, v, s);

        public static InteractableButton CompactBtn(Transform parent, float x, float y, string label, Action onPressed,
                                                    UiStyle.Variant v = UiStyle.Variant.Secondary, UiStyle.State s = UiStyle.State.Normal)
            => Btn(parent, new Vector3(x, y, 0f), UiStyle.Compact, label, onPressed, v, s);

        /// <summary>A stepper (- -- + ++): the signs are drawn from bars, big, and need no font glyph.</summary>
        public static void Stepper(Transform parent, Vector3 pos, string kind, Action onPressed)
        {
            Btn(parent, pos, UiStyle.Stepper, null, onPressed, UiStyle.Variant.Secondary);
            var col = UiStyle.ButtonLook(UiStyle.Variant.Secondary, UiStyle.State.Normal).Label;
            var n = kind.Length;
            var plus = kind[0] == '+';
            var arm = n == 2 ? 0.022f : 0.032f;
            const float th = 0.008f;
            for (var i = 0; i < n; i++)
            {
                var cx = n == 2 ? (i == 0 ? -0.015f : 0.015f) : 0f;
                var c = pos + new Vector3(cx, 0f, -0.006f);
                Bar(parent, c, arm, th, col);
                if (plus) Bar(parent, c, th, arm, col);
            }
        }

        /// <summary>The padlock toggle: a stepper-sized button, white (selected) with a closed lock when locked.</summary>
        public static void LockToggle(Transform parent, Vector3 pos, bool locked, Action onPressed)
        {
            Btn(parent, pos, UiStyle.Stepper, null, onPressed, UiStyle.Variant.Secondary, locked ? UiStyle.State.Selected : UiStyle.State.Normal);
            var c = locked ? UiStyle.Hex("#FFFFFF") : UiStyle.Hex("#C9D1DD");
            var z = new Vector3(0f, 0f, -0.006f);
            Bar(parent, pos + z + new Vector3(0f, -0.010f, 0f), 0.034f, 0.024f, c);
            Frame(parent, pos + z + new Vector3(locked ? 0f : 0.008f, locked ? 0.012f : 0.020f, 0f), 0.022f, 0.024f, 0.006f, c);
        }

        /// <summary>
        /// A strip of equal tab cells from <paramref name="x0"/> to <paramref name="x1"/>; the open
        /// one is a white plate with dark text.
        /// </summary>
        public static void Tabs(Transform parent, float x0, float x1, float y, string[] labels, int selected, Action<int> onPick)
        {
            var n = labels.Length;
            var w = (x1 - x0 - UiStyle.Gap * (n - 1)) / n;
            for (var i = 0; i < n; i++)
            {
                var idx = i;
                Btn(parent, new Vector3(x0 + w * 0.5f + i * (w + UiStyle.Gap), y, 0f), new Vector2(w, UiStyle.TabHeight), labels[i],
                    () => onPick(idx), UiStyle.Variant.Tab, i == selected ? UiStyle.State.Selected : UiStyle.State.Normal);
            }
        }

        /// <summary>The token coin: a gold octagon (two squares) with a darker centre. No font glyph.</summary>
        public static void Coin(Transform parent, Vector3 pos, float size)
        {
            var z = new Vector3(0f, 0f, -0.004f);
            var side = size * 0.83f;
            Bar(parent, pos + z, side, side, UiStyle.Gold);
            Bar(parent, pos + z, side, side, UiStyle.Gold).transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            Bar(parent, pos + z + new Vector3(0f, 0f, -0.001f), size * 0.3f, size * 0.3f, UiStyle.Hex("#8C6A14")).transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
        }

        /// <summary>
        /// A number right-aligned so it ends at <paramref name="xRight"/>, with the coin right
        /// before its first digit (measured after auto-fit, so 3 and 5 digits look the same).
        /// </summary>
        public static void CoinNumber(Transform parent, float xRight, float y, float width, long amount, Color color,
                                      float size = UiStyle.Row, float coin = UiStyle.CoinSize)
        {
            var textW = width - coin - 0.01f;
            // 0.13.2: a number shrinks to the floor and is never cut ("60,00" for 60,000); if it is
            // still wider, it runs left and the coin moves with it.
            var tmp = Text(parent, new Vector3(xRight - textW, y, 0f), textW, 0.05f, size,
                Paint(HexOf(color), $"<b>{Number(amount)}</b>"), TextAlignmentOptions.Right, fit: true,
                minSize: UiStyle.Floor, overflow: TextOverflowModes.Overflow);
            var numW = textW;
            try
            {
                if (tmp != null) { tmp.enableWordWrapping = false; tmp.ForceMeshUpdate(); var b = tmp.textBounds.size.x; if (b > 0.001f) numW = b; }
            }
            catch { }
            Coin(parent, new Vector3(xRight - numW - 0.008f - coin * 0.5f, y, 0f), coin);
        }

        /// <summary>
        /// Coin and number as one group centred on <paramref name="xc"/> (0.13.2), for a price under
        /// a button: the tonic tiers and SELL ALL. The number is measured, so the pair sits in the
        /// middle whatever its digits.
        /// </summary>
        public static void CoinNumberCentered(Transform parent, float xc, float y, float width, long amount, Color color,
                                              float size = UiStyle.Body, float coin = 0.026f)
        {
            const float gap = 0.008f;
            var textW = width - coin - gap;
            var tmp = Text(parent, new Vector3(xc - width * 0.5f + coin + gap, y, 0f), textW, 0.05f, size,
                Paint(HexOf(color), $"<b>{Number(amount)}</b>"), TextAlignmentOptions.Left, fit: true,
                minSize: UiStyle.Floor, overflow: TextOverflowModes.Overflow);
            var numW = textW;
            try
            {
                if (tmp != null) { tmp.ForceMeshUpdate(); var b = tmp.textBounds.size.x; if (b > 0.001f) numW = b; }
            }
            catch { }
            var left = xc - (coin + gap + numW) * 0.5f;
            if (tmp != null) tmp.transform.localPosition = new Vector3(left + coin + gap, y, 0f);
            Coin(parent, new Vector3(left + coin * 0.5f, y, 0f), coin);
        }

        /// <summary>The price column (0.16 m centred on <paramref name="xc"/>): coin and amount, gold or red; with a weight under it on the bag.</summary>
        public static void Price(Transform parent, float xc, float y, long amount, bool affordable = true, float? weight = null)
        {
            var w = UiStyle.PriceWidth;
            var py = weight.HasValue ? y + 0.025f : y;
            CoinNumber(parent, xc + w * 0.5f, py, w, amount, affordable ? UiStyle.Gold : UiStyle.Red);
            if (weight.HasValue)
                Label(parent, xc - w * 0.5f, y - 0.035f, w, 0.045f, UiStyle.Body, $"{weight.Value:0.0} wt", UiStyle.Text2, TextAlignmentOptions.Right);
        }

        /// <summary>A status word (LOCKED, EQUIPPED, WORN, ARMORY): small grey capitals, no frame. Returns its width.</summary>
        public static float Chip(Transform parent, float xLeft, float y, string label, Color? color = null)
        {
            var tmp = Text(parent, new Vector3(xLeft, y, 0f), 0.4f, 0.045f, UiStyle.Floor, Paint(HexOf(color ?? UiStyle.Text2), label));
            var w = 0.02f + label.Length * 0.024f;
            try { if (tmp != null) { tmp.ForceMeshUpdate(); var b = tmp.textBounds.size.x; if (b > 0.001f) w = b + 0.01f; } } catch { }
            return w;
        }

        /// <summary>
        /// The panel header: title on the left, context in grey after it, the token balance always
        /// in the top right corner, and a rule under it.
        /// </summary>
        public static void Header(Transform parent, string title, string context, long tokens, float contextSize = UiStyle.Body)
        {
            const float L = UiStyle.Left, R = UiStyle.Right, y = UiStyle.HeaderY;
            Text(parent, new Vector3(L, y, 0f), 0.36f, 0.07f, UiStyle.Title, Paint(UiStyle.TextHex, $"<b>{title}</b>"), fit: true);
            if (!string.IsNullOrEmpty(context))
                Text(parent, new Vector3(L + 0.38f, y, 0f), R - 0.29f - (L + 0.38f), 0.06f, contextSize, Grey(context), fit: true);
            CoinNumber(parent, R, y, 0.26f, tokens, UiStyle.Gold, UiStyle.Row, 0.036f);
            Rule(parent, UiStyle.RuleHeaderY);
        }

        /// <summary>Column positions of a row, from the right edge: action buttons, the padlock, the price, then the text.</summary>
        public struct Cols { public float[] Act; public float Lock, Price, TextEnd; }

        public static Cols Columns(int actions, bool lockCol = false, bool price = true)
        {
            var e = UiStyle.Right;
            var c = new Cols { Act = new float[Math.Max(0, actions)] };
            for (var i = 0; i < actions; i++) { c.Act[i] = e - UiStyle.Action.x * 0.5f; e -= UiStyle.Action.x; if (i < actions - 1) e -= UiStyle.Gap; }
            if (lockCol) { e -= UiStyle.Gap; c.Lock = e - UiStyle.Stepper.x * 0.5f; e -= UiStyle.Stepper.x; }
            if (price) { e -= UiStyle.Gap; c.Price = e - UiStyle.PriceWidth * 0.5f; e -= UiStyle.PriceWidth; }
            c.TextEnd = e - UiStyle.Gap;
            return c;
        }

        /// <summary>A row's text: the name on top; under it an optional status word, then the stats, which wrap to a second line before they shrink.</summary>
        public static void RowText(Transform row, float textEnd, string name, string stats, string chip = null, Color? statsColor = null, float textX = UiStyle.TextX,
                                   int statsLines = 2)
        {
            // 0.13.2: a long name or stats line shrinks down to the 0.30 floor before anything is cut,
            // and what still doesn't fit ends in "…" (it was cut mid-word at 80% size).
            Text(row, new Vector3(textX, statsLines == 1 ? 0.025f : 0.04f, 0f), textEnd - textX, 0.05f, UiStyle.Row, name, fit: true,
                minSize: UiStyle.Floor, overflow: TextOverflowModes.Ellipsis);
            var sx = textX;
            var sy = statsLines == 1 ? -0.03f : -0.025f;
            if (!string.IsNullOrEmpty(chip)) sx += Chip(row, textX, sy, chip) + 0.015f;
            if (!string.IsNullOrEmpty(stats))
                Text(row, new Vector3(sx, sy, 0f), textEnd - sx, statsLines == 1 ? 0.045f : 0.085f, UiStyle.Body,
                    Paint(HexOf(statsColor ?? UiStyle.Text), stats), fit: true, lines: statsLines,
                    minSize: UiStyle.Floor, overflow: TextOverflowModes.Ellipsis);
        }

        /// <summary>
        /// The left part of an item row on the SELL and bag lists: icon, type tag, name and stats.
        /// 0.13.2: an armor row gives its stats the room they need. The slot word goes under the
        /// icon instead of in a type-tag column, the text starts right after the icon, and the stats
        /// get two lines split between stats, so three stats show in full
        /// ("weapon dmg +28% · leap +26%" over "run speed +26%"). A status word (WORN) leads the name.
        /// </summary>
        public static void ItemText(Transform row, float textEnd, LootItem item, string chip)
        {
            if (!item.IsArmor)
            {
                Icon(row, new Vector3(UiStyle.IconX, 0f, -0.03f), item);
                TypeTag(row, new Vector3(UiStyle.TagX, 0f, 0f), item, chip);
                RowText(row, textEnd, NameOf(item), Booth.StatsOf(item), null, UiStyle.Text2, statsLines: 1);
                return;
            }
            Icon(row, new Vector3(UiStyle.IconX, 0.018f, -0.03f), item);
            Text(row, new Vector3(UiStyle.IconX, -0.045f, 0f), 0.13f, 0.035f, UiStyle.Floor,
                Grey(Armor.SlotNames[item.ArmorSlot].ToUpperInvariant()), TextAlignmentOptions.Center);
            var x = UiStyle.Left + 0.11f;
            var name = string.IsNullOrEmpty(chip) ? NameOf(item) : $"{Grey($"<size=70%>{chip}</size>")}  {NameOf(item)}";
            Text(row, new Vector3(x, 0.04f, 0f), textEnd - x, 0.05f, UiStyle.Row, name, fit: true,
                minSize: UiStyle.Floor, overflow: TextOverflowModes.Ellipsis);
            Text(row, new Vector3(x, -0.025f, 0f), textEnd - x, 0.085f, UiStyle.Body, Paint(UiStyle.Text2Hex, Armor.StatsTwoLines(item)),
                fit: true, lines: 2, minSize: UiStyle.Floor, overflow: TextOverflowModes.Ellipsis);
        }

        /// <summary>A new row object at <paramref name="y"/> under <paramref name="parent"/>, with its stripe.</summary>
        public static Transform NewRow(Transform parent, string name, float y, int i, float pitch = UiStyle.Pitch, Color? stripe = null)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, y, 0f);
            RowStripe(go.transform, i, pitch, stripe);
            return go.transform;
        }

        /// <summary>The pager, in the same place on every page; nothing when there is one page.</summary>
        public static void Pager(Transform parent, int page, int pages, Action prev, Action next)
        {
            if (pages <= 1) return;
            CompactBtn(parent, -0.2f, UiStyle.PagerY, "<", prev, UiStyle.Variant.Secondary, page <= 0 ? UiStyle.State.Disabled : UiStyle.State.Normal);
            Label(parent, -0.12f, UiStyle.PagerY, 0.24f, 0.05f, UiStyle.Body, $"{page + 1} / {pages}", UiStyle.Text2, TextAlignmentOptions.Center);
            CompactBtn(parent, 0.2f, UiStyle.PagerY, ">", next, UiStyle.Variant.Secondary, page >= pages - 1 ? UiStyle.State.Disabled : UiStyle.State.Normal);
        }

        /// <summary>A centred message where a list would be.</summary>
        public static void Empty(Transform parent, string text)
            => Label(parent, -0.6f, UiStyle.Row0Y - UiStyle.Pitch, 1.2f, 0.06f, UiStyle.Row, text, UiStyle.Text2, TextAlignmentOptions.Center);

        /// <summary>
        /// The small picture at a row's left: the weapon's own mesh, or a shape drawn from bars for
        /// everything else (bottle for a tonic, helmet for armor, diamond for junk), so no font glyph is needed.
        /// </summary>
        public static void Icon(Transform row, Vector3 pos, LootItem item)
        {
            if (item.IsWeapon) { WeaponPreview(row, pos, item, 0.085f); return; }
            var z = new Vector3(0f, 0f, -0.01f);
            if (item.IsBuff) TonicIcon(row, pos);
            else if (item.IsArmor)
            {
                var c = UiStyle.Hex(Armor.ClassColor(item.WeaponClass));
                Bar(row, pos + z + new Vector3(0f, 0.024f, 0f), 0.05f, 0.012f, c);
                Bar(row, pos + z + new Vector3(-0.019f, 0.002f, 0f), 0.012f, 0.034f, c);
                Bar(row, pos + z + new Vector3(0.019f, 0.002f, 0f), 0.012f, 0.034f, c);
                Bar(row, pos + z + new Vector3(0f, -0.016f, 0f), 0.026f, 0.012f, c);
                Bar(row, pos + z + new Vector3(0f, -0.028f, 0f), 0.012f, 0.014f, c);
            }
            else
                Bar(row, pos + z, 0.036f, 0.036f, UiStyle.Hex(LootTables.JunkColor(item.WeaponClass))).transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
        }

        /// <summary>A tonic bottle drawn from two bars.</summary>
        public static void TonicIcon(Transform row, Vector3 pos)
        {
            var z = new Vector3(0f, 0f, -0.01f);
            var c = UiStyle.Hex(UiStyle.TonicHex);
            Bar(row, pos + z + new Vector3(0f, 0.026f, 0f), 0.012f, 0.014f, c);
            Bar(row, pos + z + new Vector3(0f, -0.008f, 0f), 0.04f, 0.052f, c);
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
