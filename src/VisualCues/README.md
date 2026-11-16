# VisualCues 0.1.7

A mod for **Dungeons of Eternity** designed to assist Deaf/HoH users:

- **Calls.** Users can now double-click a thumbstick and it'll send out a networked ping or "call," which tells other users of the mod that the user wishes to summon them. Summoned users will see a HUD indication that goes away when you get close, and a directional marker.
- **Enemy noise.** Enemies in your vicinity that make a sound will now trigger a brief HUD indicator, pointing in their direction and estimating their distance. When you face the source of the sound it'll fade away.
- **Gates and locks.** When a gate starts to open (a lever, the end of a soul-harvest round, a key) or a lock is unlocked, a yellow marker labelled GATE OPEN or UNLOCKED points to it.
- **Rune-chest cube slots.** When a cube goes into a slot on the crypt-gem chest, the slot gets a green tick labelled RIGHT or a red X labelled WRONG.

The enemy noise, gate and cube-slot features work regardless of whether other users have the mod, but the call feature requires other users to have it. The mod doesn't affect your profile, character, progression, or save games.

## What's new in 0.1.7

**The call goes only to players who have VisualCues.** Each copy of the mod now marks its player with a small Photon player property (`vc.v`, the version), and a call is sent only to the players who carry it. Before, the call was broadcast to everyone in the room, vanilla players in public lobbies included, which had never been tested. If nobody else in the room has the mod, the call is not sent and the flash says `Nobody to call`. 0.1.6 does not announce itself, so both players need 0.1.7 to call each other. The noise, gate and cube-slot markers are unchanged and still work in any room, whoever is in it.

## What's new in 0.1.6

Two new cues for sounds a deaf player misses:

- **A gate opens or a lock is unlocked.** A yellow marker on the gate or lock for 5 seconds (`GateDurationSeconds`), labelled `GATE OPEN` or `UNLOCKED`, with the distance: a circle around it when it is in view, an arc at the edge of the view pointing to it when it is not. This covers levers, the end of each soul-harvest round, skeleton keys and other keys, and anything else that opens a gate. Gates and locks over 40 m away (`GateRangeMeters`) are ignored, and so is anything in the first 12 seconds after a level loads, when the game is only syncing what is already open. Turn it off with `GateEnabled = false`.
- **A cube goes into a rune-chest slot.** The slot gets a green tick labelled `RIGHT` or a red X labelled `WRONG` for 3 seconds (`PuzzleDurationSeconds`); the slot that completes the chest says `RIGHT - SOLVED`. Right and wrong differ in shape and text as well as colour. Off-screen, it is an arc with the same label. Turn it off with `PuzzleEnabled = false`.

Both work for every player in the room, host or not, and neither sends anything over the network.

## What's new in 0.1.5

The enemy search behind the noise markers runs only when something will use it: not at all while the markers (and the sound log) are off, and not for sounds too far away to ever get a marker. `LogSounds` (the sound log, only a tool for tuning the markers) is now off by default; an existing config keeps its setting. The call to other players is unaffected.

Incoming network messages are checked by their number first, and anything that is not this mod's is ignored before its sender and contents are read. Every Photon event of the game passes through this hook, so this saves an interop call and an allocation per event.

The session log is now written to disk once a second instead of after every line. Before, each line was a synchronous disk write on the game's main thread, so a burst of lines (a fight, a dump) could cost frame time in the headset. If the game crashes hard, at most the last second of the log is missing.

## What's new in 0.1.4

For the 2026-09-27 game update. The update added a parameter to the game's positional-sound
call, so 0.1.3 could not hook it (`patch FAILED: AudioManager.PlaySoundAt`) and enemy noise came
only from footsteps and attack animations; 0.1.4 finds the call again, and will keep finding it
if more parameters are added. The keyboard keys (quote, period, comma) now read the keyboard the
way the updated game does, instead of throwing every frame.

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
   VisualCues 0.1.7 — stick-click call (event 160), unseen-enemy noise markers, gate and puzzle-slot markers.
   Patches: 6 installed, 0 refused, 0 failed.
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
| `GateEnabled` | `true` | Marker on a gate that opens or a lock that is unlocked. |
| `GateDurationSeconds` | `5` | How long the gate marker stays. |
| `PuzzleEnabled` | `true` | Tick or X on a rune-chest slot when a cube goes in. |
| `PuzzleDurationSeconds` | `3` | How long the slot marker stays. |
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
