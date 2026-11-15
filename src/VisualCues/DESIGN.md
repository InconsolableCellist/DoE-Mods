# VisualCues — design, settings and build notes

Player-facing install and usage: [README.md](README.md). This page has the full settings
table, how each cue works, and what remains untested.

A third, independent MelonLoader mod for Dungeons of Eternity. It does four things:

- **The call.** Double-click a thumbstick in (either stick by default) and every player in the room
  who runs the mod gets an arrow to you on their HUD with your name and distance, plus the
  game's own notification line "*name* is calling you". The hand that clicked buzzes so you
  know the call went out.
- **The noise cue.** A marker toward every nearby enemy that makes a sound you cannot see:
  footsteps, attacks, growls, casts. Turn to face it and the marker goes away; when it goes
  quiet, or has been up six seconds, the marker fades. Bosses are labelled. This part is
  entirely local.
- **The gate cue.** A yellow marker labelled GATE OPEN on a gate that starts to open, and
  UNLOCKED on a lock (keyhole pedestal or door) that is unlocked, for five seconds. Local.
- **The puzzle cue.** When a cube goes into a rune-chest slot, a green tick labelled RIGHT or
  a red X labelled WRONG on that slot for three seconds. Local.

There is **no gate**: no handshake, no version check, no private-room rule. The mod works in
any room with anyone. The call needs the mod on both ends (a friend without it sees nothing;
you need it to send). The noise, gate and puzzle cues need nobody else.

## Build

```
cd src/VisualCues
dotnet build
```

