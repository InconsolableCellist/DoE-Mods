- [DoE-mod](#doe-mod)
  - [Quickstart](#quickstart)
  - [The mods](#the-mods)
  - [Ground rules](#ground-rules)
  - [Building from source](#building-from-source)
  - [Documentation](#documentation)
  - [Support](#support)
  - [License](#license)

# DoE-mod

Mods for **Dungeons of Eternity**, the co-op VR dungeon crawler by Othergate. 

**CustomAvatars** is the main mod, which allows you to export and use your VRChat avatar in the game. It works with eye and face tracking via VRCFaceTracking and Unified Expressions, full body tracking (which is more experimental) as well as full finger tracking/gestures for sign language support. It's fully networked, so everyone in your private lobby will see your avatar, and vice versa. For it to work, everyone in the lobby needs to have the same mod version and copies of each others' avatars.

**VisualCues** adds features for deaf/HoH users: a visual indicator of direction and distance for any monster noise that's occurring near the player, and a call/ping system. Users with the mod can double-depress the right joystick to send a Call, and other users will see a visual indication that that user is pinging them.

**LootOverhaul** adds an expanded loot system, with item drops ranging from trinkets to the occasional weapon, and a new Shopkeeper in the hub where you can sell and buy your loot in exchange for Tokens, which operate independently of the game's gold system. Weapons also have enchantment slots, and you can wear armor for small buffs.

**StayPutVR** integrates the game to [StayPutVR](https://github.com/InconsolableCellist/StayPutVR), allowing you to get shocked when you take damage. It also integrates with CustomAvatars and allows users to bite each other, just like with StayPutVR and [VRCBiteTech](https://jinxxy.com/Sacred/VRCBiteTech)

**Descent** adds a 16-level dungeon that gets progressively harder the deeper you go, with the ability to resume it from the hub.

## Quickstart

Find your game folder under Steam, usually `C:\Program Files (x86)\Steam\steamapps\common\Dungeons of Eternity`. In Steam: **right-click the game → Manage → Browse local files**.

1. **Install MelonLoader 0.7.3.** Download the x64 build and copy `version.dll` and the `MelonLoader` folder into the game folder, next to `DoE.exe`.
2. **Launch the game once, then quit.** The first launch takes several minutes while MelonLoader unpacks the game's code so mods can talk to it. It may look frozen. Wait for the menu.
3. **Drop the mod DLL into `Mods`** inside the game folder. Create the folder if it isn't there. Any combination of the five is fine.
4. **Launch.** A console window opens alongside the game and each mod prints a line naming itself and its version.

**Settings** are in `UserData\MelonPreferences.cfg` in the game folder, one section per mod (`[CustomAvatars]`, `[LootOverhaul]`, `[VisualCues]`, `[Descent]`, `[StayPutVR]`). The file appears after the first run with a mod installed, and most settings have a comment above it. Edit it with the game closed, or use the mod's reload key: the game writes its own copy back on quit and will undo an edit it never loaded.

**Logs** are in `MelonLoader\Latest.log` in the game folder. That is the file to send when something breaks.

**Removing a mod** is deleting its DLL from `Mods`. Deleting `version.dll` disables MelonLoader entirely, whatever else is still there. Nothing is installed outside the game folder.

The [INSTALL.md](INSTALL.md) walks through the steps in more detail.

## The mods

| Mod | Version | Needs everyone modded? |
|---|---|---|
| [CustomAvatars](src/CustomAvatars/README.md) | 0.42.7 | Yes |
| [LootOverhaul](src/LootOverhaul/README.md) | 0.9.14 | Yes |
| [VisualCues](src/VisualCues/README.md) | 0.1.3 | No |
| [Descent](src/Descent/README.md) | 0.1.0 | Yes |
| [StayPutVR](src/StayPutVR/README.md) | 0.3.0 | No |

## Restrictions

- **Private lobbies only.** Anything that touches other players is protected by a handshake that checks the room is private and every occupant is on the same build.
- **Never write to the player's account.** Doesn't write to the PlayFab
- **No redistribution of game assets.** Decompilation output and extracted assets stay out of the repo.

The full list and design decisions are in [docs/PLAN.md](docs/PLAN.md).

## Building from source

Build with `dotnet build`, once MelonLoader has run once in the game folder so its generated assemblies exist:

```
cd src/<mod>
dotnet build
```

The build copies the DLL into the game's `Mods` folder for you. [src/README.md](src/README.md) covers the prerequisites, pointing the build at a game in a different folder, and the handful of things that bite across all five mods.

## Documentation

- [docs/PLAN.md](docs/PLAN.md), the project plan: phases, milestones, ground rules.
- [docs/GAME-INTERNALS.md](docs/GAME-INTERNALS.md), what the decompilation turned up: the avatar pipeline, the networking map, the loot system, open questions.
- [docs/FACE-TRACKING.md](docs/FACE-TRACKING.md), how face tracking gets from VRCFaceTracking to a friend's screen.
- [docs/LOOT-OVERHAUL.md](docs/LOOT-OVERHAUL.md), [docs/DUNGEON-DESCENT.md](docs/DUNGEON-DESCENT.md) and [docs/STAYPUTVR.md](docs/STAYPUTVR.md), the investigation and design behind each of those mods.
- [CHANGELOG.md](CHANGELOG.md), the CustomAvatars version history.
- `dump/` holds the Il2CppDumper output and `tools/` the MelonLoader, dumper and inspection builds used to produce it. Regenerate after a game update with `tools/run_dumper.bat`.

## Support

If these mods are useful to you, you can support my work on Patreon: **[patreon.com/c/foxipso](https://www.patreon.com/c/foxipso)**.

## License

This project is licensed under the Apache 2.0 License. See [LICENSE](LICENSE) for details.

