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
- [x] **Mod project skeleton** (`src/CustomAvatars/`): `net6.0` class library.
      References: `MelonLoader/net6/MelonLoader.dll`, `MelonLoader/net6/0Harmony.dll`,
      `MelonLoader/Il2CppAssemblies/{Assembly-CSharp,PhotonUnityNetworking,PhotonRealtime,Il2Cppmscorlib,UnityEngine.CoreModule,...}.dll`
      (reference via `<Reference>` HintPaths or a copy step; add a `Directory.Build.props`
      with `$(GameDir)` so paths aren't hardcoded per-machine).
      `[assembly: MelonInfo(typeof(Core), "CustomAvatars", "0.1.0", "dan")]`,
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
      all write to `UserData/CustomAvatars/recon/recon-<timestamp>.md`. The only patch in the
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
      `Ping` and `Build` in the player property bag, so `ca.*` keys are collision-free but
      the bag is shared — and `Build:1.2.3849` is a free game-version signal to fold in):
      - `ca.ver` — mod semver
      - `ca.sha` — first 16 hex chars of the mod DLL's SHA-256
      - `ca.caps` — capability bitfield (avatars / face / items)
- [x] `ModRoster` + `ModGate` (`src/CustomAvatars/Gate/`): maps `Player → (ver, sha, caps)?`; events `PeerJoined/PeerLeft/RosterChanged`,
      and a single **`ModGate.Active`** master switch consulted by every feature. Active
      requires ALL of:
      1. room is private — **test `IsVisible == false` and nothing else** (confirmed
         2026-08-31). Do *not* test `IsOpen`: it flips to `False` the moment a run is in
         progress, which would make the mod go inert on entering a dungeon.
         `PlayerTtl = 0`, so a leave is final and the roster needs no rejoin window,
      2. every occupant advertises identical `ca.ver` **and** `ca.sha`,
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
- [x] Version policy: **exact match on both `ca.ver` and `ca.sha`**, or the gate stays shut.

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
- **One deliberate exception to "writes nothing while inert":** `ca.*` player properties are
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
- [x] **Install/uninstall guide** — [INSTALL.md](../INSTALL.md), written to be handed to a
      friend with no context. Removal gets equal weight to installation and is stated plainly
      (delete one DLL to disable the mod; delete four things to remove everything; nothing
      lives outside the game folder). Also documents the "everyone needs the *same file*, not
      the same version" constraint that falls out of the gate's SHA comparison, and how to read
      the gate's refusal messages.
- [x] Distribution v1 **(mod side built 2026-08-31, v0.3.0)**: everyone drops the same
      `.avatar` + `.manifest.json` pair into `<game>/UserData/CustomAvatars/Avatars/`.
      `Avatars/AvatarLibrary` scans that folder and **refuses any bundle whose SHA-256 doesn't
      match its manifest** — refused with a log line, never best-effort loaded. Two friends
      silently running different bytes under one avatar name is precisely what the Phase 1
      gate exists to prevent, and it would be undone here if a truncated copy got through.
      **Peer sync built 2026-08-31 (v0.10.0)**: `Avatars/AvatarSync.cs` on event 141, reliable
      (a dropped avatar message means someone looks wrong for the whole session, unlike the
      face stream where a loss costs one stale frame). Only the avatar NAME plus a 16-char hash
      prefix crosses the wire — never the model. A peer missing the file gets a log line naming
      it and the folder to put it in; a peer with a *different build* of the same name is
      refused outright, since silently using our copy would mean the two of you are looking at
      different models.
      `Avatars/AvatarSwapManager.cs` keeps one swapper per player. Avatar messages routinely
      arrive before the sender's `AvatarPlayer` has spawned, so they're held and retried each
      frame rather than dropped.
      Self-only behaviours are now gated on an `IsSelf` flag — head chop, hiding the
      first-person arms, and finger posing from *our* controllers. Applying any of those to a
      peer would leave them headless on our screen and take our own arms away for someone
      else's body. Later option: LAN/HTTP auto-fetch from the wearer, size-capped.

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

