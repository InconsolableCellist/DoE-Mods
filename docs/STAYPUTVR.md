# StayPutVR mod — design notes

Date: 2026-09-09. Sources: `dump/dump.cs` (game build 1.2.3849), the LootOverhaul recon
transcripts in `UserData/LootOverhaul/recon/`, and the StayPutVR tree at `C:\git\StayPutVR`
(`common/OSCManager.cpp`, `common/Config.hpp`, `wiki/OSC.md`). Unlike the other two
investigation docs this one was written alongside a working build, so it records what was
checked rather than what should be.

## Verdict

**Small.** Every piece already exists on both sides: the game calls one method for every hit
that lands on a player, and StayPutVR already accepts a plain OSC trigger from an unknown
sender with no handshake. The mod is about a hundred and fifty lines of decision-making between
two things that were each built to be talked to.

The only real design question is not technical. It is that this fires a shock device from a
game's damage events, so the interesting engineering is all in the refusals: the arm switch, the
rate limits, and making the current state impossible to misread.

## The game side

### The hook: `AvatarPlayer.OnDamaged`

```
// RVA: 0x3E96D0
public bool OnDamaged(float damage, float knockBackDist, Vector3 damagePosition, DamageType damageType)
```

dump.cs:27128. Returns whether the damage was applied. RVA `0x3E96D0` appears exactly once in
the dump, so it is a real body and not the shared empty-method stub at `0x35FC20` that killed
LootOverhaul 0.1.0 — but the guarded patcher in `Hooks.cs` checks that at runtime anyway.

**This is already proven to fire.** LootOverhaul's recon postfix has been on it since 0.1.0 and
the transcripts show real hits with real numbers:

```
AvatarPlayer.OnDamaged(dmg=2, kb=0, type=Melee) -> True, hp=0.83 `Player_PlasmaFox`
AvatarPlayer.OnDamaged(dmg=1, kb=0, type=Projectile) -> True, hp=0.92 `Player_PlasmaFox`
AvatarPlayer.OnDamaged(dmg=2.2, kb=0, type=Melee) -> True, hp=0.65 `Player_PlasmaFox`
```

Two things to read out of those lines. Damage is in raw HP against a `maxHP` that moves with
gear and difficulty — 2 HP was a sixth of a health bar in one session and 2.2 was a fifth in
another — so **severity has to be the fraction of max HP, not the raw number**, or the same
setting means different things in different runs. And `-> True` is the game's own "it landed",
which is the filter that keeps invulnerability frames and shrugged-off hits from firing.

`AvatarPlayer.Health` (dump.cs:25854) carries `maxHP`, `normalizedHP`, `IsAlive`, `lastChance`
and `waitingForRescue`. The mod reads `maxHP` for the fraction and treats any of
`!IsAlive || lastChance || waitingForRescue` as downed, since the last-chance state is the end
of your run as far as being hit goes even though the game has not killed you yet.

### Filtering to yourself

`OnDamaged` runs for every avatar the client owns, and in a party that includes proxies for
other players. `AvatarPlayer.LocalAvatar` (dump.cs, static property on `AvatarPlayer`) is the
same handle the other four mods use; the postfix compares native pointers against it. The
recon transcripts are all solo, so **the "other players' hits are ignored" path is the one
thing on the game side that is reasoned rather than observed** — hence the counter in
`DamageWatch.Stats()`, which prints how many foreign hits were dropped at quit.

### The damage types

`DamageType` (dump.cs:3969) is a flat enum: `Melee`, `Projectile`, `Magic`, `Splash`,
`Kinetics`, `Fall`, `Trap`, `Poison`, `Other`, `Web`, `Wraith`, `Fire`, `Ice`,
`LastChanceFailed`, `Devour`, `GeoCollision`, `Mimic`, `PvP`. Exposed as
`IgnoreDamageTypes` by name rather than as a bitmask, because the names are what a player can
reason about and the enum can gain members in a game update without invalidating a config file.

### What was considered and not used

`AvatarPlayer.LocalPlayerTookDamage` is a static `ShowDamageFXEvent : UnityEvent<float, float>`
that the game raises for exactly this purpose, and subscribing to it would need no Harmony patch
at all. It carries only two floats — no damage type, no health — and hooking an Il2Cpp
`UnityEvent` from managed code is more fragile than a postfix on a method whose signature the
dump pins down. The postfix also gets `__result`, which the event does not.

