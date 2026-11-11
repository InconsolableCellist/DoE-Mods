# StayPutVR changelog

## 0.5.3 — 2026-11-11 (performance)

**The jaw is read with a direct call.** Biting reads CustomAvatars' jaw value every frame; it went through reflection each time (an argument array, boxing, a slow invoke). The call is now bound once. While no jaw parameter arrives, the candidate names are tried once a second instead of every frame. Biting stays on by default.

Incoming network messages are checked by their number first, and anything that is not this mod's is ignored before its sender and contents are read. Every Photon event of the game passes through this hook, so this saves an interop call and an allocation per event.

The session log is now written to disk once a second instead of after every line. Before, each line was a synchronous disk write on the game's main thread, so a burst of lines (a fight, a dump) could cost frame time in the headset. If the game crashes hard, at most the last second of the log is missing.

## 0.5.2 — 2026-10-02 (untested in the headset)

**The staff and life steal stack.** Each staff tick (about one a second) and each life steal now
adds 2 s on top of whatever shield is left, up to 45 s from now, instead of the staff resetting a
flat 45 s. The first tick buys 2 s; a minute of beam holds at 45 s, which runs out 45 s after the
beam stops. Potions still set a length and only ever move the end later. `ShieldStaffSeconds` is
now seconds per tick (default 2); a cfg still holding 0.5.1's `45` is moved to 2 on load, with a
console line saying so. New `ShieldStackMaxSeconds` (45) is the cap for both.

**Full health counts.** A potion, a vampire hit, a shield absorb or the Bloodlust ring's heal on a
kill at a full bar now starts or adds to the shield; before, a heal that gained nothing was
dropped. The shield absorb and the kill heal skip the game's heal at full health, so the mod
hooks `Shield.TryShieldAbsorb` and `AvatarPlayer.OnAIKilled` and counts the steal they would have
made, once, keeping the absorb's one-a-second limit. Logged `heal at full health (life steal: …)`.
The staff cannot do this: the game will not aim a heal beam at a player at full health (unless
poisoned or frozen), so nothing reaches your client.

**One staff tick per beam.** A beam seen both by its RPC and by the heal's own frame counts once,
and a regain tick landing inside the beam's window no longer passes for a second staff tick.

## 0.5.1 — 2026-10-02 (after the first party session with the heal shield; untested in the headset)

**Longer heal shields, one length per heal.** Minor healing potion 10 s, major 20 s, healing staff
45 s after the beam's last tick, and life steal (shield absorb, vampire perks, the heal on a kill)
2 s — it was excluded before. `ShieldMinorPotionSeconds`, `ShieldMajorPotionSeconds`,
`ShieldStaffSeconds` and `ShieldLifeStealSeconds` replace `HealShieldSeconds` (the old line in
the cfg is no longer read); `0` turns a source off. A heal moves the end to whichever is later,
never earlier, so life steal cannot cut a longer shield short. A staff beam counts at full health.

