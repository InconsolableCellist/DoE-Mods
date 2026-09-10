- [DoE-mod](#doe-mod)
  - [Quickstart](#quickstart)
  - [The mods](#the-mods)
    - [CustomAvatars](#customavatars)
    - [LootOverhaul](#lootoverhaul)
    - [VisualCues](#visualcues)
    - [Descent](#descent)
    - [StayPutVR](#stayputvr)
  - [Ground rules](#ground-rules)
  - [Building from source](#building-from-source)
  - [Documentation](#documentation)
  - [Support](#support)
  - [License](#license)
  - [Acknowledgments](#acknowledgments)

# DoE-mod

Mods for **Dungeons of Eternity**, the co-op VR dungeon crawler by Othergate. There are five of
them, each its own MelonLoader DLL, and you can run any one without the others.

The big one is **CustomAvatars**: wear your VRChat avatar in the dungeon, with your face and eyes
driven by VRCFaceTracking and your legs by SteamVR trackers, and have your friends see all of it.
Around it grew a loot and shop system, a set of visual cues for deaf and hard-of-hearing players,
a sixteen-floor descent mode, and a bridge to the [StayPutVR](https://github.com/InconsolableCellist/StayPutVR)
app so the dungeon can shock you.

The mods that change what other people see or what the game hands out only work in a **private
lobby where everyone runs the same build**. In a public lobby, or with one unmodded player
present, they switch themselves off. Nothing here writes to your profile, progression or saves,
and there is no server: files are passed around by hand.

This was written by reading the game's decompiled code and watching what it does, and it is
tested by a handful of friends in a headset. Expect rough edges, and read the console when
something looks wrong. It usually says why.

## Quickstart

Your game folder is wherever Steam put it, usually
`C:\Program Files (x86)\Steam\steamapps\common\Dungeons of Eternity`. In Steam:
**right-click the game → Manage → Browse local files**.

1. **Install MelonLoader 0.7.3.** Download the x64 build and copy `version.dll` and the
   `MelonLoader` folder into the game folder, next to `DoE.exe`.
2. **Launch the game once, then quit.** The first launch takes several minutes while MelonLoader
   unpacks the game's code so mods can talk to it. It may look frozen. Wait for the menu.
3. **Drop the mod DLL into `Mods`** inside the game folder. Create the folder if it isn't there.
   Any combination of the five is fine.
4. **Launch.** A console window opens alongside the game and each mod prints a line naming
   itself and its version.

**Settings** live in `UserData\MelonPreferences.cfg` in the game folder, one section per mod
(`[CustomAvatars]`, `[LootOverhaul]`, `[VisualCues]`, `[Descent]`, `[StayPutVR]`). The file
appears after the first run with a mod installed, and every setting has a comment above it.
Edit it with the game closed, or use the mod's reload key: the game writes its own copy back
on quit and will undo an edit it never loaded.

**Logs** are in `MelonLoader\Latest.log` in the game folder. That is the file to send when
something breaks.

**Removing a mod** is deleting its DLL from `Mods`. Deleting `version.dll` disables MelonLoader
entirely, whatever else is still there. Nothing is installed outside the game folder.

Friends who have never modded a game before should be handed [INSTALL.md](INSTALL.md), which
walks through all of this, with what the console should say at each step.

## The mods

| Mod | Version | Needs everyone modded? |
|---|---|---|
| [CustomAvatars](src/CustomAvatars/README.md) | 0.42.7 | Yes |
| [LootOverhaul](src/LootOverhaul/README.md) | 0.9.14 | Yes |
| [VisualCues](src/VisualCues/README.md) | 0.1.3 | No |
| [Descent](src/Descent/README.md) | 0.1.0 | Yes |
| [StayPutVR](src/StayPutVR/README.md) | 0.3.0 | No |

### CustomAvatars

Wear a VRChat-style avatar in the dungeon, and let your friends see it.

Your avatar is driven alongside the game's own body rather than instead of it, so the game's
animation, IK, weapons, hitboxes and networking carry on underneath, untouched. The avatar is
fitted once when you put it on, head at your eyes and feet on the floor, and from there:

- **Face and eye tracking** from [VRCFaceTracking](https://docs.vrcft.io/) over OSC. The mod
  stands in for VRChat on the socket, takes the full Unified Expressions set, and forwards what
  changed to the other players at a steady rate.
- **Full-body tracking** from SteamVR trackers, with a T-pose calibration, synced to peers.
- **Hand poses** from trigger and grip, and **spring bones** rebuilt from the avatar's PhysBone
  setup, so tails and ears swing.
- **Sizing.** Be bigger or smaller with PageUp and PageDown. Everything scales together, your
  friends see it, and Home puts the game back exactly as it was.

Avatars come out of a Unity exporter in [unity/AvatarExport](unity/AvatarExport) that turns a
VRChat project into an `.avatar` bundle and a `.manifest.json` beside it. The manifest carries a
hash of the bundle, and a pair that doesn't match is refused rather than loaded.

Everything another player can see waits on the gate: private room, every occupant on the same
version and the same DLL hash, roster matching the live player count. Your own avatar goes on in
any private room with no unmodded player in it, because there is nobody there to affect.

[Read more](src/CustomAvatars/README.md), or the [install guide](INSTALL.md) for friends.

### LootOverhaul

Loot, a bag, shops and buffs, built on the game's own weapon generator.

Enemies drop weapons and armor with rarities, rolled stats and a matching outline glow. Bosses
and mini-bosses drop a pile, scaled to the party. A kobold in the hub buys what you don't want,
with a sell-all by rarity, and the armory lets you salvage for tokens. Armor and tonics apply
their stats once, on top of the game's, and every "take less damage" stat actually means less.
Everything is generated by the game's code, so the weapons are real weapons.

[Read more](src/LootOverhaul/README.md).

### VisualCues

Cues for deaf and hard-of-hearing players. This one works on its own, with no gate.

- **Enemy noise.** When an enemy near you makes a sound, a marker on your HUD points at it and
  estimates the distance. Face it and the marker fades. This works whether or not anyone else
  has the mod.
- **Calls.** Double-click a thumbstick to summon your party. Other players with the mod see a
  marker and a direction, and it goes away as they reach you.

Hearing players who only want to call the others can turn every marker off in the settings.

[Read more](src/VisualCues/README.md).

### Descent

A sixteen-floor dungeon out of the game's own generator.

A run is a seed and a name. Each floor is a vanilla dungeon, launched the way the game launches
any dungeon, with the tier, difficulty, length and hazards stepping up as you go and a boss on
the last floor. The exit pads take you down instead of home. Rewards are real on every floor,
banked through the game's own end-of-mission code before the next load, so XP and gold land on
your profile as you go. A wipe or a quit keeps the floor, and a board in the hub resumes the run.

Version 0.1.0 has been built and read through carefully but not yet played through in a
headset.

[Read more](src/Descent/README.md).

### StayPutVR

Take damage in the dungeon and your shock device fires, through the
[StayPutVR](https://github.com/InconsolableCellist/StayPutVR) app over OSC. Bite another player
and theirs does.

Arm and disarm by clicking both thumbsticks together. A panel on the desktop window shows what
is armed, what fired, and what was held back and why, and turns amber if the link to the app is
dead. Intensity and duration live in the app; the mod only decides whether to fire.

Biting needs CustomAvatars with face tracking running, since that is where the jaw comes from,
and it only ever lands on a player who has switched on the consent setting themselves.

[Read more](src/StayPutVR/README.md) and the [design notes](docs/STAYPUTVR.md).

## Ground rules

- **Private lobbies only.** Anything that touches other players is hard-gated behind a handshake
  that checks the room is private and every occupant is on the identical build.
- **Never write to the player's account.** No PlayFab, no UGS. Progression only moves when the
  game's own code moves it.
- **No redistribution of game assets.** Decompilation output and extracted assets stay out of
  the repo.

The full list is in [docs/PLAN.md](docs/PLAN.md).

## Building from source

Each mod is a `dotnet build` away, once MelonLoader has run once in the game folder so its
generated assemblies exist:

```
cd src/<mod>
dotnet build
```

The build copies the DLL into the game's `Mods` folder for you. [src/README.md](src/README.md)
covers the prerequisites, pointing the build at a game in a different folder, and the handful
of things that bite across all five mods.

One thing to know before building CustomAvatars, LootOverhaul or Descent for a group: the gate
compares a hash of the DLL between players, so **one person builds and everyone else copies
that file**. Two people who each built their own copy can end up with different hashes and the
mod will refuse to switch on.

## Documentation

- [docs/PLAN.md](docs/PLAN.md), the project plan: phases, milestones, ground rules.
- [docs/GAME-INTERNALS.md](docs/GAME-INTERNALS.md), what the decompilation turned up: the
  avatar pipeline, the networking map, the loot system, open questions.
- [docs/FACE-TRACKING.md](docs/FACE-TRACKING.md), how face tracking gets from VRCFaceTracking
  to a friend's screen.
- [docs/LOOT-OVERHAUL.md](docs/LOOT-OVERHAUL.md), [docs/DUNGEON-DESCENT.md](docs/DUNGEON-DESCENT.md)
  and [docs/STAYPUTVR.md](docs/STAYPUTVR.md), the investigation and design behind each of
  those mods.
- [CHANGELOG.md](CHANGELOG.md), the CustomAvatars version history.
- `dump/` holds the Il2CppDumper output and `tools/` the MelonLoader, dumper and inspection
  builds used to produce it. Regenerate after a game update with `tools/run_dumper.bat`.

## Support

If these mods are useful to you, you can support my work on Patreon:
**[patreon.com/c/foxipso](https://www.patreon.com/c/foxipso)**.

## License

This project is licensed under the Apache 2.0 License. See [LICENSE](LICENSE) for details.

## Acknowledgments

- [MelonLoader](https://github.com/LavaGang/MelonLoader), [Il2CppDumper](https://github.com/Perfare/Il2CppDumper),
  [Cpp2IL](https://github.com/SamboyCoding/Cpp2IL), [AssetRipper](https://github.com/AssetRipper/AssetRipper)
  and [UnityExplorer](https://github.com/sinai-dev/UnityExplorer),
  without which none of this could have been read, let alone written.
- [VRCFaceTracking](https://github.com/benaclejames/VRCFaceTracking) for the Unified
  Expressions standard and an OSC pipeline that was easy to stand in for.
- Othergate, for a game worth this much effort.
- The friends who put on a headset and tested every build.