> **Revised twice on 2026-08-31.** First: the model is not under `AvatarPlayer`. Second, after
> the swap threw the avatar 109 m across the map — **do not parent our model under
> `Model_<nick>` either.** VRIK's procedural locomotion moves the character root to follow the
> head target, and the game moves `Model_<nick>` to follow the player; two systems driving one
> position compound every frame, and the result runs away in XZ with Y pinned at floor level.
> Making it a scene root under a holder of our own **did not fix it** — still ~105 m, and the
> third run landed at x = −52.85 from x = 52.17, near enough a sign flip to name the cause:
> VRIK owns `references.root`, which is the *model* transform, and having a solver drive a
> child's world position mixes spaces. The F6 preview never had the bug because its model is a
> plain scene root with nothing above it.
>
> Final arrangement: the model is instantiated as a **bare scene root** (`DontDestroyOnLoad`,
> no holder), and **we** position it every `LateUpdate` by copying `Model_<nick>`'s position and
> rotation. That object is the game's own answer to "where are this player's feet and which way
> are they facing" — authoritative, computed for us every frame, and nothing has to converge on
> anything. `locomotion.weight` therefore defaults to **0**: procedural locomotion exists to
> move a root nobody else drives, and leaving it on meant two systems fighting over one
> transform. `SwapLocomotionWeight` can turn it back on once the basics are right.
>
> Two lessons worth keeping: **don't let a solver own a transform you also want to control**,
> and a guard has to watch the transform that actually moves — the leash silently did nothing
> for a whole debugging round because it watched the holder while VRIK moved the child.
>
> The model is not under `AvatarPlayer`. A player is two scene-root objects: `Player_<nick>` (logic, PhotonView,
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
      1. Load bundle → instantiate the model as a **bare scene root**, positioned each frame
         from `AvatarPlayer.FullBody.transform` (see the note above). No holder, no parenting.
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
**Body placement works (v0.5.5).** The avatar stands in the right place with head and hands
tracking. Two known issues from that first look:

- **Wrist orientation.** The custom hand sits roughly 90° off the arm — the game's IK targets
  are authored for its own rig's wrist convention and a VRChat rig rarely agrees. v0.6.0 calls
  FinalIK's `VRIK.GuessHandOrientations()`, which derives `wristToPalmAxis` and
  `palmToThumbAxis` from the avatar's own hand and finger bones, and additionally routes each
  arm at a *child* of the game's hand target whose local rotation comes from config
  (`SwapHandOffset{Left,Right}{X,Y,Z}`, applied every frame so F3 dials it in live).
- **Wrist offsets measured (v0.6.1).** `GuessHandOrientations` reported sensible axes
  (`wristToPalm (0,1,0)`, `palmToThumb (∓1,0,0)`) but did not fully resolve it; the working
  values on a real VRChat rig were **X = −90, Z = 180 on both wrists**, now the shipped
  defaults. Still per-avatar, but a far better starting point than zero.
- **"Solver threw the avatar 100 m away" was a false alarm.** MelonLoader's `OnLateUpdate` runs
  after every MonoBehaviour `LateUpdate`, so VRIK had already moved its root by the time the
  diagnostic sampled it — and `FollowVanillaRoot()` then put it back before the frame rendered.
  The avatar was always in the right place. Fixed by correcting the root *before* measuring;
  the per-frame displacement is now logged as information, since a large steady value would
  mean something in the solver still wants the root.
- **Hiding the body did not hide the arms (v0.7.2).** `SwapHideVanillaMesh` worked correctly
  all along — it hides `Model_<nick>/character_mesh`, the *third-person* body. What you look at
  in VR is `VR Controller/FPS-Arms-Model`, a second complete rig on the SteamVR object. Phase 0
  recorded this ("self view is two meshes, not one") and the swapper only ever handled one of
  them. Now hides renderers matching `SwapFpsArmPrefixes` (default `FPS_Arm`) under that model,
  and deliberately not the rest: the weapon-stat and kill-counter panels are parented into the
  same rig's forearm bones, so a blanket hide would take away real UI. Original enabled states
  are restored on revert — several of those renderers are already off because they belong to
  cosmetics that aren't equipped.
- **Arms: the game disables VRIK on your own body (measured 2026-08-31).** After switching to
  pose retargeting the legs were excellent and the arms sat near an A-pose, moving only a
  fraction of the controller's travel. The diagnostic said why:
  `vanilla IK: ikEnabled=True, VRIK.enabled=False, LOD=0, armWeights L=1/1 R=1/1`.
  **`VRIK.enabled=False`** — the game switches the IK component off on your *own* third-person
  body, so its arms are pure locomotion animation and are never solved to your controllers.
  That is also why it looked right under the old VRIK approach: that aimed at
  `IKTargetLeftHand`/`RightHand`, which are accurate, rather than at the body, which isn't.
  No amount of forcing fixes copying a pose that was never computed.
  Fix (v0.15.0): `Avatars/ArmIK.cs`, a two-bone analytic solver aiming the arms at the hand
  targets, **for your own avatar only**. A remote player's body *is* solved — that is how you
  see them fight — so their arms keep the copied pose, which is better than anything we would
  reconstruct from their targets. Everything below the shoulders still comes from the game's
  pose in both cases, so the legs are unaffected. `SwapForceVanillaIK` now defaults off: the
  game disables that IK deliberately and re-enabling it produced nothing useful.
- **T-posed mannequin, and the general shape of delta retargeting (v0.16.0).** Delta
  retargeting preserves whatever difference the two skeletons had *at capture*, and an imported
  avatar is instantiated in its bind pose — usually a T-pose — while the game's rig is standing
  naturally. Legs barely notice, because legs are nearly identical in both poses; **arms differ
  by about ninety degrees**, which is the A-pose that appeared on the player and the T-pose that
  appeared on the mannequin. `ArmIK` was masking it on the player, so the mannequin is where it
  showed plainly.
  Fix: `PoseRetargeter.AlignAtCapture()` rotates each stored reference by whatever turns the
  avatar's limb direction onto the game rig's, using a static parent→child bone table to get
  each limb's direction. Direction alone doesn't pin down roll about the bone, so it isn't
  perfect — but a small roll error beats a limb sticking out sideways.
  `RetargetAlignAtCapture` turns it off.
- **Wrist pinching to a straw on palm-up (v0.16.0).** All of the forearm's pronation was landing
  on the wrist joint. `ArmIK` now passes a share of the roll back to the forearm using a
  swing-twist decomposition — only the component that spins about the bone, since bending the
  elbow there would move the hand off the target just solved for. `ArmTwistShare`, default 0.5.
- **Red finger outline around held weapons (v0.21.0).** Hiding the first-person arms used a
  *hide-list* of names starting `FPS_Arm`, which only removes what we thought of. Something
  else — an outline or highlight, evidently created or enabled when a weapon is grabbed, so it
  wasn't in the Phase 0 recon dump — was left behind once the mesh beneath it vanished, leaving
  a floating red outline of the fingers. Inverted to a **keep-list**
  (`SwapFpsArmKeepPrefixes`, default `ui_counter,_StatsPanel,TMP,Holster`): everything under the
  arms rig is hidden unless its path matches, so anything unanticipated is hidden by default and
  the short list is the part that needs maintaining. Every hidden renderer is logged by name.
- **Custom hand not quite on the weapon (v0.21.0).** A custom avatar's arms are rarely the game
  character's length, and the two-bone solver clamped the target to the arm's natural reach — so
  a shorter arm stopped short of the hand target, by more the further out you reached, which is
  exactly the "differs to a lesser or greater degree at various positions" that testing found.
  `ArmStretch` (default 0.08) lets the arm extend a few percent past its natural length; the
  bone lengths scale with it so the elbow solve stays consistent.
- **Held weapon slides out of the hand while moving (v0.22.0).** Locomoting with the stick made
  the hand and the sword drift apart, worsening while moving and correcting when stopped. The
  arms chase `IKTargetLeftHand`/`RightHand`, which are the game's **networking-smoothed**
  targets (`AvatarPlayer` has `smoothingSpeed = 30`), while a held weapon is parented to the
  controller — which has no smoothing. So the two lag apart under acceleration.
  `SwapArmTargetSource = "Controllers"` aims the arms at `AvatarPlayer.LeftHand`/`RightHand`
  instead, which for the local player are the controller transforms themselves. Left defaulting
  to `IKTargets` so existing wrist offsets keep working — the two have different orientations,
  so switching means retuning `SwapHandOffset*` once.
- **Head clipping fixed (v0.7.0)** with the same trick VRChat's Head Chop uses: scale the head
  bone to ~0 so its geometry collapses out of view. The head is part of one merged
  SkinnedMeshRenderer, so there is no renderer or layer to switch off — per-bone scale is the
  only lever that reaches it. Scale is 0.0001 rather than 0, because a zero-scale bone gives
  Unity a degenerate matrix to skin through.
  Config: `SelfHideHead`, `SelfHeadBoneScale`, `SelfHeadShrinkBones` (default `Head`),
  `SelfHeadKeepBones`. The keep list scales a bone back up by the inverse to cancel its
  parent's shrink — that's the route to keeping a snout visible, and it needs the snout
  geometry weighted to its own bone. **This must stay local-only once avatars are networked**,
  or peers will see you headless.

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
- [ ] **Legs don't move when you walk.** With `SwapLocomotionWeight = 0` the legs just hold
      their rest pose. Three routes, cheapest first:
      **Route 1 was tested on 2026-08-31 and failed.** With `SwapLocomotionWeight = 1` and
      `SwapFollowVanillaRoot = false` on a bare scene root — no parenting involved — VRIK threw
      the avatar **97 m within 50 ms of the swap**, and kept doing it (11 leash trips). So the
      earlier 100 m runaway was *not* only the parenting: VRIK's procedural locomotion genuinely
      misbehaves in this setup. The displacement magnitude is suspiciously close to twice the
      distance from the world origin each time, which smells like a position applied with the
      wrong sign somewhere inside the solve, but that is a guess and not worth chasing.
      **Go to route 2.** v0.9.1 auto-recovers: five leash trips turns locomotion back off and
      says so, because the on-screen symptom is a body strobing between your feet and the far
      side of the map, which reads as "the avatar didn't appear" rather than as a bad setting.

      1. ~~**Turn VRIK's procedural locomotion back on.**~~ *(tried, see above)* It is precisely the feature for this —
         stepping legs from 3-point tracking. It was disabled because of the 100 m runaway, but
         that was diagnosed as the *parenting*, which is now fixed, and locomotion has never
         been retested against a bare scene root. It needs `SwapFollowVanillaRoot = false` as
         well: locomotion moves the root to place the feet, and pinning the root every frame
         leaves it nothing to move. Both are live-tunable as of v0.8.0, so this is a two-value
         experiment, with the 5 m leash as a backstop.
      2. **Retarget the vanilla legs by delta.** Capture both rigs' bone rotations at swap time,
         then each frame apply the game bone's rotation *change since capture* to ours. Robust
         to the two rigs having different rest poses, since only deltas are copied. Gives
         exactly the vanilla walk cycle.
      3. Unity's own humanoid retargeting is **not available**: `HumanPoseHandler` exists
         (dump.cs:849386) but only `GetHumanPose` survived IL2CPP stripping — there is **no
         `SetHumanPose`** in this build, so we can read the game rig's humanoid pose and cannot
         write it to ours. Worth knowing before anyone reaches for it.
- [ ] **Overlay GUI — scope decided 2026-08-31.** A *desktop* IMGUI panel (v0.12.1,
      `Overlay.cs`, F1 to hide) showing gate state, selected avatar, swap state and the key
      list. Deliberately **not** interactive and **not** in VR: Unity's IMGUI draws to the
      desktop mirror, never into the headset, so on-screen buttons would be no easier to reach
      than the keys they replace — you would still have to take the headset off to click them.
      A genuinely in-headset panel needs a world-space canvas plus laser-pointer interaction
      driven from `XRInput`, which is real work and can't be tested by anyone but the wearer.
      Not attempted yet, and not obviously worth it while the key list fits on one card.
- [x] **Equipment-room mannequin (v0.13.0)** — `Avatars/HologramSwapper.cs`. Turned out cheap
      for a reason worth recording: `AvatarHologram : Idler` is a **real humanoid rig with its
      own idle animation**, not a static prop, so generalising `PoseRetargeter` to take any
      `Animator` (rather than only an `AvatarPlayer`) was the whole job — and the custom avatar
      inherits the mannequin's idling and blinking for free. Scans every 2 s via
      `FindObjectsOfType<AvatarHologram>`, matches `hologram.Owner.ActorNumber` to whichever
      avatar that player is currently wearing, hides `avatarMesh` and parents ours to the
      hologram's animator transform so it picks up the pedestal's placement and scale.
      `avatarMesh` visibility is re-asserted every frame, because the hologram rebuilds itself
      whenever cosmetics change (`RecreateAvatarMesh`) and would otherwise re-enable it behind
      us. `HologramSwapEnabled` turns it off.
- [ ] **Replace the character-menu pedestal model** in the wardrobe UI (`AvatarCustomizer`),
      `MainMenu` and `UIEndMission` — the same `AvatarHologram` type, so the swapper above
      should already cover them if it finds them; verify which of those screens actually spawn
      one at runtime. The home world has your character on a
      pedestal for trying on cosmetics; showing the custom avatar there instead would make the
      swap feel like part of the game rather than a thing bolted on. Target confirmed:
      `AvatarHologram : Idler` (dump.cs:25323), held by `AvatarCustomizer.hologram`
      (dump.cs:50737 — the wardrobe menu), `PlayerRoomManager.NameDef.avatarHologram`
      (dump.cs:37730 — the home-room pedestal), `MainMenu.hologram` and `UIEndMission`.
      **Not** `Vendor : Idler`, which turns out to be an NPC shopkeeper that walks between
      locations. `AvatarHologram` clones a real `SkinnedMeshRenderer` with its own
      `boneLUT`/`remappedBones` via `CloneAvatarMesh()` / `RecreateAvatarMesh()` /
      `FinishCharacter(...)`, so the hook is either intercepting those or replacing `avatarMesh`
      after it builds. `AvatarHologram.Find(AvatarPlayer)` and `Find(PlayerHologram)` are public
      statics, which makes it easy to locate at runtime.
      Fitting real armour meshes to an arbitrary VRChat body is not realistic; roughly parenting
      the cosmetic to the matching humanoid bone is, and is probably good enough to browse with.
- [x] **Hand poses on grip/trigger (v0.9.0, built but untested)** — `Avatars/HandPoser.cs`.
      The game's own posing can't be reused: `HandPose` stores baked `Transform[] bones`
      captured from *its* rig, and those rotations are meaningless on a VRChat skeleton with
      different bone axes. So we curl procedurally, rotating each joint about the axis that
      actually bends it — derived from the avatar's own geometry as
      `cross(proximal→middle, middle→distal)`, with the palm's lateral axis as a fallback for a
      dead-straight finger. Tunable: `HandCurlDegrees` (70), `ThumbCurlDegrees` (40),
      `HandCurlSmoothing`, `HandPosesEnabled`. Negate the degrees if a rig bends backwards.
      Input, best first: `XRInput.GetFingerCurls(Handedness, bool)` for real per-finger curl;
      otherwise `left/rightHandTrigger` (grip) and `left/rightIndexTrigger`, with trigger
      driving the index and grip the rest, matching what the vanilla game does.
- [ ] **Hand poses: reuse the game's own pose selection** if the procedural curl looks wrong.
      `VRControllerHands.GetHandPoseType(float hold, float trigger, bool thumbCapTouch,
      PropRoot, bool isLeft, out float weight)` (dump.cs:17054) is the game's own decision
      function, and `LeftHandPoseOverride`/`RightHandPoseOverride` are settable — so the *pose
      choice* can be read or forced even though the pose *data* isn't portable.
- [ ] **Death and ragdoll handling.** Not on `AvatarPlayer`:
      `CharacterPrefab.SetRagdollEnabled(bool enablePhysics, bool enableColliders)`
      (dump.cs:28207) plus `isRagdolled` and `AddRagdollVelocity` act on the prefab's own
      `rigidbodies` / `colliders` / `joints` arrays, and dissolve is a global event —
      `GameEvents.InitiatePlayerDissolve` → `CharacterPrefab.OnInitiatePlayerDissolve(float)` →
      `DissolveHandler`, a shader swap over the character's renderers. `GameEvents` also exposes
      `OnLocalPlayerDied` / `OnRemotePlayerDied` / `OnPlayerSpawned` / `OnPlayerRespawned`,
      which are the clean hooks for swap lifecycle. First version stays simple: on death, show
      the vanilla mesh and hide the custom one — the vanilla ragdoll already has physics bodies
      and ours does not. Right now the
      custom avatar's fingers never move: gripping a weapon closes the vanilla hand while the
      custom paw stays open, which reads as broken even though nothing is. The game already
      solves this for its own rig with `AvatarHand` and `PropPose` (dump.cs, `AvatarHand`), so
      the grip state is available; we need to map it onto our avatar's finger bones (the
      exporter already records all of them in the humanoid bone map) and blend a closed pose in
      after VRIK in LateUpdate. Much smaller than full finger tracking, and it removes the most
      distracting artefact of actually playing.
