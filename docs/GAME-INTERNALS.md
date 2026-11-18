# Dungeons of Eternity — Game Internals Reference

Findings from the Il2CppDumper output (`dump/dump.cs`, generated 2026-08-30 against the
current Steam build). Line numbers below reference that file and **will shift after any
game update** — re-run `tools/run_dumper.bat` and re-grep after patches.

## Build facts

| Fact | Value |
|---|---|
| Engine | Unity **2022.3.62f2**, IL2CPP, x64, DX11 |
| Metadata | Un-obfuscated (magic `AF 1B B1 FA`, metadata v31, v17 MB) |
| Game code | `Assembly-CSharp.dll` (+ `Assembly-CSharp-firstpass`, `Othergate_DTOs`) |
| Networking | Photon PUN 2 (relay/P2P, master-client authority), Photon Chat |
| Backend | PlayFab (profile, economy, title data), Unity Gaming Services (Auth, Cloud Code, Wire), Vivox voice |
| VR stack | Oculus XR plugin + OpenVR/SteamVR, `Unity.XR.Management` |
| IK | RootMotion **FinalIK** — `VRIK`, `GrounderIK`, `IKSolverVR` (dump.cs ~573xxx) |
| Anti-cheat | **None** |
| Notable middleware | A* Pathfinding, Odin Serializer, Bakery, TextMeshPro, LIV, bHaptics, OVRLipSync (shipped, usage TBD) |
| PunRPC count | 296 `[PunRPC]` methods game-wide |

## Avatar system

The player avatar is a **modular merged mesh**, not a monolithic model:

- `AvatarFactory : Singleton<AvatarFactory>` (dump.cs:25069) — Resources path `"Player/AvatarFactory"`.
  - `boneReferenceMesh : SkinnedMeshRenderer` — the canonical skeleton all cosmetic parts bind to.
  - `BuildAvatarMesh(AvatarPlayer owner, GameObject meshParent, List<string> avatarMeshes, Material characterMaterial, bool logging)` — merges the selected cosmetic part meshes into one `SkinnedMeshRenderer`.
  - Static shader property IDs: `HueSkinID`, `HueHairID`, `HueArmorID`, `AccentColorMaskID`, `EmissionColorID`, `MetalTintColorID`, `TintID`, … (one master character material, hue-shifted per player).
  - Cosmetics catalogued as `(BaseModule, BaseModuleContainer, ItemType)` tuples; unlock state comes from PlayFab (`InitPlayFabData`).
- `CharacterPrefab : MonoBehaviour` (dump.cs:27728) — the rig prefab:
  - `sourceShader`, `dissolveShader` (spawn/despawn dissolve FX)
  - `VRIK ik` + `GrounderIK grounderPrefab` → **remote players are 3-point VRIK rigs with foot grounding**
  - Jaw flap is **voice-amplitude driven**: `VivoxParticipantTap audioTap`, `closedJawAngle`/`openedJawAngle`, `voiceEnergyOverride`. Not viseme-based.
  - `CharacterPrefab.AvatarDef` (dump.cs:27654): one `CosmeticModuleContainer` per slot — head, torso, legs, hands, boots, beard, headwear, facewear, cape, teeth, eyeColor, hairColor, skinColor, accentColor + `BodyType`. `Serialize()`/`Deserialize()` ↔ `Dictionary<PlayerData.CharacterValues, string>`.
- `AvatarPlayer : Agent, IPunObservable` (dump.cs:26371) — the networked player:
  - IK targets: `IKTargetHead`, `IKTargetLeftHand`, `IKTargetRightHand` (Transforms, settable properties!)
  - Network sync: three `NetworkTransform`s — `headT`, `leftHandT`, `rightHandT`. Everything else is IK-derived locally on each client.
  - `RemoteAnimator : Animator` for remote-rig animation states, `AvatarPlayer.AnimParams`.
  - Appearance sync: `SendLocalPlayerData(...)` / `SetRemotePlayerData(...)` + `CosmeticModules : List<(PlayerData.CharacterValues, string)>` — cosmetic slot→string IDs sent to peers on join, resolved locally against each client's cosmetic catalog.
  - Generic message channel: `RPC_Message(AvatarPlayer.MsgType msgType, object[] data)`; enum at dump.cs:25740 (`SendPlayerData=40`, `Teleported=20`, `KickedFromGame=50`, …).
- Face animation (`Idler`, dump.cs ~28840; `FacePose` dump.cs:28794):
  - **Blendshape-driven** — `FacePose.BlendPose { string blendShape; float value; }` faded in/out on the merged `SkinnedMeshRenderer`.
  - `Idler` owns `head/neck/jaw/leftEye/rightEye` Transforms, blink via `blinkBlendShapeLeft/Right` + curve, glancing (`Glancer`), emotions, dance FX.
  - `AvatarHologram : Idler` — non-networked avatar display (character menu) → **safe local test target**.
- `AvatarNickname`, `AvatarHolster` (weapon attach offsets), `AvatarHand` (`PropPose` hand poses), `AvatarThumbnail`.

## Networking map

- `NetworkObjectPool : MonoBehaviour, IPunPrefabPool` (dump.cs:61093) — **the** prefab
  resolver for every `PhotonNetwork.Instantiate` call. Interface impl is explicit:
  `Photon.Pun.IPunPrefabPool.Instantiate(string prefabPoolName, Vector3, Quaternion)`.
  Patching/wrapping this is the clean injection point for custom networked prefabs.
