# CustomAvatars mod

Version: {version}
Early Access

Author: Foxipso
    Linktr.ee: foxipso.com
    Gumroad: https://foxipso.gumroad.com/
    Jinxxy: https://jinxxy.com/foxipso
    X/Twitter: https://twitter.com/TheFoxipso
    Discord: foxipso
    Foxipso's Den Discord: https://discord.gg/P9ayEt2h3M

## Setup/Usage

1. Export your avatar

- Make a backup of your UnityProject
- Import DoE_Avatar Exporter.unitypackage
- Using the Hierarchy view, activate only the objects you want visible (avatar, accessories)
- Click on your avatar in the hierarchy to select it, then go to Tools->Foxipso->DoE Avatar Export, check all three options ("Strip Inactive Objects", "Allow Approximate Shape Aliases", "Apply VRCFury Armature Links") and then hit "Export Selected Avatar"
- The export directory should open (DoEExport/ in your project root). Make note of <avatarname>.avatar and <avatarname>.json, you'll need them in step 3. Also send these files (it's a copy of your avatar!) to your *trusted* friends with whom you'll play DoE

2. Install MelonLoader 0.7.3

- Download "MelonLoader.z64.zip" from here: https://github.com/LavaGang/MelonLoader/releases/tag/v0.7.3
- Extract the contents of the zip (version.dll and MelonLoader/) to `C:\Program Files\Steam\steamapps\common\Dungeons of Eternity` or similar (right click the game in Steam and browse to the local folder)
- Start Dungeons of Eternity, wait (MelonLoader may take a few minutes to generate the game assemblies) then quit

3. Install the mod

- Copy CustomAvatars.dll to the Mods/ folder (`Dungeons of Eternity/Mods/`)
- Copy your exported .avatar & .json files from step 1 to `Dungeons of Eternity/UserData/Avatars/`, creating those subfolders if they're not there (you can run the game once with the mod to create them)
- Do the same for the .avatar and .json files that your friends send you, and send your .avatar and .json files to all the *trusted* friends you'll play with

4. Launch the game. You should see a new 2D UI in the desktop window with instructions, as well as log output like: 
```
CustomAvatars 0.40.0 — ...
Mod DLL SHA-256: 8c52b45c7ad15621…
Avatar OK: `YourAvatar` — 51,823 verts / 1 mesh(es), ...
```

Your mod version must match what your friends are using.

Go to a private party. You should see in the log something like:
`*** ModGate ACTIVE — private room, all 2 peers on 0.29.0/8c52b45c7ad15621`

Refer to the UI or press F2 to cycle through avatars and then F4 to apply or unapply it. You'll also see it change on the in-game holograms/mannequins.


# Uninstalling

### Just turn the mod off, keep MelonLoader

Delete one file:

```
<game folder>\Mods\CustomAvatars.dll
```

That's it. The game runs normally the next time you start it.

### Remove everything

Delete these from the game folder:

```
version.dll
MelonLoader\          (folder)
Mods\                 (folder)
UserData\             (folder — this also removes your avatars and settings)
```

The game is now exactly as Steam installed it. Nothing else was touched: **no files outside the
game folder, no registry entries, no background services, nothing added to Windows startup.**

### If something goes wrong and the game won't start

Delete `version.dll` from the game folder. That alone disables MelonLoader completely, whatever
else is still sitting there.

## Controls

The game window has to have focus for these, so click on it first if you've been in the
headset.

| Key | What it does |
|---|---|
| **F2** | choose which avatar to wear, if you have more than one |
| **F4** | take your avatar off / put it back on |
| **F6** | spawn a copy of the avatar in front of you, to look at |
| **F3** | re-read the settings file, so you can adjust things without restarting |
| **F5** | look for newly added avatar files |
| **F7/F8/F9** | write technical details to a log file, for troubleshooting |
| **F10/F11** | full-body tracking on/off, and calibrate it |

## Settings

You can fine-tune things like bone placement and other settings in `UserData\MelonPreferences.cfg`

Press F3 after saving that file to load the settings.

**Can't see your rings?** If your avatar's fingers are thicker than normal, the ring can end up
inside the finger. Set `RingSize` in the `[CustomAvatars]` section to something like `1.5` or `2`
and press F3. It makes every ring you see on a custom avatar bigger, yours and your friends', and
only on your own screen, so each player can pick their own.

**Ring sunk into the finger, or floating above it?** Set `RingOffset` in the same section and press
F3. It lifts the ring toward the back of your hand, in finger radii: `1` raises it by one finger
radius, `-1` lowers it toward the palm. `RingSlide` moves it along the finger instead: `0.2` is a
fifth of the finger bone toward the fingertip, `-0.2` toward the hand. Both are only on your own
screen.

## Common log messages for troubleshooting 

**`Swap refused: room `x` is VISIBLE (public)`**
You're in a public lobby. The mod only runs in private parties, deliberately — including your
own avatar, which would otherwise be an exception that makes the rule meaningless.

**`ModGate INERT — 1 vanilla player(s) present`**
Someone in the party hasn't installed the mod. It stays off for everyone until they do, or
until they leave.

**`ModGate INERT — build skew across 2 peers`**
People are running different DLLs. Compare the SHA-256 line each of you sees at startup and
make sure everyone has the same file.

**`REFUSED `avatar`: bundle SHA-256 does not match its manifest`**
The `.avatar` file is damaged or paired with the wrong `.manifest.json`. Copy both again from
the same source.

**You and a friend are wearing the same avatar**
Neither of you has chosen one, so both picked the same file by default. Press **F2** until the
console names the one you want, then F4 twice.

**Your avatar doesn't go on by itself**
`AutoWear` is `false`, or you took it off with F4 earlier in the session — taking it off is
meant to stick. Press F4.

**Your friend looks like a normal character, not their avatar**
You don't have their avatar file. The console names it — copy that `.avatar` and
`.manifest.json` pair into your own Avatars folder and press F5. Both of you need both files.

**`No avatars found`**
The files aren't in `UserData\CustomAvatars\Avatars\`, or only one of the pair is there.

**Avatar stands in a T-pose and doesn't move with you**
`SwapUseVrik = false` in the settings file. Set it to `true` and press F3.

**Your old character is still visible inside the new one**
`SwapHideVanillaMesh = false`. Set it to `true` and press F3.

**Avatar is bright pink**
The avatar's shaders didn't survive being exported. Whoever exported it needs to re-export with
their shaders locked.

**Clothing floats in place, or doesn't follow the body**
The garment is skinned to its own armature and the VRCFury Armature Link that joins it to the
avatar wasn't applied. The exporter bakes those links now, so a re-export fixes it — the export
report lists each link it applied, and names any it refused.

**Clothing is tiny, bunched up at a joint (an elbow, the hips)**
The garment's armature is at a different scale from the avatar's — a rig exported at 100 onto
an avatar at 1 is common — and an older exporter collapsed it. Re-export; the report now says
`keeping a x100 factor on the merged bones` under that link.

