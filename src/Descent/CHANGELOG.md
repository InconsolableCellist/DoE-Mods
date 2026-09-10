# Descent changelog

## 0.1.0 — 2026-09-07 (untested)

First build. A run is a seed; sixteen floors in seed-shuffled realm bands, tier rising every
two floors from tier 1, difficulty / length / hazard level stepping behind it, boss battle on
the last floor. The hub board (or Backspace) validates the floor's layout with the game's own
pass, arms a countdown, and launches through `GameManager.LoadDungeon`. A Harmony prefix on
`GameManager.ReturnToLobby` turns the exit teleporter into a descent: bank the floor through
the game's own end-of-mission code (`SetRewardStats` → `SaveLoot` → counters reset), advance
the run, load the next floor. Failure and forfeit pass through untouched and keep the floor.
Run state in `UserData/Descent/runs.json` and the room property `dd.run`; events 170–179.
Dev hotkeys Backspace / End / Slash. Gate and transport carried over from LootOverhaul.