`AvatarPlayer.PlayPositionalHitHaptics(Vector3 point, BhapticsFX bodyHit)` is where the game
already routes haptics per body part, and it is the obvious future home for "shock the shocker
nearest where you were hit" if the bite-zone mapping turns out to be worth driving from
geometry rather than severity.

## The StayPutVR side

### The parameter

`common/Config.hpp:187`:

```cpp
// External shock triggers (issue #7): the bite param and the new Shock param
std::string osc_shock_path = "/avatar/parameters/Shock";
```

`OSCManager::ProcessOSCMessage` (`common/OSCManager.cpp:485`) dispatches it:

```cpp
else if (address == osc_shock_path_ && shock_callback_ && value_bool) {
    shock_callback_(true);
}
```

So: one datagram, address match, truthy value, done. The argument may be `T`/`F` (the tag-only
boolean VRChat sends), `i` or `f` — the tag switch at `OSCManager.cpp:404` accepts all three and
treats `> 0.5` / `!= 0` / `T` as truthy. Nothing is read back, no sender identity is checked, no
session is established. The bite family (`/avatar/parameters/SPVR_Bite` plus the six per-zone
suffixes, `wiki/OSC.md`) behaves the same way through `bite_callback_`.

### Why OSC Query has to be off

`application/src/ui/UIManager.cpp:205`:

```cpp
bool osc_use_query = config_.osc_query_enabled;
bool osc_init_result = OSCManager::GetInstance().Initialize(config_.osc_address, config_.osc_send_port,
                                                            config_.osc_receive_port, osc_use_query);
```

and in `OSCManager::Initialize` (`OSCManager.cpp:96`):

```cpp
local_addr.sin_port = htons(static_cast<u_short>(use_ephemeral_receive_port ? 0 : receive_port));
```

With OSC Query enabled StayPutVR binds port **0** — the OS hands it a random free port — and
advertises the real one over mDNS for VRChat to discover, precisely so two OSC apps cannot
collide on 9001. A sender that does not do mDNS discovery therefore cannot find it.

Three ways out, and the cheapest is the right one here:

1. **Ask the user to turn OSC Query off**, so the bind uses `osc_receive_port` (default 9001).
   One checkbox, once. Nothing is lost: OSC Query exists to pair StayPutVR with VRChat, and this
   link is one-way with no reply to route.
2. Implement an mDNS browser in the mod and discover the port. StayPutVR vendors one
   (`thirdparty/mdns`) but the mod would need its own, in C# inside Il2CppInterop, to solve a
   problem the user can solve with a checkbox.
3. Read the port out of StayPutVR's log or config file. Brittle and surprising.

The mod does (1) and says so in three places: the console banner at startup, the README, and
this document. **This is the single most likely reason a first-time setup appears to do
nothing**, which is why the test key exists.

### Intensity is not ours

StayPutVR holds intensity and duration per parameter — `osc_shock_intensity`,
`osc_shock_duration` and the per-zone bite equivalents (`Config.hpp:189`). The protocol carries
no magnitude to the shock trigger: `shock_callback_(true)` takes a bool. So a mod cannot say
"harder"; it can only pick a *different parameter* that the user has configured to be harder.

`TierPaths` is that, generalised — a `fraction:path` list rather than a hardcoded bite-zone
mapping, so it also works for anyone who has bound something else, and so it stays correct if
StayPutVR's zone names change. The bite zones with **Route bites by body part** on
(StayPutVR 1.5.1) are the intended target and are what the README documents.

## The safety design

Ordered by how much they matter, not by how much code they are.

1. **Disarmed at launch** (`ArmedOnStart` defaults false). Installing the mod cannot shock
   anyone. Arming is a gesture, in the headset or at the keyboard.
2. **A disarm you can reach while being mauled.** A keyboard-only toggle is not reachable in a
   headset, so **both sticks clicked in at once** toggles: a quarter second disarms, a second and
   a half arms, and the gesture latches until both come up so holding on cannot walk from one into
   the other. Read off `XRInput.Instance.L3`/`R3` as a level with the edge detected in the mod, the
   way VisualCues does it — the rig's own `L3Down` is true only during the rig's own Update and a
   mod's `OnUpdate` need not share that frame slot.

   0.1.0 used a **double click of one stick** and that was a bug, found in the first real session:
   VisualCues uses a double click of either stick to send its call, so calling a friend silently
   disarmed the shock link (`DISARMED (right stick double-clicked)` in the session log). A gesture
   for a safety control has to be checked against every other mod's gestures, not just against the
   game's — and against the *defaults*, since VisualCues' `SummonStick` is `either`.