- [ ] **Finger tracking / ASL — a requirement, not polish.** Support the same finger
      articulation VRChat does: real per-finger tracking on Valve Index controllers, and
      VRChat's gesture set on Quest controllers. Several people in the group are deaf, and
      without articulated fingers they cannot sign ASL in game at all — this decides whether
      they can communicate, so it does not get dropped if time runs short.
      The exact VRChat gesture set will be supplied when we start; don't guess at it.
      **Feasibility confirmed 2026-08-31:** `SteamVR_Action_Skeleton` (dump.cs:614311) is in the
      build with `thumbCurl`…`pinkyCurl` and `fingerCurls[]`, and `OpenVRInput` (dump.cs:19384)
      already overrides `XRInput.GetFingerCurls` using it — so genuine per-finger articulation
      is reachable on Index with no new dependency. Oculus is the open question: `OVRInput`
      appears only for buttons, axes and haptics, with no `OVRHand`/`OVRSkeleton`, so Quest
      controllers will likely need VRChat's gesture set rather than real curl data. `HandPoser`
      already prefers real curls and falls back, so the Quest path is where that work goes.
      Groundwork already in place: the exporter records the full humanoid bone map including
      every finger bone, and the game's own rig has 3-joint fingers, so the bones exist on both
      sides. Needs: reading controller finger/gesture input, a pose blend layer applied after
      VRIK in LateUpdate, and a network channel so peers see it (a hand-pose stream is small —
      far cheaper than the face stream).