- `NetworkObject` — base for many synced world objects (`Fabricator : NetworkObject`, `PlayerRoomManager : NetworkObject`).
- `Grabbable : MonoBehaviour, IPunObservable, IPunOwnershipCallbacks, …` (dump.cs:8452) — held/thrown object sync with ownership transfer.
- Player spawn RPCs on `AvatarPlayer`: `RespawnAvatar`, `RPC_SetRigType(int)`, `RPC_SpawnInventoryItems(string prefabName, Vector3 pos, int ownerViewID, int count)`, `RPC_OnInventoryItemSpawned(int[] viewIDs)`, `RPC_SetPerk(object[])`.
- Photon custom event codes 0–199 are free for mods (PUN reserves 200+). **The game does use
  the callback side**: `GameManager : Singleton<GameManager>, IOnEventCallback` (dump.cs:47615)
  and `CachedEventLogger : MonoBehaviour, IOnEventCallback, IInRoomCallbacks` (dump.cs:60055,
  a replay-window debug logger), and `AvatarPlayer` carries a `cachedEventCodes : int[]` field
  (dump.cs:26510). No `const byte` event-code declarations are visible anywhere in game code,
  so the codes are computed or inlined and the dump can't settle this. **Do not claim 140–149
  until the runtime sniffer has cleared it** — the Phase 0 recon build tallies every inbound
  code below 200.
- Vivox = voice (positional + party). Photon Chat also present.

## Loot & items

- `ChestLoot : MonoBehaviour` (dump.cs:39591) with nested `LootDef` / `LootRotation` — chest contents.
- `LootSpawn` (dump.cs:40221), `LootSpawner` (dump.cs:43573) — world loot placement, each with a `LootDef` table.
- `Weapon : Prop, IPunInstantiateMagicCallback` hierarchy: `WeaponMelee`, `WeaponStaff`, `WeaponMythic`, plus `WeaponFactory : Singleton`, `WeaponBuilder`, `WeaponPart`, `WeaponDamage : ScriptableObject`, `WeaponCost`, `WeaponProperties`.
- `Fabricator : NetworkObject` (dump.cs:52738) — vendor/crafting stations (`WeaponVendor : Fabricator`).
- Loot decisions appear master-client-side (spawners are scene objects; contents sync via normal instantiation) — verify at runtime with UnityExplorer.

## Progression & the line we don't cross

`PlayerData.CharacterValues` (dump.cs:63958) enumerates the entire server-side profile:
XP/Level/Coins/Souls, perk slots, avatar cosmetic slots (50–70), weapon loadouts (100–113),
`UnlockedCosmetics=80`, `UnlockedWeapons=131`. This all lives in **PlayFab** (see the
`/Client/*` REST paths in `stringliteral.json`) with UGS Cloud Code for server-authoritative
operations. The rule:

- **In-game gold (`Coins`) is not touched unless the mod says so up front**, at the top of its
  README and in the root README's mod list. A mod that does change gold uses the game's own
  calls (`PlayerProfile.IncrementCharacterData`) and saves the profile the way the game does.
  Today that is only **LootOverhaul** (since 0.15.0: the kobold buys and sells for gold).
  Any new gold write needs that note before it merges, and the PR has to argue for it.
- **Everything else in the profile stays off-limits**: XP, level, souls, perks, unlocks,
  cosmetics, loadouts. Those writes are shared-economy tampering and plausibly
  server-validated. Reading is fine. Mod state stays local + session-scoped.

## Runtime findings (recon session 2026-08-31, solo, PCVR/SteamVR)

Source: `recon-20260831-012648.md`, mod v0.1.0. Dan's machine — RTX 5090, OpenVR.

### Rendering environment — **decided**

| Fact | Value | Consequence |
|---|---|---|
| Stereo rendering | **`SinglePassInstanced`** | Every custom avatar shader must be SPS-I aware. liltoon / locked Poiyomi / Standard are fine; anything hand-written must declare instancing. |
| XR loader | `OpenVRLoader` ("OpenVR Display") | SteamVR path, not the Oculus plugin, on this machine. |
| Graphics API | Direct3D 11, shader level 50 | |
| `QualitySettings.skinWeights` | **`FourBones`** | VRC avatars authored for 4 bones/vertex deform correctly. No re-weighting needed. |
| Eye texture | 3072×3264, `renderViewportScale` 1 | |
| Quality level | 5 (`PCVR`), AA 4, shadow distance 30, LOD bias 0.7 | |
| Target frame rate | 90 | Budget for the parallel rig + face tracking. |

### Player object layout — **three scene-root objects**, all in `DontDestroyOnLoad`

```
Player_<nick>            <PhotonView, AvatarPlayer>          logic only — ZERO renderers
  IKTargets/{IKTargetHead, IKTargetLeftHand, IKTargetRightHand}
  AvatarHolsters

Model_<nick>             <Animator, VRIK, CharacterPrefab>   the third-person body
  _Skeleton_Pose         [inactive] 98 bones, 19650 verts, dissolve material
  fx_Hologram, fx_HologramDrone   [inactive]
  character_mesh         ← the merged SkinnedMeshRenderer
  root/pelvis/spine_01/…  the 98-bone skeleton
  CharacterBlobShadow

VR Controller            local player only — the SteamVR rig
  OpenVR Rig (SteamVR)(Clone)/[CameraRig]/{Camera, Controller (left|right)}
  FPS-Arms-Model         ← a SECOND full skeleton; the first-person arms you actually see
  Holsters/{HolsterSidearmLeft, HolsterArrows, HolsterBack, HolsterInventory}
```

`AvatarPlayer.FullBody` and `.RemoteRig` both point at `Model_<nick>`.
`AvatarPlayer.Head`/`Eye`/`LeftHand`/`RightHand` point into the SteamVR rig.

**462 renderers exist under `Model_<nick>`; exactly 2 are visible** — `character_mesh` and
`CharacterBlobShadow`. The entire cosmetic catalogue (`SK_H_Addon_*`, `SK_Exo_*`, every hair,
cape, beard and bag variant) is instantiated and inactive on every player object.

