# StayPutVR (Dungeons of Eternity mod) — 0.3.0

Take damage in the dungeon and your shock device fires, through the
[StayPutVR](https://github.com/InconsolableCellist/StayPutVR) app. Bite another player and theirs
does.

This mod is named after the app it talks to. Where the two need telling apart below, the app is
called "the StayPutVR app".

Nothing is written to your profile, character, progression or saves.

## Install

1. **MelonLoader 0.7.3** in the game folder — `version.dll` and the `MelonLoader` folder next to
   `DoE.exe`. Skip if you already run CustomAvatars, LootOverhaul, VisualCues or Descent.
2. **Launch once and quit.** The first launch takes several minutes while MelonLoader unpacks the
   game's code. It may look frozen.
3. **Copy `StayPutVR.dll` into `Mods`.**
4. **Launch.** The MelonLoader console should show:

   ```
   StayPutVR 0.3.0 — a hit fires /avatar/parameters/Shock at 127.0.0.1:9001. Logs in ...
   patch ok: AvatarPlayer.OnDamaged @0x...
   Patches: 1 installed, 0 refused, 0 failed.
   PhotonHook installed on LoadBalancingClient.OnEvent.
   ```

## Set up the StayPutVR app

**Turn OSC Query off** — *Settings → OSC*, clear **Enable OSC Query**, restart its OSC. With it on
the app binds a *random* receive port and advertises it over mDNS for VRChat to find. This mod
sends to a port you name, so with OSC Query on the messages go nowhere. Nothing is lost: OSC Query
exists to pair the app with VRChat, and a shock trigger is one datagram in one direction with no
reply.

**Note the receive port** on the same tab and put it in `Port` below. It defaults to 9001 but is
easy to have changed.

**Enable the Shock trigger** — *Integrations → OSC Triggers* → **Shock**, and set its intensity and
duration. That is where intensity lives: the mod only decides *whether* to fire. For biting, enable
**Bite** on the same tab.

## Using it

**Click both thumbsticks in at once.** A moment disarms, a second and a half arms. One gesture, and
how long you hold it decides which way it goes, so the quick version is the one that gets you out.
There are no keyboard keys. The state is remembered across sessions.

Both sticks rather than one because the game uses a single stick click itself, and VisualCues uses
a double click of either stick to send a call.

**The panel** in the top right of the desktop window (not the headset — Unity's IMGUI only draws to
the desktop mirror) shows the arm state, where triggers go, the limits, what has fired and what was
held back and why. It turns amber if the socket is failing, because an armed-looking panel over a
dead link is the one thing you must not be able to mistake for a working one.

## Biting

Open your mouth and snap it shut next to another player and their device fires. It needs
**CustomAvatars running with face tracking**, because that mod owns the socket VRCFaceTracking
sends to and two processes cannot share a port — the jaw is read out of it at runtime. No face
tracking, no biting, and the log says so.

A bite is a chomp, not an open mouth: past `BiteOpenThreshold`, held between `BiteMinOpenSeconds`
and `BiteMaxOpenSeconds`, then shut to `BiteCloseThreshold` within `BiteSnapSeconds`. Speech never
crosses the threshold for long, a yawn stays open too long, a laugh drifts shut instead of
snapping. It lands as your teeth meet.

You bite the nearest willing player within `BiteRangeMeters` horizontally, `BiteVerticalMeters`
above or below, and `BiteFacingAngle` of your gaze. Horizontal rather than a sphere because the
game's remote player puppet pins its head near 1.48 m, so a tall friend reads lower than they look.

**`BiteVictimEnabled` is a consent switch.** A bite is only ever sent to a player who has
advertised they accept them, only to that one player, and their client checks its own switch again
before anything happens. The bitten player's own client applies the hit point to itself. A player
without the mod, or with the switch off, cannot be damaged or shocked by any of it.

## Settings

`UserData\MelonPreferences.cfg`, section `[StayPutVR]`. Edit with the game closed; it rewrites the
file on quit.

| Setting | Default | What it does |
|---|---|---|
| `Enabled` | `true` | `false` installs nothing and draws nothing. |
| `Armed` | — | Remembered from your last session. The sticks change it. |
| `Host` / `Port` | `127.0.0.1` / `9001` | Where the StayPutVR app is listening. |
| `ShockPath` | `/avatar/parameters/Shock` | What a hit fires. |
| `ValueType` | `bool` | `bool`, `int` or `float`. |
| `ReleaseSeconds` | `0.15` | Gap before the `false` that releases the trigger. |
| `MinDamage` | `0` | Ignore hits under this many HP. |
| `MinDamageFraction` | `0` | Ignore hits under this share of max HP. |
| `CooldownSeconds` | `2` | No second trigger inside this window. |
| `MaxPerMinute` | `15` | Max triggers per rolling minute. `0` = no limit. |
| `IgnoreDamageTypes` | *(empty)* | Types that never fire, e.g. `Fall,Poison,GeoCollision`. |
| `BiteEnabled` | `true` | Your chomp bites other players. |
| `BiteVictimEnabled` | `true` | Other players' bites fire your device. |
| `BitePath` | `/avatar/parameters/SPVR_Bite` | What a bite fires. |
| `BiteDamage` | `1` | HP a bite costs you. `0` = shock only. |
| `BiteDamageType` | `Melee` | How the game records it. |
| `BiteMaxPerMinute` | `6` | Max bites per minute, on top of the cooldown. |
| `BiteRangeMeters` | `0.6` | Horizontal reach. |
| `BiteVerticalMeters` | `0.9` | How far above or below you they may be. |
| `BiteFacingAngle` | `70` | How far off your gaze, degrees. |
| `BiteCooldownSeconds` | `3` | Shortest gap between bites you send. |
| `BiteOpenThreshold` | `0.55` | Jaw value that counts as open. |
| `BiteCloseThreshold` | `0.15` | Jaw value that completes the chomp. |
| `BiteMinOpenSeconds` | `0.08` | Shortest hold. Filters twitches. |
| `BiteMaxOpenSeconds` | `1.2` | Longest hold. Filters yawns. |
| `BiteSnapSeconds` | `0.2` | How fast the mouth must shut. Filters speech. |
| `BiteGestureCooldownSeconds` | `0.6` | Shortest gap between recognised chomps. |
| `BiteJawParam` | *(empty)* | Force a face parameter. Empty tries the usual spellings. |
| `HudEnabled` | `true` | Draw the desktop panel. |

`Fall` fires by default, so put it in `IgnoreDamageTypes` if tripping over scenery should not count.

## Remove

Delete `Mods\StayPutVR.dll`.

## If something's wrong

`UserData\StayPutVR\logs\` has one line per hit — fired or held back, with the reason — and every
chomp with how long it was held and how fast it shut. `MelonLoader\Latest.log` keeps the console
lines.

**Nothing fires.** Arm it: hold both sticks until the panel says `ARMED`. If the log says the
datagram went out and nothing happened, the problem is between the StayPutVR app and the device,
and it is almost always OSC Query still on.

**The panel is amber.** The socket is failing and the reason is on it.

## Layout

```
src/StayPutVR/
├── Core.cs              MelonMod entry, wiring
├── ModConfig.cs         MelonPreferences
├── ModPaths.cs          UserData/StayPutVR/
├── Interop.cs           Il2CppInterop null/name/path helpers
├── Hooks.cs             guarded Harmony patching (shared-stub check)
├── ShockLog.cs          session log: every hit and every decision
├── Osc/                 OSC 1.0 encoder, the one outbound socket
├── Trigger/             the damage hook, the policy, the stick gesture
├── Bite/                the jaw, the chomp, who gets bitten
├── Net/                 consent and bites, Photon events 180-181
├── Hud/                 the desktop panel
└── tests/               standalone checks for Osc/ — see tests/README.md
```

Design notes: [../../docs/STAYPUTVR.md](../../docs/STAYPUTVR.md).
