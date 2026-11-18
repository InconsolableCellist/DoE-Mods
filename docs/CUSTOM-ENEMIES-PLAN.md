# Custom enemies — feasibility and plan

Status: **approved through Phase 2; Phase 0a done except a dark-room lighting reading, Phase 0b
built and waiting on a test with a friend** (2026-11-17, see "Phase 0a: what the dump settled",
"Phase 0a: what the probe settled" and `GAME-INTERNALS.md` → Enemies). Written 2026-11-17 from the repo alone (no
dump, no game in the session that wrote it). Everything marked **[recon]** is an inference that
has to be confirmed against `dump.cs` or a running game before code is written on top of it.

This file is the reasoning and the decisions. The step-by-step work order is kept outside the
repo.

## Decided (Dan, 2026-11-17)

- Scope is Phases 0 to 2. Variants (tier 3) are a later go/no-go; tier 4 is not scheduled.
- A new mod, `CustomEnemies`.
- First model: the Sketchfab "Death Wolf Rig (Puss In Boots 2)". Poly, bone and material
  counts to be checked when the file is in hand.
- **Glide by design.** The wolf floats in a robe as Death does, legs up and dangling, mildly
  animated: procedural at first, Blender Actions later.
- Materials and emission carry the look: glowing red eyes and lighting that matches the
  dungeon's mood are requirements, not polish.
- Implementation happens on Dan's machine, not in a cloud session.

Goal, in order:

1. Replace the look of the Death hazard enemy with a different model (first skin: the wolf).
2. Make that a general "enemy skin" mechanism, so any enemy can be reskinned.
3. Variants: an existing enemy's brain with a new body and new numbers.
4. Entirely new creatures with their own behaviour.

## Verdict

| Tier | Feasible? | Risk | Needs everyone modded? |
|---|---|---|---|
| 1. Reskin Death | Yes. It is the CustomAvatars trick pointed at an enemy. | Low for the mechanism, **medium for lighting and glow** (below) | No, local and cosmetic |
| 2. Skin framework | Yes, falls out of 1 if 1 is built data-driven | Low | No |
| 3. Variants | Probably. Depends on how spawning and stats are authored. | Medium | Yes |
| 4. New behaviour | Possible, expensive, fragile across game updates | High | Yes |
| Outline bug | Very likely fixable; cause not yet identified | Low to medium | No |

## What the repo already establishes

- Enemies are `Il2CppSauron.AI` components. Mods already hook `AI.InitializePooledObject` and
  `AI.OnRespawn` (both run on every client when a pooled enemy takes the floor,
  `LootOverhaul/Loot/Goblins.cs`), `AI.OnKilled(killerActor, damageType)`, and the animation
  events `AI.AE_Footstep` / `AI.AE_FX` (`VisualCues/Cues/NoiseCue.cs`).
- An enemy is identified by `ai.references.IDHash` against `SauronKeys.IDHash*`; it also carries
  `references.PVO` (PhotonView), `references.defaultScale`, `references.spawnedAsClass`
  (4 = miniboss, 5 = boss), `type`, `armorTier`, and a `modules` list (`AIRunner`,
  `AIKamikaze`, ...). `Sauron.ActiveAIList` enumerates live enemies.
- **Every client animates every enemy locally** (that is why the noise cue works without
  networking). A skin therefore needs no network traffic at all.
- Enemy prefabs are `Resources` assets with plain names: `death`, `death_hazard`, `death_nest`,
  `death_whistle`, plus `wolf` (the pet, a quadruped), `skeletons_*`, `imp_*`, `golem`,
  `wraith`, `zombie`, ... (`docs/RESOURCE-PATHS.txt`). `playground_enemies` and `ai sandbox`
  exist and may be a ready-made test scene **[recon]**.
- Death is a hazard: `GameManager.HazardModifier.Death`, switched on in
  `GameManager.InitHazardsForLevel` (`HazardTweaks/Hazards.cs`).
- CustomAvatars already contains every hard part of a skin: bundle loading
  (`AvatarBundle`), delta pose retargeting between unlike skeletons (`PoseRetargeter`), spring
  bones for tail and cloak (`SpringBones`), procedural legs (`LegIK`, `WalkState`), the Unity
  exporter, and the hard-won rule that **a hidden renderer must stay in culling**
  (`ShadowsOnly`, not `enabled = false`) or the game stops animating the thing.