### Self view — **you render a full body AND separate FPS arms**

`Model_<nick>/character_mesh` is **enabled on the local player**, so looking down shows your
real body. Independently, `VR Controller/FPS-Arms-Model` carries its own complete skeleton
(`root/pelvis/spine_01/…/clavicle_l/upperarm_l/…`) and draws the first-person arms, with
weapon-stat UI panels parented into its forearm bones.

**Phase 2 consequence:** swapping the avatar covers the third-person body only. The hands you
see are a different mesh on a different rig. PLAN 2b's "start with no self body" is still the
right first move, but "self view" is two problems, not one.

### The character rig

| Fact | Value |
|---|---|
| `sourceShader` | **`PotoVR/Characters-Array`** |
| `dissolveShader` | `PotoVR/Characters-Array_Dissolve` |
| Live material | `M_H_Alpha_Rogue_01 (Instance)`, renderQueue 2000, GPU instancing **False** |
| Shader keywords | `BAKERY_VOLUME _ALPHA_ON _DOUBLEALPHA_ON _XRAY_ON` |
| `character_mesh` | 5672 verts, 1 submesh, 98 bones, `updateWhenOffscreen = true` |
| `CharacterPrefab.scale` | 1 |
| `visibleDistance` | 10 |
| Jaw angles closed/open | −119.5 / −128 |
| `blinkTime` | (3, 8) seconds |
| `defaultBlendShapes` | 0 entries |
| VRIK | `RootMotion.FinalIK.VRIK` **on `Model_<nick>` itself** |
| GrounderIK | on a separate `Player Grounder` object |
| Vivox tap | `VOIPSource` under `…/neck_01` |

**Skeleton (98 bones, UE4-style naming)** — `root/pelvis/spine_01..03/neck_01/head`,
`clavicle_{l,r}/upperarm/lowerarm/hand` with full 3-joint fingers and twist bones,
`thigh/calf/foot/ball`. Face bones exist: **`head/eyeR`, `head/eyeL`, `head/jaw`**.
Attachment bones: `WeaponsL`, `WeaponsR` (in the hands), `Helmet`, `Hip Attachment`.
Secondary chains: `hair1..3/hair_tip`, and three 9-segment capes (`Cape_*`, `CapeL_*`, `CapeR_*`).

**`character_mesh` blendshapes (27)** — three groups:
- emotions: `relaxed, idle, happy, angry, sad, surprise, pain, tired`
- blinks: `blinkR, blinkL`
- head morphs: `Elf, var1…var6`
- **visemes: `sill, PP, FF, TH, DD, kk, CH, SS, nn, ih`** — an OVRLipSync-style set (10 of the
  15; `RR/aa/E/oh/ou` absent).

So the vanilla mesh carries viseme shapes even though `CharacterPrefab` drives the jaw from
Vivox energy. **No ARKit or Unified Expressions shapes exist on the vanilla mesh** — irrelevant
for Phase 3, which drives our own avatars, but it rules out any "reuse the vanilla face" shortcut.

### Private rooms — `IsVisible` is the gate; `IsOpen` is not

Two dumps, same private party, solo:

| Property | In lobby | Mid-dungeon |
|---|---|---|
| `IsVisible` | **`False`** | **`False`** |
| `IsOpen` | `True` | **`False`** |
| `MaxPlayers` | 4 | 4 |
| `PlayerTtl` / `EmptyRoomTtl` | 0 / 0 | 0 / 0 |

**`IsOpen` flips to `False` once a run is in progress** — so `ModGate.Active` must test
`IsVisible == false` and must not touch `IsOpen`, or the mod would go inert the moment you
enter a dungeon.

Room `CustomProperties` are substantial and useful:
`room_name, lvl_tier, curScn, in_progress, VoiceChannel, game_mode, quest_name,
dungeon_seed (Int32), quest_type`. `dungeon_seed` and `in_progress` are directly relevant to
Phase 4.

**Correction to the 2026-08-31 01:26 reading:** player `CustomProperties` is *not* empty. The
early lobby dump caught it before it populated; in a settled room it reads
`{Ping=(Int32)72, Build=(String)Build:1.2.3849}`. `ca.*` keys still can't collide with
`Ping`/`Build`, but the bag is not ours alone. `Build:1.2.3849` is also a free game-version
signal worth folding into the Phase 1 handshake.

### Photon event codes — 140–149 clear across a full run

Full solo dungeon run (Soul Harvest, enemies active), lobby → dungeon → lobby:

| Code | Count | Range |
|---|---|---|
| 1, 2, 50, 70 | ×1 each | **mod-usable — used by the game** |
| 200, 210, 226, 253, 255 | ×2–14 | PUN/Realtime internals |

The game's own custom events cluster low (1, 2, 50, 70). **Nothing touched 140–149**, so the
planned block survives contact with a real run. Caveat: still a single-client sample — a second
player's join/leave traffic hasn't been observed.

### `NetworkObjectPool` — prefab table is a lazy cache

`NetworkObjectPool.Instance` lives at `__Game__/ObjectPool`. `UnpooledPrefabs` is **not** a
static manifest — it grew 5 → 22 entries as the run progressed. Keys are plain strings, some
with a folder prefix:

```
Torch, Arrow, Axe_Gen1, Crossbow_Gen1, Potion, Key_Universal,
Player/DefaultPlayerPrefab, GameModes/Soul_Harvest_Altar_01,
Cave_Room_02, Cave_Hallway_01, Cave_Hallway_04, Cave_DeadEnd_02, Buried_Hallway_02,
ExplodingBarrel_01, BreakableBarrel_01, BreakableBarrel_S_01, BreakableBox_01,
BreakablePot_L_01, Chest_Small_02, Coin_Pile_01, Coin_Pile_05, Coin_Pile_10
```

