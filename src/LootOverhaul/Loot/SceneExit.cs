using System;
using Il2Cpp;
using Il2CppInterop.Runtime;
using LootOverhaul.Recon;
using UnityEngine.Events;

namespace LootOverhaul.Loot
{
    /// <summary>
    /// Floor loot must die with the dungeon. The game's pooled prop bodies survive a scene
    /// load (they live under the pool's DontDestroyOnLoad root), and a Photon room object
    /// that is never network-destroyed stays in the room's event cache too: the next dungeon
    /// showed the previous one's drops in the same cells, and a rejoining player got them
    /// all re-created in the air (2026-09-04). The master destroys what it still owns on the
    /// game's own pre-scene-load event; <c>Core.OnSceneWasInitialized</c> is the fallback.
    /// </summary>
    public static class SceneExit
    {
        private static bool _hooked;
        private static float _nextTry;

        public static void Install() { _hooked = false; _nextTry = 0f; }

        public static void Tick()
        {
            if (_hooked) return;
            var now = UnityEngine.Time.unscaledTime;
            if (now < _nextTry) return;
            _nextTry = now + 2f;
            try
            {
                var ev = GameManager.PreSceneLoadCallback;
                if (ev == null) return;
                ev.AddListener(DelegateSupport.ConvertDelegate<UnityAction>((Action)OnPreSceneLoad));
                _hooked = true;
                Core.Log.Msg("Floor loot is destroyed on the game's pre-scene-load event.");
            }
            catch (Exception e) { Core.Log.Warning($"PreSceneLoadCallback hook failed ({e.GetType().Name}); scene-change cleanup falls back to OnSceneWasInitialized."); _nextTry = now + 30f; }
        }

        private static void OnPreSceneLoad()
        {
            try
            {
                if (LootRegistry.Count == 0) return;
                ReconLog.Line($"pre-scene-load: {LootRegistry.Count} floor tag(s) still live");
                LootRegistry.Clear("scene about to load", destroyOwned: true);
            }
            catch (Exception e) { Core.Log.Warning($"Pre-scene-load cleanup threw: {e.GetType().Name}: {e.Message}"); }
        }
    }
}
