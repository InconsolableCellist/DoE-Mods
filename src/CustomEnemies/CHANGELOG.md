# CustomEnemies changelog

## 0.2.0 (2026-11-17)

Phases 1b and 1c of docs/CUSTOM-ENEMIES-PLAN.md, minus sounds. Untested in the game.

- **Bundle skins, made the way avatars are.** A skin is an ordinary avatar export
  (`<name>.avatar` + `<name>.manifest.json` from Tools ▸ Foxipso ▸ DoE Avatar Export) plus a
  `<name>.enemy.json` sidecar naming the enemy it replaces, all in `UserData/CustomEnemies/`.
  `AvatarBundle`, `AvatarManifest` and `PoseRetargeter` are CustomAvatars' own, compiled in as
  linked source (unchanged; `Shared/CustomAvatarsShim.cs` supplies the three things they use).
- **Pose copy from the enemy's humanoid Animator**, captured once the enemy is out of its spawn
  intro; the skin is scaled so its head sits at the enemy's head height.
- **Hanging legs** (the glide design): thighs forward, knees bent, a slow sway, trailing behind
  travel. **Held weapons**: a skin bone (a scythe) follows another (a hand). **Eye halos**:
  camera-facing additive glows on the eye bones, from the enemy's own additive material.
- **Lit like the enemy**: the skin's materials are copies of the enemy's body material carrying
  the skin's own base colour texture. **Dissolves with it**: while the enemy wears its dissolve
  shader, the skin's materials wear it too and copy its values.
- The enemy's particle, trail and line renderers (Death's aura and weapon trail) are switched
  off under a skin (`HideVanillaEffects`).
- **Session report** in `UserData/CustomEnemies/recon/`: each skin's build and capture, then
  head and hand offsets from the enemy, vanilla leaks, re-hides, dissolves and frame cost.
- The placeholder stays, for enemies listed in `PlaceholderOn` that have no skin file.
- Unity: `DoEEnemySkinBatch.cs` imports an FBX as a Humanoid (bones named as Unity's humanoid
  bones are pinned to those slots) and runs the avatar exporter on it.

## 0.1.0 (2026-11-17)

Phase 1a of docs/CUSTOM-ENEMIES-PLAN.md. Untested in the headset.

- New mod. Draws a skin over an enemy on this client only; the enemy stays vanilla underneath
  (shadows-only renderers, which keep it animating). No gate, no network traffic.
- The only skin is a built-in placeholder made of primitives, on Death by default
  (`PlaceholderOn`). Rigid follow of the enemy's root, head on the enemy's head bone.
- Pool aware: built once per pooled enemy, reset on each spawn (`AI.InitializePooledObject`,
  `AI.OnRespawn`), destroyed with the enemy object.
- Keys: Numpad 1 placeholder on the nearest enemy, Numpad 2 all skins off/on, Numpad 3 reload.