- `NetworkObjectPool` keys are plain strings and a `dfm:` prefix scheme for injected prefabs
  is already planned (PLAN Phase 4b). Mod Photon event codes 140–189 are taken (CustomAvatars
  140–149, LootOverhaul 150–159, VisualCues 160–169, Descent 170–179, StayPutVR 180–189);
  190–199 is the only free block.

## Phase 0a: what the dump settled (2026-11-17)

Facts with sources are in `GAME-INTERNALS.md` → Enemies. What they change here:

- **Death's attack is a distance check** (`AIDeath`: `killTime`, `killDistance`; player side
  `CombatNotification.DeathTouch`). There is no weapon collider to line up with; the visible
  weapon is cosmetic and sized to read as the kill distance. Recon 3 is mostly answered.
- **Death is very likely immortal** ("Immortal Death", the immortal kill/revive path on `AI`)
  and is put down by the `DeathWhistle` prop (`KillDeathDelayed`), with directional fall
  clips (`AIDeathAnimation.fallDirections`). The skin has to survive a put-down and revive.
- **Outline cause found** (recon 7): `FXOutline` keeps its own renderer list, resolved from
  the `CharacterPrefab`'s hierarchy, and `FXOutlineManager` draws it from a command buffer,
  which ignores `ShadowsOnly`. The avatar lives at the scene root, so it is never in that
  list. The fix is the "renderer list" one below, plus `shellSource` for HD mode.
- **Hit flash and hover glow** (recon 5) are a `MaterialPropertyBlock` colour on one renderer
  (`HitFlashHandler`); dissolve swaps materials (`DissolveHandler`). Mirroring both is
  bounded work: copy a colour, follow a float.
- **Bloom is optional per player** (recon 8): the halo stays required.
- **Pausing an enemy disables its renderers** (`AI.PauseEnemy`): the skin follows it.
- **Specs are shared ScriptableObjects** (recon 6): tier 3 overrides need per-instance
  copies or a patch on `References.GetSpecs*`. Spawns are chosen by `AIClass`
  (`CombatRunner.SpawnEnemy`).
- **A test bench exists**: the sandbox spawns Death (`SandboxUI.SpawnDeath`,
  `CombatRunner.SpawnSandboxEnemy(EnemyType.Death = 33, …)`).

## Phase 0a: what the probe settled (2026-11-17)

One solo sandbox run with the CustomAvatars probe (details in `GAME-INTERNALS.md` → Enemies →
"What the runtime probe read"). What it changes for the plan:

- **Death floats** (`navSolverType` Flight, 0.25–0.5 m off the terrain) and has a `Teleport`
  module. The glide decision fits the vanilla motion; the follow has to snap on a teleport,
  not ease into it.
- **Death's Animator is humanoid** (`Soul Keeper_01Avatar`). The upper-body pose copy can go
  through Unity's humanoid muscle space (`HumanPoseHandler`) instead of a hand-written bone
  map, if the skin is imported as humanoid too. The bone map stays as the fallback.
- **Four states carry the encounter**: `Spawn Intro`, `Idle`, `Move`, `Attack`
  (`attackspecial` and hit reactions exist but did not play). No `AE_FX` events, so nothing to
  mirror there.
- **The reach is 3 m** (`killDistance`), held for 0.3 s (`killTime`). That is long: it favours
  the glaive over two sickles.
- **Death cannot be hit**: `IgnoreHit`, every collider disabled. For the first skin there is no
  hitbox to match and no hit flash to mirror; the dissolve (3 s in, 2 s out) still is.
- **The body shader is `PotoVR/Characters_Normal`**, not an `-Array` shader, lit from the
  Bakery volume (`BAKERY_VOLUME`). The sandbox has no realtime lights and no probes, so a skin
  on its own shader would get flat ambient only. Running the skin on the game's shader is
  now the clear first choice.
- **Death's own eyes** are `PotoVR/Candle` quads (`fx_skeleton-eyes`) at queue 4000. That
  material is a candidate for the skin's eyes as is.
- **Bloom was on** on the test machine. Not a default, so the halo stays required.

Still open: lighting in a dark dungeon room, the whistle revive, and the bloom default.

## What is not known (Phase 0 recon)

None of this is answerable without the dump or the game. It decides the animation approach.

1. **Death's rig.** Skeleton bone list, whether its Animator is humanoid (`isHuman`), whether it
   has leg bones at all under the robe, renderer and material list, `Animator.cullingMode`.
2. **Death's motion.** Does it hover above the floor, pass through walls or over pits, or
   follow the navmesh at floor height? Speed range. A wolf can only walk where a floor is.