**Staff beam diagnostics.** The beam's own RPC is now watched too: `staff beam on you from actor N
(kinetic type T)` once per run of beams, `but no heal followed` if the heal never arrives, and a
count when it stops. A heal within 0.75 s of the beam counts as the staff's. The staff detection
itself was already working — the friend it missed had disarmed, and the cue is armed-only; a
shield started while disarmed is now logged `heal shield not shown: disarmed`.

**The cue** is a little brighter and thicker and flashes briefly as it appears.

## 0.5.0 — 2026-09-28 (after the game's 2026-09-27 update; untested in the headset)

**Heal shield.** For `HealShieldSeconds` (5) after a healing potion or a healing staff heals you,
hits do not shock; each one is logged `held back: heal shield, … s left`. Life steal (shield
absorb, vampire perks, heal on a kill), revives and the game's own regain do not count. The killing
blow still fires. While it is up and armed, a faint arc low in the headset view shortens as it runs
out, and the panel shows the time left.

**Old-app warning.** A session fired 17 of 61 hits: the installed app predated 1.5.2, which drops
every float under 0.5 and never answers OSC Query. After ten unanswered questions the panel now
warns in amber and the log says so once.

**Severity against the right max.** The update added an overheal bonus to max health, and
`normalizedHP` now divides by the max with it; the share of a hit now uses the same max
(`GetMaxHP`) instead of the base `maxHP` field. `DarkLight` is a new damage type.

## 0.4.0 — 2026-09-12 (OSC Query discovery; untested in the headset)

**The port is found, not configured.** The StayPutVR app advertises its receive port over OSC
Query, and the mod now asks for it: one small mDNS question every couple of seconds until the app
answers, then every ten to catch a restart, which puts the app on a new port. With app 1.5.2 or
newer and OSC Query left on — its default — there is nothing to copy between the two, and the
fight over port 9001 with VRCFaceTracking, which is what made turning OSC Query off necessary in
the first place, is gone. `Host` and `Port` stay as the fallback while nothing answers: an older
app, OSC Query off, or the app not running yet. The panel's `Link:` line says which is in use, and
the console and session log say when the app is found, moves, or goes quiet.

The mod never binds the mDNS port or joins the multicast group. It asks from an ordinary socket
and the app answers straight back to it — a legacy unicast query in RFC 6762's words — so there is
no new inbound listener and nothing for the firewall to ask about. Answering that way is the
change in StayPutVR 1.5.2; VRChat's and VRCFaceTracking's own discovery are untouched by it, and
CustomAvatars needed no change.

**Worse hits shock harder.** The trigger now carries how hard the hit was, 0 to 1, and the app
(1.5.2, which learned to read a float on the Shock parameter for this) fires at that fraction of
a new Shock max, the whole range from nothing up to it. The measure is the share of the health you
had that the hit took, so the same blow hurts more the closer to death it leaves you, a bigger blow
hurts more at the same health, and the killing blow is the worst. `SeverityCurve` (0.5) lifts small
hits; `FallSeverityFloor` (0.5) makes a tumble read as a serious hit. The health left after a hit is
read off the game's health object in the same postfix.

**`ValueType` is gone and the app must be 1.5.2 or newer.** Every trigger is a float now. The
setting shipped in 0.3.0 as `bool`, and MelonLoader keeps what it wrote, so a default of `float`
would never have reached an existing install without a hand edit; the first live run proved it,
with every hit landing at the plain intensity. A stale line in the config file is ignored.

**One shock per death.** The game keeps reporting hits while you lie there waiting for rescue,
every one flagged as downed, and "death always fires" let each of them past the cooldown and the
ceiling — nine shocks in four seconds in that same first run. Now the hit that puts you down is
the one allowed past the limits, and the rest are held until you are back up or the scene
changes.

The quit summary was written twice because the game calls the quit hook twice; it is now once.

Tested: the question and the parser against bytes produced by the app's own mDNS library
(`tests/MdnsAnswerDump.cpp`), the discovery thread against a fake app on loopback — found, lost
after silence, back on a new port, moved, another app's answer ignored, a dead target silent — and
the severity numbers against the table in the README. Not tested: a live session with the real app,
and whether a curve of 0.5 and a fall floor of 0.5 feel right in a dungeon.

## 0.2.1 — 2026-09-09

**The link no longer disarms itself.** The in-headset gesture was a double click of either stick,
which is exactly what VisualCues uses to send its call — so calling a friend silently disarmed the
shock link (`DISARMED (right stick double-clicked)`, found in the first real session). It is now a
click of **both** sticks at once: a quarter of a second disarms, a second and a half arms, and the
gesture latches until both come up. Nothing else in these mods or the game asks for both sticks
together, and it cannot happen while walking around. `VrToggleStick` and
`VrToggleDoubleClickSeconds` are gone; `VrDisarmHoldSeconds` replaces them.

**The arm key is Numpad Plus.** It joins the mod's other three keys on the numpad. Every
nav-cluster key is already taken by a sibling mod — Home, PageUp and PageDown by CustomAvatars,
Insert and Delete by LootOverhaul, Backspace, End and Slash by Descent — so End was not available.
Existing installs must edit `HotkeyArm` in `MelonPreferences.cfg`, as MelonLoader persists it.

The desktop panel now carries the bite block: the live jaw value and gesture phase, how many peers
accept bites, chomps seen, bites sent, and why the last chomp found nobody.

## 0.2.0 — 2026-09-09 (biting; untested with two players)

**Biting.** A chomp — jaw past a threshold, held briefly, then snapped shut — next to another
player fires their device, driving StayPutVR's own bite trigger the way VRChat's bite prefabs do.
The jaw is read out of the CustomAvatars mod by reflection, because that mod owns the socket
VRCFaceTracking sends to and two processes cannot share a port; without it, biting stays off with
a reason in the log. Range is horizontal distance plus a vertical window rather than a sphere,
because the game's remote puppet pins its head at about 1.48 m and a sphere would refuse bites
that visually connect.

Off at both ends by default, and the two switches are separate: `BiteEnabled` lets you bite,
`BiteVictimEnabled` lets others bite you. The second is a consent switch, and the design follows
from it — a bite goes to one actor and only to one that advertised it accepts them (Photon events
180 and 181), the receiver re-checks its own switch and its own per-minute ceiling, and the bitten
player's **own** client applies the hit point to itself through the game's own `OnDamaged`. The
biter never calls `ApplyRemoteDamage`, so a player without the mod cannot be damaged or shocked by
any of it.

**The arm key is no longer Pause.** MelonLoader always opens a console window, and Break in a
Windows console sends CTRL_BREAK_EVENT, which kills the process — so with the console focused
rather than the game, that key quit the game instead of arming. A tester lost a session to it.
Configuring Pause or Break now warns in the log.

Numpad 2 hides the desktop panel.

## 0.1.0 — 2026-09-09 (first build)

First build. A postfix on `AvatarPlayer.OnDamaged`, filtered to `AvatarPlayer.LocalAvatar` and
to hits the game actually applied, hands each hit to a policy layer that decides whether to
fire: arm state, an absolute and a fractional damage floor, a cooldown, a rolling per-minute
ceiling, an ignored-damage-type list, and the killing blow as the one hit allowed past the
cooldown and the ceiling. Firing means one OSC datagram to StayPutVR's receive port — by
default `/avatar/parameters/Shock`, its dedicated external shock trigger — followed by a
release. `TierPaths` maps damage severity onto several parameters so StayPutVR's per-parameter
intensities (its bite zones) can stand in for an intensity this protocol cannot carry.

Send only, one socket, no OSCQuery and no mDNS on this side, which is why StayPutVR must have
OSC Query **off** for its receive port to be the configured one.

Disarmed at launch. Pause arms and disarms, Numpad 0 sends a test trigger (the lightest tier, so
proving the link is not the hardest shock you own), Numpad 1 reloads settings, Numpad 2 hides the
panel; in the headset a stick double-click disarms and a 1.5 s hold arms. A panel in the top
right of the **desktop** window — not the headset, the same as the CustomAvatars overlay —
carries the arm state, the target, the limits, the counters, the reason a hit was held back and
any socket trouble.

Verified: the encoder's bytes against the OSC 1.0 layout; those same bytes through oscpp, the
parser StayPutVR itself vendors, reaching the shock decision; the real sender over loopback,
including that sends to a dead port do not poison the socket; and a live game session in which
the patch installed (`AvatarPlayer.OnDamaged`, 1 installed / 0 refused / 0 failed), the socket
opened and the stick gestures were readable. Tests in `tests/`.

Confirmed working the same day: armed, took a 2 HP melee hit in a dungeon and the trigger went out
to StayPutVR; the cooldown held the follow-up back. Not yet verified: the damage hook under a real
party, and whether the default cooldown and ceiling suit a real dungeon.