Plain-string keys confirm Phase 4b's `dfm:`-prefix injection scheme will work cleanly.

### `Multiflex` — the game's own spring-bone solver

`Multiflex : MonoBehaviour` (dump.cs:2555), referenced from `CharacterPrefab.multiflex`. This is
what animates the vanilla capes and hair chains, and it's a proper Burst/Jobs solver:

- `Multiflex.Chain { string name; Transform[] transforms; }` — a linear bone chain. Simple.
- Global (per-component, **not** per-chain) parameters: `gravity`, `gravitySpace`,
  `inheritVelocity`, `followAnimationSpeed`, `damper`, `fixTransforms`.
- Four collider types: `FlexColliderSphere` / `Capsule` / `Cone` / `Plane`, each a Transform +
  center + radius (+ height/axis).
- `Constraint { Transform bone1, bone2 }` for linking chains.
- Public API: `Read(float dt)`, `Schedule()`, `Complete()`, `Write()`, `StoreLocalState()`,
  `FixTransforms()`.

**Correction (research pass, 2026-08-31):** the "who drives it" question is answered.
`MultiflexScheduler : LazySingleton<MultiflexScheduler>` (dump.cs:2714) is the manager:

```
public MultiflexScheduler.Mode mode;      // LateUpdate = 0, Delayed = 1
public List<Multiflex> list;
public void Add(Multiflex multiflex);     // dump.cs:2729
public void Remove(Multiflex multiflex);  // dump.cs:2732
public void OnPresimulate(float deltaTime);
public void OnPostSimulate(float deltaTime);
```

So registering our own `Multiflex` is a matter of `MultiflexScheduler.Instance.Add(...)` — no
manual Read/Schedule/Complete/Write ordering to reverse-engineer. Other users:
`MultiflexWind` (dump.cs:2757), `AvatarHologram` (25323), `Vendor` (30165), `Sauron.AIMultiflex`
(101153).

The mod still ships its own Verlet solver (`Avatars/SpringBones.cs`) for now — that's a shipped,
tunable thing rather than a better one to be built — but Multiflex is a genuinely available
upgrade, not a speculative one. Remaining unknowns: whether `Initiate()` re-runs when `chains`
is repopulated after `OnEnable`, and whether `NativeArray` lifetime is manageable across
interop. Note also the parameter-model mismatch: **VRC PhysBone parameters are per-chain,
Multiflex's are per-component**, so differing per-chain settings would need one Multiflex per
parameter group.

Note the parameter-model mismatch: **VRC PhysBone parameters are per-chain, Multiflex's are
per-component**, so honouring differing per-chain settings would mean one Multiflex per distinct
parameter group.

### FinalIK surface for the avatar swap

`VRIK : IK : SolverManager` (dump.cs:573786) — `public VRIK.References references` and
`public IKSolverVR solver`, plus `AutoDetectReferences()`.

`VRIK.References` (dump.cs:573710), all `Transform`, in order:
`root, pelvis, spine, chest, neck, head, leftShoulder, leftUpperArm, leftForearm, leftHand,
rightShoulder, rightUpperArm, rightForearm, rightHand, leftThigh, leftCalf, leftFoot, leftToes,
rightThigh, rightCalf, rightFoot, rightToes` — plus `isFilled`, `GetTransforms()` and
`static bool AutoDetectReferences(Transform root, out References)`.

`IKSolverVR` (dump.cs:577002) — what the swap actually sets:

| Member | Purpose |
|---|---|
| `spine.headTarget`, `spine.positionWeight`, `spine.rotationWeight` | head follows `IKTargetHead`. **Note the head's weights are plain `positionWeight`/`rotationWeight` on `Spine`** — there is no `headPositionWeight`. |
| `spine.pelvisTarget`, `pelvisPositionWeight`, `pelvisRotationWeight` | optional hip target |
| `leftArm.target` / `rightArm.target` (+ `positionWeight`, `rotationWeight`, `shoulderRotationWeight`, `bendGoal`) | hands |
| `leftLeg` / `rightLeg` (`target`, weights, `bendGoal`) | only for full-body |
| `locomotion.mode` (`Procedural`/`Animated`), `.weight`, `.footDistance`, `.stepThreshold`, `.rootSpeed`, `.stepSpeed` | procedural legs for a 3-point rig |
| `plantFeet`, `scale`, `LOD` | |

Also `SetToReferences(VRIK.References)`, `Reset()`, `IsValid(ref string)`.

`GrounderIK : Grounder` (dump.cs:572626): `IK[] legs` (assign the IK components themselves),
`Transform pelvis`, `Transform characterRoot`, `rootRotationWeight/Speed`,
`maxRootRotationAngle`; base `Grounder` has `float weight` and `Grounding solver`.

**`AvatarHolster.bone` is a `HumanBodyBones`** (dump.cs:25556) resolved through
`Initiate(Animator animator)` — holsters attach via the Animator's humanoid map, not via VRIK.
This confirms PLAN 2b's fairness property: keep the vanilla skeleton alive and holsters, grab
poses and hitboxes are untouched by a cosmetic swap.

`AvatarNickname` (dump.cs:25589): `Vector3 offsetFromHead`, `Init(AvatarPlayer)`,
`UpdatePlayerName()`, `Hide(bool)`, `ForceHidden` property, `static HideAll`. The `anchor`
Transform is private — repositioning above a custom head goes through `offsetFromHead`.

`DissolveHandler` (dump.cs:70246) is a plain class:
`Init(Transform modelParent, Shader source, Shader dissolve, bool includeDisabledRenderers)`
scans renderers under the parent, so it can be pointed at a custom model.
`MaterialHandler` (dump.cs:70540) binds to **exactly one** `SkinnedMeshRenderer` — a custom
avatar with several renderers needs one per renderer.