3. **Death's attack.** Where the damage collider lives (weapon bone? touch volume?), reach,
   whether it can be parried, staggered or killed, and what happens visually when it "dies"
   or leaves (dissolve, ragdoll, despawn).
4. **Animator surface.** Controller state names and hashes, parameters, clip list and lengths,
   which states fire `AE_FX`.
5. **Hit feedback.** How hit flashes, blood, stuck arrows and the dissolve are applied
   (`MaterialHandler` and `DissolveHandler` on enemies? one renderer or many?).
6. **Spawning** (tiers 3–4 only): the spawner API, how `Specs` are authored (ScriptableObject
   or prefab fields), which values are read by the master only, the `AIModule` base class and
   its virtuals, the `AIClass` enum.
7. **The outline system** (see the last section).
8. **Lighting and glow.** Is there any post-processing on the VR camera, bloom in particular?
   How are characters lit in a dungeon (`BAKERY_VOLUME` is a keyword on the player material:
   baked volumes, light probes, or both)? Which realtime lights exist and what do they cost?
   How does a custom avatar's shader currently read in a dark room next to a vanilla body?

Deliverable: a recon dump written by a dev-only hotkey, in the style of the existing `Recon/`
probes: `Resources.Load("death")` and `death_hazard` walked and logged (hierarchy, components,
bones, colliders, animator states), plus a live-encounter sample (position vs floor height,
animator state over time). The dump excerpts stay out of the repo, as always.

## Tier 1 and 2: the skin

### Mechanism

New mod `src/CustomEnemies` (own DLL; skins are local, so no gate and no handshake). The
retargeter, bundle loader and spring bones are shared with CustomAvatars by linking the source
files into the new project rather than by a DLL dependency, so each mod still installs alone.

- A skin is a `.enemy` bundle plus manifest, produced by a new exporter menu item next to the
  avatar one. Manifest: which enemy it replaces (prefab name or Sauron ID), bone map, scale,
  offsets, weapon attachment, leg mode, spring chains, optional sound replacements.
- Postfix on `AI.InitializePooledObject` / `OnRespawn`: if a skin matches, instantiate it once
  per pooled instance, bind it, and hide the vanilla renderers with `ShadowsOnly` (or force
  `cullingMode = AlwaysAnimate` if recon shows that is the only thing at stake).
- The vanilla enemy stays whole and invisible: AI, colliders, hitboxes, damage, networking and
  ragdoll are untouched. Other players, modded or not, are unaffected. This is the same
  fairness property the avatar swap has.
- **Shadows.** `ShadowsOnly` keeps the hidden Death casting its shadow, so with the skin's own
  there would be two. A skin either sets its own renderers to cast no shadow and lets the
  vanilla shadow stand in, or takes over the shadow and switches the vanilla one off by a means
  that keeps the enemy in culling (recon 1 decides which is safe). Manifest field: `shadow`.
- Skins live under `UserData/CustomEnemies/` and are never committed. The repo ships the
  mechanism and a placeholder skin made of primitives for testing.

### Animation: three options

**Chosen: A for the upper body, with the legs as dangling chains instead of a stepper**, since
the wolf glides by design. B stays available later for authored Blender Actions. The options
as originally weighed:

**A. Delta pose copy (upper body) + own legs.** `PoseRetargeter` copies spine, arms and head
from the invisible Death every frame. Attacks keep the vanilla timing exactly, so the telegraph
the player reads is the real one. Legs come from a procedural stepper (the `LegIK` /
`WalkState` approach) planted on the floor, because a robed floating reaper almost certainly
has no usable leg animation **[recon 1]**. Zero authored animation. Needs Death's upper body to
map onto a humanoid (generic rigs are fine, the bone map is in the manifest).

**B. State mirroring.** The skin carries its own Animator and authored clips (walk, idle,
attack, hit). A driver reads the vanilla animator's current state hash and normalised time and
plays the matching clip. Best possible look, the most authoring work, and attack clips must be
timed to the vanilla hit frames by hand. Worth it only for a hero skin. Can be layered on A
later for locomotion alone (authored walk cycle below the waist, pose copy above).

**C. Rigid follow.** The model glides with the vanilla root, legs static. Only acceptable if
recon shows Death is meant to hover and the skin is designed as a floating thing.

### Will it feel good?

The honest split:

