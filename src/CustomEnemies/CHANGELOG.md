# CustomEnemies changelog

## 0.1.0 (2026-11-17)

Phase 1a of docs/CUSTOM-ENEMIES-PLAN.md. Untested in the headset.

- New mod. Draws a skin over an enemy on this client only; the enemy stays vanilla underneath
  (shadows-only renderers, which keep it animating). No gate, no network traffic.
- The only skin is a built-in placeholder made of primitives, on Death by default
  (`PlaceholderOn`). Rigid follow of the enemy's root, head on the enemy's head bone.
- Pool aware: built once per pooled enemy, reset on each spawn (`AI.InitializePooledObject`,
  `AI.OnRespawn`), destroyed with the enemy object.
- Keys: Numpad 1 placeholder on the nearest enemy, Numpad 2 all skins off/on, Numpad 3 reload.
