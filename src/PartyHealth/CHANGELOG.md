# PartyHealth changelog

## 0.1.1 — 2026-09-13 (distance growth halved)

**The bar no longer looms at range.** 0.1.0 grew it in proportion to distance beyond three
metres, which keeps it the same size on screen at any range, and in the first session with
friends that read as too big on a friend across the room. The growth is now the distance ratio
raised to `DistanceGrowth` (0.5), so half the shrinking is undone: a friend at twelve metres gets
a bar twice its real size rather than four times, and it never passes `MaxGrowth` (3×). Set
`DistanceGrowth = 1` for the old behaviour, 0 for a real-sized object.

**What the first session's log settled.** The remote copy of each avatar holds the right HP and
max HP from the spawn call on, so polling and the RPCs agree; DOWN and the rescue that followed
were both tracked. The mod ran with two friends on vanilla builds, and the bars showed.

## 0.1.0 — 2026-09-12 (first build)

A small health bar over each friend's head. Hidden at full health, fades in on the first hit
and out a couple of seconds after they are back to full; green to amber to red; a white flash
per hit; DOWN pulsing over an empty bar while they wait for a rescue; grows with distance past
three metres so it stays readable, fades out past forty; drawn through walls.

Health comes from the game's own RPCs on each remote avatar (`RPC_OnDamaged`, `RPC_OnHealed`,
`RespawnAvatar`, `RPC_ResetHealth`, `RPC_Revive`, `RPC_Rescue`, `RPC_RescueOutOfTime`) through
read-only postfixes, and from polling the remote copy's `AvatarPlayer.Health` ten times a
second. The two are reconciled by trusting whichever moved last; the session log counts which
did the work, and the `Tracking` line per friend dumps what the remote health object held on
first sight, which settles whether it is kept up to date at all. Max HP falls back to the
highest HP seen when it reads as zero.

The bar anchors to `IKTargetHead`, the networked head target, not the display body's head bone,
which CustomAvatars found pinned at 1.48 m on remote clients.

Desktop keys: H hides and shows, J runs a demo bar in front of you for testing alone, K reloads
settings. No gate, no network traffic, no Photon event code.

Verified in this build: the DLL compiles and deploys; nothing has run in the headset yet.