Lifecycle on `AvatarPlayer`: `Init()`, `ResetForSpawn()`, `InitRemoteAvatar()`,
`RespawnLocalPlayer(bool, SpawnType, bool)`, `SpawnLocalPlayer(...)`,
`[PunRPC] RespawnAvatar(Vector3, Vector3, bool, bool, float, bool)`, `TeleportPlayer(...)`,
`[PunRPC] RPC_Revive()`, `ReviveLocal(SpawnType)`, `Net_SetRigType` / `RPC_SetRigType(int)`,
`ResetHasSpawned()`. **No `Die`/`Ragdoll`/`Dissolve` methods exist on `AvatarPlayer`** — only
the `IsDead` property; death handling lives elsewhere.

### Build identity

Game build `1.2.3849` (`Application.version` reports `1.2`). Worth pinning in the handshake and
in re-dump triage after a patch.

### Still open

- [ ] **Remote-player rig.** No second player has been present yet (`PlayerCount` stayed 1 in
      both sessions), so it's unconfirmed whether a remote `AvatarPlayer` uses the same
      three-root split and whether `FPS-Arms-Model` exists only locally.
- [ ] **Photon event codes with a second player** — join/leave/ownership traffic unobserved.
- [ ] Whether `ca.*` player custom properties replicate as expected (Phase 1 exit criteria).
- [ ] Whether OVRLipSync actually *drives* the viseme shapes at runtime, or whether they're
      vestigial and only the Vivox energy jaw is live. (The shapes exist; the driver is
      unproven.)
- [ ] `AvatarPlayer.CosmeticModules` slot keys — the recon dump printed the
      `PlayerData.CharacterValues` half of each tuple as a constant garbage value
      (`1657167936`), an Il2CppInterop `ValueTuple` field-access artifact. The string half is
      correct (`Torso_Worn Shirt_var2`, `Hair_Brunette`, `Eyes_Green`, …). Cosmetic only —
      Phase 2 replaces this pipeline rather than reading it.

## Enemies (dump of 2026-11-13, game 1.3)

Read from `dump/dump.cs` for the custom enemies plan (`docs/CUSTOM-ENEMIES-PLAN.md`, Phase 0a).
Names and signatures only; line numbers are this dump's. Anything the dump cannot show (prefab
data, animator states, which modules a given enemy carries) is marked **[runtime]** and waits
for the hotkey probe.

### `Il2CppSauron.AI` (dump.cs:125375)

`AI : Agent, IPunObservable, IOnPhotonViewOwnerChange, IPoolable, IBurn, IPunInstantiateMagicCallback`, sealed.

- **Data:** `type : AI.Type` (Light, Medium, Heavy, Elite, Legend, Fire, Ice, Poison, Gold),
  `armorTier`, `specs : Specs`, `health : AI.Health`, `references : References`,
  `AIModules : AI.ModuleSlot[]` (settings assets) and `modules : AIModule[]` (the live
  instances), `room`, `cachedRenderers : Renderer[]`, `pooledMeshObjects : List<StuckArrow>`.
- **Lifecycle:** `InitializeTemplate(ObjectPool)` (pool), `OnPoolObjectInstantiated`,
  `InitializePooledObject`, `Initialize`, private `OnRespawn(Vector3, object, SpawnIntroType)`,
  `OnPhotonInstantiate`, `ReleaseToPool`, `ReleaseImmediately`, `Net_Release`,
  `DestroyMeshObjects(float dissolveTime)`, `OnPreDestroy`, `OnDestroy`. Updates come from
  Sauron (`OnSauronUpdate`), plus `Update`, `OnAnimatorMove`, private `OnLateUpdate`.
- **Pause hides renderers itself:** `PauseEnemy(bool)` → `SetRenderersEnabled(bool)` with
  `renderersDisabledForPause`. A skin must follow this (hide with the enemy, show with it).
- **Animation events:** `AE_Footstep(Footstep)`, `AE_FX(string)`, `AE_Throw(int)`,
  `AE_ShootProjectile()`, each with an `AddOnAE_*Listener`. Also `Step`, `StartJump`.
- **Animation sync:** the owner calls `Net_CrossFade(hash, fixedTime)`,
  `Net_CrossFadeSecondary`, `Net_SetBool(hash, bool)`, `Net_SetFloat(hash, float)`; each has an
  `RPC_*` twin. So every client runs its own Animator, driven by state-hash RPCs, and the root
  comes from `networkTransform : NetworkTransform`. This is why a local skin needs no traffic.
- **Damage in:** `Net_Damage(totalDamage, weaponDamage, damageType, actorNr, criticalHit)` →
  `RPC_Damage(hp, hpDamage, damageType, actorNr)`; `RPC_SetHealth`, `RPC_SyncHealth`,
  `Net_Heal(int)`. Hits: `OnHit`, `OnStab`, `OnBreakStab`, `TrySever`, `OnHitProp`.
- **Death and despawn:** `Kill(DamageType)`, `Net_Kill`, `OnKilled(killerActorNr, damageType)`,
  private `KillNow`, `ReleaseDelayed`; immortal enemies use `Net_ImmortalKill`,
  `OnImmortalKilled`, `OnImmortalRevived(float)` with their listener events.
- **Hover:** `Hover(float intensity)` / `AddOnHoverListener` (the aim highlight, see hit flash).
- `AI.Health` (dump.cs:125013): `maxHP`, `regainSpeed`, `mortality : AI.Health.Mortality`
  (Mortal, Resurrectable, **Immortal**, IgnoreHit), `HP : ProtectedFloat`, `CalculateMaxHP(Specs)`.