3. **A cooldown and a rolling per-minute ceiling, both on by default.** The recon transcripts
   show five hits in under a minute in an ordinary room; a bad fight is worse. `FireOnDeath` is
   the one exception allowed past both, and only because it is opt-out.
4. **A panel that cannot lie about the state.** It shows disarmed as well as armed
   (`HudHideWhenDisarmed` defaults false: "off" is the state most worth confirming), turns amber
   on a socket failure or a missing damage hook, and names the reason a hit did not fire. An
   armed-looking panel over a dead link is the failure mode the panel exists to prevent.

   It lives on the **desktop window**, drawn from `MelonMod.OnGUI` like the CustomAvatars
   overlay, not in the headset. The first build put a head-locked TextMeshPro panel in the
   corner of the view and that was wrong twice over: Unity's IMGUI cannot reach the headset
   anyway, and the arm state is something you check while setting the link up at a keyboard, not
   something that should sit in your eyeline for a whole dungeon. In VR the shock is its own
   confirmation and the disarm gesture is on the stick, so nothing there depends on a display.
   (The head-locked version also had a side effect worth remembering: being `DontDestroyOnLoad`,
   its label became the first `TextMeshPro` in the scene, and VisualCues and Descent both
   captured their font off it. Harmless — they read `font`/`fontSharedMaterial`, which are the
   game's own shared assets — but a reminder that a persistent TMP object is visible to every
   other mod's `FindObjectsOfType`.)
5. **Never leaving a parameter latched true.** `SendRelease` follows each trigger with a
   `false`; disarming drops any pending release; `OnApplicationQuit` disarms and flushes.
6. **Every refusal written down.** `UserData/StayPutVR/logs/` gets a line per hit whether it
   fired or not, with the reason, unconditionally — not behind a verbose flag. An unexpected
   shock and an unexpectedly quiet run are equally worth diagnosing.
7. **The test key fires the lightest configured tier**, not the heaviest. Proving the link
   should not mean taking the hardest shock you own.

## Biting (0.2.0)

The idea: watch the jaw, and treat "open, briefly, then snapped shut" next to another player as a
bite that fires their device. It is the VRChat bite mechanic, driving the trigger StayPutVR built
for exactly that, from a game that has no bite of its own.

### Where the jaw comes from

CustomAvatars already owns the UDP socket VRCFaceTracking sends to (`Face/VrcftBridge.cs`, port
from `FaceOscListenPort`) and keeps every parameter's latest value in a `FaceState` keyed by the
full OSC address. A recon transcript from 2026-08-31 shows the live value arriving as
`/avatar/parameters/FT/v2/JawOpen`.

Two processes cannot bind the same port, so this mod cannot listen for itself. Four options were
considered:

1. **Reference CustomAvatars.dll** — inherits its load order, its version and its gate, and turns
   two independent mods into one.
2. **Have CustomAvatars relay the jaw** over loopback OSC to a port StayPutVR owns — clean, but it
   means changing a much larger, working mod for one consumer.
3. **Reflect into CustomAvatars at runtime** — walk to `CustomAvatars.Core.Instance.FaceState` and
   call `TryGet`. No build dependency, no load-order requirement, and a graceful "biting is off,
   here is why" when CustomAvatars is absent, renamed, or has its face bridge switched off.
4. Have the user point VRCFT at StayPutVR instead — VRCFT sends to one target, so this would break
   CustomAvatars' face sync.

`Bite/FaceLink.cs` does (3). `TryGet` rather than `Get`, so "the parameter never arrived" is
distinguishable from "the mouth is shut", and the resolved name is re-checked every frame so a
tracking drop or an avatar change re-resolves rather than reading a stale zero.

### The gesture

`Bite/JawWatch.cs` is a three-phase machine: shut → open → closing → shut. A chomp requires all
three of a threshold crossing, a hold inside a window, and a fast close, because a mouth opens all
day for reasons that are not bites. Speech does not cross the open threshold for long; a yawn
overstays `BiteMaxOpenSeconds`; a laugh fails `BiteSnapSeconds` because it drifts shut. The bite is
reported on the closing edge, so it lands when the teeth meet.

### Who gets bitten, and the 1.48 m problem

Range is **horizontal** distance plus a generous vertical window, not a sphere. The game's remote
player puppet never lifts its head above about 1.48 m — the same pin recorded in the CustomAvatars
peer notes — so a tall player's head reads far below where it appears, and a true 3D distance test
would refuse bites that visually connect. Horizontal distance is unaffected by the pin, and the
facing test is measured in the horizontal plane for the same reason.

### Consent, which is the whole design

A bite fires **someone else's** shock device, so this is the one feature in the mod where the
network layer exists for a safety reason rather than a gameplay one.

* Event **180** is a presence broadcast: "I run StayPutVR, and here is whether I accept bites."
  Sent on entering a room, on every change, on a six-second heartbeat, and straight back at anyone
  whose presence we hear so a late joiner learns about everyone immediately. A peer silent for
  eighteen seconds is dropped from the roster — fail closed, no bites to a ghost.
* Event **181** is a bite, sent to **one** actor, and only to an actor whose presence said bites
  are accepted.
* The receiver **re-checks its own switch** and its own rate limit, because a hostile or buggy peer
  could send 181 without ever having heard a presence.
* The bitten player's **own client** applies the hit point to itself, through the game's own
  `AvatarPlayer.OnDamaged`. The biter never calls `ApplyRemoteDamage` on anyone.

That last point is what makes the feature safe rather than merely polite. The alternative —
`victim.ApplyRemoteDamage(1, …)` from the biter, which is the vanilla PvP path and would have
needed no custom transport at all (`xref` shows `Mimic$$AE_Bite` and `PlayerMuscle$$TryDamage`
using it, and `RPC_RemoteDamage` calling `OnDamaged` on the owner) — would let a biter damage a
player who has no mod, no switch, and no say. Routing everything through the victim's own client
means a vanilla peer is untouchable by construction rather than by our good behaviour.

Because the bitten client fires the shock itself and then takes the hit point, its own
`OnDamaged` postfix would fire a second trigger for the same bite. `ShockPolicy.SuppressNextHit`
covers that one re-entrant call with a half-second window, and disarming clears it.

`BiteMaxPerMinute` defaults to 6 and applies on top of the ordinary cooldown: consent to be bitten
is not consent to be chain-shocked.

### Event codes

180–181, in the block 180–189. CustomAvatars owns 140–149, LootOverhaul 150–159, VisualCues
160–169, Descent 170–179; the game itself was measured on 1, 2, 50 and 70, and PUN reserves 200 up.

### Not yet verified

1. **A two-player bite**, which needs two headsets, two copies of the mod, face tracking on the
   biter and `BiteVictimEnabled` on the victim.
2. **The gesture thresholds** against a real jaw. Every chomp is logged with its hold and snap
   times precisely so these can be set from a session rather than guessed.
3. **Whether `BiteRangeMeters` 0.6 m feels like biting** given the puppet-head pin.
4. **That a vanilla peer tolerates codes 180/181.** The same open question VisualCues has for its
   code 160: the other mods never send a foreign code to a vanilla client, this one broadcasts
   presence to the room. Watch a vanilla player for a disconnect in the first mixed session.
5. **`OnDamaged` called on our own avatar by us** rather than by the game. It is the same entry the
   game's own `RPC_RemoteDamage` uses, so it should be ordinary, but it has not been done yet.

## The arm key, and why it is not Pause

The first build defaulted to Pause, on the reasoning that nothing else in these five mods and
nothing in the game binds it. That reasoning was incomplete, and a tester lost a session to it on
2026-09-09: their log runs healthily for ninety seconds — patch installed, socket open, panel
drawn — then stops mid-session with no shutdown, no exception and, tellingly, **no `ARMED` line**,
which `SetArmed` writes before it does anything else.

MelonLoader always opens a console window alongside the game. In a Windows console the Break key
sends `CTRL_BREAK_EVENT`, which terminates the process immediately. The mod's keys only work when
the *game* window has focus, so a user who clicked the console — the visible window on the
desktop — and pressed Break did not arm anything; they killed the game.

Two things stop it recurring. The default arm key is now `KeypadEnter`, which no console consumes
and which is findable by feel with a headset on. And `ParseKey` warns in the log if Pause or Break
is configured anyway, rather than accepting it silently — it is still honoured, because it is the
user's keyboard.

The same session on another machine, with the same build and the same Pause key, armed and fired
correctly (`ARMED (Pause pressed)`, then two hits fired), which is what points at window focus
rather than at the arming code. Note also that MelonLoader persists every preference once
written, so changing the code default does not reach a client that has already run the old build —
`HotkeyArm` has to be edited or deleted in `MelonPreferences.cfg`. That is the same stale-preference
trap the CustomAvatars notes record.

## The socket

One `Socket(InterNetwork, Dgram, Udp)`, left **unconnected**, with `SendTo` per datagram. This
is deliberate: on Windows a *connected* UDP socket surfaces the ICMP "port unreachable" that
comes back when nothing is listening as a `ConnectionReset` on the **next** send, so every
trigger would fail for one shot after StayPutVR restarts. An unconnected socket ignores that
ICMP — a trigger sent while StayPutVR is down is dropped by the kernel and the next one works.

Sends are inline on the caller's thread. A loopback datagram is one non-blocking syscall, so
there is no queue and no worker thread to go wrong; the whole failure surface is one try/catch
and a counter, and failures are logged at most once every ten seconds so a dead link cannot
flood the console.

## What was verified

- **The encoder's bytes against the OSC 1.0 layout** — address and type-tag strings
  null-terminated and padded to four-byte boundaries (including the case where the address is
  already a multiple of four and still needs a whole extra word), arguments big-endian, every
  packet a multiple of four bytes for address lengths 1 to 40, and the address validator
  rejecting the OSC pattern characters `# * , ? [ ] { }` as well as spaces and non-ASCII.
- **Those same bytes through oscpp** — the header-only parser StayPutVR itself vendors — with
  StayPutVR's tag switch replayed around it. `T`, `int 1` and `float 1.0` on
  `/avatar/parameters/Shock` all reach the fire decision; `F` correctly does not.
- **The mod compiles clean** against the game's interop assemblies, no warnings.
- **It loads and patches in a live game.** From `MelonLoader/Latest.log`, 2026-09-09 15:41:

  ```
  [StayPutVR] patch ok: AvatarPlayer.OnDamaged @0x7FFE524896D0
  [StayPutVR] Patches: 1 installed, 0 refused, 0 failed.
  [StayPutVR] OSC link opened to 127.0.0.1:9001 (send only; no OSCQuery).
  [StayPutVR] StayPutVR 0.1.0 started. Damage hook installed; link 127.0.0.1:9001.
  [StayPutVR] Disarmed. Press Pause to arm.
  [StayPutVR] Stick clicks readable from XRInput: double-click to disarm, hold 1.5 s to arm.
  ```

  So the guarded patcher accepts the target, the socket opens, the arm state starts closed, and
  the stick gestures are readable. That session predates the desktop panel.
- **The whole chain works end to end.** From `UserData/StayPutVR/logs/shocks-20260909-154731.md`:

  ```
  ARMED (Pause pressed) — hits now fire 127.0.0.1:9005 /avatar/parameters/Shock (2 s apart, at most 15/min; death always fires)
  hit 2 HP (17%) Melee — fired /avatar/parameters/Shock
  hit 2 HP (17%) Melee — held back: cooldown, 1 s left
  hit 2 HP (17%) Fall — fired /avatar/parameters/Shock
  ```

  A real hit in a real dungeon fired a real trigger at StayPutVR, the cooldown held the follow-up
  back, and `Fall` fired as well — which is the argument for putting `Fall` in
  `IgnoreDamageTypes` if tripping over the scenery should not count.

The tests live in `src/StayPutVR/tests/` and are described there.

## Not yet verified — the first headset session

1. ~~The damage hook end to end~~ — done, 2026-09-09; see above.
2. **The desktop panel**: does it fit at `HudWidth` 460 without cutting a line off, and does the
   amber trouble state actually show when StayPutVR is closed.
3. **The stick gestures**: does a double click disarm without the game's own single click
   getting in the way, and is 1.5 s a comfortable arm hold.
4. **Are the defaults right**: `CooldownSeconds` 2 and `MaxPerMinute` 15 are guesses from the
   recon transcripts, not from a real fight.
5. **A party session**: confirm the foreign-hit counter is nonzero and that no other player's
   damage ever fires the link.
6. **`FireOnDeath` past the limits**: die inside a cooldown and confirm the killing blow still
   fires, and that the log says it was allowed past.
