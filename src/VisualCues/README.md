# VisualCues 0.1.3

A mod for **Dungeons of Eternity** designed to assist Deaf/HoH users:

- **Calls.** Users can now double-click a thumbstick and it'll send out a networked ping or "call," which tells other users of the mod that the user wishes to summon them. Summoned users will see a HUD indication that goes away when you get close, and a directional marker.
- **Enemy noise.** Enemies in your vicinity that make a sound will now trigger a brief HUD indicator, pointing in their direction and estimating their distance. When you face the source of the sound it'll fade away.

The enemy noise feature works regardless of whether other users have the mod, but the call feature requires other users to have it. The mod doesn't affect your profile, character, progression, or save games.

## Install

Your game folder is wherever Steam put it, usually:

```
C:\Program Files\Steam\steamapps\common\Dungeons of Eternity
```

Find it by **right-clicking the game in Steam → Manage → Browse local files**.

1. **MelonLoader 0.7.3.** Download the x64 build and copy `version.dll` and the `MelonLoader`
   folder into the game folder, next to `DoE.exe`. Skip this if you already run
   CustomAvatars or LootOverhaul.
2. **Launch the game once, then quit.** The first launch takes several minutes while
   MelonLoader unpacks the game's code. It may look frozen. Wait for the menu.
3. **Copy `VisualCues.dll` into the `Mods` folder** inside the game folder. Create the
   folder if it isn't there.
4. **Launch.** The MelonLoader console should show:

   ```
   VisualCues 0.1.3 — stick-click call (event 160), unseen-enemy noise markers.
   Patches: 3 installed, 0 refused, 0 failed.
   ```

## Settings

`UserData\MelonPreferences.cfg` in the game folder, section `[VisualCues]`. Edit the file and
press **,** (comma) in the game to reload.

**Hearing player who only wants to call others:** set these three and you'll never see
anything from the mod.

```
NoiseEnabled = false
SummonShowIncoming = false
SummonSentFlash = false
```

Other settings:

| Setting | Default | What it does |
|---|---|---|
| `SummonStick` | `either` | `left`, `right` or `either`. |
| `SummonPress` | `double` | `double`, `single`, or `hold` (hold the stick in for 0.6 s). |
| `SummonDurationSeconds` | `4` | How long the caller's marker stays. |
| `NoiseRangeMeters` | `18` | Ignore enemies farther than this. |
| `NoiseDurationSeconds` | `2.5` | How long an arc stays after the last sound. |
| `HudScale` | `1.0` | Bigger or smaller markers and text. |
| `HudDistance` | `1.2` | How far in front of your eyes the markers float, in metres. |

The full list is in [DESIGN.md](DESIGN.md).

## Remove

Delete `Mods\VisualCues.dll`. To remove MelonLoader too, delete `version.dll` and the
`MelonLoader` folder from the game folder.

## If something's wrong

The console prints `[VisualCues]` lines. `MelonLoader\Latest.log` in the game folder keeps
them after you quit, and `UserData\VisualCues\logs\` has a fuller record of every call and
every enemy sound the mod saw.
