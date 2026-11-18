# CustomEnemies 0.1.0

Draws a different model over an enemy, **on your screen only**. The enemy itself stays vanilla:
its movement, attacks, hitboxes, stats, AI and network traffic are untouched, and only its
renderers are switched to shadows-only so it keeps animating underneath. No gate and nothing
sent over the network, so it works in public lobbies and next to players without the mod.

Status: **Phase 1a of docs/CUSTOM-ENEMIES-PLAN.md, a test build.** The only skin is a built-in
placeholder made of primitives (a robed, hooded dummy with red eyes); it rides the enemy rigidly
and its head follows the enemy's head bone. Loading real skins from bundles, pose copy, a
weapon, dangling legs and the look come in Phases 1b and 1c. Not in the release yet.

## Settings

`UserData/MelonPreferences.cfg`, `[CustomEnemies]`:

| Setting | Default | What |
|---|---|---|
| `Enabled` | true | Off: the mod does nothing. |
| `PlaceholderOn` | `Death` | Enemies that get the placeholder, by name or Sauron ID, comma separated. Empty = none. |
| `HideVanilla` | true | Hide the vanilla body under a skin. Off draws both, for lining a skin up. |
| `HeadFollowsBone` | true | The placeholder's head follows the enemy's head bone. Off: fully rigid. |
| `VerboseLogging` | true | Log every skin attached, reset and removed. |

## Keys (game window focused)

| Key | What |
|---|---|
| Numpad 1 | Placeholder on or off for the enemy nearest to you (any enemy, listed or not) |
| Numpad 2 | Every skin off (vanilla everywhere), or back on |
| Numpad 3 | Reload the `[CustomEnemies]` settings |

## How it works

- A postfix on `AI.InitializePooledObject` and `AI.OnRespawn` catches each enemy as the pool
  hands it out; a once-a-second scan of `Sauron.ActiveAIList` catches anything else.
- Enemies are pooled, so a skin is built once per pooled enemy, reset on each spawn, and
  destroyed with the enemy object. Removing the DLL leaves the game vanilla.
- The skin is a scene root that follows the enemy in LateUpdate, never a child of it: the
  enemy's own code walks its hierarchy for renderers and must not find ours.
- The placeholder's materials are tinted copies of the enemy's own body material, so it is lit
  the way the enemy is; its eyes use the enemy's own eye material when there is one.
