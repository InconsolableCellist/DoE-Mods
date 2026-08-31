# Installing and removing DoEFriendsMod

A friends-only cosmetic mod for **Dungeons of Eternity**. It puts custom avatars on the people
in your party. It only does anything in a **private lobby where everyone is running the same
build** — in a public lobby, or with one vanilla player present, it turns itself off completely.

Everything below is manual file copying. There is no installer and no server.

---

## What you need

Ask whoever set this up for **one zip** containing:

| File | What it is |
|---|---|
| `DoEFriendsMod.dll` | the mod |
| `<avatar>.avatar` | an avatar's model and textures |
| `<avatar>.manifest.json` | that avatar's settings — **always travels with the .avatar** |

> **Everyone must use the same `DoEFriendsMod.dll` file.** Not "the same version" — the same
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

Copy `DoEFriendsMod.dll` into the `Mods` folder in the game directory. Create the folder if it
isn't there.

### 4. The avatar

Copy **both** avatar files into:

```
<game folder>\UserData\DoEFriendsMod\Avatars\
```

Create those folders if they don't exist. Copy both files — an `.avatar` without its
`.manifest.json` is refused, on purpose.

### 5. Check it worked

Launch, and look at the MelonLoader console window:

```
DoEFriendsMod 0.7.0 — ...
Mod DLL SHA-256: 8c52b45c7ad15621…
Avatar OK: `YourAvatar` — 51,823 verts / 1 mesh(es), ...
```

**Compare that SHA-256 with your friends'.** If they don't match, you don't have the same DLL,
and the mod will stay switched off when you play together.

Get into a **private** party. You should see:

```
*** ModGate ACTIVE — private room, all 2 peers on 0.7.0/8c52b45c7ad15621
```

Then press **F4** to put your avatar on. Press it again to take it off.

---

## Removing it

### Just turn the mod off, keep MelonLoader

Delete one file:

```
<game folder>\Mods\DoEFriendsMod.dll
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
| **F4** | put your avatar on / take it off |
| **F6** | spawn a copy of the avatar in front of you, to look at |
| **F3** | re-read the settings file, so you can adjust things without restarting |
| **F5** | look for newly added avatar files |
| **F7/F8/F9** | write technical details to a log file, for troubleshooting |

Settings live in `UserData\MelonPreferences.cfg`, under `[DoEFriendsMod]`. Edit it, save, then
press **F3** in game — most settings apply immediately.

Two things worth knowing about that file:

- **Press F3 after every edit.** MelonLoader writes its own copy of the settings back to disk
  when the game closes, so an edit you never loaded can be overwritten and appear to have been
  ignored.
- **`SwapUseVrik` and `SwapHideVanillaMesh` are diagnostic switches.** If your avatar stands in
  a T-pose and doesn't follow your head, `SwapUseVrik` is set to `false`. If your old body is
  still visible through the new one, `SwapHideVanillaMesh` is `false`. Both should be `true`
  normally. On startup and on every F3, the console prints the current values.

---

## When it isn't working

The console tells you why. The common ones:

**`ModGate INERT — room `x` is VISIBLE (public)`**
You're in a public lobby. The mod only runs in private parties, deliberately.

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

**Your friend looks like a normal character, not their avatar**
You don't have their avatar file. The console names it — copy that `.avatar` and
`.manifest.json` pair into your own Avatars folder and press F5. Both of you need both files.

**`No avatars found`**
The files aren't in `UserData\DoEFriendsMod\Avatars\`, or only one of the pair is there.

**Avatar stands in a T-pose and doesn't move with you**
`SwapUseVrik = false` in the settings file. Set it to `true` and press F3.

**Your old character is still visible inside the new one**
`SwapHideVanillaMesh = false`. Set it to `true` and press F3.

**Avatar is bright pink**
The avatar's shaders didn't survive being exported. Whoever exported it needs to re-export with
their shaders locked.

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