### `References` (dump.cs:128366)

`keychain`, `ID`, `IDHash`, `family : AI.Family` (Undead, Critter, Sorcerer, Monster),
`defaultSpawnType : AI.SpawnType` (Ground, Ceiling, Air), `spawnedAsClass : AIClass`, `PVO`,
`head`, `animator`, `secondaryAnimator`, `isHuman`, `defaultStateHash`, `materialHandler`,
`characterOptimizer`, `puppetMaster`, `muscles`, `colliderArmors`, `props`, `defaultScale`,
`despawnMode : AI.DespawnMode`, and navigation: `navSolverType : NavSolverType` (None, Unity,
**Flight**, Navigator), `navAgentType`, `navAgentRadius/Height`, `baseOffset`, `squeezeFloor`
(its tooltip is about flyers squeezing through doorways). Lookups by name:
`GetTransformBindings`, `GetColliderBindings`, `GetBinding<T>`. Stage-dependent specs:
`GetStage(healthPerc)`, `GetSpecsFloat/Int/Bool/Probability/Multiplier(key, …, stage)`.

`AIClass` (dump.cs:126667): Minion, Warrior, Ranged, Creature, Miniboss = 4, Boss = 5,
BreakableSpawn, NestSpawn, LargeCreature, FlyingCreature, Lurker.

### Specs and modules

- `Specs : ScriptableObject` (dump.cs:129724): `HP`, `multHPByNumPlayers`, `scale`, special loot,
  and keyed arrays `multipliers`, `booleans`, `probabilities`, `integers`, `floats`,
  `damageTypes`, `stages`. **A shared asset, not per instance**: a variant (tier 3) that changes
  numbers has to clone it per enemy or override at the read (`References.GetSpecs*`).
  `AIDef(specs, type, armor)` and `AI.SetDef(AIDef)` assign one.
- `AIModuleSettings : ScriptableObject` (dump.cs:126406): `keychain`, `animatorController`,
  abstract `Create(AI, DebugPanelMode) : AIModule`.
- `AIModule` (dump.cs:126426), abstract, plain managed base (not a MonoBehaviour). Virtuals:
  `OnPostInit`, `UpdateModule`, `OnSauronUpdate`, `OnUpdateIK`, `OnHit`, `OnHitStart`, `OnStab`,
  `OnKilled`, `CanStartHit`, `CanStartAttack`, `CanBeStunned`, `OnAttackStart`, `OnHitProp`,
  `OnDamage`, `OnDestroy`, `OnHazard`, `BlocksThrownProp`, `OnPropThrownAt`,
  `OnThrownPropBlocked`, `CanBeStabbed`, `OnPooled`, `OnRespawn`, `OnKinetics`, `OnKineticsEnd`,
  `OnOwnershipChange`, `OnPlayerEnteredRoom`, `OnWriteStream`, `OnReceiveStream`, `OnRPC` (two),
  `OnSendEvent`, `OnShootProjectile`, plus the debug virtuals.
- Concrete modules: AIAimIK, AIAimIKAttack, AIAIvsAI, AIAnimationFX, AIAnimationVariation,
  AIBatBolt, AIBatForm, AIBreakableArmor, AIBurn, AICCDIKAttack, AICeilingSpawn,
  AICollisionDamage, AICombatMelee, AICrystalGuardian, **AIDeath**, **AIDeathAnimation**,
  AIDissolve, AIDodgeDissolve, AIEmpty, AIEvent, AIExoGrounded, AIFlight, AIGeyserAttack,
  AIGrounderIK, AIGrounderQuadruped, AIHologram, AIInertia, AIKamikaze, AILocomotion,
  AILookAtIK, AIMultiflex, AIPatrol, AIPhalanxAttack, AIPhaseAttack, AIPullAttack, AIPuppet,
  AIRagdollExplosion, AIRelax, AIRunner, AISandGraveAttack, AISatellite, AISecondHandOnProp,
  AISeverableLimb, AISkeleconda, AISlimeCube, AISmashTrigger, AISnakeIK, AISonicVeil,
  AISpecialAttack, AISpecialHit, AISpawnAttack, AIStingAttack, AIStreamAttack, AITeleport,
  AITemplate, AIThrowAttack, AITrain, AITypeFX, AIWolf, AIWolfIK, AIWraithAttack.

### Death

- Identity: `SauronKeys.IDHashDeath`, `EnemyTypeButton.EnemyType.Death = 33`,
  `GameManager.HazardModifier.Death = 2`, `GameManager.Hazard_DeathSpawn`. Strings in the
  metadata: `Death`, `Death_Hazard`, `Death_Whistle`, `Death Whistle`, `Immortal Death`,
  `SpawnDeath`, `enemytype.death`.
- Placement: `DungeonBuilder.LayoutDeathHazard(List<DungeonRoom>)` places
  `DungeonFeature.DeathHazard = 130` and `DungeonFeature.DeathWhistle = 120`. Who turns the
  feature into a spawned enemy is not visible in the dump **[runtime]**.
- **`AIDeath` / `DeathSettings` (dump.cs:132961 / 132941): the attack is a distance check, not
  a weapon hit.** Settings: `killTime`, `killDistance`, `attackStartSound`, `killPlayerSound`,
  `despawn`, `despawnTime`. The module keeps `attackTimer` and `hasAttacked` and overrides
  `OnRespawn`, `OnAttackStart`, `UpdateModule`. The player-side result is
  `CombatRunner.CombatNotification.DeathTouch = 15`.
- `AIDeathAnimation` / `DeathAnimationSettings` (dump.cs:132912 / 132888): an `idle` state
  name, `fallDirections : DirAction.Settings[]` (directional fall clips when it is put down),
  `crossfadeTime`, `timeScaleTarget`/`timeSmoothSpeed` (it slows its animator), `fx` +
  `fxBinding`. `OnKilled` breaks held stabs (`BreakHeldStabs`).