- [ ] Optional polish backlog: eye-glance reuse of `Glancer`, per-avatar shader keyword QA,
      victory-move/emote handling.

**Avatar selection was a real trap.** With two avatars installed and `Avatar`
empty, both clients fell back to `library.First()` — alphabetically first — so two people wore
the same model and each thought the mod had picked wrong. Now: a loud startup warning listing
every installed avatar when the choice is ambiguous, a confirmation line when it isn't, and
**F2** cycles the selection and saves it.

**First two-player session, 2026-08-31 — it works.** Both players saw each other's custom
avatars. Eight issues found, and the diagnosis of the worst one changed the architecture:

**Remote arms and weapons didn't track (and the head dragged the whole torso).** The remote
`AvatarPlayer` dump explains it: on a remote client `AvatarPlayer.LeftHand` points at
`Model_<nick>/…/hand_l` — the vanilla rig's **already-solved** hand bone — not at a controller.
The game computes a complete, correct pose for every player, local and remote, including legs;
that is simply what a vanilla character looks like. We were ignoring it and asking VRIK to
re-derive the same thing from targets that behave differently on remote clients.

So `Avatars/PoseRetargeter.cs` copies the vanilla pose instead, and `SwapPoseSource` now
defaults to `VanillaRig` (set it to `VRIK` to go back). Retargeting is by **delta** — each frame
we apply the source bone's rotation *change since capture* to the target bone — which makes it
immune to the two skeletons disagreeing on rest pose and bone axes, as a UE4-style rig and a
VRChat rig always will. Hips translation is copied too, for crouching.

