# CustomEnemies 0.2.0

Draws a different model over an enemy, **on your screen only**. The enemy itself stays vanilla:
its movement, attacks, hitboxes, stats, AI and network traffic are untouched, and only its
renderers are switched to shadows-only so it keeps animating underneath. No gate and nothing
sent over the network, so it works in public lobbies and next to players without the mod.

Status: **test build, not in the release yet** (docs/CUSTOM-ENEMIES-PLAN.md, Phases 1a to 1c).

## Skins

A skin is three files in `UserData/CustomEnemies/`:

- `<name>.avatar` and `<name>.manifest.json`: an ordinary export from the avatar exporter
  (`unity/AvatarExport`, Tools ▸ Foxipso ▸ DoE Avatar Export). The model must be a Humanoid.
  `DoEEnemySkinBatch.cs` (Tools ▸ Foxipso ▸ DoE Avatar Export ▸ Import Selected FBX As Enemy
  Skin) imports an FBX whose bones carry Unity's humanoid names and exports it in one step.
- `<name>.enemy.json`: which enemy it replaces and how it sits on it.

```json
{
  "replaces": ["Death"],
  "credit": "who made the model, and its licence",
  "scale": 1.0, "yaw": 0, "offset": [0, 0, 0],
  "gameShader": true, "keepMaterials": [],
  "eyes": ["LeftEye", "RightEye"], "haloSize": 0.10, "haloColour": [1.0, 0.12, 0.04, 1.0],
  "weapons": [{ "bone": "Scythe1.R", "to": "HandItem.R", "euler": [0, 0, 0] }],
  "legs": { "thighPitch": 50, "kneeBend": 70, "sway": 6, "swayPeriod": 2.6, "lag": 18 }
}
```

The skin copies the enemy's pose (its Animator must be humanoid, as Death's is), is scaled so
its head sits at the enemy's head height, and is drawn with the enemy's own shader so it is lit
the same. `legs` hangs the legs instead of copying them; leave it out to copy them. No model
files are shipped with the mod or kept in this repo. An enemy listed in `PlaceholderOn` with no
skin file gets the built-in placeholder (a dummy made of primitives).

## Settings

`UserData/MelonPreferences.cfg`, `[CustomEnemies]`:

| Setting | Default | What |
|---|---|---|
| `Enabled` | true | Off: the mod does nothing. |
| `PlaceholderOn` | `Death` | Enemies that get the placeholder, by name or Sauron ID, comma separated. Empty = none. |
| `HideVanilla` | true | Hide the vanilla body under a skin. Off draws both, for lining a skin up. |
| `HeadFollowsBone` | true | The placeholder's head follows the enemy's head bone. Off: fully rigid. |
| `HideVanillaEffects` | true | Switch off the enemy's own effects (Death's aura and weapon trail) under a skin. |
| `VerboseLogging` | true | Log every skin attached, reset and removed. |
| `RetargetHipsFollow` | 1 | How much of the enemy's hip movement the skin's hips copy. |
| `RetargetAlignAtCapture` | true | Line the skin's limbs up with the enemy's when the pose copy starts. |

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