Copies `VisualCues.dll` into `<GameDir>\Mods\`. Pass `-p:NoDeploy=true` to skip. Prerequisites
are the same as the other two mods (see [../README.md](../README.md)).

## Using it

Everything is automatic. In VR:

| Action | What happens |
|---|---|
| Double-click a stick in | Sends the call. The game's own single click (item outline) still works. `SummonStick` picks left / right / either; `SummonPress` can be `single` or `hold` instead. |
| Someone else calls | Marker toward them, name and distance, for `SummonDurationSeconds`. Calling again restarts the pulse. Walk to within `SummonDismissMeters` of them while facing them and the marker goes away. |
| An enemy you can't see makes noise | Orange arc on the edge ring in its direction, with the distance. Pink for a boss. |
| A gate opens, a lock is unlocked | Yellow circle (in view) or arc (out of view), `GATE OPEN` / `UNLOCKED`, with the distance. |
| A cube goes into a rune-chest slot | Green tick `RIGHT` or red X `WRONG` on the slot (in view), or an arc with that label. The slot that completes the chest says `RIGHT - SOLVED`. |

Desktop keys (window focused): **'** (quote) sends a call, **.** (period) marks the nearest
enemy whether or not you can see it (to check the HUD without a fight), **,** (comma)
reloads `MelonPreferences.cfg`.

### Reading the HUD

The HUD is a plane 1.2 m in front of the eyes (`HudDistance`). A cue whose target is in view
is a thin circle around the target. A cue outside the view is a short arc of a ring at the
edge of the view (`HudRingDegrees`) in the target's direction, with a small tip pointing the
way to turn and the label just inside the arc. A cue behind you is on the ring too, on the
side that is the shorter turn, and its label says *behind*; straight behind is the bottom of
the ring. Markers pulse briefly when they appear and fade over the last half second.

A noise marker appears on a sound and fades `NoiseDurationSeconds` after the last one; an
enemy that keeps making noise keeps it up, but only until `NoiseMaxSeconds`, after which it
fades and that enemy gets no new marker for `NoiseRepeatSeconds`. Facing an enemy within
`NoiseFacingAngle` counts as seeing it whatever is between you (a gate, bars), so the marker
goes away as soon as you have turned to it.

Markers are drawn over everything when the game has the `UI/Default` shader (the log says
`drawn over everything` or `depth-tested`). Depth-tested markers can be hidden by a wall or an
enemy closer than the HUD plane.

## Settings

`UserData/MelonPreferences.cfg`, sections `[VisualCues]` and `[VisualCues_Dev]`. Every value is
read live; press **,** after editing.

**A hearing player who only wants to send calls** sets these three and sees nothing at all:

```
NoiseEnabled = false
SummonShowIncoming = false
SummonSentFlash = false
```

Their double-click still buzzes the controller and sends the call; nothing is ever drawn.

| Setting | Default | Meaning |
|---|---|---|
| `SummonStick` | `either` | Which stick click sends the call. |
| `SummonPress` | `double` | `double` = two clicks within `SummonDoubleClickSeconds` (default 0.45); `single`; `hold` = held for `SummonHoldSeconds` (default 0.6). |
| `SummonShowIncoming` | `true` | Show other players' calls. |
| `SummonSentFlash` | `true` | Flash "Call sent" on your own HUD. |
| `SummonDismissMeters` | `10` | The caller's marker goes away once you are this close and facing them (`SummonFacingAngle`, 25°). 0 = never. |
| `SummonCooldownSeconds` | `1.5` | No second call inside this window. |
| `SummonDurationSeconds` | `4` | How long the receiver's arrow stays. |
| `SummonToast` | `true` | Also show the game's notification line. |
| `SummonUseGameArrow` | `false` | Also point the game's revive-style arrow at the caller. Off because it is the same arrow the game uses for downed players. |
| `SummonHaptics` | `true` | Buzz the hand that clicked. |
| `NoiseRangeMeters` | `18` | Enemies farther away are ignored. |
| `NoiseSeenAngle` | `40` | Degrees from straight ahead. Inside this, with a clear line of sight, counts as seen. |
| `NoiseFacingAngle` | `15` | Inside this, seen regardless of line of sight. |
| `NoiseIncludeVisible` | `false` | Mark seen enemies too. |
| `NoiseDurationSeconds` | `2.5` | Marker lingers this long after the last sound. |
| `NoiseMaxSeconds` | `6` | Marker fades after this long even if the noise continues. 0 = never. |
| `NoiseRepeatSeconds` | `4` | After a marker times out, no new marker for that enemy for this long. |
| `NoiseFootsteps` / `NoiseAnimationFx` / `NoiseAttributeSounds` | `true` | The three noise sources (below). |
| `NoiseAttributeRadius` | `2.0` | Metres. A positioned sound this close to an enemy is that enemy's. |
| `NoiseMaxMarkers` | `8` | Nearest enemies win. |
| `GateEnabled` | `true` | Gate and lock markers. |
| `GateRangeMeters` | `40` | Gates and locks farther away get no marker. |
| `GateDurationSeconds` | `5` | How long the gate marker stays. |
| `PuzzleEnabled` | `true` | Rune-chest slot markers. |
| `PuzzleRangeMeters` | `25` | Slots farther away get no marker. |
| `PuzzleDurationSeconds` | `3` | How long the slot marker stays. |
| `HudDistance` / `HudRingDegrees` / `HudScale` | `1.2` / `24` / `1.0` | Plane distance, ring radius as an angle, size multiplier for arc width, tip and labels. |
| `HudOnTop` | `true` | Draw over walls and enemies when the shader allows it. |
| `HudShowDistance` | `true` | Distance next to each marker. |
| `LogSounds` (Dev) | `true` | Write every positioned sound with its nearest enemy to the session log. Turn off once tuned. |

## How it works

**The call** is one Photon custom event, code **160** (CustomAvatars owns 140–149,
LootOverhaul 150–159; the game was measured on 1, 2, 50 and 70). Payload: `S|<version>|x,y,z`,
the caller's head position. Receivers look the sender up by actor number and follow their
live avatar; the position is the fallback while an avatar is mid-respawn. Sent reliable to
`Others`. It is the only thing the mod ever sends.

**The noise cue** has three local sources, all hooked with a guarded Harmony patch (the
shared-empty-method check from LootOverhaul, on the exact overload):

1. `Sauron.AI.AE_Footstep` — the animation event behind every enemy footstep.
2. `Sauron.AI.AE_FX(string)` — the animation event behind attack, cast and vocal effects.
3. `Othergate.Audio.AudioManager.PlaySoundAt(Vector3, SoundFX, …)` — the funnel every
   positioned sound goes through (checked in the disassembly: all six `SoundFXRef` play
   paths and `SoundFX.PlaySoundAt` end there). A sound is attributed to the nearest active
   enemy within `NoiseAttributeRadius`, unless the local player's head, a hand or the feet
   are closer (then it is our own sword, footstep or spell).

"Seen" = inside `NoiseFacingAngle` of the camera forward, or inside `NoiseSeenAngle` **and** a
`Physics.Raycast` from the eyes (triggers ignored) reaches the enemy (a hit under the enemy's
own transform, or within 0.75 m of its head, counts as reaching it). The facing rule exists
because a gate stops the ray but not the eyes: in the first test an enemy behind a gate kept
its marker while the player looked straight at it.

**The gate cue** has two hooks, both checked in the disassembly to run on every client:

1. `Gate.Open` (RVA 0x6D6560, a `[PunRPC]`). `Operable.Net_Open` sends it to `RpcTarget.All`
   (offline it calls it directly), and `SoulHarvest.UpdateRoundFinished` calls it on each
   gate in its list directly, from the round state machine every client runs. A prefix reads
   the state; only a call that takes the gate from Closed or Closing to Opening, the same test
   `Operable.Open` makes, shows a marker. A late joiner gets gate state through
   `SyncInitialState`, not `Open`. The marker sits on the middle of the gate's renderers'
   bounds (fallback: a metre above its transform).
2. `Lock.Remote_Unlock` (RVA 0xBBC4B0), the body of `RPC_Unlock`, which `Lock.Net_Unlock`
   sends to `RpcTarget.All` when a key is turned. `Door.Remote_Unlock` calls it. Only a lock
   that was still locked counts. `Lock.OnPlayerEnteredRoom` also sends it to a late joiner for
   locks already open, so nothing is shown for the first 12 seconds after a scene loads.

**The puzzle cue** hooks `RuneChest.RPC_SyncLockState(int[] data, int changedIndex)`
(RVA 0x70D540). The chest's owner judges the cube in `OnKeyAdded` and sends this RPC to
`RpcTarget.All`; inside it every client plays `validAttachmentSound` when
`data[changedIndex]` is 1 or `invalidAttachmentSound` when it is 2, at
`receptacles[changedIndex]`. The postfix makes the same test at the same place. The late-join
sync sends `changedIndex = -1` and a removed cube sets 0, so neither shows anything.

The HUD is repositioned to the main camera every LateUpdate (falling back to the local
avatar's eye, then head, transform). The arc and the circle are two generated double-sided
meshes at unit radius, scaled to the ring; labels are 3D TextMeshPro with the game's font,
captured from any live text. The puzzle tick and X are two more generated stroke meshes;
right and wrong differ in shape and label as well as colour.

## Tested so far

First solo session (2026-09-05, 0.1.0): stick click read, HUD drawn, noise markers shown.
The enemy-behind-a-gate case (marker stayed while facing it) is what 0.1.1's facing rule and
lifetime cap are for. Still to check, in order of what would stop the mod working:

1. **A vanilla player receiving code 160.** The other two mods never send a custom event to
   a player without the mod, by design, so this is the first time a vanilla client sees a
   foreign code. PUN hands unknown codes to `IOnEventCallback` listeners and the game's
   `GameManager.OnEvent` should ignore what it does not know, but watch a vanilla friend for
   a disconnect on the first call. If it happens, that is a hard stop for the call in mixed
   rooms and the README needs to say so.
2. **`XRInput.Instance.L3` / `R3` actually reflect the click** on the SteamVR rig. The log
   prints `Stick clicks readable from OpenVRInput` when the rig answers; the **'** key is the
   keyboard stand-in either way.
3. **`Camera.main` is the headset camera.** The log prints `HUD follows Camera.main: <path>`;
   if that path is not under the SteamVR rig, set the fallback order in `CueHud.ResolveHead`.
4. **The marker shader.** Look for `HUD markers use shader UI/Default, drawn over everything`.
5. **Noise attribution.** With `LogSounds` on, `UserData/VisualCues/logs/cues-*.md` has one
   line per positioned sound: name, nearest enemy distance, player distance, decision. Sounds
   that should have been an enemy's but read `skip`, or the reverse, are the tuning input for
   `NoiseAttributeRadius`. Footsteps from `AE_Footstep` need no attribution.

6. **The gate and puzzle cues (0.1.6), never seen in a headset.** The console prints
   `First gate cue fired — …` and `First puzzle cue fired — …` once per session; every later
   one is a `gate cue:` or `puzzle cue:` line in the session log, including the ones skipped
   for range or the settle window. Check that a lever, a soul-harvest round end and a
   skeleton key each give one, as host and as a guest, and that a cube in a slot gives RIGHT
   or WRONG matching the game's sound.

Grep the MelonLoader log for `[VisualCues]`; the session log is the fuller record.

## Layout

```
src/VisualCues/
├── Core.cs               MelonMod entry, hotkeys, wiring
├── ModConfig.cs          MelonPreferences
├── ModPaths.cs           UserData/VisualCues/
├── Hooks.cs              guarded Harmony patching (exact overload, shared-stub refusal)
├── CueLog.cs             session log
├── Interop.cs            Il2Cpp proxy helpers
├── Controls/StickPress.cs  L3/R3 from XRInput: double click, single, or hold
├── Net/PhotonHook.cs     the read-only prefix on Photon's inbound dispatch
├── Net/CueNet.cs         event 160, send and receive
├── Cues/SummonCue.cs     the call: send, receive, follow the caller
├── Cues/NoiseCue.cs      the three noise hooks, attribution, seen test
├── Cues/GateCue.cs       Gate.Open and Lock.Remote_Unlock: gate and lock markers
├── Cues/PuzzleCue.cs     RuneChest.RPC_SyncLockState: right/wrong slot markers
└── Hud/CueHud.cs         the head-locked HUD: markers, ring, labels, flash
```
