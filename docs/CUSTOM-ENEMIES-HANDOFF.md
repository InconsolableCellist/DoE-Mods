# Custom enemies — implementation handoff

For the agent picking this up on Dan's machine. Read
[CUSTOM-ENEMIES-PLAN.md](CUSTOM-ENEMIES-PLAN.md) first: it holds the reasoning, the feasibility
verdict and the decisions. This file is the work order.

The plan was written without the dump or the game. **Treat every statement about Death's rig,
motion, attack, lighting and the outline system as a hypothesis until Phase 0 confirms it.**
Statements about what the existing mods hook are read from source and are reliable.

## What you have and what you need from Dan

| Thing | Where |
|---|---|
| Game folder with MelonLoader 0.7.3 and generated `MelonLoader/Il2CppAssemblies` | Dan's machine; builds resolve it through `src/Directory.Build.props` |
| `dump.cs` (Il2CppDumper) | Dan's machine, outside the repo. Re-run the dumper if the game has updated since. |
| Unity 2022.3.62f2 project with `unity/AvatarExport` | Dan's machine |
| The wolf model | Dan downloads it from Sketchfab (logged in, original format, not the glTF conversion) |
| Test runs in VR | Dan. Ask for `MelonLoader/Latest.log` and the recon file after each. |

Ground rules that apply here as everywhere in this repo: no game assets, no dump excerpts
beyond class and member names, no model files and no film audio in the repo or in releases.
Skins live in `UserData/CustomEnemies/` on each player's disk.

## Order of work

Phase 0a, then 0b, then 1a, 1b, 1c, then 2. Stop at each exit criterion and report. Do not
start tier 3 (variants); it needs a go from Dan.

## Phase 0a: recon

Add the probes to CustomAvatars' existing `Recon/` (it already has `HierarchyDump`,
`ModelRecon`, `ReconLog` and the dev hotkeys) rather than standing up a new mod for them.
Output goes to a recon file, not the main log. Two parts.

**From the dump (no game needed).** Record class names, member names and signatures in
`docs/GAME-INTERNALS.md` under a new "Enemies" heading:

- `Sauron.AI`: fields, the `references` and `specs` types, lifecycle methods, every
  `AE_*` animation event, how damage in and damage out are routed, the kill and despawn path.
- The module base class, its virtuals, and the list of concrete modules.
- Everything with `Death` in its name, and how `GameManager.HazardModifier.Death` turns into a
  spawned enemy (`death` vs `death_hazard` vs `death_nest`, and what `death_whistle` does).
- The spawner: who calls what to put an enemy on the floor, and what goes over Photon.
- `MaterialHandler`, `DissolveHandler` and any hit-flash code as used by enemies.
- Any class matching `Outline|XRay|Highlight|Silhouette`, and what the stick press calls.
- Camera post-processing components and the `Bakery` volume classes.

**From the game (hotkey probe, Dan runs it).**

1. On demand: `Resources.Load` each of `death`, `death_hazard`, `death_nest` and walk it:
   full hierarchy with components, every renderer (mesh, vertex and bone counts, materials,
   shader, keywords, `shadowCastingMode`), every collider (type, size, layer, trigger or not,
   which bone), the Animator (`isHuman`, `cullingMode`, `updateMode`, controller name, layer
   and parameter list, every clip with length and events), audio sources.
   Also try `playground_enemies` and `ai sandbox` and log what they are.
2. During a live Death encounter, once a second: root position, height above the floor
   (raycast down), velocity, current animator state hash and normalised time, whether the
   body is inside geometry (overlap test), renderer `isVisible`.
3. On each `AE_FX` / `AE_Footstep` from that enemy: the event argument and the state it
   fired in. On damage dealt to the player by it: attacker position, weapon collider bounds.
4. Lighting, once in the hub and once in a dark dungeon room: components on the VR camera,
   `RenderSettings` ambient mode and colour, light probe count, the realtime lights within
   15 m with type, range and shadows, and the keywords on a live enemy's material.
5. Outline: with a swapped avatar, press the stick and log every renderer the outline system
   holds, and anything newly enabled or created in that frame.

**Exit:** the eight questions in the plan answered in `GAME-INTERNALS.md`, each as a fact with
its source (dump line or recon file name). If an answer contradicts the plan, change the plan
before changing course in code.

## Phase 0b: the outline bug

