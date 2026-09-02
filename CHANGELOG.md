# Changelog

Versions are the mod's `Version` constant in `src/CustomAvatars/Core.cs`. This file was reconstructed from the git history on 2026-09-01.

## 0.40.0 — unreleased

### Added
- Player sizing is back, as a setting rather than a measurement. `AvatarSize` scales your play space and your game body by one number that nothing ever measures into: PageUp and PageDown step it, Home puts it back to 1, and 1 touches nothing. Changing size re-wears the avatar, peers and mannequins show you at your chosen size, and a fuse turns the feature off for the session if anything else rewrites the scale. (5c7f90b)
- A second mod, LootOverhaul, is scaffolded next to CustomAvatars with its feasibility notes in `docs/LOOT-OVERHAUL.md`. It does not compile or run yet. (fc0d511)

### Fixed
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
