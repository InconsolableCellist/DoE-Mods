# Descent 0.1.0 — a sixteen-floor dungeon out of the game's own generator

A fourth, independent MelonLoader mod. The investigation and the design decisions are in
[docs/DUNGEON-DESCENT.md](../../docs/DUNGEON-DESCENT.md); this page is what the build does and
how to test it.

## What it is

A **run** is a seed and a name. Each **floor** is a vanilla-generated dungeon — the game's
own `DungeonBuilder` on the game's own mission object — launched through the game's own
launch coroutine, in a realm band (four floors per realm, order shuffled by the seed), at a
tier that rises every two floors from tier 1, with the difficulty setting, mission length and
hazard level stepping up behind it and a boss battle on floor 16.

- **The exit goes down.** Everyone on the pads → the game's `MissionSuccess` fires as usual
  → its `ReturnToLobby` is caught by the mod → the floor is banked, the run advanced, the
  next floor loaded. On floor 16 the exit is the normal way home, tally screen and all.
- **Rewards are real, every floor.** Before the next load the mod runs the game's own
  end-of-mission code in the game's own order: `SetRewardStats` (party stats),
  `SaveLoot` (XP and gold onto the profile, level-ups, achievements, the PlayFab save,
  leaderboards), then the per-mission counters reset. Nothing is invented; the game settles
  up at a point it otherwise would not.
- **A wipe or a quit keeps the floor.** Failure and forfeit go through the vanilla return
  untouched; the run stays at that floor and the hub board resumes it there. Mobs and chests
  respawn on a resumed floor; progress inside a floor is not kept.
- **Anyone can start.** The board arms a countdown (10 s), peers see it and can cancel; the
  host's client performs the load, and the room follows as it does for any dungeon.
- **Late joiners** read the floor from the room property `dd.run`; a new host reads the same
  property, so the run survives a host switch.

Gate: private room, every occupant on the same `Descent.dll` (`dd.ver` / `dd.sha` player
properties), own checksum passed. Solo in a private room passes. Event codes 170–179.

## Where things are

- The board: beside LootOverhaul's kobold in the hub by default; press **/** (slash) in the
  lobby to put it 1.5 m in front of you and remember the spot.
- Runs: `UserData/Descent/runs.json`. Recon transcript: `UserData/Descent/recon/recon-*.md`.
- Settings: `UserData/MelonPreferences.cfg`, `[Descent]` and `[Descent_Dev]`.

## Hotkeys (game window focused)

| Key | In the hub | In a floor |
|---|---|---|
| **Backspace** | new descent, right now (no board needed) | force the exit: `MissionSuccess` + `ReturnToLobby`, i.e. descend without walking to the pads (host) |
| **End** | resume the most recent unfinished run | surface: forfeit and return, run kept at this floor (host) |
| **/** | place the board where you stand | — |

## First session (solo, private room) — 0.1.0 has never run in a headset

The point of the session is the transcript. Play it in this order and quit normally (the
counters are written on quit).

1. **Hub.** Wait for `*** ModGate ACTIVE`. The board should be beside the kobold; if it is
   not, press **/** once and look in front of you. Either way, **Backspace** starts a run.
   Expect: a toast naming the run, "checking the floor…" for a few seconds (the game's own
   layout pass on the seed), then a 10-second countdown, then the load.
2. **Floor 1.** Look at the exit pads: they should read `DESCEND TO FLOOR 2`. Kill a few
   things, open a chest, then either walk to the exit or press **Backspace**.
   Expect: success music, a toast `Floor 1 banked: +N XP`, a toast `Descending to floor 2`,
   the avatar dissolve, a load, and a new dungeon in a different band or the same one.
3. **Floor 2.** Check three things: enemies spawn and fight (the stale-end-state reset
   worked), you can be revived / respawn as usual, and the tier feels right. Press
   **Backspace** again to reach floor 3, then **End** to surface.
   Expect: the vanilla forfeit return; in the hub the board says "floor 3 of 16"; the XP bar
   already includes floors 1 and 2.
4. **Resume.** Press **End** in the hub (or RESUME on the board). Expect floor 3, same
   layout as before (same seed), mobs back.
5. Optional: `ObserveOnly=true` in `[Descent_Dev]` turns the mod into a pure watcher for a
   vanilla dungeon, to compare transcripts.

What to grep afterwards, in `MelonLoader/Latest.log` and the recon file:

- `Harmony patches` section: `GameManager.ReturnToLobby`, `GameManager.MissionSuccess` and
  `DungeonBuilder.InitBuilder` must be listed `ok:` (a `REFUSED` means a shared address).
- `validate:` — the layout pass result and the dungeon's room count / XP / gold bonus.
- `Launching floor` and either `GameManager.LoadDungeon called` (vanilla launch coroutine)
  or `manual launch:` (the delegate could not be converted; the mod's copy ran).
- `Floor generated:` — rooms, hazards, tier, and the room list.
- `InitBuilder:` — the floor length override applied on the host's generation.
- `Banking floor` section — profile XP before/after and the party stats line; `Banked floor`.
- `resetting stale MissionEndState` — whether the end-state reset was needed.
- `SCENE Initialized twice` in `Player.log` — would mean the same-scene reload tripped the
  game's guard.

## Known gaps in 0.1.0

- Untested in a headset; every step is logged for that reason.
- The last floor's rewards go through the vanilla hub path (they are banked, just later).
- No cleared-room memory on resume; no quest objectives yet (the run record has the slot).
- Longer/branchier floors: the seam is in place (`InitBuilderHook`), the knobs are not
  turned yet.
- The board is a panel; the doorway with a staircase comes later.
- LootOverhaul's own loot save path (`GetEarnedLoot`) has not been checked against per-floor
  banking with both mods on.