This should fix remote arms (#5), head-drags-torso (#2) and missing leg locomotion (#7) in one
change, and it retires the whole class of VRIK problems — the runaways, the 73 m/frame root
drift, procedural locomotion. Fingers stay with `HandPoser`; it runs after the retarget.

Other fixes in v0.11.0:
- **Clothing meshes blinking out (#6):** `updateWhenOffscreen` is now set on every skinned mesh.
  A SkinnedMeshRenderer culls against bounds derived from its **bind pose**, so an avatar posed
  far from bind gets culled while plainly on screen.
- **One pinky bending backwards (#4):** a near-straight finger makes `cross(v1,v2)` degenerate
  and the fallback axis can land mirrored. Fingers on a hand now have to agree — any whose axis
  opposes the majority is flipped. The thumb is exempt, since it genuinely differs.
- **Spring chains folding through the body (#1):** VRChat's PhysBone limits are per-chain and
  apply to every bone in it, with four parts: `limitType` (None / Angle / Hinge / Polar),
  `maxAngleX`, `maxAngleZ`, and **`limitRotation`** — the frame the limit is measured against.
  The first attempt only had `limitType`/`maxAngleX` and measured against the raw rest
  direction, which centres a cone on the wrong axis. v0.11.1 captures all four in the exporter
  and implements all three shapes: Angle is a cone about the reference direction; Hinge flattens
  the bone onto the hinge plane and clamps its swing within it; Polar is an elliptical cone
  clamped separately on two axes. `SpringMaxAngleFallback` (75°) still applies a generous cone to
  chains that set no limit at all. Hinge and Polar are close approximations rather than
  reproductions — the SDK's exact solve isn't public — but they constrain the right axes by the
  right amounts. **Needs a re-export**: `maxAngleZ` and `limitRotation` aren't in manifests
  produced before this change.

Fixed in v0.12.0:
- **#3 remote fingers (`Avatars/HandSync.cs`, event 144).** Ten bytes, one per finger,
  unreliable, ~12 Hz, sent only when a finger actually moved, with a keyframe every two seconds
  for late joiners. Unreliable is the right call: a dropped packet costs one stale pose for a
  fraction of a second and the next tick fixes it, whereas retransmitting stale hand positions
  would be worse than skipping them. `HandPoser` gained a `RemoteDriven` mode — peers get the
  same poser fed from the wire instead of from our controllers. Deliberately the same shape the
  face stream will take, so the awkward parts (rate limiting, change gating, per-sender state)
  get worked out carrying ten bytes rather than ninety-eight.
- **The `Interop.Alive` bug, and everything it caused (v0.18.0).** `Alive()` only checked the
  managed proxy's pointer, never whether Unity had **destroyed** the native object — a
  limitation written into its own doc comment on day one and never fixed. A scene change
  destroys the old `AvatarPlayer` and `CharacterPrefab`; the stale references passed every
  guard and threw on first member access. That single defect produced three separate symptoms
  in one death test: a `NullReferenceException` from `HologramSwapper` every frame of the
  end-of-mission screen, a silent `Swap threw` on every F4 afterwards, and a player left with
  **no body at all** — old model gone, vanilla mesh still hidden on an object nothing could
  reach. `Alive()` now checks `m_CachedPtr`, which is Unity's own destroyed-object test, so
  every existing call site is fixed at once.
  Also: `AvatarSwapManager.HealSelf()` re-applies the avatar when the player object underneath
  it is replaced, and `Revert()` restores the vanilla mesh first and defensively — being left
  bodiless with no way back is much worse than a cosmetic glitch.
- **Death: ragdoll properly (v0.18.0).** The first attempt hid the custom avatar and showed the
  vanilla body, on the assumption that a ragdoll needs physics bodies our model doesn't have.
  That was wrong: the ragdoll drives the vanilla rig's **bones**, and retargeting copies bone
  rotations, so the custom avatar ragdolls for free. It stays visible now. The one thing it
  can't inherit is travel — a ragdoll's hips move while `Model_<nick>`'s transform may not — so
  the root follows the source hips while down.
- **Death and respawn (v0.12.0).** There was no lifecycle handling at all. Death is a ragdoll —
  the game switches physics on over `CharacterPrefab`'s rigidbodies and dissolves its renderers —
  and our model has neither physics bodies nor dissolve-capable materials, so following it would
  leave a custom avatar standing rigidly upright beside the falling corpse. Instead the vanilla
  body is shown for those few seconds and the custom one hidden, then swapped back on respawn.
  The retarget's captured reference is rebuilt on the way back, since the rig was ragdolled and
  re-posed while we weren't looking.

Still open from that session:
- **#3 remote fingers don't move.** *(fixed above)* Finger poses are read from *our* controllers, so peers see
  nothing. Needs a hand-curl stream — small, and the same shape as the planned face stream.
- **#8 holsters sit loosely on the custom body.** Expected: holsters attach to the vanilla
  skeleton, which is the right thing for hitboxes but means the visual anchor is the old body's
  proportions. Re-parenting the holster *visual* to the equivalent custom bone would fix the
  look without touching hit detection.

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
      `UserData/CustomAvatars/inventory.json` (who's carrying which custom item), restored
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
├── src/             CustomAvatars/ (mod source) + build docs
├── unity/           AvatarExport/ (drop-in exporter for the ALCOM avatar project)
└── bundles/         built .avatar bundles + manifests  [to create]
```
