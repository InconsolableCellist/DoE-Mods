# StayPutVR changelog

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

**Worse hits shock harder.** With `ValueType=float` the trigger carries how hard the hit was, 0 to
1, and the app (1.5.2, which learned to read a float on the Shock parameter for this) scales the
shock between its Shock intensity and a new Shock max. The measure is the share of the health you
had that the hit took, so the same blow hurts more the closer to death it leaves you, a bigger blow
hurts more at the same health, and the killing blow is the worst. `SeverityCurve` (0.7) lifts small
hits; `FallSeverityFloor` (0.5) makes a tumble read as a serious hit. The health left after a hit is
read off the game's health object in the same postfix. `bool` stays the default because an app older
than 1.5.2 drops floats under 0.5.

Tested: the question and the parser against bytes produced by the app's own mDNS library
(`tests/MdnsAnswerDump.cpp`), the discovery thread against a fake app on loopback — found, lost
after silence, back on a new port, moved, another app's answer ignored, a dead target silent — and
the severity numbers against the table in the README. Not tested: a live session with the real app,
and whether 0.7 and 0.5 feel right in a dungeon.

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
