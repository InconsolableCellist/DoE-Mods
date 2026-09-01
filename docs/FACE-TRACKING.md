# Face Tracking Design — Unified Expressions over OSC

Target standard: **VRCFaceTracking Unified Expressions (UE)**
(https://docs.vrcft.io/docs/tutorial-avatars/tutorial-avatars-extras/unified-blendshapes).
Avatars author UE-named blendshapes; the mod speaks UE end-to-end: VRCFT → OSC → local
avatar → Photon → friends' copies of the avatar.

## Pipeline

```
[eye/face HW] → VRCFaceTracking (UE model)
    → OSC floats /avatar/parameters/<prefix>/v2/<Name> → 127.0.0.1:9000
    → mod OSC listener → UEState (float[NUM_UE])
    → local apply (blendshapes + eye bones), LateUpdate after IK
    → quantize/delta → Photon event 142 (unreliable, 12–15 Hz)
    → peers: dequantize → interp (~100 ms) → apply via their mapping for MY avatar
```

Design point that simplifies everything: **we are the "VRChat side" of the OSC link**, so
we decide which parameters VRCFT sends. We request the full UE *base* set as plain floats
— no binary bit-packing, no combined/simplified params (`SmileFrown` etc.) to decompose.
Combined params exist only to squeeze into VRChat's 256-bit parameter budget, which we
don't have.

### Getting VRCFT to send to us — **settled 2026-08-31 by reading the source**

The OSCQuery work is **not needed**. Two findings from `/mnt/c/git/VRCFaceTracking`:

**1. The send target is plain configuration, and OSCQuery never overrides it.**
`OscSendService` connects a UDP socket to `IPEndPoint(OscTarget.DestinationAddress,
OscTarget.OutPort)` and nothing else changes it. Those come from
`%AppData%\VRCFaceTracking\VRCFaceTracking\ApplicationData\LocalSettings.json`:

| Key | Default | Meaning |
|---|---|---|
| `OSCAddress` | `127.0.0.1` | where VRCFT sends |
| `OSCOutPort` | `9000` | port it sends to — **we listen here** |
| `OSCInPort` | `9001` | port it listens on — we send here |

So we listen, and that is the whole delivery mechanism. No mDNS responder, no HTTP server.

**2. `/vrcft/settings/forceRelevant` replaces avatar negotiation.**
By default every `/avatar/parameters/...` parameter starts `Relevant = false` and only switches
on when VRCFT matches it against a parameter list the receiver declared — normally via an
OSCQuery `/avatar` response or a VRChat avatar-config JSON on disk. But
`OscQueryService.HandleNewMessage` also accepts `/vrcft/settings/forceRelevant` (bool), which
sets `AllParametersRelevant` and switches on **everything**. One UDP packet to
`127.0.0.1:9001` replaces the entire discovery mechanism. That's what the mod sends at startup.

**3. Some parameters arrive with no negotiation at all.** `AlwaysRelevantParameter` sends
`/tracking/eye/LeftRightPitchYaw` and `/tracking/eye/EyesClosedAmount` unconditionally. Gaze and
eyelids therefore work even if everything above fails — a useful floor to degrade to.

### Wire details that matter to the receiver

- Addresses are `"/avatar/parameters/" + name`; raw shapes are `v2/<UnifiedExpressionsName>`.
- **Raw shape weights are 0..1. Combined parameters are signed −1..1** (`v2/JawX`,
  `v2/BrowExpression`, `v2/MouthX`, `v2/SmileFrown`, …), computed by VRCFT itself from the raw
  shapes. Each parameter also has a companion bool.
- Type tags: `f` float, `i` int, `T`/`F` bool — **`T`/`F` carry no payload bytes**, the tag is
  the value. Getting that wrong desynchronises the rest of the message.
- Bit-packed "binary" parameters are only produced for bit-steps the receiver *declared*, so
  with `forceRelevant` we get plain floats and can ignore that mechanism entirely.
- Sent as **OSC bundles** on a 10 ms tick, change-gated — unchanged values are not resent. So
  the parser must handle `#bundle`, and a value that stops arriving means it stopped moving,
  not that tracking died.

### Correction to the shape table below

The ordered list in this document was **written from the VRCFT docs, not from its source**, and
the real `UnifiedExpressions` enum (`VRCFaceTracking.Core/Params/Expressions/UnifiedExpressions.cs`)
is in a different order and includes `SoftPalateClose`, `ThroatSwallow`, `NeckFlexRight/Left`
before its `Max` sentinel. Since the face stream has not shipped, **the enum's own order should
become the canonical one** rather than ours — it costs nothing now and avoids a permanent
translation layer between our IDs and everyone else's.

Parse note: accept any address whose trailing segments are `v2/<Name>` (VRCFT allows
nested prefixes like `FT/v2/JawOpen`); also accept bare `<Name>` for tools that skip the
prefix. Values are floats 0..1 (a few gaze params are −1..1).

## Canonical UE shape table

The wire format and the manifest both index into this fixed, ordered list — **never
reorder; append only** (it defines the network protocol). 98 shapes, IDs 0–97.

Eye gaze (8): `EyeLookOutRight, EyeLookInRight, EyeLookUpRight, EyeLookDownRight,
EyeLookOutLeft, EyeLookInLeft, EyeLookUpLeft, EyeLookDownLeft`

Eyelids/pupils (10): `EyeClosedRight, EyeClosedLeft, EyeSquintRight, EyeSquintLeft,
EyeWideRight, EyeWideLeft, EyeDilationRight, EyeDilationLeft, EyeConstrictRight,
EyeConstrictLeft`

Brow (8): `BrowPinchRight, BrowPinchLeft, BrowLowererRight, BrowLowererLeft,
BrowInnerUpRight, BrowInnerUpLeft, BrowOuterUpRight, BrowOuterUpLeft`

Nose (6): `NoseSneerRight, NoseSneerLeft, NasalDilationRight, NasalDilationLeft,
NasalConstrictRight, NasalConstrictLeft`

Cheek (6): `CheekSquintRight, CheekSquintLeft, CheekPuffRight, CheekPuffLeft,
CheekSuckRight, CheekSuckLeft`

Jaw (8): `JawOpen, MouthClosed, JawRight, JawLeft, JawForward, JawBackward, JawClench,
JawMandibleRaise`

Lips — suck/funnel/pucker (14): `LipSuckUpperRight, LipSuckUpperLeft, LipSuckLowerRight,
LipSuckLowerLeft, LipSuckCornerRight, LipSuckCornerLeft, LipFunnelUpperRight,
LipFunnelUpperLeft, LipFunnelLowerRight, LipFunnelLowerLeft, LipPuckerUpperRight,
LipPuckerUpperLeft, LipPuckerLowerRight, LipPuckerLowerLeft`

Mouth (26): `MouthUpperUpRight, MouthUpperUpLeft, MouthLowerDownRight, MouthLowerDownLeft,
MouthUpperDeepenRight, MouthUpperDeepenLeft, MouthUpperRight, MouthUpperLeft,
MouthLowerRight, MouthLowerLeft, MouthCornerPullRight, MouthCornerPullLeft,
MouthCornerSlantRight, MouthCornerSlantLeft, MouthFrownRight, MouthFrownLeft,
MouthStretchRight, MouthStretchLeft, MouthDimpleRight, MouthDimpleLeft, MouthRaiserUpper,
MouthRaiserLower, MouthPressRight, MouthPressLeft, MouthTightenerRight,
MouthTightenerLeft`

Tongue (12): `TongueOut, TongueUp, TongueDown, TongueRight, TongueLeft, TongueRoll,
TongueBendDown, TongueCurlUp, TongueSquish, TongueFlat, TongueTwistRight,
TongueTwistLeft`

Skipped: `SoftPalateClose, ThroatSwallow, NeckFlexRight/Left` (unused by all VRCFT
interfaces per docs). Add later by appending if an interface starts emitting them.

> The authoritative constant lives in code (`UEShapes.cs`, shared by mod and exporter);
> this table is documentation. Keep the exporter's copy generated from the mod's.

## Decision: drive blendshapes ourselves, not the avatar's FX controller

Researched properly on 2026-08-31 and **decided against** running the avatar's own animator
controller, despite it being available and initially the more attractive option.

What was found:

- A **fully merged FX controller already exists on disk** — VRCFury writes one during
  `Tools/VRCFury ▸ Build an Editor Test Copy`, which runs the SDK build pipeline locally with
  no upload. For the test avatar that's
  `Packages/com.vrcfury.temp/Builds/Rex_Que_urCheeks/VRCFury FX.controller`: 4.6 MB, 13 layers,
  327 parameters, 981 embedded clips. So obtaining it was never the obstacle.
- Its clips bind almost entirely to `blendShape.*`, plus `m_IsActive` toggles and two material
  floats. **No transform curves**, and Write Defaults is off — so it could not have fought our
  bone writes.
- It references four VRChat `StateMachineBehaviour` types (`VRC_AvatarParameterDriver`,
  `VRC_PlayableLayerControl`, `VRC_AnimatorTrackingControl`, `VRC_AnimatorPlayAudio`). These are
  compiled SDK types; outside VRChat they should deserialise to nothing and no-op. None writes
  bones or blendshapes.

So it was feasible. The reason not to is what the controller is mostly *made of*: **OSCmooth
smoothing layers and Binary_Gen quantisation**, both of which exist to fit VRChat's 256-bit
parameter sync budget and its slow parameter updates. Neither constraint applies to us — our
Photon stream carries raw bytes at whatever rate we choose, and we smooth on our own terms.

**Correction to an earlier claim in this document:** I asserted that blendshapes driven via
`BinaryOut/FT/v2/...` would ignore raw floats, forcing us to quantise into bits so the
controller could un-quantise them. A closer read of the template says otherwise — the
un-suffixed `FT/v2/<Name>` floats **are** the inputs, and the `BinaryOut/...`, `OSCm/...` and
bit-weight (`1`/`2`/`4`/`Negative`) families are internal to VRChat's sync path. Driving the
controller locally is a matter of setting ~140 plain floats.

So Option B is more viable than that paragraph implied. It stays deferred for the remaining
reasons — 4.6 MB of controller, a dependency on VRCFury's build step, and an eye-rotation layer
that drives **humanoid muscle curves** (`Left Eye In-Out`, etc.) rather than bone transforms,
which silently does nothing unless the avatar's eye bones are mapped into its humanoid rig.
Two values worth remembering if it's ever revisited: `FT/v2/EyeLidLeft`/`Right` default to
**0.75** and `FT/v2/PupilDilation` to **0.43**, so a controller fed all-zeros renders a face
with its eyes shut.

What we give up: an author's bespoke corrective shapes and any mapping that isn't name-matched.
Worth revisiting for a specific avatar that needs it; not worth paying for by default, when the
exporter already resolves 75 of 98 UE shapes to a renderer and index directly.

## Does VRCFT need a handshake? No — but the request can be missed

Answering directly, since it decides the setup instructions:

- **No avatar descriptor, no `/avatar/change`, no OSCQuery response is required.** With
  `/vrcft/settings/forceRelevant` sent once, VRCFT marks every parameter relevant and streams
  the lot. Nothing is negotiated and nothing has to be kept alive afterwards.
- **`/tracking/eye/LeftRightPitchYaw` and `/tracking/eye/EyesClosedAmount` need not even that** —
  they're sent unconditionally.
- **The catch is ordering, not handshaking.** `forceRelevant` is one fire-and-forget UDP packet.
  Sent once at melon init, it is simply lost if VRCFaceTracking isn't running yet — which is the
  normal case, since the game takes minutes to load and people start VRCFT afterwards. v0.20.0
  re-sends every 5 s until data arrives, then every 30 s as a keepalive in case VRCFT restarts.

### Eye motion: bones or blendshapes?

**Neither is required.** The exporter records `eyeUseBones` when the avatar has humanoid
`LeftEye`/`RightEye`, and the mod prefers bones because rotation is exact and needs no authored
shapes. An avatar without eye bones is driven through gaze *blendshapes* instead
(`EyeLookOutLeft` and friends), which are ordinary targets fed by the same derived mapping from
the signed `EyeLeftX`/`EyeRightX`/`EyeY` parameters. Both paths work; bones are simply better
when they exist.

### Seeing your own face

You can't — your head is scaled away in first person, and you couldn't look at it regardless.
**F6 is the mirror**: the preview avatar's face is driven from the same tracking data, so
expressions show up on a copy of yourself standing in front of you. That is the only way to
evaluate face tracking without a second player.

## Per-avatar mapping (manifest.json)

The Unity exporter auto-detects UE-named blendshapes on the avatar's renderers
(exact-name and common-prefix matches: `JawOpen`, `UE.JawOpen`, `ft.JawOpen`, `v2/JawOpen`)
and writes:

```json
{
  "faceTracking": {
    "shapes": { "JawOpen": {"renderer": "Body", "index": 42}, ... },
    "eyeBones": { "left": "Armature/.../LeftEye", "right": ".../RightEye" },
    "eyeUseBones": true,
    "eyeMaxDegrees": {"x": 25, "y": 20},
    "visemes": { "aa": {"renderer": "Body", "index": 3}, ... },
    "blink": ["EyeClosedLeft", "EyeClosedRight"]
  }
}
```

- **Eyes**: bone rotation (humanoid `LeftEye`/`RightEye`), driven from the **combined**
  parameters — see the correction below.
- **VRCFaceTracking does not send `EyeClosed*` or `EyeLook*` at all.** Its `UnifiedExpressions`
  enum contains neither: eye openness and gaze live in a separate eye structure and go out as
  combined parameters — `EyeLidLeft`/`EyeLidRight` (where **1 is open**, not closed) and
  `EyeLeftX` / `EyeRightX` / `EyeY` (signed −1..1). Reading the raw UE names returns nothing,
  so eyelids and gaze silently never moved in v0.19.0. Fixed in v0.19.1, which derives
  `EyeClosed* = 1 − EyeLid*` and splits the signed gaze axes into the four UE directions for
  any avatar whose blendshapes are named that way.
- **Alias table (implemented 2026-08-31).** The exporter runs two passes: canonical UE names
  first, then a fallback table covering ARKit and the older, coarser UE names most avatars were
  actually authored against. Every alias is tagged with a **kind**, because "we found a shape
  with a similar name" covers three quite different situations:

  | Kind | Meaning | Default |
  |---|---|---|
  | `Rename` | Same motion, different naming convention. `MouthSmileLeft` **is** `MouthCornerPullLeft`. | on |
  | `Split` | Source is coarser and the targets are its components — one `mouthPucker` → all four `LipPucker*`, ARKit `browDownLeft` → `BrowLowererLeft` + `BrowPinchLeft`. Sharing one index is correct here. | on |
  | `Approximate` | A genuinely different motion faked with a near neighbour — `MouthCornerSlant` from `MouthSmile`, `MouthUpperDeepen` from `MouthUpperUp`. | **off** |

  Approximations are off by default because a plausible-but-wrong mapping is worse than a shape
  that simply doesn't move: driving `MouthCornerSlant` from the smile shape makes every smirk
  read as a grin. They're one menu toggle away, and the report names exactly what you'd gain.
- **Collision rule.** Because UE splits several ARKit shapes, aliasing legitimately points two
  or more UE parameters at the *same* blendshape index. **The runtime must combine those with
  `max()`, not last-write-wins** — otherwise whichever parameter is applied last wipes the
  other. The exporter lists every collision it creates; the manifest records `"via"` on any
  aliased shape so a bad mapping is traceable after the fact.
- **Growing the table.** The exporter also dumps every *unconsumed* blendshape name on the face
  mesh. When a shape reports as "missing", its real name is in that list — that is the evidence
  for the next alias, rather than guessing at naming conventions.
- **No face tracking hardware / no shapes**: `hasFaceTracking:false` → peers keep the
  Vivox voice-energy jaw fallback (parity with vanilla behavior, driven through the
  avatar's `JawOpen`/viseme shape).

## Local apply

- Order: after the game's IK/animation (`LateUpdate`, and after our VRIK solves) so
  nothing stomps eyelid/jaw values.
- Smoothing: exponential toward target, ~10–15 Hz effective, per-shape saturation at
  manifest-configurable multipliers (some faces want JawOpen × 0.8, etc.).
- Blink arbitration: when face tracking is live, disable our `Idler`-style auto-blink;
  re-enable after 5 s of stale data (VRCFT closed / HW asleep) and decay all shapes to 0.

## Wire format (event code 142)

- Unreliable, sent only to modded peers (Phase 1 roster), 12–15 Hz tick.
- Payload: `[u8 seq][u8 count][(u8 shapeId, u8 value) * count]` — value = shape × 255.
  Delta-gated: include shapeId only if |Δ| since last-acked-keyframe > 2/255.
- Full keyframe (all active shapes) every 2 s and on peer join — handles loss without acks.
- Worst case all 98 shapes: 198 bytes/tick ≈ 3 KB/s; typical chatter ≈ 300 B/s.
- Receiver: per-sender `UEState`, lerp window ~100 ms, drop out-of-order seq (u8 wrap).

## Measured against a real avatar (2026-08-31)

First export of a production VRChat avatar (`Rex_Quest…`) through the exporter:

- **61 of 98** UE shapes under canonical names; **71/98 after renames and splits**, 75 if
  approximations are enabled. (An earlier run reported 69 — the extra 8 were gaze shapes wrongly
  picked up from a VRCFury debug panel.) The avatar is authored against the older UE naming:
  `MouthSmile*`, `browDown*`, `LipPucker{Left,Right}`.
- The **15 shapes still missing** after aliasing are `NasalDilation/Constrict*`, `JawBackward`,
  `JawClench`, `JawMandibleRaise`, `LipSuckCorner*` and the six exotic tongue shapes — the ones
  almost no VRCFT interface emits in the first place. This avatar is face-tracking-ready.
- It also carries a full **MMD** blendshape set (まばたき, 笑い, あ/い/う/え/お, …) alongside the UE
  ones. Harmless, and irrelevant to us — but it's most of the 229 unconsumed shapes.
- **The 8 `EyeLook*` gaze shapes are absent, and that is fine**: the avatar has eye *bones*,
  which is the preferred path anyway. The exporter now says so rather than listing them as
  losses.
- **15/15 VRC visemes** present (`vrc.v_*`).
- **Eye bones present** (`Eye_L` / `Eye_R` under `…/Neck/Head`) → gaze goes through bone
  rotation, the preferred path.
- **No jaw bone.** Typical for VRC avatars, which do jaw with blendshapes. So the Vivox
  voice-energy fallback (what non-face-tracked friends see, matching vanilla behaviour) must
  drive the **`JawOpen` blendshape**, not a bone. The exporter warns when an avatar has
  neither.
- Caution learned the hard way: an avatar with a VRCFury face-tracking **debug panel** carries
  a full second copy of the UE shape set on a floating quad. Any shape scan must exclude
  scaffolding, or gaze gets mapped to the debug window.

Vanilla game mesh, for contrast: 27 blendshapes — emotions, `blinkL/R`, head morphs, and 10
OVRLipSync visemes. **No ARKit or UE shapes at all**, which rules out reusing the vanilla face
for anything.

## Open items

- [ ] Verify current VRCFT build's manual-endpoint support (decides OSCQuery work).
- [ ] Confirm VRCFT keeps emitting with no headset-proximity signal (it tracks HMD state
      via OSCQuery/VRChat heartbeat in some configs).
- [ ] Author the ARKit⇄UE alias table (exporter side) and sanity-check on Dan's avatar.
- [ ] Decide whether tongue shapes are worth the 12 IDs on avatars that have them (yes,
      obviously. Tongue stays.)
