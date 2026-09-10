# CustomAvatars 0.42.7

Wear a VRChat-style avatar in Dungeons of Eternity, in a private lobby with friends who run the
same build. Face and eye tracking come from VRCFaceTracking over OSC, full-body tracking from
SteamVR trackers, and both are synced to the other modded players.

The design notes are in [docs/PLAN.md](../../docs/PLAN.md) and
[docs/GAME-INTERNALS.md](../../docs/GAME-INTERNALS.md). How to build every mod in this folder is in
[../README.md](../README.md).

## What it does

- **Avatar swap.** Your VRChat rig is driven in parallel with the game's own body, so the game's
  animation, IK and networking keep working underneath. Bundles are SHA-256'd against their
  manifest and a mismatch is refused rather than loaded.
- **Peers see it too**, when every player in the room runs an identical DLL.
- **Face and eyes** from VRCFaceTracking, on a socket that stands in for VRChat, forwarded to peers
  at a fixed 10 Hz and only when a shape actually moved.
- **Full-body tracking** from SteamVR pucks, with a T-pose calibration, and synced to peers.
- **Hand poses** from trigger and grip, and **spring bones** for secondary motion, rebuilt from the
  captured PhysBone setup.
- **Player sizing** — your avatar's real height, applied after spawn.

## The gate

Nothing that other players can see happens unless the gate is open: the room is private
(`IsVisible == false`), every occupant advertises the same version *and* the same DLL hash, the live
player count matches the roster, and the self-checksum passed. Watch for `*** ModGate ACTIVE` or
`*** ModGate INERT` with the reason attached.

Your own avatar is drawn on a weaker condition — a private room with no vanilla player — because
in a solo room there is nobody to affect.

## Install

1. **MelonLoader 0.7.3** next to `DoE.exe`, then launch once and quit so it can unpack the game's
   code. That first launch takes several minutes and may look frozen.
2. **Copy `CustomAvatars.dll` into `Mods`.**
3. **Copy both exporter files** into `UserData\CustomAvatars\Avatars\`:

   ```
   <name>.avatar
   <name>.manifest.json
   ```

   Set `Avatar` in `MelonPreferences.cfg` to pick between several, or leave it empty for the first.

## Keys

The desktop panel in the top left lists these, and it is the thing to trust if this page and the
build ever disagree. Keys need the game window focused, which in VR means clicking it once.

| Key | | Key | |
|---|---|---|---|
| F1 | hide the panel | F2 | next avatar |
| F3 | reload settings | F4 | wear avatar on/off |
| F5 | rescan avatars | F6 | preview in front of you |
| F7 | dump environment and face params | F8 | dump avatars |
| F9 | dump room and Photon events | F10 | full-body tracking on/off |
| F11 | calibrate full-body (T-pose) | PgUp / PgDn | bigger / smaller |
| Home | normal size | | |

## Settings

`UserData\MelonPreferences.cfg`, section `[CustomAvatars]`, with diagnostics in
`[CustomAvatars_Dev]`. There are a lot of them, most for tuning a rig. Press F3 to reload without
restarting; spring constants apply live.

The ones you are most likely to touch: `Avatar`, `AutoWear`, `AvatarSize`, `FbtEnabled`,
`FaceOscEnabled` and `FaceOscListenPort`, `SwapHideVanillaMesh`, `SwapHideFpsArms`, `LegStretch`
and `ArmStretch`.

**MelonLoader keeps every setting once written**, so a changed default never reaches a machine that
ran an older build. If one player behaves differently from the others, diff their file against the
defaults before suspecting the rig.

## Layout

```
src/CustomAvatars/
├── Core.cs        MelonMod entry, keys, wiring
├── Overlay.cs     the desktop status panel
├── ModConfig.cs   MelonPreferences
├── Gate/          who is in the room, and whether anything is allowed to happen
├── Net/           the single inbound Photon patch, and the only place bytes leave
├── Avatars/       bundle loading, the swap, retargeting, arm and leg IK, sizing, springs
├── Face/          the VRCFaceTracking socket, blendshape driving, peer sync
├── Fbt/           tracker reading, calibration, the full-body rig, peer sync
└── Recon/         the transcript writer and every dump that feeds docs/GAME-INTERNALS.md
```

Transcripts land in `UserData\CustomAvatars\recon\`, and `MelonLoader\Latest.log` keeps the console
lines after you quit.
