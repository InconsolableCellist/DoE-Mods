# DoE Friends-Mod — Project Plan

Goal, in order: **(1)** custom avatars (VRChat-style models) visible to modded friends in
private lobbies, **(2)** face/eye tracking via VRCFaceTracking bridged over OSC and synced
to friends, **(3)** stretch: custom inventory/loot. Everything is friends-only,
private-lobby-gated, and never touches PlayFab/UGS progression.

Companion doc: [GAME-INTERNALS.md](GAME-INTERNALS.md) — the decompiled class map this plan
is built on. Re-read it before each milestone; re-dump after every game patch.

---

## Ground rules

1. **Private lobbies only.** The mod hard-gates every feature behind a room check
   (room is invite-only/invisible AND every occupant completed the mod handshake).
   In a room with any vanilla player, the mod goes fully inert.
2. **Never write to PlayFab/UGS.** No granting items, no XP/coin/unlock writes, no Cloud
   Code calls. Custom inventory persists to a local JSON only. (No anti-cheat exists, but
   the economy is server-side and shared; this is both etiquette and ban-safety.)
3. **No hardcoded RVAs/offsets.** All hooks go through Il2CppInterop-generated assemblies
   by type/method name (survives updates better; breakages become compile errors after
   re-generation instead of silent memory corruption).
4. **Don't redistribute extracted game assets.** AssetRipper output is for reference only.
   Avatars: only models you have the license to use and share with your friends.

---

## Phase 0 — Environment & first boot (~1 evening)

- [x] **Install MelonLoader 0.7.3** (extracted at `tools/MelonLoader.x64/`; installed by
      copying `version.dll` + `MelonLoader/` next to `DoE.exe`).
      First VR-less sanity check: launch once; ML will run Cpp2IL + Il2CppInterop and emit
      `MelonLoader/Il2CppAssemblies/*.dll`. The game itself may refuse to start without an
      HMD — that's fine, assembly generation happens before scene load. Logs:
      `MelonLoader/Latest.log`.
      **Status 2026-08-31: installed but never launched** — `MelonLoader/Il2CppAssemblies/`
      does not exist yet and there are no logs. Nothing in `src/` can compile until it does;
      this is the one remaining hard blocker in Phase 0.
