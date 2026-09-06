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
- **[INSTALL.md](INSTALL.md)** — installing and (importantly) removing the mod. This is the
  page to hand to a friend; it assumes no knowledge of the project.
- **[src/README.md](src/README.md)** — the mods themselves: `src/CustomAvatars/`, the
  independent `src/LootOverhaul/`, and `src/VisualCues/` (deaf-accessibility cues: a
  stick-click call arrow and unseen-enemy noise markers; no gate); how to build them.
- **tools/** — MelonLoader 0.7.3, Il2CppDumper, Cpp2IL, AssetRipper, UnityExplorer builds.

Game install: `C:\Games\Steam\steamapps\common\Dungeons of Eternity`

Ground rules (see PLAN.md for the full list): private lobbies only, hard-gated behind a
mod handshake; never write to PlayFab/UGS; no redistribution of extracted game assets.
