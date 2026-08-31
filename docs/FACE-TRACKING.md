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

### Getting VRCFT to send to us (investigation order, from PLAN Phase 3)

1. VRCFT manual OSC endpoint config (if current builds still allow bypassing OSCQuery).
2. Implement minimal **OSCQuery**: mDNS advertise `_oscjson._tcp` (+ `_osc._udp`) and
   serve the OSCQuery JSON tree listing our `/avatar/parameters/v2/*` float endpoints.
   VRCFT then discovers us exactly as it would VRChat. (~300 lines: mDNS responder + tiny
   HTTP server; both trivial over raw sockets.)
3. Fallback: standalone relay process.

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

- **Eyes**: prefer bone rotation (humanoid `LeftEye`/`RightEye`) driven by the 8 gaze
  shapes (`Out−In → yaw`, `Up−Down → pitch`); fall back to gaze blendshapes if present
  and no bones. Eyelids from `EyeClosed*` (+ `EyeWide*` counter-shape when present).
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