- [x] **Mod project skeleton** (`src/DoEFriendsMod/`): `net6.0` class library.
      References: `MelonLoader/net6/MelonLoader.dll`, `MelonLoader/net6/0Harmony.dll`,
      `MelonLoader/Il2CppAssemblies/{Assembly-CSharp,PhotonUnityNetworking,PhotonRealtime,Il2Cppmscorlib,UnityEngine.CoreModule,...}.dll`
      (reference via `<Reference>` HintPaths or a copy step; add a `Directory.Build.props`
      with `$(GameDir)` so paths aren't hardcoded per-machine).
      `[assembly: MelonInfo(typeof(Core), "DoEFriendsMod", "0.1.0", "dan")]`,
      `[assembly: MelonGame("Othergate LLC", "Dungeons of Eternity")]` (verify exact
      company/game strings from `app.info` — they are `Othergate LLC` / `Dungeons of Eternity`).
      Build → drop DLL in `<game>/Mods/`.
      **Done 2026-08-31** — see [src/README.md](../src/README.md). `Directory.Build.props`
      resolves `$(GameDir)` from CLI → `DOE_GAME_DIR` → `src/Local.props` → the default Steam
      path; the csproj globs every DLL in `Il2CppAssemblies` instead of a hand-maintained
      list, and post-build copies into `<game>/Mods/`. Restores and evaluates clean;
      *unverified past that* — it cannot compile until Il2CppAssemblies exists.
- [ ] **UnityExplorer**: `tools/UnityExplorer.MelonLoader.IL2CPP.zip` is stale for ML 0.7.x —
      expect it to fail; if so, grab a maintained fork build (search current DoE/ML modding
      community consensus) or temporarily use BepInEx 6 + the BepInEx IL2CPP build in a
      separate game copy for recon. Getting *some* runtime inspector working is the
      highest-leverage step in this phase.
- [x] **Recon build written** (supersedes the planned hello-world hooks): the v0.1.0 melon
      discovers `AvatarPlayer`s by polling the game's own `AvatarPlayer.AllPlayers` roster
      rather than patching spawn methods — cheaper, unable to destabilise the game, and it
      also catches avatars that existed before the melon woke up. Environment/XR/quality,
      per-avatar hierarchy + bones + shaders, Photon room options and an event-code sniffer
      all write to `UserData/DoEFriendsMod/recon/recon-<timestamp>.md`. The only patch in the
      build is a read-only prefix on `LoadBalancingClient.OnEvent`.
- [x] **Runtime recon checklist** — *done 2026-08-31 over two solo sessions (lobby, and a full
      Soul Harvest run with enemies active). Answers written up in GAME-INTERNALS.md →
      Runtime findings.* Everything except remote-player behaviour is now measured, and that
      is Phase 1 exit criteria rather than a Phase 0 blocker. Original list:
      hierarchy dump of a spawned `AvatarPlayer` (self + one remote), bone names of the
      merged `SkinnedMeshRenderer`, character shader name, whether the local body renders
      for self, `NetworkObjectPool` prefab table, Photon room options inside a private
      party (`IsVisible/IsOpen/PlayerTtl`), any `RaiseEvent` codes seen
      (`PhotonNetwork.NetworkingClient.EventReceived` logger).

**Exit criteria:** mod loads, logs player spawns, recon questions answered. — **Met
2026-08-31**, except the UnityExplorer item (skipped: the recon melon answered everything
UnityExplorer was wanted for, so a runtime inspector is no longer on the critical path) and
remote-player observations, which move to Phase 1.

---

## Phase 1 — Handshake & integrity gating (core infra, ~1–2 evenings)

Everything later depends on knowing "who else here runs the mod." Policy decision (Dan,
2026-08-30): **ALL features are gated** — including purely local/cosmetic ones — behind
checksummed handshake + private-room checks. Design principle: the mod must be provably
inert with respect to the base game and its servers unless every participant is running
the identical build by choice.

- [x] **Self-checksum at startup** (`Recon/SelfCheck.cs`): the mod SHA-256s its own DLL
      (`Assembly.Location`) and every avatar bundle against its manifest's `sha256`
      (mismatched bundles are refused with a log line, not "best-effort loaded").
- [x] On joining a room, set Photon **player custom properties** (`Gate/ModRoster.Advertise`)
      (replicate automatically to all clients, including late joiners; the game puts
      `Ping` and `Build` in the player property bag, so `dfm.*` keys are collision-free but
      the bag is shared — and `Build:1.2.3849` is a free game-version signal to fold in):
      - `dfm.ver` — mod semver
      - `dfm.sha` — first 16 hex chars of the mod DLL's SHA-256
      - `dfm.caps` — capability bitfield (avatars / face / items)
- [x] `ModRoster` + `ModGate` (`src/DoEFriendsMod/Gate/`): maps `Player → (ver, sha, caps)?`; events `PeerJoined/PeerLeft/RosterChanged`,
      and a single **`ModGate.Active`** master switch consulted by every feature. Active
      requires ALL of:
      1. room is private — **test `IsVisible == false` and nothing else** (confirmed
         2026-08-31). Do *not* test `IsOpen`: it flips to `False` the moment a run is in
         progress, which would make the mod go inert on entering a dungeon.
         `PlayerTtl = 0`, so a leave is final and the roster needs no rejoin window,
      2. every occupant advertises identical `dfm.ver` **and** `dfm.sha`,
      3. local self-checksum passed.
      Any condition fails (or a vanilla player joins mid-run) → live-downgrade to fully
      inert: despawn custom avatars, stop all custom event traffic, unhook cosmetic
      patches. Re-evaluate on every join/leave/property-change callback.
- [x] Honest threat model note (recorded in `ModGate`'s doc comment): peer checksums are self-reported — this is
      **anti-footgun, not anti-malice** (a hostile client can lie). Within a friend group
      that's exactly the right guarantee: it prevents version-skew bugs and accidental
      mixed-lobby activation, and the *hard* guarantees (no PlayFab writes, no
      stat-affecting patches outside `ModGate.Active`) are structural properties of the
      mod code itself, enforced by review of our own diffs.
- [x] Custom traffic transport (`Net/ModNet.cs` + `Net/PhotonHook.cs`): **140–149 cleared 2026-08-31** over a full solo dungeon run —
      the game's own custom events cluster low (codes 1, 2, 50, 70) and nothing came near the
      block. Re-check once a second player's join/leave traffic has been observed.
      One code per subsystem: `140=handshake/misc`, `141=avatar-manifest`,
      `142=face-stream`, `143=items`. Wrap `PhotonNetwork.RaiseEvent` +
      `EventReceived` in a tiny `ModNet` API (targeted send, reliable/unreliable flag).
- [x] Version policy: **exact match on both `dfm.ver` and `dfm.sha`**, or the gate stays shut.

**Exit criteria:** two modded clients in a private party see each other's version; a
vanilla test client causes clean inert-mode. — **Built 2026-08-31 (v0.2.0), unverified with a
second player.**

Implementation notes worth keeping:

- **Polling, not `IInRoomCallbacks`.** Implementing an Il2Cpp interface from managed code is a
  known source of interop pain; a 2 Hz poll over a four-player room costs nothing. Same
  reasoning as the recon avatar watcher.
- **Fail closed on staleness.** Because the roster is 2 Hz, `ModGate` also compares
  `CurrentRoom.PlayerCount` (live, every frame) against the roster size. A mismatch means
  there's an occupant we haven't vetted, and an unvetted occupant means inert. Without this
  there'd be a half-second window where a joining vanilla player is invisible to the gate.
- **`ModNet` is the only place bytes leave the process**, and it refuses to send — and drops
  inbound — whenever the gate is shut. Putting the check at the boundary rather than at each
  call site is what makes inertness structural instead of a promise to be careful.
- **One deliberate exception to "writes nothing while inert":** `dfm.*` player properties are
  advertised as soon as we're in a *private* room, before the gate opens. Peers can't be
  discovered without someone speaking first. A public lobby therefore sees nothing at all from
  this mod, and the worst case in a private lobby with a vanilla friend is two inert string
  properties nobody reads.
- Event **140** carries a hello whose only job is to prove `RaiseEvent` round-trips between two
  modded clients before Phase 2 builds on it.

---

## Phase 2 — Custom avatars (the marquee feature)

### 2a. Asset pipeline (Unity side, ~1 evening once, then per-avatar minutes)

- [ ] **Export project**: Dan's existing ALCOM/VCC avatar project (Unity 2022.3.x —
      VRChat projects are already on 2022.3.22f1, bundle-compatible with the game's
      2022.3.62f2). Exporting straight from the VRC project is fine: the exporter strips
      SDK components from a clone, the original is untouched.
- [x] **Automated exporter written**: `unity/AvatarExport/Editor/DoEAvatarExporter.cs` —
      drop the `AvatarExport/` folder into the project's `Assets/`, select the avatar,
      `Tools ▸ DoE Mod ▸ Export Selected Avatar`. Strips VRC components + missing
      scripts, validates the humanoid rig, auto-detects Unified Expressions blendshapes /
      visemes / eye bones, builds the Win64 LZ4 bundle, and emits
      `<name>.avatar` + `<name>.manifest.json` (with sha256) into `<project>/DoEExport/`.
      Untested until the Unity project exists — expect a shakedown pass.
      **Updated 2026-08-31** with the measured target constraints: flags shaders not vouched
      for under Single Pass Instanced, reports max bone influences per vertex against the
      game's `FourBones` clamp, records the full humanoid bone map and the jaw bone in the
      manifest (so the mod wires VRIK and the voice-jaw fallback by lookup rather than by
      guessing names), and warns when there is neither a jaw bone nor a `JawOpen` shape to
      drive the Vivox fallback with.
**First real export, 2026-08-31** — Dan's `Rex_Quest_12_No_Feathers_Modified_FurCheeks`, a
fully-loaded VRChat avatar (VRCFury, 158 VRC components, toggled outfits, 16 shaders). It
produced a working bundle, and it found four exporter bugs that a simple avatar never would
have:

1. **Eye-bone paths came out `null`** in the manifest while `humanoidBones.LeftEye` was
   correct — the clone is destroyed before the manifest is written, and a `Transform` into a
   destroyed hierarchy evaluates as null. Bone paths are now captured as strings up front.
2. **Gaze shapes were sourced from a VRCFury face-tracking *debug panel***
   (`…/Face Tracking UE Debug/WorldObject/Window/FT_Debug`), not the face. A first-match scan
   over an arbitrary renderer order will do that. The scan now skips scaffolding by name and
   visits meshes richest-in-UE-shapes first.
3. **Height read 3.87 m** for a normal avatar — the bounds seed included the avatar origin and
   swept in effect quads parked off-body. Now measured from active skinned meshes only, and
   the manifest records `humanScale` plus a `suggestedScale` to land the head at the game's
   1.5 m.
4. **`Hidden/InternalErrorShader` was filed under "shader we can't vouch for"** when it
   actually means a broken material that renders magenta in-game. Now a named ERROR.

Measured profile of a representative VRC avatar, for planning: **37 MB bundle**, 69/98 UE
shapes, 15/15 visemes, eye bones present, **no jaw bone** (so the Vivox fallback must drive the
`JawOpen` shape), locked Poiyomi throughout, max 4 bone influences (within the game's clamp).

Bundle size is the open problem: four of these load at once in a dungeon at 90 Hz, and every
friend needs the file on disk first. The exporter now strips inactive objects by default
(toggled outfits and variants), reports mesh cost, and warns above 25 MB.

**Second pass (analysis mode)** confirmed the fixes and narrowed the size question. Stripping
12 inactive objects removed the broken material entirely (it was on a disabled mesh) and all
UE shapes now resolve from the `Body` mesh alone. The geometry turns out to be **lean** —
1 skinned mesh, 51,823 verts, 5 submeshes — so the 37 MB is **textures**, not meshes. The
exporter now reports unique textures with dimensions and memory, biggest first, so the size
problem has an address.

**Final numbers after Dan's texture pass: bundle 37.1 → 17.8 MB**, textures 46.9 → 23.4 MB,
75/98 UE shapes (with approximate aliases enabled), 15/15 visemes, all shaders locked Poiyomi.
Ready to load.

**Third pass** located the size precisely: **21 textures, 46.9 MB in GPU memory** — one 2048² BC7 body
albedo at 10.7 MB and five more 2048² DXT1 maps at 5.3 MB each, including an *emission* map and
an *ambient occlusion* map for cheek fluff. Halving the secondary maps to 1024² would take
roughly 26 MB down to 6 MB without touching the silhouette. Geometry needs no work at all.

- [ ] Per avatar: prefab containing — humanoid-rigged model (Animator, humanoid avatar,
      T-pose), meshes, materials, **shaders included in the bundle**.
      Strip all VRC SDK components (descriptors, PhysBones, contacts, constraints) —
      they'd be missing-script stubs in game. Keep blendshapes (visemes + face tracking
      shapes: ARKit or Unified Expressions naming).
- [ ] **Shader constraints** — *confirmed 2026-08-31: DX11, `SinglePassInstanced`,
      `skinWeights = FourBones`, shader level 50, 90 Hz target*:
      - Shader must support **SPS-I** (modern VRC shaders do: liltoon, Poiyomi 8/9 —
        Poiyomi must be *locked* before export; when in doubt, liltoon or Standard).
      - No VRC-specific shader features (audio-link, VRC light volumes) — they just no-op,
        but avoid depending on them visually.
      - Test matrix per avatar: renders in both eyes, correct in mirror-less VR, dissolve
        not required (we skip the game's dissolve shader for custom avatars initially).
- [ ] Build script (`Editor/BuildAvatarBundles.cs`): `BuildPipeline.BuildAssetBundles`
      → `StandaloneWindows64`, chunk-based compression (LZ4 — fast partial loads),
      output `bundles/<name>.avatar` + a `manifest.json`
      (`name, sha256, rigType, viseme/facial blendshape map, eye bones, jaw?, scale hints`).
- [x] Distribution v1 **(mod side built 2026-08-31, v0.3.0)**: everyone drops the same
      `.avatar` + `.manifest.json` pair into `<game>/UserData/DoEFriendsMod/Avatars/`.
      `Avatars/AvatarLibrary` scans that folder and **refuses any bundle whose SHA-256 doesn't
      match its manifest** — refused with a log line, never best-effort loaded. Two friends
      silently running different bytes under one avatar name is precisely what the Phase 1
      gate exists to prevent, and it would be undone here if a truncated copy got through.
      Still to do: the peer-sync half (name+sha in the avatar-manifest event on code 141;
      mismatch → that peer keeps their vanilla avatar). Later option: LAN/HTTP auto-fetch from
      the wearer, size-capped.

**Preview spawner (v0.3.0).** Before any of 2b, `Avatars/AvatarPreview` loads a bundle and
stands the avatar in front of you (F6), with no IK, no networking and no swapping. It exists
to settle three things that can only be settled on a GPU inside this game:

1. does a locked-Poiyomi bundle survive the round-trip, or come back as
   `Hidden/InternalErrorShader` (magenta)?
2. does it render in **both** eyes under Single Pass Instanced?
3. do the manifest's `(renderer path, blendshape index)` pairs actually resolve on the
   instantiated object?

It logs a runtime shader audit and a manifest↔mesh cross-check for exactly those. Gated like
everything else — ground rule 1 has no cosmetic exemption, and a preview that ignored the gate
would be the first crack in it.

Bundle loading goes through MelonLoader's `Il2CppAssetBundle`, not Unity's `AssetBundle`:
`AssetBundle.LoadAsset<T>` is a generic native method Il2CppInterop can't dispatch cleanly.

### 2b. In-game attachment (~1–2 weeks of evenings, the real work)

Strategy: **parallel-rig puppet**, not mesh-graft. Don't fight `AvatarFactory`'s merged
mesh — hide it and run our own model beside the game's skeleton:

> **Revised 2026-08-31 after the first recon session.** The model is not under
> `AvatarPlayer`. A player is two scene-root objects: `Player_<nick>` (logic, PhotonView,
> IK targets, holsters — *no renderers at all*) and `Model_<nick>` (`CharacterPrefab` + the
> whole visual rig). Reach the model via `AvatarPlayer.FullBody` / `.RemoteRig`. The game's
> own rig is **humanoid** (`RemoteAnimator.isHuman == true`, avatar `Player_01Avatar`), which
> is the good news: a humanoid VRC avatar maps onto it directly. See GAME-INTERNALS.md →
> Runtime findings.

**Bundle sharing (v0.5.1).** Unity refuses to load a bundle file that is already loaded —
`LoadFromFile` simply returns null — so pressing F4 while the F6 preview was still spawned
failed with a misleading "wrong Unity version" message. `AvatarBundle` is now refcounted and
keyed on the full path: preview and swap share one load, and the file unloads only when the
last holder releases it.

**Swap succeeds but nothing is visible (v0.5.2 investigation).** Every step reports success —
model instantiated, VRIK wired, vanilla mesh hidden, spring chains built — and no avatar appears
anywhere. The F6 preview of the same bundle IS visible, and the two differ in only two ways:
parenting under `Model_<nick>`, and VRIK. Added placement diagnostics (world position, lossy
scale, layer, distance from `IKTargetHead`, per-renderer bounds, `VRIK.enabled` /
`references.isFilled` / `solver.initiated`, and every camera's culling mask against the model's
layer) plus two bisect toggles, `SwapUseVrik` and `SwapHideVanillaMesh`.

Leading hypothesis, and it would invalidate an earlier conclusion: **the local player's own
`Model_<nick>` may not be rendered to the local camera at all** — hidden by a culling mask
rather than by a disabled renderer, which is why the Phase 0 recon read it as
`enabled: True, activeInHierarchy: True` and I concluded "you render a full body". Setting
`SwapHideVanillaMesh=false` tests it directly: if the vanilla body is also invisible, the
conclusion in GAME-INTERNALS → "Self view" is wrong and self-swap cannot be verified solo.

**First cut built 2026-08-31 (v0.5.0), untested.** `Avatars/AvatarSwapper.cs`, F4 toggles it on
the local player. Steps 1, 2 and 4 below are implemented; grounding (3), self-view policy,
nickname repositioning and lifecycle hooks are not yet. `VRIK.References` is filled from the
manifest's 53-bone humanoid map rather than FinalIK's `AutoDetectReferences()` — auto-detect
guesses from bone names and VRChat rigs use every convention there is, whereas the exported map
came from Unity's own humanoid rig and is authoritative. The component is added while the model
is **inactive**, because VRIK's `Awake` initiates its solver and must not run before
`references` is populated.

- [ ] `AvatarSwapper` component per `AvatarPlayer`:
      1. Load bundle → instantiate model under a neutral child of **`Model_<nick>`**
         (`AvatarPlayer.FullBody.transform`), not the `AvatarPlayer` root.
      2. Add our own **VRIK** (the game ships FinalIK — use the game's own
         `Il2CppRootMotion.FinalIK.VRIK` type, no need to bundle it; confirmed live on
         `Model_<nick>` itself, with `GrounderIK` on a separate `Player Grounder` object).
         Auto-detect
         references from the humanoid Animator; wire solver targets to the game's
         `IKTargetHead` / `IKTargetLeftHand` / `IKTargetRightHand` transforms
         (they exist and sync for remote players via `headT/leftHandT/rightHandT`).
      3. Add `GrounderIK` mirroring `CharacterPrefab.grounderPrefab` settings for feet.
      4. Hide the vanilla merged `SkinnedMeshRenderer` — it's `CharacterPrefab.characterMesh`
         at `Model_<nick>/character_mesh`, 5672 verts over 98 bones (+ nickname repositioned
         above the custom head via `AvatarNickname`). Note `Model_<nick>` also holds 460 other,
         inactive cosmetic renderers; leave them alone.
      5. Scale handling: scale model so its head sits at `IKTargetHead` height
         (`AvatarPlayer.headYHeight` exists — investigate); clamp to sane range.
- [ ] **Self view** — *Phase 0 answered this: it's two meshes, not one.* Your third-person
      body (`Model_<nick>/character_mesh`) **is** enabled locally, and independently the
      first-person arms come from `VR Controller/FPS-Arms-Model`, a second complete skeleton on
      the SteamVR rig with the weapon-stat UI panels parented into its forearm bones.
      Start with *no self body* (hide `character_mesh` for yourself, leave the FPS arms
      vanilla): it side-steps the "my face blocks the camera" bugs and keeps the weapon UI
      intact. Replacing the FPS arms is a separate, later piece of work.
- [ ] **Own arms in first person — backlog, not v1.** Decided 2026-08-31. It is achievable, but
      the cheap route is *not* to build a second arms rig: it's to render our own avatar body
      for the local player (head bone shrunk or layer-culled, the way VRChat does it) and
      disable only the vanilla arm *mesh* renderers on `VR Controller/FPS-Arms-Model`
      (`FPS_Arm_Armor`, `FPS_Arm_NovaGuild*`) while leaving the UI renderers parented into its
      forearm bones (`ui_counter_weapon_left/_StatsPanel`, `ui_counter_kills`) alive — or
      re-parenting them onto our avatar's forearms. Reasons to defer: nobody else sees it, VRC
      avatars have wildly varying arm proportions so it needs per-avatar calibration, and the
      weapon-stat UI is easy to break. Zero impact on what friends see, which is the Phase 2
      exit criterion.
- [ ] Weapon/holster compatibility: `AvatarHolster` offsets attach to game skeleton bones —
      keep the vanilla skeleton alive (bones still animate via the game's own VRIK even
      with renderer hidden), so holsters, grab poses, and hit detection stay untouched.
      Custom model is *cosmetic-only*; **hitboxes remain vanilla** — important fairness
      property, document it for the group.
- [ ] Lifecycle hooks: spawn (`AvatarPlayer` init / `RespawnAvatar`), death/ragdoll
      (vanilla ragdoll uses the vanilla mesh — first version: on death, unhide vanilla
      mesh + hide puppet; polish later), dissolve/teleport FX, `RPC_SetRigType`.
- [ ] Idle life: blink via avatar's blink blendshapes (reuse `Idler` timings), voice jaw
      flap fallback: drive avatar's jaw-open blendshape from the same
      `VivoxParticipantTap` energy the game uses (pre-face-tracking parity).
- [x] **Secondary motion (v0.4.0)** — promoted out of the polish backlog, because an avatar
      whose tail sticks straight out reads as broken, not unpolished. Two halves:
      *Exporter*: captures `VRCPhysBone` / `VRCPhysBoneCollider` configuration **before** the
      strip pass (reflection, no SDK reference) and flattens each PhysBone's subtree into
      root-to-leaf chains in `manifest.dynamics`. One PhysBone can yield several chains where
      the subtree branches, e.g. hair strands.
      *Mod*: `Avatars/SpringBones.cs`, a Verlet spring chain driven from `OnLateUpdate`, with
      sphere-collider push-out. Not a MonoBehaviour — injecting a managed type into the Il2Cpp
      domain is avoidable friction.
      Chose this over the game's own `Multiflex` (see GAME-INTERNALS): Multiflex is better, but
      it has no `Update` of its own and its state is in `NativeArray` fields we don't control
      through interop. Recorded as the quality upgrade path.
      **Unity fake-null bug, found on the first re-export** (0 chains from 3 PhysBones): an
      unassigned serialized `Transform` reference is not C# null — it's a live object whose
      overloaded `==` reports null. So `GetMember(...) as Transform ?? c.transform` never took
      the fallback, and `if (root == null) continue` then silently skipped every chain. Both
      VRCPhysBone and DynamicBone document an unassigned root as meaning "the GameObject this
      component is on"; the exporter now emulates that through an `AsTransform()` helper that
      collapses fake-null properly. Anything reading a Transform by reflection must go through
      it. The exporter also reports each component's owner, resolved root, child count and
      chains produced, and reads the older `DynamicBone` asset.
      **Mapping is a heuristic, not a reproduction** of the PhysBone model: `max(stiffness,
      pull)` → restoring force, `immobile` → drag, `gravity` → fraction of 9.81 m/s².
      Expect to tune it in the headset; capsule/plane colliders and angle limits are unhandled.

      **Unit bug, v0.4.0 → v0.4.1.** The first working build had the tail stick straight out and
      ignore gravity entirely. Cause: the force terms were in different units — the restoring
      force was a per-frame *fraction* of the positional error (~0.18 of the error each frame)
      while gravity was `9.81 * dt²` (~0.0012 m/frame). Restore outweighed gravity by roughly
      150:1, so the chain snapped back to the animated pose regardless of the PhysBone's gravity
      setting, and the leftover energy read as a bounce. Replaced with the VRM SpringBone
      formulation: stiffness is a fixed push *along the rest direction* and gravity a downward
      push, both in m/s and both scaled by `dt`, so their ratio — not their units — decides
      where the chain settles.

      Tuning constants live in MelonPreferences and are read **per frame**, so editing the .cfg
      and pressing **F3** retunes a spawned avatar with no respawn: `SpringStiffnessScale`,
      `SpringGravityScale`, `SpringDragBase`, `SpringDragFromSpring`, `SpringCollidersEnabled`,
      `SpringsEnabled`. Chain parameters and collider radii/positions are logged at spawn.
      **Tuned in-headset on a real avatar: `SpringGravityScale = 0.6`** (down from the 1.5 I
      guessed) with everything else at default — now the shipped default. Capture confirmed
      working at the same time: 3 chains / 12 bones from VRCPhysBone, 0 sphere colliders, which
      also retired the "is a collider holding it out?" theory.
      `endpointPosition` **is** emulated — PhysBone appends a virtual bone past the last real
      one, and without it the final segment of a tail stays rigid.
- [ ] Optional polish backlog: eye-glance reuse of `Glancer`, per-avatar shader keyword QA,
      victory-move/emote handling.

**Exit criteria:** two modded friends in a private lobby see each other's VRC models with
correct head/hand tracking, feet grounded, weapons still holster on the (invisible)
vanilla skeleton, and vanilla peers still see stock avatars.

---

## Phase 3 — Face & eye tracking (VRCFaceTracking bridge)

Standard: **Unified Expressions** (the VRCFT blendshape standard) end-to-end — avatars
author UE-named shapes, the wire protocol indexes the canonical UE table. Full design,
shape table, manifest schema, and wire format: **[FACE-TRACKING.md](FACE-TRACKING.md)**.

### 3a. Local capture (~1 week of evenings incl. investigation)

- [ ] **OSC ingest**: tiny UDP+OSC parser in the mod (OSC is trivial — address, type tags,
      floats; no dependency needed), listening on `127.0.0.1:9000` (configurable).
- [ ] **VRCFT integration** — investigate in this order:
      1. VRCFT settings for a fixed OSC endpoint (legacy/manual mode, no OSCQuery) — if
         current VRCFT still supports it, done, zero extra work.
      2. Else implement minimal **OSCQuery**: mDNS advertise `_oscjson._tcp` + a tiny HTTP
         endpoint serving an avatar-parameter JSON, so VRCFT auto-discovers us as if we
         were VRChat. (~300 lines, well-documented protocol.)
      3. Fallback: a standalone relay exe (VRCFT → its normal output → relay → our port).
- [ ] **Parameter mapping**: VRCFT v5 emits Unified Expressions floats
      (`/avatar/parameters/v2/JawOpen`, `EyeLidLeft`, …). Map UE names → avatar blendshape
      names via the avatar's `manifest.json` (same mapping table style the VRC face-tracking
      community uses; support ARKit-named shapes too). Eyes: UE gaze → eye bone rotations.
- [ ] Apply locally at LateUpdate after IK, with smoothing + saturation.

### 3b. Network sync (~2–3 evenings)

- [ ] Face stream on event code 142, **unreliable**, 12–15 Hz: quantize each active
      parameter to 1 byte, delta-gated (send only params that moved > ε; full keyframe
      every 2 s for late joiners). ~40 params × 15 Hz ≈ <1 KB/s — negligible next to voice.
- [ ] Receivers interpolate toward targets (~100 ms lerp window) and apply through the
      same mapping layer.
- [ ] Mutual capability flag in the avatar manifest (`hasFaceTracking`), so non-tracked
      friends keep the Vivox jaw-flap fallback.

**Exit criteria:** your eyebrows move on your friend's screen.

---

## Phase 4 — Stretch: inventory & loot

Do this only after 1–3 are stable; it's the most invasive layer.

- [ ] **Loot-table tweaks (easy tier)**: Harmony patches on `ChestLoot`/`LootSpawn`/
      `LootSpawner` `LootDef` selection, active only when the whole room is modded and only
      on the master client (vanilla replication handles the rest). House-rules territory:
      drop rates, curated chest pools, "mutation" nights.
- [ ] **Custom networked items (hard tier)**: intercept
      `NetworkObjectPool.Photon.Pun.IPunPrefabPool.Instantiate` — names with a `dfm:`
      prefix resolve from our bundles (prefab must carry a `PhotonView` and the right
      `Grabbable`/`Prop`/`Weapon`-derived components; study a vanilla weapon prefab via
      AssetRipper first). All-clients-modded is a hard requirement (already guaranteed by
      the Phase 1 gate). Start with a purely cosmetic prop (a mug you can throw), then a
      reskinned `WeaponMelee` clone with vanilla stats.
- [ ] **Mod-side inventory**: session persistence in
      `UserData/DoEFriendsMod/inventory.json` (who's carrying which custom item), restored
      on room join via event 143. Explicitly *not* PlayFab — custom items have zero coin/
      XP/economy value and vanish in vanilla lobbies.
- [ ] Fairness guardrail: custom weapons clamp to vanilla damage tables (`WeaponDamage`
      ScriptableObjects) unless the whole group opts into a labeled "sandbox night" toggle.

---

## Effort summary

| Milestone | Estimate (hobby pace) |
|---|---|
| 0. Loader + recon | 1–2 evenings |
| 1. Handshake/gating | 1–2 evenings |
| 2. Custom avatars | 2–3 weeks |
| 3. Face tracking | 1.5–2 weeks |
| 4a. Loot tweaks | 2–3 evenings |
| 4b. Custom items | 2–4 weeks |

## Standing risks

- **Game updates** re-shuffle everything: re-run `tools/run_dumper.bat`, regenerate
  Il2CppAssemblies (delete `MelonLoader/Il2CppAssemblies` and relaunch), rebuild, re-check
  the Open Questions list. Budget a maintenance evening per patch.
- **Il2CppInterop friction**: Il2Cpp objects are proxies — no C# `??`/`==` null semantics
  on destroyed Objects, `Il2CppSystem.Object` vs `System.Object` boxing at RPC boundaries
  (RPC `object[]` params!), coroutines need `MelonCoroutines`. Expect this to be where
  the swearing happens.
- **VR iteration cost**: every test is a headset session. Mitigate with aggressive
  logging, a debug HUD, and hot-reloadable config (`MelonPreferences`).
- **ToS**: no anti-cheat, but this is still EULA-gray. Private lobbies, no economy writes,
  no public distribution of anything containing game assets. Accept the residual risk
  knowingly.

## Repo layout

```
DoE-mod/
├── docs/            PLAN.md (this), GAME-INTERNALS.md, FACE-TRACKING.md
├── dump/            Il2CppDumper output: dump.cs, DummyDll/, stringliteral.json, il2cpp.h
├── tools/           run_dumper.bat, MelonLoader.x64/, UnityExplorer zips, AssetRipper, Cpp2IL
├── src/             DoEFriendsMod/ (mod source) + build docs
├── unity/           AvatarExport/ (drop-in exporter for the ALCOM avatar project)
└── bundles/         built .avatar bundles + manifests  [to create]
```
