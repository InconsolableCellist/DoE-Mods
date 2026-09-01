# CustomAvatars — building

## One-time prerequisites

1. **MelonLoader 0.7.3 installed into the game folder** — `version.dll` + `MelonLoader/`
   next to `DoE.exe`. (Done: see `tools/MelonLoader.x64/`.)
2. **Launch the game once with MelonLoader present.** ML runs Cpp2IL + Il2CppInterop and
   writes `MelonLoader/Il2CppAssemblies/*.dll`. This build references those, so *nothing
   here compiles until that folder exists* — the project fails with an explicit message
   rather than a wall of missing-type errors.
   First generation takes several minutes; watch `MelonLoader/Latest.log`.
3. **.NET SDK** capable of targeting `net6.0` (SDK 9 is fine).

## Build

```
cd src/CustomAvatars
dotnet build
```

The build copies `CustomAvatars.dll` into `<GameDir>\Mods\` automatically.
Pass `-p:NoDeploy=true` to skip that.

**Game in a different folder?** Copy `src/Local.props.example` to `src/Local.props` and set
`<GameDir>`, or set `DOE_GAME_DIR`, or pass `-p:GameDir=...`. No csproj hardcodes a path.

## What this build does

**v0.2.0 — Phase 0 recon + Phase 1 gating.** Still no gameplay patches; the only Harmony patch
in the build is a read-only prefix on `LoadBalancingClient.OnEvent`, shared by the recon
event-tally and the mod's own event dispatch.

- `Gate/` — `ModRoster` discovers modded peers from Photon player custom properties
  (`ca.ver` / `ca.sha` / `ca.caps`); `ModGate` is the master switch every future feature
  consults; `ModHandshake` sends one hello on code 140 to prove the transport works.
- `Net/` — `PhotonHook` owns the single inbound patch; `ModNet` is the **only** place bytes
  leave the process, and it refuses to send (and drops inbound) whenever the gate is shut.
- `Recon/` — everything from Phase 0, unchanged.

The gate is open only when: the room is private (`IsVisible == false` — **not** `IsOpen`,
which flips false mid-dungeon), every occupant advertises an identical version *and* DLL hash,
the live `PlayerCount` matches the roster (fail closed on stale data), and our own self-checksum
passed. Watch the MelonLoader console for `*** ModGate ACTIVE` / `*** ModGate INERT` lines with
the reason attached.

Output: `<GameDir>\UserData\CustomAvatars\recon\recon-<timestamp>.md`, plus headlines in the
MelonLoader console and `MelonLoader/Latest.log`.

It answers the Open Questions in `docs/GAME-INTERNALS.md`:

| Question | Where the answer lands |
|---|---|
| Stereo rendering mode / XR loader / GPU / skinWeights | `## Environment / XR / graphics` |
| Is the local player's own body rendered? | `SELF-BODY VERDICT` line in the local avatar dump |
| Runtime hierarchy + bone names of the merged mesh | `### Hierarchy` and each `#### SkinnedMeshRenderer` |
| Character shader name + keywords | `Materials:` under the merged mesh |
| Private-room Photon options (`IsVisible`/`IsOpen`/`PlayerTtl`) | `## Photon room state` |
| Which custom event codes the game already uses | `### Photon event codes seen this session` |
| Are VRIK/GrounderIK on the rig at runtime? | `FinalIK scan` in the avatar dump |

## Using it

Everything fires automatically — **you do not need to press anything in the headset.**

- environment: on the first scene
- each `AvatarPlayer`: on sight, then **again `RedumpDelaySeconds` later** (default 10 s) so
  the settled rig is captured after the merged mesh finishes building
- room state: whenever it changes
- `NetworkObjectPool` prefab table: once, on entering a room

Desktop hotkeys (game window focused — these do work in VR if you click the window first):

- **F2** — cycle which avatar you wear (saved to config)
- **F3** — reload MelonPreferences.cfg (spring constants apply live, no respawn)
- **F4** — swap your own avatar on/off (Phase 2b)
- **F5** — rescan `UserData/CustomAvatars/Avatars/`
- **F6** — spawn/despawn the custom avatar preview in front of you
- **F7** — re-dump environment
- **F8** — re-dump every `AvatarPlayer`
- **F9** — room state + Photon event tally

## Installing an avatar

Copy **both** files from the Unity exporter's `DoEExport/` folder into
`<game>/UserData/CustomAvatars/Avatars/`:

```
<name>.avatar
<name>.manifest.json
```

The mod SHA-256s the bundle against the manifest on startup (and on F5) and **refuses a
mismatch** rather than loading it anyway. Set `Avatar` in MelonPreferences.cfg to
pick between several; leave it empty to use the first.