- **Combat feel is vanilla's**, because every number, collider and timing is vanilla's. It
  cannot get worse than fighting Death is today. Death's kill is not a weapon hit: `AIDeath`
  kills a player who stays within `killDistance` for `killTime` (dump, see below). So the
  weapon only has to *read* as that reach: it is parented to the vanilla weapon bone for the
  swing, and its length is sized to `killDistance`, which the probe read as 3 m: a glaive's
  reach, not a pair of sickles'.
- **Silhouette vs hitbox.** Not an issue for Death, which cannot be hit (probe). For later
  enemies the player aims at what they see: the skin is scaled so head and torso overlap the
  vanilla colliders; snout, ears and tail will have no hitbox. Tolerable, and tunable per skin.
- **Hit feedback is the first thing that will read as janky.** Flash, blood and dissolve are
  applied to the vanilla renderer and will not show on the skin until they are mirrored
  **[recon 5]**. Death never takes a hit, so for the first skin that is the dissolve alone;
  the rest is budgeted for Phase 2.
- **Legs: settled by the glide decision.** A walking biped that slides is the jank that makes
  an enemy not worth fighting; a floating one has no feet to slide. The legs hang from the
  hips as spring chains with a slow procedural sway and lag behind on acceleration, which
  reads as intentional. The robe hem gets the same treatment.
- **Glow and lighting are now the main risk** **[recon 8]**. Without bloom, an emissive eye is
  flat red paint: the glow has to be built from an unlit emissive eye, an additive halo
  sprite facing the camera, and possibly a small red light. A skin whose shader ignores the
  dungeon's baked lighting will look pasted on, either too bright or black. See "Look" below.
- Custom avatars feel good because a human drives them. An enemy has only its clips, so the
  upper body will look exactly as alive as vanilla Death does, no more.

### Look: materials, eyes, lighting

Decided once recon 8 is in, in this order of preference:

1. **Body shader.** Death uses `PotoVR/Characters_Normal` (probe), an ordinary texture
   shader, not an `-Array` one. Use it at runtime on the skin: it is lit exactly like every
   other creature (from the Bakery volume), and it brings the hit flash and dissolve with it.
   The fallback, a bundled SPS-I safe shader, would have to sample the Bakery volume itself,
   since the sandbox has no probes or realtime lights.
2. **Eyes.** Separate material slot, unlit emissive, never darkened by scene lighting. Add a
   camera-facing additive halo quad per eye, sized and faded by distance. Bloom is a player
   setting (`EnableBloom`, dump), so the halo is needed either way; with bloom on, the
   emissive eye gets it for free. The game's own enemy eyes are an instanced system
   (`AIDissolve.instancedEyes`); they are hidden with the body, and may be reusable.
3. **Light.** An optional low-range red point light at the head, off by default, switchable
   in settings, because a realtime light in a baked scene costs frame time in VR.
4. **Manifest-driven.** Eye anchors, halo size and colour, light on/off and range are skin
   data, so the second skin does not need code.

## The model

Free rigged fan models exist, so modelling and rigging from scratch is not required. Listing
details below come from search results; the store pages could not be opened from the planning
session (network policy), so verify before relying on any of them.

| Source | What | Notes |
|---|---|---|
| Sketchfab, "Death Wolf Rig (Puss In Boots 2)" (Guilherme Navarro) | Rigged, about 26k triangles, free download | Taken from the kart racing game: real-time ready, on-model. Best first candidate. |
| Sketchfab, "Big Bad Wolf / Death V2 T-pose Rigged" (CVRxEarth) | Rigged, T-pose, expression shape keys, free | T-pose helps humanoid mapping |
| Sketchfab, "Death's Sickles" (NZFawkes), "Death's Weapon" (ALIEEEN) | Props, free | For the weapon attachment |
| Gumroad, BizyMouse "Death Wolf VRChat Avatar" | About $15, already a Unity humanoid avatar | Would go through the existing exporter almost unchanged |
| Gumroad, raccoonraster3d; CGTrader "rigged"; itch.io enticia | $15 to $49 `.blend` rigs | Film-style rigs, likely heavier and built for offline rendering |
| CGTrader "Death base model" | Free, unrigged, retopologised | Fallback base for a self-rig |

Uploader licences (CC BY on Sketchfab) do not clear the underlying character. Model files and
any film audio stay out of the repo and out of releases; that is the standing ground rule.

Hunyuan3D / Trellis from a reference image: usable for static props, not for the character.
The output is one fused, unrigged mesh (cloak, fur, weapons and body welded, no clean edge
loops at joints), so it costs a retopo and a rig, which is the laborious part being avoided.
Not needed given the models above.

