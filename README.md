# DoE-mod

Friends-only modding project for **Dungeons of Eternity** (Othergate, Unity 2022.3.62f2,
IL2CPP, Photon PUN 2). Goals: custom VRChat-style avatars in private lobbies, face/eye
tracking sync via VRCFaceTracking + OSC, and (stretch) custom inventory/loot.

- **[docs/PLAN.md](docs/PLAN.md)** — the project plan: phases, milestones, ground rules.
- **[docs/GAME-INTERNALS.md](docs/GAME-INTERNALS.md)** — decompilation findings: avatar
  pipeline, networking map, loot system, open questions.
- **dump/** — Il2CppDumper output (`dump.cs` is the searchable class dump; `DummyDll/` for
  IDE reference). Regenerate after game updates with `tools/run_dumper.bat`.
- **[docs/LOOT-OVERHAUL.md](docs/LOOT-OVERHAUL.md)** — feasibility investigation for a
  separate loot/inventory/shop mod built on the game's own weapon generator.
- **[docs/DUNGEON-DESCENT.md](docs/DUNGEON-DESCENT.md)** — feasibility investigation for a
  multi-floor "descent" dungeon built on the game's own generator, exit teleporter and
  custom-seed launch path (2026-09-07); `src/Descent/` 0.1.0 is the first build.
- **[docs/STAYPUTVR.md](docs/STAYPUTVR.md)** — design notes for `src/StayPutVR/`: taking damage
  in the dungeon fires a shock through the
  [StayPutVR](https://github.com/InconsolableCellist/StayPutVR) app over OSC, and biting another
  player fires theirs (2026-09-09). Where the damage hook is, why OSC Query has to be off, and the
  consent design behind biting.
- **[INSTALL.md](INSTALL.md)** — installing and (importantly) removing the mod. This is the
  page to hand to a friend; it assumes no knowledge of the project.
- **[src/README.md](src/README.md)** — the five mods with their versions, how to build any of
  them, and the handful of things that bite across all of them. Each mod's own page lives beside
  its code: [CustomAvatars](src/CustomAvatars/README.md),
  [LootOverhaul](src/LootOverhaul/README.md), [VisualCues](src/VisualCues/README.md),
  [Descent](src/Descent/README.md), [StayPutVR](src/StayPutVR/README.md).
- **tools/** — MelonLoader 0.7.3, Il2CppDumper, Cpp2IL, AssetRipper, UnityExplorer builds.

Game install: `C:\Games\Steam\steamapps\common\Dungeons of Eternity`

Ground rules (see PLAN.md for the full list): private lobbies only, hard-gated behind a
mod handshake; never write to PlayFab/UGS; no redistribution of extracted game assets.