- `DeathWhistle : Prop` (dump.cs:9227, `Prop.Type.DeathWhistle = 47`): `RPC_Whistle`, and a
  coroutine `KillDeathDelayed(AI)`. The whistle is how Death is put down; with "Immortal Death"
  and the immortal kill/revive path on `AI`, it very likely revives **[runtime]**.
- Test path: the sandbox can spawn it. `SandboxUI.EV_SpawnTheDeathReaper`, `SpawnDeath`,
  `SandboxToggle.ToggleMod.DeathReaper`, `SandboxUI.DeathReaperEnabled`, and the general
  `CombatRunner.SpawnSandboxEnemy(EnemyTypeButton.EnemyType, Vector3, Quaternion)`.

### Spawning

`CombatRunner : Singleton<CombatRunner>, IOnEventCallback` (dump.cs:60336) owns it:
`SpawnEnemy(AIClass, …, Room, Encounter.SpawnPointMode, SpawnPointGroup, context, …)`,
`SpawnEnemy(AIClass, Vector3, Quaternion, SpawnIntroType, context, poolIndex)`, static
`SpawnDynamicEnemy`, `SpawnOffsetEnemy` (by `AIClass` or by `ObjectPool`), the sandbox
variants, `LoadEnemyPoolForCheats(ObjectPool)` / `RPC_LoadPool(string poolName)`, and the
encounter/wave RPCs (`RPC_ActivateEncounter`, `RPC_WaveStarted`, `RPC_WaveFinished`). Spawns
are picked by `AIClass`, so a variant hooked on spawn sees the class, not a name.

### Look: renderers, hit flash, dissolve

- `CharacterOptimizer : MonoBehaviour` (dump.cs:43491): merges the enemy's meshes at `Awake`
  (`mergeMeshes`, `mergedRenderer`, `MainRenderer`), holds `renderers`, `baseMaterial`, one
  `MaterialPropertyBlock` for the body (`SetMaterialPropertyBlock`, event
  `OnMaterialBlockChanged`), and per-`AI.Type` `materialVariations`, `dissolveVariations`,
  `fXVariations` (`SetMaterialToAIType`). Stab wounds are decals (`stabWoundDecals`).
- `MaterialHandler` (dump.cs:98975): wraps **one** `SkinnedMeshRenderer`; modes `Default`,
  `DepthPass`, `FresnelPulse`, `Custom`; `SetHidden(bool)` swaps in `hiddenMaterials`. A second
  way to hide a body that keeps the renderer enabled, worth comparing with `ShadowsOnly`.
- `HitFlashHandler` (dump.cs:139430): **one renderer, through a `MaterialPropertyBlock`**
  colour (`flashColorId`). Flash 0.125 s at intensity 3; aim hover glow 0.35 held 0.1 s.
  `StartFlash(intensity, time)`, `Hover(float)`, `UpdateHover`, global switch
  `HitFlashEnabledGlobal`. Mirroring it onto a skin = copy that colour every frame, or postfix
  `StartFlash` / `Hover`.
- `DissolveHandler` (dump.cs:98618) + `AIDissolve` / `DissolveSettings` (dump.cs:133028 /
  132987): swaps the model's renderers to a dissolve shader and drives `_Dissolve` (keyword
  `_ALPHA_DISSOLVE` for the alpha mode); settings `inTime`, `outTime`, `fxTime`,
  `useAllRenderers`, `rendererTransform`, `deactivateOnDissolved`. `AIDissolve` also registers
  **`instancedEyes`** (`AddInstancedEyes`, `UnregisterInstancedEyes`): the game's own enemy eyes
  are a separate instanced system, to be hidden with the body and maybe reused for the wolf.

### Outlines (the blue outline bug)

- `FXOutline : MonoBehaviour` (dump.cs:65986) holds **its own renderer list**:
  `Entries : List<FXOutline.Entry { renderer, mesh, isSkinned, subMeshCount }>`, filled by
  `ResolveRenderers` / `CollectEntries` from its hierarchy, minus `excludedObjects` and
  `effectNameFilter`. HD mode builds a shell from `shellSource : SkinnedMeshRenderer` (or
  `bakedShellMesh`) with weld buffers. `glowVisibility` is AlwaysOnTop or OnlyWhenOccluded.
  API: `SetHighlighted`, `SetGlowColor`, `SetGlowWidth`, `Refresh`, `ResolveRenderers`.
- `FXOutlineManager : LazySingleton` (dump.cs:66165) draws every registered outline from
  **one command buffer** on the camera (`CameraEvent` 17, AfterForwardAlpha; compute at 10),
  passes mask, shell, silhouette, blur and compose. It draws meshes by reference, so a
  renderer's `shadowCastingMode` does not matter to it. That is the bug.
- Players: `CharacterPrefab.highlightEffect : FXOutline`, `EnableXRayMode(bool,
  CharacterPrefab.XRayState)` with states XRay, Danger, XRayHidden and per-state colour and
  width. The stick press is `XRayPing` (dump.cs:42265): `WasXRayButtonPressed`, `Ping`, static
  `UnityEvent<bool, XRayState> EnableXRayMode`, `displayXRaySecs`.
- A custom avatar is a scene-root object (`DontDestroyOnLoad`, `AvatarSwapper`), not under
  the `CharacterPrefab`, so `ResolveRenderers` can never find it.