Prep work per model, in Unity 2022.3.62f2: humanoid (or manual) bone map, digitigrade leg
check (three-segment legs do not map cleanly onto humanoid legs and decide how `LegIK` is fed),
tail and cloak chains for the spring solver, SPS-I safe shader, scale, weapon anchor.

## Tier 3: variants

Avoid injecting prefabs if possible. Proposed shape **[recon 6]**:

- The master spawns a **vanilla** enemy through the game's own path, so pooling, PhotonView
  allocation and unmodded-safe behaviour are the game's.
- The master then sends one mod event (code from 190–199, the only block no mod has claimed): `viewID → variantId`.
  Every client applies the variant: skin, scale, and the stat overrides the variant defines
  (health, speed, damage, module tuning). Late joiners get the table on join.
- Private lobbies with the existing handshake only, since stats change.
- Descent is the natural first consumer (variants on deep floors).

If stats turn out to be shared ScriptableObjects rather than per-instance values, overrides
need per-instance copies; if they are master-only, only the master applies them.

## Tier 4: new behaviour

Two routes, neither cheap:

- **Puppeteer.** A master-side controller that steers a vanilla AI through its public surface
  (target, destination, forced attacks) and Harmony patches. Stays in managed code, survives
  updates better, limited to recombining what the host enemy can already do.
- **Injected module.** A `ClassInjector`-registered subclass of the game's AI module base with
  overridden virtuals, added at runtime (injected types cannot be serialised into a bundle).
  Real new behaviour, but virtual overrides on injected Il2Cpp types are the most
  update-fragile thing this repo would contain.

New attacks also need authored animation, effects and sound. Recommendation: do not plan this
tier until tiers 1–3 are in players' hands and recon item 6 has been read.

## The blue outline bug (CustomAvatars)

Symptom: the blue outline (stick press, or a teammate in another room) traces the hidden
vanilla body, not the avatar.

Reading: the vanilla body is hidden with `ShadowsOnly`, which removes it from every normal
camera pass. Something still draws its silhouette, so the outline is drawn **explicitly from a
renderer reference** (a command buffer `DrawRenderer`, a `Graphics.DrawMesh`, or a dedicated
outline camera), not by the body's own material pass. The live character material does carry
`_XRAY_ON`, so a shader-side x-ray pass exists too and has to be ruled in or out **[recon 7]**.

Recon: grep the dump for `Outline|XRay|Highlight|Silhouette`, find what the stick press calls,
and log which renderers it holds for a swapped player.

Fix, by cause:

- Renderer list: postfix the method that collects renderers (or rewrite the list after a swap)
  so it holds the avatar's renderers instead of `character_mesh`; restore on revert. The
  outline uses its own material, so avatar shaders do not matter.
- Shader pass on the body's own material: draw the avatar's renderers ourselves with the
  game's x-ray material while the game's outline is active, and suppress the vanilla one.

Both triggers have to be covered on every screen: your own stick press, and the outline other
players' clients draw of you when you are in another room. A fix that only rewrites the local
player's list would leave peers tracing your hidden body, so it has to apply to every swapped
avatar, local and remote.

Enemy skins will hit the same system if enemies are ever outlined, so this is fixed once,
in shared code, before Phase 1.

## Phases

| Phase | Work | Exit |
|---|---|---|
| 0a | Recon probe for Death and for the outline system | The eight questions answered in `GAME-INTERNALS.md` |
| 0b | Outline fix in CustomAvatars | Outline traces the avatar, locally and on a peer, and reverts cleanly |
| 1a | `CustomEnemies` skeleton: bundle + manifest, attach on spawn, hide vanilla, rigid follow, placeholder skin | A primitive-built dummy rides Death through a whole encounter, pooling and respawn included |
| 1b | Pose copy, weapon attachment, dangling legs and robe | Attacks read correctly; legs and hem trail the motion and never stretch or flip |
| 1c | Body shader, eyes and halo, hit feedback, dissolve/despawn, sounds, exporter menu item | A full encounter with nothing visibly vanilla, eyes that read as glowing in a dark room |
| 2 | Second skin on a different enemy, straight from data | No code change needed for it |
| 3 | Variants | After a go/no-go on recon item 6 |
| 4 | New behaviour | Not scheduled |

## Decisions

All five of the original questions are answered under "Decided" at the top. Still open, for
Phase 1b: whether the first release uses one glaive or two sickles (the 3 m reach leans to
the glaive), and
whether the red light ships on or off (cost, recon 8).
