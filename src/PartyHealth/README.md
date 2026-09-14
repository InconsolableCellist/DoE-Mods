# PartyHealth 0.1.1

A mod for **Dungeons of Eternity** that puts a small, quiet health bar over each friend's head
in a networked game, so you know who needs a potion or a rescue without asking.

- **Nothing at full health.** The bar appears on the first hit and fades away a couple of
  seconds after they are back to full. (`ShowWhenFull = true` keeps it up always.)
- **Green, amber, red** as their health drops, a brief white flash on each hit, and **DOWN**
  pulsing over an empty bar while they wait for a rescue.
- **Readable across a room.** Beyond three metres the bar grows with distance, by half of what
  would keep it the same size in your view, and it fades out past forty metres.
- **Through walls**, so a friend behind a pillar still shows.

Your friends need nothing installed: the game already tells every client what happens to every
player's health, and this mod only listens. It sends nothing over the network, never touches
your own avatar, and does not affect your profile, character, progression or save games. The
base game has no such option (its only health-bar setting is for bosses).

## Install

Your game folder is wherever Steam put it, usually:

```
C:\Program Files\Steam\steamapps\common\Dungeons of Eternity
```

Find it by **right-clicking the game in Steam → Manage → Browse local files**.

1. **MelonLoader 0.7.3.** Download the x64 build and copy `version.dll` and the `MelonLoader`
   folder into the game folder, next to `DoE.exe`. Skip this if you already run any of the
   other mods here.
2. **Launch the game once, then quit.** The first launch takes several minutes while
   MelonLoader unpacks the game's code. It may look frozen. Wait for the menu.
3. **Copy `PartyHealth.dll` into the `Mods` folder** inside the game folder. Create the
   folder if it isn't there.
4. **Launch.** The MelonLoader console should show:

   ```
   PartyHealth 0.1.1 — a health bar over each friend's head.
   Patches: 7 installed, 0 refused, 0 failed.
   ```

## Try it alone

Click the game window once so it has the keyboard, then press **J**. A demo bar appears a metre
and a half in front of you: full, it drains to nothing, lies DOWN, is rescued, heals back to
full and fades. That is everything the real bar does. **H** hides and shows all bars, **K**
reloads the settings file.

## Settings

`UserData\MelonPreferences.cfg` in the game folder, section `[PartyHealth]`. Edit the file with
the game closed, or edit it and press **K** in the game to reload.

| Setting | Default | What it does |
|---|---|---|
| `ShowWhenFull` | `false` | Keep the bar up at full health. |
| `HoldAfterFullSeconds` | `2.5` | How long the bar stays after they are back to full. |
| `MaxDistanceMeters` | `40` | No bar for friends farther than this. |
| `WidthMeters`, `HeightMeters` | `0.24`, `0.022` | Bar size at arm's length. |
| `AboveHeadMeters` | `0.28` | How far above the head it floats. |
| `SizeWithDistance`, `SizeFromMeters` | `true`, `3` | Grow with distance beyond three metres so it stays readable. |
| `DistanceGrowth` | `0.5` | How much of the shrinking with distance is undone: 1 = same size on screen at any range, 0 = a real object. |
| `MaxGrowth` | `3` | The bar never grows past this many times its real size. |
| `Opacity` | `0.8` | How solid the bar is, 0 to 1. |
| `ShowName` | `false` | Write the friend's name above the bar. |
| `ShowPercent` | `false` | Write the health percentage beside the bar. |
| `ShowDownedLabel` | `true` | Say DOWN when they are waiting for a rescue. |
| `OnTop` | `true` | Draw through walls and enemies. |

`[PartyHealth_Dev]`: `LogHealthChanges` writes every change the mod sees to the session log,
`VerboseLogging` echoes the log to the console, `HotkeysEnabled` turns off H, J and K.

## How it knows

The game's own damage method only runs for your own avatar, so that is no use for friends. What
every client does receive is a set of remote-procedure calls from each player's game on its
copy of their avatar: one on every hit carrying the health that resulted, one on every heal,
one on every spawn (also sent to a late joiner for everyone already in the room), and the
reset, revive, rescue and rescue-failed calls. The mod has a read-only hook on each of those.
As a second source it also polls each remote avatar's own health object ten times a second; a
polled value that moves is taken as the truth, one that never moves is ignored in favour of the
calls. The first session with friends (2026-09-13) showed both work: the remote copy's health
object holds the right HP and max HP from the moment a friend spawns, and every hit arrived
through the calls as well.

If a friend's maximum health ever reads as zero, the highest health seen for them stands in.

## Remove

Delete `Mods\PartyHealth.dll`. To remove MelonLoader too, delete `version.dll` and the
`MelonLoader` folder from the game folder.

## If something's wrong

The console prints `[PartyHealth]` lines. `MelonLoader\Latest.log` in the game folder keeps
them after you quit, and `UserData\PartyHealth\logs\` has the session log: a `Tracking` line
for each friend with everything the mod could read off them the first time (health object,
HP, max HP, the head transforms and their heights), every health change with its source, and a
`Quit` line counting how many changes came from polling and how many from the game's calls.
Those are the lines to send after a session that looked wrong.
