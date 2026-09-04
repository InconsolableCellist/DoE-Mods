# Changelog

Versions are the mod's `Version` constant in `src/CustomAvatars/Core.cs`. This file was reconstructed from the git history on 2026-09-01.

## 0.42.5 (2026-09-03)

### Fixed
- Legs no longer run ahead of the body when you walk with the stick under full-body tracking. Your game body is a display object smoothed for the network, so it lags behind where you actually are while you move, and the leg solver adds that lag to the game's foot positions to bring vanilla feet from the lagging body under your avatar. With trackers the game's feet are solved to world targets and are not on the lagging body at all, so the lag was counted twice: standing still the legs were right, and the moment you moved the foot targets ran one to two metres ahead of your hips, the legs stretched to their cap and the feet locked far past it. The offset is now scaled down by how much a tracker is driving each foot, so a tracked foot gets none and a puck that has faded out hands the offset back smoothly.
- The leg line's reach-drift check no longer flags the one-shot height fit as drift; the build-time length is compared at the model's current scale.

### Added
- The `legs:` line reports the display body's lag and how much each foot is tracker-driven.

## 0.42.4 (2026-09-03)

### Fixed
- Legs no longer stretch away into the distance under full-body tracking. The leg solver measures the leg's length from its bones every frame; when a solve misses, the foot lock moves the foot bone to the target, and since the retarget writes only rotations, that displacement stayed in the shin's frame and was measured as shin length from then on, so the next solve planned for a shin that did not exist and missed by more. Every session started with a 164 cm miss on the first frame, the game's display body being far from ours, which left the leg reading 195 cm instead of 85 for the rest of the session. Without trackers that was survivable, because the game's feet sit close enough to the hips that a solve rarely misses again. With trackers the game's hip-to-foot distance is your real leg, this avatar's leg is shorter, and the solve missed on every frame: the measured reach ran from 85 cm to 390 cm within seconds. The foot is now put back on the end of the shin before every solve, the same fix the arm solver got in 0.42.0. The `legs:` line reports a reach that has wandered from its build-time value, with the build-time value beside it.

## 0.42.3 (2026-09-03)

