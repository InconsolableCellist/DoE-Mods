# Custom enemies — feasibility and plan

Status: **proposal, nothing built.** Written 2026-11-17 from the repo alone (no dump, no game
in the session that wrote it). Everything marked **[recon]** is an inference that has to be
confirmed against `dump.cs` or a running game before code is written on top of it.

Goal, in order:

1. Replace the look of the Death hazard enemy with a different model (first skin: the wolf).
2. Make that a general "enemy skin" mechanism, so any enemy can be reskinned.
3. Variants: an existing enemy's brain with a new body and new numbers.
4. Entirely new creatures with their own behaviour.

## Verdict

| Tier | Feasible? | Risk | Needs everyone modded? |
|---|---|---|---|
| 1. Reskin Death | Yes. It is the CustomAvatars trick pointed at an enemy. | Low for the mechanism, **medium for the legs** (below) | No, local and cosmetic |
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
  is already planned (PLAN Phase 4b); mod Photon event codes 140–149 are clear.

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
- Skins live under `UserData/CustomEnemies/` and are never committed. The repo ships the
  mechanism and a placeholder skin made of primitives for testing.

### Animation: three options, in order of preference

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
  cannot get worse than fighting Death is today, provided the visible weapon sits where the
  real damage collider is. So the skin's weapon is parented to the vanilla weapon bone and
  sized to the vanilla reach. The wolf's joined-sickles glaive form is a polearm and should
  cover a scythe's reach closely; two short sickles would lie about the range.
- **Silhouette vs hitbox.** The player aims at what they see. The skin is scaled so head and
  torso overlap the vanilla colliders; snout, ears and tail will have no hitbox. Tolerable,
  and tunable per skin.
- **Hit feedback is the first thing that will read as janky.** Flash, blood and dissolve are
  applied to the vanilla renderer and will not show on the skin until they are mirrored
  **[recon 5]**. Budget for it in Phase 1, not as polish.
- **Legs are the real risk.** A biped that slides is the jank that makes an enemy not worth
  fighting. If Death travels at floor height on the navmesh, procedural stepping at its speeds
  is the same problem already solved for avatars. If it hovers through walls **[recon 2]**,
  either accept a ghostly glide (option C, designed for) or pick a different host enemy for
  the wolf, such as `skeletons_boss` or another humanoid melee boss with real leg animation,
  which makes option A complete with no leg work at all.
- Custom avatars feel good because a human drives them. An enemy has only its clips. Expect
  "good, with visible seams at the feet" from option A, and "native" only from option B.

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
- The master then sends one mod event (code from the 140–149 block): `viewID → variantId`.
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

Enemy skins will hit the same system if enemies are ever outlined, so this is fixed once,
in shared code, before Phase 1.

## Phases

| Phase | Work | Exit |
|---|---|---|
| 0a | Recon probe for Death and for the outline system | The seven questions answered in `GAME-INTERNALS.md` |
| 0b | Outline fix in CustomAvatars | Outline traces the avatar, locally and on a peer, and reverts cleanly |
| 1a | `CustomEnemies` skeleton: bundle + manifest, attach on spawn, hide vanilla, rigid follow, placeholder skin | A primitive-built dummy rides Death through a whole encounter, pooling and respawn included |
| 1b | Pose copy, weapon attachment, leg mode chosen from recon | Attacks read correctly; feet do not slide at walking speed |
| 1c | Hit feedback, dissolve/despawn, springs, sounds, exporter menu item | A full encounter with nothing visibly vanilla |
| 2 | Second skin on a different enemy, straight from data | No code change needed for it |
| 3 | Variants | After a go/no-go on recon item 6 |
| 4 | New behaviour | Not scheduled |

## Decisions needed

1. Scope: stop after Phase 2 for now, with 3 as a later go/no-go?
2. New mod `CustomEnemies` (recommended), or fold skins into CustomAvatars?
3. Which model, and who preps it in Unity?
4. If Death turns out to hover through geometry: glide by design, or move the wolf onto a
   walking humanoid boss and leave Death alone?
5. Where implementation runs. It needs the game folder (MelonLoader's generated assemblies to
   build, the dump to read, the game to test), none of which a cloud session has.
