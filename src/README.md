# The mods

Five independent MelonLoader mods for Dungeons of Eternity. They share only
`Directory.Build.props`; none of them references another's DLL.

| Mod | Version | What it is |
|---|---|---|
| [CustomAvatars](CustomAvatars/README.md) | 0.42.7 | VRChat-style avatars, face and eye tracking, full-body tracking. Gated to private lobbies where everyone runs an identical build. |
| [LootOverhaul](LootOverhaul/README.md) | 0.9.14 | Loot, a bag, shops and buffs built on the game's own weapon generator. Gated. |
| [VisualCues](VisualCues/README.md) | 0.1.3 | Accessibility cues for deaf players: a stick-click call arrow and unseen-enemy noise markers. No gate. |
| [Descent](Descent/README.md) | 0.1.0 | A sixteen-floor dungeon out of the game's own generator. Gated. |
| [StayPutVR](StayPutVR/README.md) | 0.3.0 | Taking damage, or being bitten by another player, fires a shock through the StayPutVR app over OSC. No gate. |

## One-time prerequisites

1. **MelonLoader 0.7.3 installed into the game folder** — `version.dll` + `MelonLoader/` next to
   `DoE.exe`. (Done: see `tools/MelonLoader.x64/`.)
2. **Launch the game once with MelonLoader present.** ML runs Cpp2IL + Il2CppInterop and writes
   `MelonLoader/Il2CppAssemblies/*.dll`. These builds reference those, so *nothing here compiles
   until that folder exists* — the projects fail with an explicit message rather than a wall of
   missing-type errors. First generation takes several minutes; watch `MelonLoader/Latest.log`.
3. **.NET SDK** capable of targeting `net6.0` (SDK 9 is fine).

## Build

```
cd src/<mod>
dotnet build
```

The build copies the DLL into `<GameDir>\Mods\` automatically. Pass `-p:NoDeploy=true` to skip
that. A build cannot overwrite a DLL while the game is running; it says so rather than failing.

**Game in a different folder?** Copy `src/Local.props.example` to `src/Local.props` and set
`<GameDir>`, or set `DOE_GAME_DIR`, or pass `-p:GameDir=...`. No csproj hardcodes a path.

## Things that bite, across all five

- **MelonLoader keeps every preference once written**, so changing a default in code never reaches
  a machine that ran an older build. When one player behaves differently, diff their
  `MelonPreferences.cfg` against the `CreateEntry` defaults first.
- **The game rewrites `MelonPreferences.cfg` on quit** from what it holds in memory, so editing the
  file while it runs is silently reverted. Edit with the game closed.
- **IL2CPP folds identical method bodies**, so every empty method shares one native address
  (`0x35FC20` in `dump.cs`). Patching one patches thousands and the process dies. Each mod's
  `Hooks.cs` checks a target's native pointer against a known empty method before patching.
- **Photon event codes** are divided up: CustomAvatars 140–149, LootOverhaul 150–159, VisualCues
  160–169, Descent 170–179, StayPutVR 180–189. The game itself was seen on 1, 2, 50 and 70, and PUN
  reserves 200 and up.
- **Desktop keys need the game window focused**, which in VR means clicking it once. Never bind
  Pause or Break: MelonLoader opens a console, and Break in a Windows console kills the process.
