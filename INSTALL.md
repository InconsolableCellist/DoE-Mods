# Installing and removing CustomAvatars

A friends-only cosmetic mod for **Dungeons of Eternity**. It puts custom avatars on the people
in your party. It only does anything in a **private lobby where everyone is running the same
build** — in a public lobby, or with one vanilla player present, it turns itself off completely.

Everything below is manual file copying. There is no installer and no server.

> **Upgrading from DoEFriendsMod?** It's the same mod under a better name. Delete
> `Mods\DoEFriendsMod.dll` — leaving it there runs both copies at once — and drop in
> `CustomAvatars.dll` instead. Your avatars move themselves: the mod renames
> `UserData\DoEFriendsMod\` to `UserData\CustomAvatars\` the first time it starts, and says so
> in the console. Your settings reset to defaults, which is deliberate; the old file mixed
> everyday settings with debugging switches and the new one doesn't.

---

## What you need

Ask whoever set this up for **one zip** containing:

| File | What it is |
|---|---|
| `CustomAvatars.dll` | the mod |
| `<avatar>.avatar` | an avatar's model and textures |
| `<avatar>.manifest.json` | that avatar's settings — **always travels with the .avatar** |

> **Everyone must use the same `CustomAvatars.dll` file.** Not "the same version" — the same
> file. The mod compares a checksum of the DLL between players, and two people who each built
> their own copy can end up with different checksums and the mod will refuse to switch on. One
> person builds it, everyone else copies that file.

You also need **MelonLoader 0.7.3**.

---

## Install

Your game folder is wherever Steam put it, usually:

```
C:\Games\Steam\steamapps\common\Dungeons of Eternity
```

In Steam you can find it with **right-click the game → Manage → Browse local files**.

### 1. MelonLoader

Download MelonLoader 0.7.3 (x64) and copy `version.dll` and the `MelonLoader` folder into the
game folder, next to `DoE.exe`.

### 2. Launch the game once, and quit

The first launch is slow — several minutes — because MelonLoader is unpacking the game's code
so mods can talk to it. It may look frozen. Let it finish and reach the menu, then quit.

### 3. The mod

Copy `CustomAvatars.dll` into the `Mods` folder in the game directory. Create the folder if it
isn't there.

### 4. The avatar

Copy **both** avatar files into:

```
<game folder>\UserData\CustomAvatars\Avatars\
```

Create those folders if they don't exist. Copy both files — an `.avatar` without its
`.manifest.json` is refused, on purpose.

### 5. Check it worked

Launch, and look at the MelonLoader console window:

```
CustomAvatars 0.29.0 — ...
Mod DLL SHA-256: 8c52b45c7ad15621…
Avatar OK: `YourAvatar` — 51,823 verts / 1 mesh(es), ...
```

**Compare that SHA-256 with your friends'.** If they don't match, you don't have the same DLL,
and the mod will stay switched off when you play together.

Get into a **private** party. You should see:

```
*** ModGate ACTIVE — private room, all 2 peers on 0.29.0/8c52b45c7ad15621
```

Your avatar goes on by itself. Press **F4** to take it off, and again to put it back on.

You can also do this in the menu, before joining anything. Wearing your own avatar, the F6
preview and the mannequins only change what **your** machine draws, so they're allowed whenever
you aren't in a room at all. Everything that involves other people — seeing their avatars,
sending them your face — still waits for a private lobby with everyone on the same build.

---

## Removing it

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

If you want to be certain, **Steam → right-click the game → Properties → Installed Files →
Verify integrity of game files**. That restores anything you deleted by accident.

### If something goes wrong and the game won't start

Delete `version.dll` from the game folder. That alone disables MelonLoader completely, whatever
else is still sitting there.

---

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
| **PageUp / PageDown** | be bigger / smaller, in 5% steps |
| **Home** | back to your normal size |

### Your size

Out of the box the mod never changes how big you are. The avatar is fitted once, when you
put it on, so its head is at your eyes and its feet are on the floor — the same silhouette
the game's own body has — and that number does not change again while you wear it. Put it
on standing up.

If it went on wrong — legs twisted, put on sitting down — stand up and **hold a T-pose for a
second and a half**: arms straight out to the sides, level with your shoulders. The avatar is
taken off and put straight back on — exactly what pressing F4 twice does — and you'll hear
the same rising two-note chime full-body tracking plays when it locks in.

**Being a different size.** Press **PageDown** to be smaller and **PageUp** to be bigger.
Everything scales together: your eye height, your reach, your hit boxes, the game's own body
and your avatar, so the world reads as bigger or smaller and your feet stay on the floor.
Friends running the mod see you at that size; the mannequins in the equipment room show you
at it too. A moment after you stop pressing, the avatar is taken off and put back on (same
chime as the T-pose) so it is fitted to your new size. **Home** puts you back to normal — and
normal means the mod has let go completely, nothing is written, the game is exactly as it
was. The size is remembered in the settings file as `AvatarSize` (1 = normal); editing it to
1 there and pressing F3 is the same as Home.

Things that are metres to the game stay metres: you walk and jump at the same speed whatever
your size (there is a `SizeMoveSpeedBlend` setting if you'd rather move at your body's pace),
a held sword is still a full-size sword, and chests and ledges are as high as they always
were. Full-body tracking works at any size; a calibration taken at one size is used at another.

If the console ever prints `the game is fighting for it`, the mod has given up on sizing for
that session and put you back to normal on its own — please send that log.

## Settings

They live in `UserData\MelonPreferences.cfg`, in three sections. Every setting has a comment
above it explaining what it does, so the file is worth opening even if you change nothing.

| Section | What's in it |
|---|---|
| `[CustomAvatars]` | The ordinary ones. Which avatar, whether it goes on by itself, which ports face tracking uses, and on/off switches for the big features. |
| `[CustomAvatars_Tuning]` | Dialling in one particular avatar — wrist angles, how far tails swing, how far the eyes turn, how much traffic face tracking is allowed. Safe to change; the defaults are sensible and you only come here when something looks wrong on **your** avatar. |
| `[CustomAvatars_Dev]` | Switches for taking the mod apart when something is broken. **These are already correct.** Changing one is how you work out which half of a problem is at fault, not how you set the mod up. |

Edit, save, then press **F3** in game — most settings apply immediately.

Two things worth knowing:

- **Press F3 after every edit.** MelonLoader writes its own copy of the settings back to disk
  when the game closes, so an edit you never loaded can be overwritten and appear to have been
  ignored.
- **If something is broken in a way that seems impossible, look in `[CustomAvatars_Dev]`
  first.** A value left over from a debugging session looks exactly like a bug. If your avatar
  stands in a T-pose, `SwapUseVrik` is `false`. If your old body shows through the new one,
  `SwapHideVanillaMesh` is `false`. Both should be `true`. The console prints the current values
  at startup and on every F3.

---

## When it isn't working

The console tells you why. The common ones:

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

---

## What this mod does and doesn't touch

- It is **cosmetic**. Weapons, hitboxes, damage and loot are the vanilla game. Your character's
  real body is still there underneath, just not drawn — that's what weapons attach to and what
  enemies hit.
- It **never writes to your account**: no XP, coins, unlocks or progression. Custom avatars
  exist only on the machines running the mod, and only during a session.
- It **sends nothing to any server of ours** — there isn't one. Avatar files are passed around
  by hand.
- Outside a private, all-modded lobby it is **inert**: no changes and no network traffic.
