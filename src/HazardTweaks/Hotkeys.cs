using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;

namespace HazardTweaks
{
    /// <summary>
    /// Keyboard hotkeys through the Input System package. The 2026-09-27 game update switched
    /// the player's active input handling to the Input System, and since then every
    /// <c>UnityEngine.Input</c> call throws InvalidOperationException (every frame, so the log
    /// filled with "Hotkey threw"). A press is the key going down between two polls, read from
    /// <c>isPressed</c> rather than <c>wasPressedThisFrame</c> so it does not depend on the
    /// game's Input System update mode. No keyboard (<c>Keyboard.current</c> null) reads as
    /// nothing pressed; an exception switches the hotkeys off with one warning.
    /// </summary>
    public static class Hotkeys
    {
        private static readonly Dictionary<Key, bool> WasDown = new Dictionary<Key, bool>();
        private static bool _failed, _noKeyboardLogged;

        public static bool Pressed(Key key)
        {
            if (_failed) return false;
            try
            {
                var kb = Keyboard.current;
                if (kb == null)
                {
                    if (!_noKeyboardLogged) { _noKeyboardLogged = true; Core.Log.Msg("No keyboard device yet (Input System); hotkeys wait for one."); }
                    return false;
                }
                var down = kb[key].isPressed;
                var was = WasDown.TryGetValue(key, out var w) && w;
                WasDown[key] = down;
                return down && !was;
            }
            catch (Exception e)
            {
                _failed = true;
                Core.Log.Warning($"Keyboard unreadable through the Input System, hotkeys off for this session: {e.GetType().Name}: {e.Message}");
                return false;
            }
        }
    }
}