### Fixed
- Full-body tracking no longer hauls the body sideways, or stretches the legs, when a puck loses sight of its base stations. SteamVR keeps such a puck flagged valid while it coasts on the puck's own motion sensor: the position drifts for a moment and then freezes wherever it got to, and the mod trusted every flagged-valid pose. One session's hip puck did this for ten seconds: the pelvis target sat on the frozen point, the whole body leaned after it, and the left hand ended up 122 cm from its shoulder while the dropout counter saw only the two frames at either end of the event. A pose now has to be one SteamVR itself reports as tracking (`Running_OK`) to drive a limb; anything else counts as a dropout, and the blip line says when a pose was flagged valid but not trusted. `FbtStrictTracking = false` restores the old behaviour, for comparison.
- A dropout no longer leaves its target nailed to a spot in the dungeon. The last usable pose was held in world space, so under joystick locomotion the player walked away from their own hip or foot. It is now held relative to the play space and rides along.
- A dropout that lasts is a missing limb, not a stuck one: after `FbtStaleSeconds` (the same setting that already governed a peer's quiet stream, default one second) the target's weight eases out over a quarter of a second and the game places that limb itself, then eases back in when the puck returns. Both transitions are logged with the reason and the duration. Peers get the same fade: the stream carries which of the sender's pucks are lost, and the receiver fades that target rather than following a held pose at full weight. Older senders never set the flags and read as fully tracked.
- Calibration refused for bad tracker data now writes the full tracker enumeration to the recon log. FBT that starts enabled from settings never runs the F10 path, which was the only place that dumped, so a session refused for a foot puck at 8401 m left no record of which device said what. A pose that is finite but farther than 25 m from the play space is also rejected at the read now, by name.

### Added
- Calibration logs which puck became which limb, whether by its SteamVR role or by geometry, where it was and what its tracking state was, on every attempt including the ones that lock cleanly.
- Calibration logs the game rig's own hip and foot positions relative to its head at the moment of lock, and whether its VRIK is enabled. Offsets are measured against those bones; two recalibrations seconds after a release came out 15 cm larger than a first calibration against an idle rig, and this line will say whether the rig had relaxed.

## 0.42.2 (2026-09-03)

### Fixed
- A peer's avatar no longer ends up a hip's height in the air, legs stretched down to the floor and arms down to their hands, after they die and come back. The pose is copied from their game body as a delta from a reference, and the one part of that reference the alignment at capture cannot repair is where the hips were: the reference is re-taken the frame a peer comes back to life, when their body is still a ragdoll, and the settled re-take half a second later found the body still being stood up (one capture stood 66° round from the rig it was copying; one leash trip after a respawn read 9.8 m). Measured from hips that were on the floor, every frame afterwards shoved the avatar the difference into the air, for as long as it was worn. Their own view was fine because their own reference was taken on their own machine at a different moment; two F4s fixed it because a fresh swap takes a fresh reference. The hips origin is now learned rather than trusted — the highest the hips sit while the body is solved, not ragdolled, and standing where a person's hips can be, for most of a second — and any reference that disagrees with it by more than a crouch is corrected to it. Until one has been learned, an origin that is not a standing body's is not followed. The settled re-capture also waits for the hips to have been steady, not just for the game to report it is solving. A crouch caught at capture, which used to stand the avatar up in the air by the depth of the crouch, is corrected the moment they stand. `RetargetHipsGuard = false` restores the old behaviour.
- Holding a T-pose now does to your avatar on everyone else's screen what two F4 presses do: peers are told it came off before they are told it went back on, so they build a fresh copy. Before, they heard only "still wearing it", which their side treats as a resize and keeps the copy they had — with whatever was wrong with it.

### Added
- The peer pose line reports the hips shift being applied and how far the avatar's hips are from the game body's. A separate warning fires, at most every ten seconds, when a peer's avatar hips are more than half a metre from their game body's, with the state of the guard: that is the body-in-the-air symptom, named directly.

## 0.42.1 (2026-09-02)

### Fixed
- A head no longer comes out turned 180 degrees round on an avatar whose eye bones are mapped left for right. The head is aligned on the line between the two eyes, which is the only measurement that reads the head's own turn — but that line's direction came from the humanoid map's labels, and nothing else on an avatar cares which eye is which, so a face grafted in from another model can carry `LeftEye` and `RightEye` swapped for its whole life with nothing in Unity to show it. Swapped, the same measurement points out the back of the head. The sign now comes from the rig's own shoulder line (the hip line if an arm is unpaired), measured separately on each rig, so a mislabelled pair is turned back before it is used. The pairing line says which of the three measurements aligned the head and whether an eye line had to be turned back.
- The head is also aligned now when one of the rigs has no usable pair of eye bones: it falls back to the shoulder line, which gives the same facing minus whatever the head was turned by at capture, rather than to where the eyes sit relative to the head bone — the measurement that pointed one avatar's head at its own back. Before, a rig with no eyes at all left the head unaligned entirely.
- Exporter: the report warns when the bone mapped as `LeftEye` sits on the avatar's right, so the mapping can be fixed at the source rather than worked around.

## 0.42.0 (2026-09-02)

### Added
- The elbow now points somewhere deliberate. The arm solver used to bend in whatever plane the vanilla body's arm happened to be in, and with a hand at the cheek drawing a bow that plane was noise: the elbow, folded tight and a forearm's length out at head height, whipped through the headset from frame to frame. Each arm now has a pole built from the avatar's own torso (down, outward, a little back, measured from its hips, neck and shoulder joints) blended with the hand's finger direction, which puts a drawing elbow out to the side where it belongs. The pole is smoothed in the torso's frame and rolled off the head when the elbow's circle passes too close to it. Only the roll about the shoulder-to-hand line is touched, so the hand cannot move. `ArmPoleHandWeight`, `ArmPoleFollowRate` and `ArmElbowHeadClearance` tune it; the arm log line reports the roll.
- `SwapArmTargetSource = "FpsHands"` drives your arms from the game's own first-person hand bones. Those are the hands you see in vanilla, and the game snaps them to a weapon's grip and holds them on a bow's string at full draw; the IK targets and the controllers keep following your real hand past the string, which is why the draw hand used to leave the bow. The target frame is measured from that rig's knuckles the same way the avatar's is, and its Animator is set to keep animating while its meshes are hidden.
- A hand-target watch on your own body logs when an IK target leaves its controller, when the first-person hand leaves the IK target, and any single-frame jump, each with the arm's state, so a hand that moves for no reason of ours can be told from one the solver moved.

### Changed
- `ArmStretchUpperShare` defaults to 1: all of the stretch goes to the upper arm and the forearm keeps its shape. A saved 0.7 from an older build still applies.
- The collarbone yield measured the arm with last frame's stretch included, saw no shortfall whenever the stretch had covered it, and let go. It now measures the arm's own length, so the shoulder contributes first and the stretch covers only what is left.

### Fixed
- The arm no longer flickers between two solutions, or drags its wrist, at full draw. The solver measures bone lengths from the bones each frame, the wrist lock moves the hand bone off the end of the forearm whenever a solve misses, and nothing put it back, so every miss left the forearm measuring longer and the next solve planned for a forearm that did not exist. The reach read 147 cm on a 55 cm arm in one session. The hand's rest position is now restored before every solve, and the arm log fires whenever the measured reach drifts from its build-time value.
- The hand no longer floats a foot off the bow at full draw. When the vanilla body's arm was straight, which it is whenever its animation reaches, the bend plane could not be found and the solve gave up for the frame with no bend, no swing and no lock, leaving the hand wherever the animation had put it. The bend now happens in the pole's plane instead, and the swing and the lock always run. The arm log says "bent from the pole" when that path was taken.

## 0.41.0 (2026-09-02)

0.40.0 was the working build number between 0.39.0 and this release; everything below ships as 0.41.0.

### Added
- Player sizing is back, as a setting rather than a measurement. `AvatarSize` scales your play space and your game body by one number that nothing ever measures into: PageUp and PageDown step it, Home puts it back to 1, and 1 touches nothing. Changing size re-wears the avatar, peers and mannequins show you at your chosen size, and a fuse turns the feature off for the session if anything else rewrites the scale. (5c7f90b)
- A second mod, LootOverhaul, is scaffolded next to CustomAvatars with its feasibility notes in `docs/LOOT-OVERHAUL.md`. It does not compile or run yet. (fc0d511)

### Changed
- Arm stretch is split between the bones instead of lengthening the whole arm uniformly: `ArmStretchUpperShare` (default 0.7) sends most of the extra length to the upper arm, which is mostly out of view in first person, and the forearm keeps more of its shape. 1 leaves the forearm undistorted entirely.

### Fixed
- Hands no longer come out rolled ninety degrees on an avatar whose hand bones are labelled differently from the one the mod was tuned on. The wrist rotation that turns the game's hand target into the avatar's hand bone was a constant in the config, (-90, 0, 180), measured on one rig; on a rig whose hand bone puts X rather than Z out of the back of the hand it left both palms a quarter turn off about the forearm, and the forearm twisted 45 degrees to meet them. That rotation is now measured from the avatar's own bones when it is worn — wrist to middle knuckle for the fingers, little knuckle to index knuckle across the palm — and the swap log says which bone axis each hand runs its fingers and its back along. The config entries are renamed `SwapHandTrimLeft/Right X/Y/Z` and default to zero, so a saved (-90, 0, 180) from an older build is ignored rather than applied twice. Copied poses (peers without solved arms, the pedestal mannequin) pin the hand's roll on the same knuckle line, where before they matched the finger direction only.
- An avatar whose root GameObject carried a scale in the scene (a rig authored in inches or centimetres, made human-sized by a 0.024 or 0.01 typed onto its root) no longer spawns dozens of metres tall on the wearer, the pedestal and the preview. The mod overwrote the root's scale with the manifest's `suggestedScale` and threw the compensating factor away; it now multiplies whatever the prefab root carries. The exporter folds that root scale into `suggestedScale` and ships a unit root, reports the root scale and the rig's `humanScale` (42 means inches, 100 centimetres), and the manifest carries the folded value as `rootScale`. Old bundles and new ones land at the same size.
- An avatar whose rest pose faces a different way from the rig it is copying no longer stands on the pedestal with its head and arms toward you and its chest and legs turned 180 degrees the other way. The torso and legs were aligned by direction only, which says nothing about which way a near-vertical bone faces; they are now aligned on the hip line and the shoulder line as well, the way the head already was on its eyes. The pairing line reports how far round the avatar stood when it differs.
- A PhysBone root with several children no longer gets rotated toward every strand under it. The exporter wrote the root into every chain, so one avatar's Head was pulled toward fourteen hair tips a frame, its toe roots toward four toes each and its dress root toward three panels: the head faced backwards, the feet twisted toward the floor and the dress flickered. The exporter now follows VRChat's Multi Child Type (the root stays with the animation unless it is set to First), and the mod drops a shared first bone from every chain that has it and lets one chain own any bone further down, so bundles from older exporters are repaired on load.
- The head is now aligned by the line between the two eyes rather than by where the eyes sit relative to the head bone. On a rig whose eye bones sat well above the head bone with a forward-leaning neck, the old measure came out behind the head and the mannequin faced its own back. Feet are aligned on the shin as well as the toe direction, so a foot can no longer match its toes with the sole turned sideways.
- PhysBone collider radii are scaled by the bone they sit on, as their offsets already were. Taken raw, an inch-authored rig's 3.6 unit hip collider was a 3.6 metre sphere that every strand and toe was pushed out of each frame: tail, dress and hair stood straight out from the body and flickered between colliders, and the toes curled toward the floor. The collider log line now shows the world radius next to the raw one.
- A dropped item is only put back to full size when it is exactly your size factor too small, the one case the size code was written for. Any other ratio is the game animating the item (spawning it in from the inventory, shrinking it into a holster) and is now left alone; scaling a mid-animation item by its ratio could make it enormous.
- Sizing waits until your game body has spawned and settled for a second, and lets go whenever the body disappears or is replaced. Being scaled while the game set your height up left your eyes at full height with everything else shrunk, and Home then overshot by a metre; scaled after the spawn, the numbers are exact. A `SizeDebug` setting prints every transform between the headset and the rig at each change.
- A bundle handle that had lost its prefab on a scene change evicted the fresh cache entry when it was released, so every swap for the next two seconds failed with "LoadFromFile returned null" and was retried each frame. The stale handle now leaves the cache alone, and a failed self re-apply retries once a second.
- Gripping no longer bends the whole right hand, or a single finger, backwards. Each finger's bend axis is now derived from the avatar's own palm direction, so it comes out right on both hands; `HandCurlFlipLeft` and `HandCurlFlipRight` remain as an escape hatch. This fix also went out on its own to a tester as a 0.39.1 build. (8ff4c5d)
- The avatar's height now comes from its manifest rather than a live measurement, so an avatar caught mid-settle at spawn no longer comes out enormous until you re-wear it. (d7908ef)
- Avatars that carry a unit scale inside the rig (one keeps x70 above its hips) no longer have their hips shoved by every bob of the game's hips. Their mannequin stays on the pedestal and the wearer's legs stop missing by a metre. The mannequin's model is also pinned to the pedestal every frame, the prefab's own Animator is switched off on every copy the mod spawns, and the exporter drops controllers and root motion so future bundles ship without one. (68ec553)

## 0.39.0 (2026-09-01)

### Changed
- Holding a T-pose now takes the avatar off and puts it back on, exactly as pressing F4 twice would, instead of re-binding in place. The in-place re-bind did not restore the position and fit a fresh swap gives. The same chime plays when it takes. (7997eed)

### Fixed
- Exporter: a garment rig authored at a different unit scale from the avatar (armature at 100 against an avatar at 1) no longer renders at a hundredth of its size when its armature is linked. The scale ratio is kept and whatever still separates the two bones is folded into the mesh's bindposes. (9c1dbf5)

## 0.38.0 (2026-09-01)

### Added
- Holding a T-pose for a second and a half re-binds the avatar: the reference pose is taken again, both solvers are rebuilt, the fit is redone and the springs reset. That is what taking it off and putting it back on had been doing for people whose legs came out twisted. A rising two-note chime confirms it, the same one FBT calibration plays, and a low buzz sounds if it fails; both honour `FbtAudioCues`. (7a6a68c, 792ceb5)

### Fixed
- The wrist no longer visibly pulls the hand off the forearm in first person. The arm stretches to cover the shortfall (up to `ArmStretch`, default half again) and the wrist covers only what is left; legs likewise with `LegStretch` at a quarter. (7a6a68c)
- Digitigrade knees no longer solve backwards or flip from frame to frame: the bend plane now comes from the vanilla leg, whose knee is always forward. (7a6a68c)

## 0.37.0 (2026-09-01)

### Fixed
- Feet are now solved to where the game's own feet are, instead of hanging off the head at a fixed leg length. They no longer float when you stand taller than the fit was measured at or sink into the floor in a crouch, and they follow the game's own planting, stepping and full-body trackers. For your own avatar the leg targets are re-based onto the unsmoothed player object so the legs don't trail when you stick-move. (6cb5a22)

## 0.36.0 (2026-09-01)

### Removed
- The player-resizing feature from 0.33 through 0.35 is removed. Two real runs showed the game rewriting the rig's scale every frame while the measured eye height fed back into the scale, so players shrank on their own and could not get back to the size they started at. The rig is the game's; the mod never touches it. (Sizing returns, done differently, in 0.40.0.) (227f52e)

### Changed
- The avatar is fitted once when it goes on, head at your eyes and feet on the floor, and never re-measured while worn. The standing-taller refit from 0.35 is gone. (227f52e)
- Peers' arms are now solved to their own hand targets by default rather than copied from their rig, which stops their elbows flicking between the walking animation and the solved pose. (227f52e)
- FBT calibration thresholds are back in plain metres. (227f52e)

## 0.35.0 (2026-09-01)

### Fixed
- Hands now land exactly on their targets (`ArmLockHands`). Arm stretch had been compounding, with the forearm scaled twice over, so only the upper arm is scaled now and any previous stretch is divided out before measuring. A full arm geometry dump prints once per swap and whenever a hand misses by more than its reach explains. (5fac664)
- Wrist twist is measured against the bind pose rather than the animation's wrist, and anything past `ArmWristTwistLimitDegrees` goes to the forearm. (5fac664)
- Height scaling no longer alternates between two wrong answers on every refit; standing eye height and the avatar's base head height are measured once and every fit is computed from them. PgUp no longer switches the feature on, Home suspends it for the session instead of rewriting the config, and dropped weapons are no longer shrunk by the prop restore. (5fac664)

## 0.34.0 (2026-09-01)

### Added
- Player scaling, off by default behind `HeightScalingEnabled`: be the size of your avatar rather than just wearing its face. A single scale on the VR rig shrinks eye height, reach and hit capsules together, so a small player really is a smaller target with a shorter reach. Peers are told your size on the avatar message. Merged in from a side branch. (433d0bb, 47c2010)
- The overlay strip now shows author, title and site. (cdb5716)
- Exporter: VRCFury Armature Link is baked at export, so clothes skinned to their own armature follow the body instead of standing in place while the body walks off. Toggles, Full Controller and mesh merging stay unapplied by design. The export report names each skipped link and any bone pose mismatch, since those are what otherwise ship as a quietly deformed mesh. (04fa1c1)

### Fixed
- A peer's head no longer pitches down at the floor. Pitch comes from the neck-to-head axis and the eyes only decide which way round the head faces. (a9e5683)
- Height scaling could leave you permanently the wrong size, and its drift check was pinning the rig's scale for everyone, including players who never used it. The vanilla body now scales with the rig, eye height is measured in rig-local space, turning the setting off restores everything it touched, and the scaler is not constructed at all unless the setting was already on at launch. F12 is dropped as a shortcut because Steam takes it for screenshots; back to vanilla size is Home. (a6b5841)

## 0.33.4 (2026-09-01)

### Added
- `SwapSolvePeerArms` (off by default) solves a peer's arms to their own hand targets, so their hands land where their real hands are rather than slightly inside them. (4f514b1)

### Fixed
- A peer's head now points where they are actually looking. The head was the one bone never lined up at capture, so it kept whatever offset the two rigs had and left a peer's face turned up and to the right for as long as they wore the avatar. (4f514b1)
- The reference pose now waits for a respawning body to settle (`RetargetSettleSeconds`), so it is no longer taken while the body is still snapping round to face its spawn direction, which left it turned 180 degrees for the rest of the session. (4f514b1)

## 0.33.3 (2026-09-01)

### Fixed
- Peers no longer slide around in a slack neutral pose with no arm reach, no head turning and no full-body tracking. Hiding their vanilla body had told the game they were off screen, so it stopped solving them. The body is now hidden as shadows-only, which leaves a human-shaped shadow under each avatar, with bounds large enough that it never counts as culled. (49374be)
- The reference pose is no longer captured against a rig the game isn't solving; if the game isn't solving at swap time, the swap says so and retries on the first solved frame. A per-peer diagnostic line (`DiagPeerPoseSeconds`) reports which half is broken. (49374be)

## 0.33.0 (2026-09-01)

### Added
- Mannequins get a tail, a face and fingers: spring bones, the face driver and the hand poser now run on each pedestal avatar. Your own pedestal mirrors your live tracking; a peer's runs from the shape values and finger curls already being streamed for their in-world avatar. (3fd206a)

### Fixed
- A peer's tail stopped swinging for exactly as long as they were face-tracking; an early return was skipping the spring simulation. (3fd206a)

## 0.32.1 (2026-09-01)

### Fixed
- Under full-body tracking each foot now sits sideways where its tracker says, not at the rig's own stance width, removing a constant sideways error on every step. The calibration format bumps to v4, so older calibrations are discarded and you will be asked to calibrate again. (05763d2)
- First attempt at keeping peers animating while their vanilla body is hidden, and a stray copy of the vanilla IK restore that ran every frame is removed. (The hiding approach turned out not to work either and was replaced in 0.33.3.) (4787700)

## 0.32.0 (2026-09-01)

### Added
- Full-body tracking with SteamVR trackers on hips and feet. F10 turns it on and off. F11, or simply holding a T-pose, shows a puck on every tracker SteamVR can see, and squeezing both triggers for a second locks the calibration in, with beeps confirming each step since you can't see the console from a headset. Calibration is stored per tracker serial so the next session doesn't ask again, and modded peers see your legs over a new tracker stream. Known rough edge: feet land a little to the side of your real feet. (0fc9c0e)

## 0.31.0 (2026-08-31)

### Fixed
- The avatar is anchored at your head rather than at the play-space origin. Taking a physical step in your room used to leave the body a foot behind you with the hand targets up to a metre from the shoulders, which is most of what the last several versions had been blaming on the arm solver. The avatar is scaled to your head height, measured once a second while you are standing so crouching doesn't shrink it. (1cc5a0c)
- When dead, the arm solver stops so the corpse's wrists don't strain toward your controllers, the head comes back so you can see your own body from outside, and the avatar is no longer dragged back under a head it is no longer attached to. (1cc5a0c)
- The game-over mannequin's wrists no longer stick straight out sideways. (1cc5a0c)

## 0.30.0 (2026-08-31)

### Changed
- Your own avatar, the F6 preview and the mannequins now work in the main menu and outside a room, since they only change what your own machine draws. Anything that touches other players still requires a private lobby with everyone on the same build, and everything stays off in a public lobby or one with a vanilla player. (094e77d)
- The settings file is written once at startup, so a fresh install has a file to edit before its first clean exit. Settings comments are trimmed to the ones that say something the name doesn't. (094e77d)

## 0.29.0 (2026-08-31)

### Changed
- The mod is renamed from DoEFriendsMod to CustomAvatars: the assembly, the namespace, the UserData folder and the Photon properties players use to find each other. On first start an existing `UserData/DoEFriendsMod` folder is moved to `UserData/CustomAvatars` and the log says so; if both folders exist it touches neither and asks you to sort it out. (972289c)
- Settings are now in three sections, the ordinary ones, per-avatar tuning and diagnostic switches, each with a comment saying what it does. (972289c)
- The avatar goes on by itself when you join a private lobby; take it off with F4 and it stays off. Hotkeys no longer disappear when the recon dumps are switched off. (972289c)

### Fixed
- When a hand is out of reach the collarbone now turns with it, by an amount worked out from the avatar's own bones, so hands stop missing their targets by up to 10 cm at full extension. Anything still out of reach is stretched as before. (67b81a8)

## 0.28.0 (2026-08-31)

### Added
- Face tracking is streamed to peers: at most ten messages a second, one byte per shape, only for shapes that moved, and nothing at all for a still face. A peer's face relaxes when the stream stops and falls back to the voice-driven jaw. The mod logs its own message and byte rate once a minute. (06815ca)

## 0.27.2 (2026-08-31)

### Fixed
- Pressing F4 after a scene change no longer throws a null reference. The bundle cache checks its prefab is still loaded and reloads the bundle if Unity unloaded it, and says what is wrong instead of letting the engine throw. (9f2c856)

## 0.27.1 (2026-08-31)

### Fixed
- Hands were landing about 10 cm off target even well inside the arm's reach, because the upper arm was being twisted about a line that doesn't pass through the shoulder. That twist is gone; the wrist keeps its share of the roll. (c5302b5)

## 0.27.0 (2026-08-31)

### Fixed
- Arm stretch now really lengthens the upper arm and forearm rather than only changing the elbow angle, eased so an arm at the edge of its reach doesn't pop. The solver reports once a second how far each hand ended up from its target, along with arm length and the distance asked for, so a short arm can be told apart from an unreachable target. (4900024)

## 0.26.0 (2026-08-31)

### Added
- The mouth now opens from voice loudness whenever face tracking isn't supplying a jaw, using the value the game already works out for every player. That covers anyone without face tracking and peers with no face stream, with no network traffic. (00449dd)

### Fixed
- Eyes are fully open at rest. VRCFaceTracking rests the eyelid value at 0.75, and reading it as one minus the value left the eyes a quarter shut; values above the rest point now drive the wide-eyed shapes. Eye travel defaults are 50 and 60 degrees, measured in the headset. (00449dd)

## 0.25.0 (2026-08-31)

### Fixed
- Looking left no longer sends one eye up and the other down. Eye bones are rotated in the avatar's own frame rather than their mirrored local axes, and the eye axis settings, the eye sweep test and the periodic face logging are removed. (342b086)
- Held weapons no longer drift out of the hand while moving with the stick: your own avatar's root now follows the unsmoothed player object rather than the network-smoothed display body. (342b086)
- Wrist roll is shared with the upper arm as well as the forearm, so a palm fully down no longer collapses the mesh. (342b086)

Version 0.24.0 was skipped.

## 0.23.0 (2026-08-31)

### Fixed
- Smiles, sneers, brows and mouth movement now work on avatars built on the face tracking templates. Those are driven by VRCFaceTracking's combined parameters (SmileFrownLeft, JawX and so on), which weren't being read; each raw name now falls back to the combined parameter that carries it. (a4b99c4)
- The face override generator names its file after the avatar root rather than whichever child object was selected. (a4b99c4)

## 0.22.0 (2026-08-31)

### Added
- Per-avatar face tuning lives in an overrides file next to the avatar, with a min, max and gamma per shape, so it survives a re-export. A Unity menu item reads the ranges out of the avatar's own face tracking controllers and writes that file; an existing file is never overwritten. (4e1e964, 32b6444)
- An option for the arms to follow the controllers directly instead of the game's smoothed hand targets, not yet the default. (4e1e964)
- F7 dumps every face tracking parameter received, which is the only way to tell a shape the hardware never produces from a wrong name. (4e1e964)

## 0.21.0 (2026-08-31)

### Fixed
- The red finger outline around held weapons is gone. Everything under the first-person arms rig is now hidden except the weapon and kill counter panels, and each hidden renderer is logged by name. (2a948e8)
- The custom hand sat a little off the weapon at full reach; the arm can now stretch a few percent. (2a948e8)

## 0.20.0 (2026-08-31)

### Added
- The F6 preview avatar is now a mirror: its face is driven from your own tracking data, so you can judge your expressions without a second player. (6c0abce)

### Fixed
- The request that tells VRCFaceTracking to send everything is re-sent every few seconds until data arrives, then occasionally as a keepalive, so face tracking works when VRCFaceTracking starts after the game. (6c0abce)

## 0.19.1 (2026-08-31)

### Fixed
- Eyelids and gaze now move. They are read from the EyeLid and signed EyeLeftX, EyeRightX and EyeY parameters that actually carry them. (87e112d)

## 0.19.0 (2026-08-31)

### Added
- The face is driven directly from Unified Expressions values: blendshapes from the exporter's mapping, eye bones from the four gaze values, and the face relaxes if the data stops arriving. The avatar's own FX controller is deliberately not run. (cd0897d)

## 0.18.0 (2026-08-31)

### Changed
- Death now ragdolls the custom avatar instead of hiding it. (6affb17)

### Fixed
- Changing scene no longer leaves an exception every frame on the end-of-mission screen, a silently broken F4, or a player with no body at all. Destroyed objects are now detected the same way Unity detects them, the avatar goes back on when the player object underneath it is replaced, and reverting restores the vanilla body first. (6affb17)

## 0.17.0 (2026-08-31)

### Added
- Face tracking data is received from VRCFaceTracking over OSC, using a small built-in OSC reader rather than an extra dependency. Nothing consumes the values yet. (ceae0bc)

## 0.16.0 (2026-08-31)

### Fixed
- The mannequin no longer T-poses. The two skeletons are lined up before poses are copied, so an imported avatar's arms no longer sit ninety degrees off the game's. (4f9e510)
- Turning your palm up no longer pinches the wrist mesh; part of the forearm twist goes to the forearm bone. (4f9e510)

## 0.15.0 (2026-08-31)

### Changed
- Your own arms are now solved by a two-bone IK aimed at the hand targets, because the game switches IK off on your own third-person body and copying that pose left the arms floating in an A-pose. Remote players keep the copied pose; the legs are unchanged. (e05f188)

### Removed
- The F10, F11 and F12 diagnostic shortcuts. The config file was working all along, and F12 is Steam's screenshot key. (e05f188)

## 0.14.0 (2026-08-31)

### Added
- `SwapArmSource = IKTargets` aims the arms at the hand targets with a two-bone solver while everything else still comes from the game's pose, and the vanilla body can be forced to solve fully. F10, F11 and F12 toggle the diagnostic switches. (50e2bf7)

## 0.13.0 (2026-08-31)

### Added
- Custom avatars appear on the equipment room mannequins, with the mannequin's own idle animation and blinking. (ef525c8)

## 0.12.1 (2026-08-31)

### Added
- A desktop-only status panel showing gate state, the chosen avatar, whether it is worn, and the key list. (dad284f)

### Changed
- The avatar always follows the body when copying the game's pose; `SwapFollowVanillaRoot` no longer gets a vote, since left false it parked the avatar wherever it spawned. (dad284f)

## 0.12.0 (2026-08-31)

### Added
- Peers see each other's fingers close: ten bytes, one per finger, sent only when something moved, with an occasional keyframe for late joiners. (4ce6c83)
- On death the vanilla body is shown for the ragdoll and the custom avatar comes back on respawn, rather than standing upright next to the falling body. (4ce6c83)

## 0.11.1 (2026-08-31)

### Fixed
- PhysBone limits now honour all three shapes (Angle, Hinge and Polar) and the limit rotation, so a cone is no longer centred on the wrong axis. Avatars need re-exporting to pick up the two new fields. (a4a6246)

## 0.11.0 (2026-08-31)

### Changed
- The avatar now copies the game's own solved pose instead of running VRIK, which is the new default for `SwapPoseSource`. This fixes remote players' arms and weapons not moving and turning the head dragging the whole torso. (7f9803d)

### Fixed
- Clothing meshes no longer blink out, one finger no longer bends backwards, and tails can no longer fold back through the body. (7f9803d)

## 0.10.0 (2026-08-31)

### Added
- Players now see each other's avatars. Only the avatar name and a short hash go over the wire; if a peer has a different build of an avatar with the same name, theirs is not shown rather than showing the wrong model. F2 cycles through installed avatars and saves the choice, which also fixes two clients both picking the alphabetically first one. Documented in the README. (0681fa6, b27c704)

## 0.9.2 (2026-08-31)

### Fixed
- Fingers weren't moving on controllers without finger tracking (Galaxy XR through Virtual Desktop), because SteamVR hands back all-zero curl data on those. Per-finger data is only trusted once it has reported something non-zero; grip and trigger drive the fingers until then. A `HandPoseDebug` setting prints the raw values. (c44d4ed)

## 0.9.1 (2026-08-31)

### Fixed
- VRIK's procedural locomotion threw the avatar about 97 m away within a frame, and the leash dragging it back looked like the avatar never appeared. After five trips it now switches itself off and explains in the log. Legs still don't step. (72da851)

## 0.9.0 (2026-08-31)

### Added
- The avatar's fingers curl from the controllers: per-finger on Index through SteamVR skeletal input, otherwise the trigger bends the index finger and the grip bends the rest, the same split the game uses. (33d1c50, bbf71b8)

## 0.8.0 (2026-08-31)

### Changed
- Locomotion settings can be changed while the game is running, and a `SwapFollowVanillaRoot` setting lets VRIK's locomotion own the root, as another try at getting the legs to step. (4de0b6d)

## 0.7.2 (2026-08-31)

### Fixed
- The avatar swap works in first person. The model is positioned from the game's own player model every frame, wrists get a configurable rotation offset, the head bone is scaled away so it doesn't clip the camera, and the first-person arms are hidden except for the weapon and kill counter panels. Settings that change how the swap looks are printed at startup and on F3. INSTALL.md added. (339581d)

## 0.5.3 (2026-08-31)

### Added
- Initial import: recon logging, the private lobby gate and peer roster, the Photon event transport, avatar loading from AssetBundles, the VRIK avatar swap, the spring bone solver, and the Unity editor script that turns a VRChat avatar into a bundle and manifest. (a22f73d)
