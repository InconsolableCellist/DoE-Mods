using System;
using Il2Cpp;
using UnityEngine;
using LootOverhaul.Recon;
using Interop = LootOverhaul.Recon.Interop;

namespace LootOverhaul.Loot
{
    /// <summary>
    /// The light potion: a tonic that puts a light on your head until you return to the lobby.
    ///
    /// Two lights, because the game has two kinds of dark (read from the assembly 2026-09-28):
    ///  - Ordinary dungeons are lit; a plain realtime Unity point light adds to that (on PCVR the
    ///    game keeps plain realtime lights, <c>FXLight.forceRealtime</c>'s tooltip).
    ///  - Crypt dungeons are black by shader (<c>DarkRoomManager</c>, the ENABLE_DARKNESS keyword):
    ///    a lamp does nothing there. The darkness is revealed only around up to 16 "active lights"
    ///    per room (<c>DarkRoomManager.IActiveLight</c>: the player torch, the torch staff, the room
    ///    crystals). <c>UpdateDarkRoom</c> takes every light that is not disabled and is either
    ///    <c>AlwaysUse</c> or in that room, and eases its <c>Radius</c> toward
    ///    <c>DefaultRadius × radiusMultiplier</c>. The game's own <c>FXDarkLight</c> is a plain
    ///    MonoBehaviour implementation: <c>Awake</c> destroys it unless <c>enableDarkRoomSupport</c>,
    ///    copies <c>darkLightRange</c> into its default radius, and <c>Start → SetState(Burning)</c>
    ///    calls <c>DarkRoomManager.AddLight</c>; every other reference it holds is null-checked. So
    ///    one is added to an inactive object, configured, then activated.
    ///
    /// Local only: no PhotonView, nothing sent. Only the drinker sees the light (0.9.20; 0.9.19 put
    /// the same light on the drinker's head in every modded peer's world).
    /// </summary>
    public static class LightPotion
    {
        public const string Stat = "Light";

        /// <summary>Sold with the tonics; the three tiers are the light's reach in metres (the player torch is 10).</summary>
        public static readonly Buffs.Def Def = new Buffs.Def
        {
            Stat = Stat, Name = "Light Potion", Flavor = "light around you", Short = "light",
            Mults = new[] { 6f, 9f, 13f }, Prices = new[] { 80, 240, 800 },
        };

        /// <summary>Share of trinket drops in a crypt (dark) dungeon that are a Minor Light Potion instead.</summary>
        public const double CryptDropShare = 0.2;

        private static readonly Color LightColor = new Color(1f, 0.9f, 0.75f);
        private const float Intensity = 1.2f;

        private static float _radius;                 // ours; 0 = none
        private static GameObject _local;
        private static float _nextCheck;

        public static bool Active => _radius > 0f;
        public static float Radius => _radius;

        /// <summary>A Minor one for a crypt kill's trinket roll (the master's side).</summary>
        public static LootItem Dropped(int killerActor)
        {
            var item = Buffs.MakeItem(Def, 0);
            try { item.FoundBy = AvatarPlayer.FindByActorNo(killerActor)?.name; } catch { }
            return item;
        }

        /// <summary>Light up for this run. The strongest wins: a radius at or below the current one is refused (false) and changes nothing.</summary>
        public static bool Activate(float radius, string why)
        {
            if (radius <= _radius + 0.0001f) return false;
            _radius = radius;
            // No head yet is fine: Tick builds the light once the avatar exists.
            Rebuild(ref _local, LocalHead(), _radius, "LootOverhaul_LightPotion");
            _nextCheck = 0f;
            Core.Log.Msg($"light potion: {why}, radius {_radius:0.#} m{(DarkRoomManager.Instance != null ? " (crypt: darkness reveal on)" : "")}");
            return true;
        }

        public static void Drink(LootItem item)
        {
            var inv = BagManager.Inventory;
            if (!Activate(item.BuffMult, $"drank {item.Name}"))
            {
                BagManager.Toast($"A light as strong or stronger is already on ({_radius:0.#} m). {item.ColoredName} stays in your bag.");
                return;
            }
            inv.Remove(item.Id);
            inv.Save();
            BagManager.Toast($"Drank {item.ColoredName}: light around you ({_radius:0.#} m) until you return to the lobby.");
            BagPanel.Refresh();
        }

        /// <summary>The run is over: lobby or main menu.</summary>
        public static void Clear(string why)
        {
            var had = Active;
            _radius = 0f;
            Destroy(ref _local);
            if (had) Core.Log.Msg($"light potion cleared: {why}");
        }

        public static void Tick()
        {
            var now = Time.unscaledTime;
            if (now < _nextCheck) return;
            _nextCheck = now + 1f;
            if (!Active) return;
            try
            {
                // The head can be rebuilt (respawn, scene load) and the crypt's manager comes and
                // goes with the scene: re-parent or rebuild, and re-register with a manager that
                // arrived after the light did (AddLight ignores a light it already has).
                var head = LocalHead();
                if (!Interop.Alive(_local) || (Interop.Alive(head) && _local.transform.parent != head))
                    Rebuild(ref _local, head, _radius, "LootOverhaul_LightPotion");
                Register(_local);
            }
            catch (Exception e) { Core.Log.Warning($"Light potion tick threw: {e.GetType().Name}: {e.Message}"); _nextCheck = now + 10f; }
        }

        private static Transform LocalHead()
        {
            try { var local = AvatarPlayer.LocalAvatar; return Interop.Alive(local) ? local.Head : null; } catch { return null; }
        }

        private static void Rebuild(ref GameObject go, Transform head, float radius, string name)
        {
            Destroy(ref go);
            if (!Interop.Alive(head)) return;
            go = new GameObject(name);
            go.SetActive(false);   // FXDarkLight.Awake must see the fields below
            go.transform.SetParent(head, false);
            go.transform.localPosition = new Vector3(0f, 0.25f, 0f);

            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = radius;
            light.intensity = Intensity;
            light.color = LightColor;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;

            var dark = go.AddComponent<FXDarkLight>();
            dark.enableDarkRoomSupport = true;
            dark.darkLightRange = radius;
            dark.AlwaysUse = true;
            dark.state = FXDarkLight.State.Burning;
            go.SetActive(true);
        }

        private static void Register(GameObject go)
        {
            if (!Interop.Alive(go) || DarkRoomManager.Instance == null) return;
            var dark = go.GetComponent<FXDarkLight>();
            if (!Interop.Alive(dark)) return;
            // Put out by the game (RPC_SetLightsEnabledRanged puts out every light in a radius):
            // the potion lasts the run, so it relights.
            if (!dark.IsLit) dark.state = FXDarkLight.State.Burning;
            DarkRoomManager.AddLight(dark.Cast<DarkRoomManager.IActiveLight>());
        }

        private static void Destroy(ref GameObject go)
        {
            if (!Interop.Alive(go)) { go = null; return; }
            try
            {
                var dark = go.GetComponent<FXDarkLight>();
                if (Interop.Alive(dark) && DarkRoomManager.Instance != null) DarkRoomManager.RemoveLight(dark.Cast<DarkRoomManager.IActiveLight>());
            }
            catch { }
            try { UnityEngine.Object.Destroy(go); } catch { }
            go = null;
        }
    }
}
