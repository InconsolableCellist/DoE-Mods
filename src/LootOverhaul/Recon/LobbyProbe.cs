using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppPhoton.Pun;
using LootOverhaul.Gate;
using UnityEngine;
using UnityEngine.Events;

namespace LootOverhaul.Recon
{
    /// <summary>
    /// Everything the lobby booth needs to know before it can be built.
    ///
    /// <see cref="Survey"/> (Backslash): scene flags, where the fabricators and vendors stand, how
    /// the holsters are laid out (which slot accepts what), where you are standing — then
    /// drop two marker cubes, one plain and one DontDestroyOnLoad, so the next scene change
    /// can report which survives (design item 5).
    ///
    /// <see cref="ButtonTest"/> (Scroll Lock): clone a vanilla fabricator button, put it in
    /// front of you, bind a managed handler to its onPressed, and register it with the local
    /// hands' pointable list. If pointing at it and pulling the trigger logs a line, the whole
    /// bag-and-booth UI plan is validated (design item 8).
    /// </summary>
    public static class LobbyProbe
    {
        private static GameObject _markerPlain, _markerPersistent;
        private static string _markerScene;
        private static GameObject _button;
        private static int _presses;

        public static void Survey()
        {
            if (!(ModGate.LocalOnly || ModGate.Active))
            {
                ReconLog.Headline($"Lobby survey refused: gate says {ModGate.LocalReason}");
                return;
            }

            ReconLog.Section("Lobby survey");
            ReconLog.TryKeyValue("scene", () => UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
            ReconLog.TryKeyValue("GameManager.IsLobbyScene", () => GameManager.IsLobbyScene);
            ReconLog.TryKeyValue("GameManager.IsMegaLobbyScene", () => GameManager.IsMegaLobbyScene);
            ReconLog.TryKeyValue("GameManager.CurrentRealm", () => GameManager.CurrentRealm);
            ReconLog.TryKeyValue("Photon", () => PhotonNetwork.InRoom
                ? $"room `{PhotonNetwork.CurrentRoom.Name}` players={PhotonNetwork.CurrentRoom.PlayerCount} master={PhotonNetwork.IsMasterClient} visible={PhotonNetwork.CurrentRoom.IsVisible}"
                : "not in a room");
            ReconLog.TryKeyValue("gate", () => $"active={ModGate.Active} ({ModGate.Reason}); local={ModGate.LocalOnly} ({ModGate.LocalReason})");

            Vector3 here = Vector3.zero;
            ReconLog.Try("local player", () =>
            {
                var local = AvatarPlayer.LocalAvatar;
                if (!Interop.Alive(local)) { ReconLog.KeyValue("local avatar", "none"); return; }
                here = local.Head.position;
                ReconLog.KeyValue("head position", Interop.Vec(here));
                ReconLog.KeyValue("head forward", Interop.Vec(local.Head.forward));
            });

            ReconLog.Try("fabricators", () =>
            {
                var fabs = UnityEngine.Object.FindObjectsOfType<Fabricator>();
                ReconLog.KeyValue("Fabricator count", fabs.Length);
                foreach (var f in fabs)
                {
                    if (!Interop.Alive(f)) continue;
                    var kind = f.GetIl2CppType().Name;
                    var btn = "none";
                    try { btn = Interop.Alive(f.fabricateButton) ? Interop.ScenePath(f.fabricateButton.transform) : "null"; } catch { }
                    ReconLog.Line($"- {kind} `{Interop.ScenePath(f.transform)}` at {Interop.Vec(f.transform.position)} active={f.gameObject.activeInHierarchy} fabricateButton={btn}");
                }
            });

            ReconLog.Try("vendors", () =>
            {
                var vendors = UnityEngine.Object.FindObjectsOfType<Vendor>();
                ReconLog.KeyValue("Vendor count", vendors.Length);
                foreach (var v in vendors)
                    if (Interop.Alive(v))
                        ReconLog.Line($"- {v.GetIl2CppType().Name} `{Interop.ScenePath(v.transform)}` at {Interop.Vec(v.transform.position)}");
            });

            ReconLog.Try("interactable buttons", () =>
            {
                var buttons = UnityEngine.Object.FindObjectsOfType<InteractableButton>();
                ReconLog.KeyValue("InteractableButton count", buttons.Length);
                var shown = 0;
                foreach (var b in buttons)
                {
                    if (!Interop.Alive(b) || shown++ >= 12) continue;
                    ReconLog.Line($"- `{Interop.ScenePath(b.transform)}` mode={b.mode} hold={b.holdToPress} tooltip={b.enableTooltip}");
                }
            });

            ReconLog.Try("hands", () =>
            {
                var hands = UnityEngine.Object.FindObjectsOfType<VRControllerHands>();
                ReconLog.KeyValue("VRControllerHands count", hands.Length);
                foreach (var h in hands)
                    if (Interop.Alive(h)) ReconLog.Line($"- `{Interop.ScenePath(h.transform)}` pointablesInRange={(h.pointablesInRange == null ? -1 : h.pointablesInRange.Count)}");
            });

            ReconLog.Try("holsters", () =>
            {
                var holsters = UnityEngine.Object.FindObjectsOfType<Holster>();
                ReconLog.KeyValue("Holster count", holsters.Length);
                foreach (var h in holsters)
                {
                    if (!Interop.Alive(h)) continue;
                    ReconLog.Line($"- holster#{h.index} `{Interop.ScenePath(h.transform)}` mode={h.positioningMode} inventory={h.isInventoryHolster} capacity={h.itemCapacity} canPickUp={h.canPickUp}");
                    try
                    {
                        var ps = h.propSettings;
                        if (ps != null)
                            foreach (var p in ps)
                                ReconLog.Line($"  - accepts {p.type} saveSlot={p.saveSlot} capacity={p.capacity} prefab=`{p.prefabName}` label=`{p.label}`");
                    }
                    catch (Exception e) { ReconLog.Line($"  - propSettings unavailable: {e.GetType().Name}"); }
                    try
                    {
                        var dw = h.defaultWeapons;
                        if (dw != null)
                            foreach (var d in dw)
                                ReconLog.Line($"  - default saveSlot={d.saveSlot} fallback=`{Interop.Name(d.fallbackProp)}`");
                    }
                    catch (Exception e) { ReconLog.Line($"  - defaultWeapons unavailable: {e.GetType().Name}"); }
                }
            });

            ReconLog.Try("loadout (read-only)", () =>
            {
                ReconLog.KeyValue("current loadout", PlayerProfile.GetCurrentLoadout());
                foreach (var lv in new[] { PlayerData.LoadoutValues.WeaponL, PlayerData.LoadoutValues.WeaponR, PlayerData.LoadoutValues.WeaponB, PlayerData.LoadoutValues.WeaponS })
                {
                    var data = PlayerProfile.GetLoadoutData(lv);
                    ReconLog.Line($"- {lv}: {(data == null ? "null" : Interop.OneLine(data.ToString()))}");
                }
                var unlocked = PlayerProfile.GetUnlockedWeapons();
                ReconLog.KeyValue("armory (unlocked) count", unlocked.Count);
                for (var i = 0; i < unlocked.Count && i < 8; i++)
                    ReconLog.Line($"- armory[{i}]: \"{Interop.OneLine(unlocked[i].GetDisplayName(false))}\" `{unlocked[i].GetPrefabName()}` {unlocked[i].GetWeaponClass()} tier={unlocked[i].GetWeaponTier()}");
            });

            ReconLog.Try("decoration meshes", () =>
            {
                // Every mesh in the scene whose name suggests a trinket: candidates for loot
                // bodies (the spawned prop's mesh can be swapped for one of these on every client).
                var filters = UnityEngine.Object.FindObjectsOfType<MeshFilter>();
                var kw = new System.Text.RegularExpressions.Regex("cup|mug|goblet|chalice|tankard|bottle|flask|plate|bowl|candle|lantern|book|scroll|vase|urn|jar|skull|bone|gem|coin|ring|amulet|idol|statue|relic|trophy|dice|jewel|crown|treasure|bag|sack|pouch|orb|horn", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                var seen = new HashSet<string>();
                var n = 0;
                foreach (var mf in filters)
                {
                    if (!Interop.Alive(mf) || !Interop.Alive(mf.sharedMesh)) continue;
                    var m = mf.sharedMesh;
                    if (!kw.IsMatch(m.name) && !kw.IsMatch(mf.gameObject.name)) continue;
                    if (!seen.Add(m.name)) continue;
                    var b = m.bounds.size;
                    string mat = "?";
                    try { var r = mf.GetComponent<Renderer>(); if (Interop.Alive(r) && Interop.Alive(r.sharedMaterial)) mat = r.sharedMaterial.name; } catch { }
                    ReconLog.Line($"- mesh `{m.name}` on `{mf.gameObject.name}` size {b.x:0.00}×{b.y:0.00}×{b.z:0.00} verts={m.vertexCount} material=`{mat}` at {Interop.ScenePath(mf.transform)}");
                    n++;
                }
                ReconLog.KeyValue("decoration mesh candidates", n);
            });

            ReconLog.Try("marker cubes", () =>
            {
                if (here == Vector3.zero) { ReconLog.Line("- no player position; markers skipped"); return; }
                _markerPlain = MakeMarker(here + Vector3.forward * 0.6f + Vector3.left * 0.3f, Color.cyan, "LootOverhaul_Marker_Plain");
                _markerPersistent = MakeMarker(here + Vector3.forward * 0.6f + Vector3.right * 0.3f, Color.magenta, "LootOverhaul_Marker_DDOL");
                UnityEngine.Object.DontDestroyOnLoad(_markerPersistent);
                _markerScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                ReconLog.Line($"- dropped a cyan (plain) and a magenta (DontDestroyOnLoad) cube near your head in `{_markerScene}`; the next scene change reports which survived");
            });

            ReconLog.Headline($"Lobby survey written to {ReconLog.CurrentFile}");
        }

        private static int Strip(UnityEventBase ev, string name)
        {
            if (ev == null) return 0;
            var n = 0;
            try
            {
                for (var i = 0; i < ev.GetPersistentEventCount(); i++)
                {
                    ReconLog.Line($"  - {name}[{i}] -> {ev.GetPersistentTarget(i)?.name}.{ev.GetPersistentMethodName(i)} : OFF");
                    ev.SetPersistentListenerState(i, UnityEventCallState.Off);
                    n++;
                }
                ev.RemoveAllListeners();
            }
            catch (Exception e) { ReconLog.Line($"  - {name}: strip failed: {e.GetType().Name}: {e.Message}"); }
            return n;
        }

        private static GameObject MakeMarker(Vector3 pos, Color color, string name)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = pos;
            go.transform.localScale = Vector3.one * 0.15f;
            try { UnityEngine.Object.Destroy(go.GetComponent<Collider>()); } catch { }
            try
            {
                var r = go.GetComponent<Renderer>();
                if (Interop.Alive(r)) r.material.color = color;
            }
            catch { }
            return go;
        }

        /// <summary>On scene change: which marker survived? Which spawned weapons?</summary>
        public static void OnSceneChanged(string newScene)
        {
            if (_markerScene == null) return;
            ReconLog.Line($"markers from `{_markerScene}` after entering `{newScene}`: plain={(Interop.Alive(_markerPlain) ? "ALIVE" : "destroyed")} " +
                          $"DontDestroyOnLoad={(Interop.Alive(_markerPersistent) ? "ALIVE" : "destroyed")}");
        }

        public static void ButtonTest()
        {
            if (!(ModGate.LocalOnly || ModGate.Active))
            {
                ReconLog.Headline($"Button test refused: gate says {ModGate.LocalReason}");
                return;
            }

            ReconLog.Section("Cloned button + pointer test");
            try
            {
                if (Interop.Alive(_button)) { UnityEngine.Object.Destroy(_button); _button = null; }

                InteractableButton source = null;
                foreach (var f in UnityEngine.Object.FindObjectsOfType<Fabricator>())
                {
                    try { if (Interop.Alive(f) && Interop.Alive(f.fabricateButton)) { source = f.fabricateButton; break; } } catch { }
                }
                if (source == null)
                {
                    var all = UnityEngine.Object.FindObjectsOfType<InteractableButton>();
                    foreach (var b in all) if (Interop.Alive(b)) { source = b; break; }
                }
                if (source == null) { ReconLog.Headline("No InteractableButton in this scene to clone — run this in the lobby."); return; }
                ReconLog.KeyValue("source button", Interop.ScenePath(source.transform));

                var local = AvatarPlayer.LocalAvatar;
                if (!Interop.Alive(local)) { ReconLog.Headline("No local avatar."); return; }
                var head = local.Head;
                var pos = head.position + head.forward * 0.8f;

                _button = UnityEngine.Object.Instantiate(source.gameObject);
                _button.name = "LootOverhaul_TestButton";
                _button.transform.SetParent(null, true);
                _button.transform.position = pos;
                _button.transform.rotation = Quaternion.LookRotation(-head.forward, Vector3.up);
                _button.SetActive(true);

                var btn = _button.GetComponent<InteractableButton>();
                if (!Interop.Alive(btn)) { ReconLog.Headline("Clone has no InteractableButton component."); return; }

                // The clone inherits the fabricator's SERIALIZED listeners (EV_Fabricate and
                // friends), and on 2026-09-02 two presses reached PlayerProfile.IncrementCharacterData
                // (Coins, -9999) before our handler ran. RemoveAllListeners only clears runtime
                // listeners, so every persistent one is switched off explicitly, on every event.
                ReconLog.Try("strip inherited listeners", () =>
                {
                    var stripped = 0;
                    stripped += Strip(btn.onPressed, "onPressed");
                    stripped += Strip(btn.onHover, "onHover");
                    stripped += Strip(btn.onStartHolding, "onStartHolding");
                    stripped += Strip(btn.onHolding, "onHolding");
                    stripped += Strip(btn.onInterrupted, "onInterrupted");
                    btn.tooltipHandler = null;
                    btn.enableTooltip = false;
                    btn.holdToPress = false;
                    ReconLog.Line($"- {stripped} persistent listener(s) disabled on the clone");
                });
                ReconLog.Try("verify strip", () =>
                {
                    var live = 0;
                    for (var i = 0; i < btn.onPressed.GetPersistentEventCount(); i++)
                        if (btn.onPressed.GetPersistentListenerState(i) != UnityEventCallState.Off) live++;
                    if (live > 0) { ReconLog.Headline($"REFUSING to place the test button: {live} inherited onPressed listener(s) still active."); UnityEngine.Object.Destroy(_button); _button = null; }
                    else ReconLog.Line("- onPressed has no active inherited listeners");
                });
                if (_button == null) return;

                ReconLog.Try("SetLabel", () => btn.SetLabel("LOOT TEST"));
                ReconLog.Try("ShowButton", () => btn.ShowButton(true, "LOOT TEST", null));

                ReconLog.Try("onPressed bind", () =>
                {
                    Action pressed = () =>
                    {
                        _presses++;
                        ReconLog.Headline($"*** TEST BUTTON PRESSED (#{_presses}) — managed handler reached through UnityEvent.");
                    };
                    var action = DelegateSupport.ConvertDelegate<UnityAction>(pressed);
                    btn.onPressed.AddListener(action);
                    ReconLog.Line("- onPressed listener attached");
                });

                ReconLog.Try("AddPointable", () =>
                {
                    var hands = UnityEngine.Object.FindObjectsOfType<VRControllerHands>();
                    if (hands.Length == 0) { ReconLog.Line("- no VRControllerHands found; the game's pointer may still find the button by itself"); return; }
                    // Reflection rather than a compile-time cast to IPointable: if the interop
                    // proxy doesn't implement the interface, this logs instead of failing the build.
                    var add = typeof(VRControllerHands).GetMethod("AddPointable");
                    foreach (var h in hands)
                    {
                        if (!Interop.Alive(h)) continue;
                        try { add.Invoke(h, new object[] { btn }); ReconLog.Line($"- registered with `{Interop.ScenePath(h.transform)}`"); }
                        catch (Exception e) { ReconLog.Line($"- AddPointable on `{Interop.ScenePath(h.transform)}` failed: {(e.InnerException ?? e).GetType().Name}: {(e.InnerException ?? e).Message}"); }
                    }
                });

                ReconLog.Headline("Test button placed in front of you. Point at it and pull the trigger; a headline logs each press. Press Scroll Lock again to re-place it. Inherited listeners are OFF, so it does nothing but log.");
            }
            catch (Exception e) { ReconLog.Error("button test", e); }
        }
    }
}