Tunables live in `UserData/MelonPreferences.cfg` under `[CustomAvatars]`
(`HierarchyMaxDepth`, `MaxBlendShapesLogged`, `MirrorReconToConsole`, …).

## Session checklist

### Solo pass (v0.1.1 — no friend required)

This is what closes the remaining Phase 0 questions. Just play; the melon does the work.

1. Boot into a private lobby and **stand still for ~15 seconds** so the settled re-dump fires
   with your model fully built.
2. Open the character menu (spawns an `AvatarHologram` — the non-networked, safe test target).
3. **Start an actual dungeon run** and play for a few minutes. This is the part the first
   session missed: the Photon event-code tally from a lobby idle isn't enough to clear the
   140–149 block, and loot/enemy traffic is where extra codes would show up.
4. Quit and grep the transcript.

### Two-player pass (Phase 1 exit criteria — needs one friend)

Both of you on the **same** `CustomAvatars.dll` (identical SHA — the console prints it at
startup; if they differ the gate stays shut by design).

1. **Private party, both modded.** Expect `*** ModGate ACTIVE` on both clients, a
   `Roster + actor N ... dfm 0.2.0 / <sha>` line, and `*** Handshake received from actor N`.
   That's `RaiseEvent` round-tripping on code 140.
2. **Vanilla test.** Have them remove the DLL and rejoin. Expect
   `*** ModGate INERT — 1 vanilla player(s) present` within about a second, and any later send
   attempt logged as refused.
3. **Skew test.** Edit one client's version string, rebuild, rejoin — expect
   `*** ModGate INERT — build skew`.
4. Also grab a **remote `AvatarPlayer` dump** while you're both in (it happens automatically),
   to confirm remote players use the same three-root split.

## Layout

```
src/
├── Directory.Build.props      GameDir resolution, shared compiler settings
├── Local.props.example        per-machine GameDir override template
└── CustomAvatars/
    ├── Core.cs                MelonMod entry, hotkeys, wiring
    ├── ModConfig.cs           MelonPreferences
    ├── Gate/
    │   ├── ModGate.cs         the master switch — nothing acts unless this is Active
    │   ├── ModRoster.cs       peer discovery via ca.* player custom properties
    │   ├── ModPeer.cs         one room occupant, modded or vanilla
    │   ├── ModCaps.cs         capability bitfield (append-only)
    │   └── ModHandshake.cs    code-140 hello; proves the transport round-trips
    ├── Avatars/
    │   ├── AvatarManifest.cs  exporter manifest schema + tolerant loader
    │   ├── AvatarLibrary.cs   folder scan, SHA-256 verification
    │   ├── AvatarBundle.cs    Il2CppAssetBundle wrapper, prefab lookup
    │   ├── AvatarPreview.cs   F6 spawn + runtime shader / manifest audit
    │   ├── AvatarSwapper.cs   F4 parallel-rig swap onto a player, VRIK-driven
    │   └── SpringBones.cs     Verlet secondary motion from captured PhysBones
    ├── Net/
    │   ├── PhotonHook.cs      the single read-only prefix on Photon's inbound dispatch
    │   └── ModNet.cs          the only place bytes leave the process; gate-enforced
    └── Recon/
        ├── ReconLog.cs        transcript writer (file primary, console headlines)
        ├── Interop.cs         Il2CppInterop null/name/path helpers
        ├── SelfCheck.cs       mod DLL SHA-256 (Phase 1 groundwork)
        ├── EnvironmentRecon.cs  XR / graphics / quality
        ├── HierarchyDump.cs   GameObject tree, skinned meshes, shaders
        ├── ModelRecon.cs      CharacterPrefab / Model_<nick> rig + NetworkObjectPool table
        ├── AvatarWatcher.cs   AvatarPlayer discovery + per-avatar dump
        └── PhotonRecon.cs     room options + event-code sniffer
```

## Known soft spots (first-compile suspects)

- `using Il2Cpp;` in `AvatarWatcher.cs` assumes Il2CppInterop rewrote the game's global
  namespace to `Il2Cpp`. If it doesn't resolve, open
  `MelonLoader/Il2CppAssemblies/Assembly-CSharp.dll` in a decompiler and fix the one line.
- Same for `Il2CppPhoton.Pun` / `Il2CppPhoton.Realtime` / `Il2CppExitGames.Client.Photon`.
- The csproj globs *every* DLL in `Il2CppAssemblies`. If two of them export the same type
  and the compiler complains about ambiguity, add an exclusion to the `Il2CppAssembly` item.