- From the disassembly: `EnableXRayMode` returns at once for the local player (you never see
  your own outline) and otherwise enables `highlightEffect` and sets its colour and width.
  `ResolveRenderers` (`GetComponentsInChildren<Renderer>`, LOD groups, `CollectExcluded`,
  `CollectEntries`, then a signature of the entries' instance IDs) is called from `OnEnable`,
  `Refresh` and `UpdateRegistration`. The manager draws entries with `CommandBuffer.DrawRenderer`
  and `DrawMesh` (combined shell of readable meshes) and the HD shell via `RecordProceduralShell`.
- **Fix (CustomAvatars 0.42.23, `Avatars/OutlineFollower.cs`):** a postfix on
  `ResolveRenderers` replaces a swapped player's entries with the avatar's mesh renderers and
  clears `shellSource`/`bakedShellMesh` while swapped (weld data and the manager's skinned
  binding are released once, on attach); revert restores both. It runs on every client for
  every swapped player. Untested in the headset.
- Props have their own `Prop.fxOutline`. Neither `AI` nor `References` holds one, so enemies
  are probably never outlined **[runtime]**.

### Lighting and post-processing

- **Bloom is a player setting**, not a given: `PlayerData.Values.EnableBloom = 1023`,
  `BloomIntensity = 1032`, `GraphicsSettingId.Bloom = 6`, `FastBloom : MonoBehaviour`
  (dump.cs:113770, threshold, softness, iterations, also sharpen and crease AO). A glowing eye
  has to read without it, so the halo is needed whether or not bloom is on.
- Also present: `MobilePostProcessing` (Quest path), `VolumetricLight` and
  `VolumetricLightsPostProcess`, `FastVolumetricsManager`.
- Bakery: `BakeryVolume` (static `globalVolume`), `BakeryVolumeReceiver`, `BakeryVolumeTrigger`,
  `BakeryPointLight`, `BakeryDirectLight`, `BakerySkyLight`, `ftLightmaps`. Characters are lit
  from Bakery volumes (the player material's `BAKERY_VOLUME` keyword) **[runtime: confirm on an
  enemy material]**.

### What the runtime probe read (2026-11-17, sandbox, solo, game 1.3)

From the CustomAvatars enemy probe (Numpad 7/8/9, see the CA README). One sandbox Death, one kill.

- **Rig.** Root `Death` (scale 1.44 from `Specs.scale`) carries `Sauron.AI` and
  `CharacterOptimizer`. Two skinned renderers on layer 21: `Death` (one submesh, 80 bones, root
  bone `WraithPelvis`) and `Death_weapon` (one bone, `WeaponsR` under the right palm). The
  skeleton is `Wraith*`: pelvis, two spine bones, ribcage, collarbones, arms, five fingers per
  hand, neck ×2, head, jaw, hood chain, four robe chains (front, back, L, R) and short legs
  (`WraithLLeg1/2/Ankle`, same on R). Head path is in `References.head`.
- **Animator is humanoid** (`isHuman` True, avatar `Soul Keeper_01Avatar`), `AlwaysAnimate`,
  `applyRootMotion` False, one layer. Parameters `IsMoving` (bool), `AttackSpeed` (float).
  States (hashes matched with `Animator.StringToHash`): `Spawn Intro` (default), `Idle`, `Move`,
  `Attack`, plus hit reactions and `attackspecial`. The only clip event is an `AE_Footstep` on
  one hit clip; no `AE_FX` fired during the encounter.
- **Modules** (by their settings): `CombatAirborne`, `Death`, `Flight`, `LookAtIK`,
  `DeathAnimation`, `Teleport`, `Dissolve`. No `AIRunner`. `navSolverType` is **Flight**: it
  floats, 0.25–0.5 m above the terrain in the samples, and can teleport.
- **It cannot be hit.** `Health.mortality` is `IgnoreHit` (6 HP, never lost), and all seven
  capsule colliders (layer 11, NPC) are disabled.
- **The kill.** `killDistance` 3 m, `killTime` 0.3 s. In the sample the `Attack` state was
  playing at 3.1 m, the player died with Death 2.7 m from their head, and Death went back to
  `Move` then `Idle`. In the sandbox the player respawned about 5 s later.
- **Leaving.** `despawn` True, after the `Lifetime in minutes` parameter (60 here).
  `DissolveSettings`: in 3 s, out 2 s, all renderers, `PotoVR/Characters_Normal` →
  `PotoVR/Characters_Normal_Dissolve`.
- **Materials.** Body and weapon share `M_Death_01`, shader `PotoVR/Characters_Normal`
  (queue 2000, instanced), keywords `BAKERY_VOLUME _ALPHA_ON _DOUBLEALPHA_ON _XRAY_ON`. Eyes are
  a child `fx_skeleton-eyes` (inactive on Death), quads with `PotoVR/Candle` at queue 4000.
  `Aura_Wraith-Shadow` is a child effect; the weapon carries an additive particle trail.
- **Lighting (sandbox).** No realtime lights within 15 m, no light probes, one lightmap, one
  `BakeryVolume` (the global one). Ambient Flat at 0.151 grey × 1.77, linear fog. Enemy and
  player materials all carry `BAKERY_VOLUME`, so characters are lit from the Bakery volume and
  little else: a shader that does not sample it only gets flat ambient.
- **Bloom.** `FastBloom` is on the SteamVR camera and enabled on the test machine (amount 2.5,
  threshold 0.6, softness 1, 3 iterations). Whether that is the game's default is not known.
- **Outlines.** `FXOutlineManager.HDMode` False, `Disabled` False. The local player's outline is
  disabled (as the dump said) but the redirect wrote the avatar's two skinned renderers into
  `Entries` without an error.

### Still open

- [ ] Ambient, probes and lights in a dark dungeon room (the sandbox is the only reading).
- [ ] Whether the whistle's kill is followed by a revive, and what Death does after it.
- [ ] Whether bloom is on by default for a fresh profile.
- [ ] The outline redirect seen from a friend's client.