Symptom, hypothesis and the two candidate fixes are in the plan's last section. Work in
`CustomAvatars/Avatars/AvatarSwapper.cs` beside `ApplyVanillaMeshVisibility`, which documents
why the vanilla body is hidden with `ShadowsOnly` and must stay in culling. Do not change the
hide mode to fix the outline: `enabled = false` and `forceRenderingOff` both make the game
stop solving the player (measured, see that method's comment).

Write the fix so it takes "a set of renderers standing in for another renderer", not "the
local avatar": enemy skins will need the same call.

**Exit:** the outline traces the avatar for the local player and for a peer, in both
triggers (stick press, teammate in another room), survives a size change and an avatar
switch, and is back on the vanilla body after the avatar is removed. CustomAvatars version
bumped, CHANGELOG entry written.

## Phase 1: `src/CustomEnemies`

### Shape of the mod

Copy the layout of a small ungated mod (`src/HazardTweaks` for the csproj and `Core.cs`,
`src/VisualCues` for a mod with no gate). Its own `Hooks.cs`, with the empty-method native
pointer check every mod here carries (see "Things that bite" in `src/README.md`: patching a
folded empty method kills the process).

- **No gate, no handshake, no Photon traffic.** A skin changes only what this client draws.
  It must work in public lobbies and next to unmodded players, and must never touch the
  enemy's transforms, colliders, stats or components other than renderer visibility.
- **Shared code by linked source**, not by a DLL reference: add
  `<Compile Include="..\CustomAvatars\Avatars\PoseRetargeter.cs" Link="..." />` and the like.
  Expect to refactor first: `PoseRetargeter.Build(Animator, GameObject, AvatarManifest)`,
  `SpringBones.Build(GameObject, AvatarManifest)` and `AvatarBundle.Acquire(AvatarManifest)`
  all take the avatar manifest type. Give them a narrow interface or plain parameters that
  both manifests satisfy, in a commit that changes nothing else, and re-test CustomAvatars
  before building on it. `PoseRetargeter` is keyed on `HumanBodyBones`; if Death's Animator is
  not humanoid, add a source lookup by bone name fed from the skin manifest.
- Settings in `[CustomEnemies]`, a reload hotkey, a dev hotkey to cycle skins and one to spawn
  the placeholder on the nearest enemy. Check the other mods' `Hotkeys.cs` for keys taken.

### Skin format

`UserData/CustomEnemies/<name>.enemy` plus `<name>.manifest.json`, written by a new exporter
menu item, `Tools ▸ DoE Mod ▸ Export Enemy Skin`, in `unity/AvatarExport/Editor/`. Reuse the
avatar exporter's stripping, shader vetting (SPS-I, DX11) and hashing. Tolerant parsing, as
`AvatarManifest` does it. Proposed fields:

```
schema, name, bundle, sha256, prefab, unity
replaces        prefab name ("Death") and/or Sauron ID; a skin matches either
scale, offset, rotation            relative to the vanilla root
bones           source bone name -> skin bone path, for the pose copy
weapon          { anchorBone (vanilla), prefabPath, offset, rotation, hideVanilla }
dangle          chains hung from the pose: legs, tail, robe hem, ears
                per chain: root, stiffness, damping, gravity, swayAmplitude, swayPeriod
eyes            [{ bone, offset, haloSize, colour, intensity }]
light           { enabled, bone, colour, range, intensity }
sounds          optional { vanilla sound name -> clip in bundle }
hideRenderers   "all" or a list of vanilla renderer paths
```

### 1a: attach

- Postfix `AI.InitializePooledObject` and `AI.OnRespawn` (`paramCount: 3`), as
  `LootOverhaul/Loot/Goblins.cs` does. Match on prefab name or `references.IDHash`.
- Enemies are pooled: build the skin once per pooled instance, keep it, and reset it on
  respawn (springs, fade, visibility). Destroy it only when the enemy object is destroyed.
- Hide the vanilla renderers the way recon says keeps the enemy animating. Default to
  `ShadowsOnly`, re-applied each frame only to the property we own, exactly as the avatar
  swap does and for the same reasons.
- Rigid follow of the vanilla root in `LateUpdate`, with the manifest offset and scale.
- Ship a placeholder skin built from primitives at runtime (no bundle), so the mechanism can
  be tested and released with no model on disk.
- Patches never throw into the game: wrap every body, log once, carry on.

**Exit:** the placeholder rides Death through a whole encounter, including despawn and a
second spawn from the pool, with no vanilla renderer visible and no change in Death's
behaviour. Removing the DLL leaves the game vanilla.

### 1b: motion

- Pose copy for spine, arms, neck and head by delta from a captured rest pose. Capture when
  the animator has settled, not on the spawn frame (the avatar code learned this the hard
  way: see `RetargetSettleSeconds`).
- Weapon: the skin's weapon is parented to the vanilla weapon bone, so the visible blade is
  where the damage collider is. Choose glaive or twin sickles from the measured reach.
- Legs, tail and robe hem are dangle chains: spring simulation plus a slow sine sway, with
  angle limits so nothing folds backwards or stretches. They lag on acceleration and settle
  when Death stops. Digitigrade legs are three segments; give each joint its own limit.
- If Death's ragdoll or death animation moves the vanilla bones, the pose copy follows for
  free. Check what the dangle chains do during it.

**Exit:** on video from Dan, attacks read correctly against the real hit timing, the weapon
tip matches where hits land, and the legs and hem trail the motion without stretching,
flipping or jittering at any speed Death reaches.

### 1c: look and feedback

Follow the "Look" section of the plan, in its order of preference, using recon answer 8.

- Body shader chosen so the wolf is lit like the creatures around it.
- Eyes: unlit emissive slot plus a camera-facing additive halo per eye, correct in both eyes
  under single-pass instanced rendering. Optional red light, off by default.
- Mirror hit feedback onto the skin: flash, and whatever else recon 5 found. Stuck arrows and
  decals parent to vanilla bones; check they sit plausibly on the skin and adjust scale
  before inventing anything.
- Dissolve or despawn: the skin leaves the way Death does, not by popping.
- Optional sound replacement through the `AudioManager.PlaySoundAt` funnel that
  `VisualCues/Cues/NoiseCue.cs` already patches. Local only.
- Exporter menu item, README for the mod, a row in the root README table and in
  `src/README.md`, CHANGELOG, and an entry in `src/release.py` (bare DLL, like the others).

**Exit:** a full encounter with nothing visibly vanilla, eyes that read as glowing in a dark
room, and frame time within noise of an unskinned encounter on Dan's machine.

## Phase 2: second skin from data

Reskin a different enemy (a skeleton is the obvious one: humanoid, walks, common) with a
second bundle. Walking enemies need no dangle and should work on pose copy alone.

**Exit:** no code change was needed. Anything that did need one is a gap in the manifest;
fix the format, not the special case.

## Model prep (Dan, or an agent with Unity access)

1. Download the original format from Sketchfab. Record triangle, vertex, bone and material
   counts. Target: under about 30k triangles, four bone weights per vertex, as few materials
   as the look allows (body, eyes, robe, weapon).
2. Import into the 2022.3.62f2 project. T-pose or a clean rest pose. Humanoid mapping if it
   takes; otherwise a manual bone list for the manifest.
3. The robe: if the model's poncho does not hide the hips, extend it, since the glide design
   relies on cloth covering where the legs start.
4. Split the eyes into their own material slot. Mark eye anchor transforms.
5. Mark the dangle chain roots (thighs, tail, hem bones, ears).
6. Weapon as a separate prefab with its pivot at the grip.
7. Export. The bundle and manifest go to `UserData/CustomEnemies/` and nowhere else.

## Traps already paid for in this repo

- A hidden renderer that leaves culling makes the game stop animating its owner.
- IL2CPP folds empty methods; check native pointers before patching.
- Use `Il2CppAssetBundle`, and never load the same bundle file twice (`AvatarBundle`).
- `MaterialHandler` binds to exactly one `SkinnedMeshRenderer`.
- `AI.OnKilled` can fire more than once for one death (`LootOverhaul/Loot/RunTracker.cs`).
- A game update can add parameters to a method; match overloads loosely
  (`NoiseCue.FindPlaySoundAt`).
- MelonLoader keeps old preference values; a changed default does not reach existing installs.
- Hand-written shaders must be single-pass-instanced aware or they draw in one eye.

## Reporting

After each phase: what was built, what recon contradicted, what Dan needs to test, and the
version bumped. Keep `GAME-INTERNALS.md` as the place facts go, and the plan as the place
decisions go.
