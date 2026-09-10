# Dungeon Descent — feasibility investigation

Date: 2026-09-07. Sources: `dump/dump.cs` (game build 1.2.3849), disassembly of `GameAssembly.dll`
with `tools/disasm.py` plus a call-site scan (`tools/xref.py`, new — see "Recon tooling" at the
end), the Unity `Player.log` of the last sessions, the
CustomAvatars recon dumps, and a raw string scan of `DoE_Data/data.unity3d`. Nothing below has
been verified at runtime by *our* code yet; the "verify first" list at the end is the recon
session this needs before any mod code.

The ask: a dungeon that goes on longer, descends floor by floor with a gentle difficulty ramp,
lets you teleport back to the hub and resume where you left off, works solo and with friends
coming and going, and still feels procedurally generated. Three candidate directions were on the
table: rebuild Daggerfall's dungeons, rebuild Diablo's, or bend the game's own generator.

## Verdict

**Feasible, and direction 3 (bend the existing generator) is the one to build.** The other two
are not separate projects: Diablo's structure is what the game's generator already produces, so
"recreate Diablo" reduces to a design spec for direction 3; Daggerfall would be a content
pipeline (modelling and rigging rooms the generator accepts), not a mod.

It is cheaper than it looks because the game already ships every hard piece:

1. **A seed-deterministic room-graph generator with exposed knobs** (`DungeonBuilder`,
   dump.cs:34643): main path + side branches + secret rooms + locked rooms, every parameter a
   plain field on a ScriptableObject.
2. **"Launch any seed, any time" plumbing** (`DungeonScanner.AddCustomMission`, dump.cs:36642, the
   `DungeonSeedConsole` where you type a seed, the `SavedDungeonConsole` that replays saved
   ones). The "dungeons regenerate on a timer" behaviour only touches the *menu list*; a seed
   never expires.
3. **Master-authoritative room replication with late-join sync.** Only the master client runs
   the generator; rooms arrive on everyone else as Photon room objects carrying their type and
   features. Friends joining mid-run is vanilla behaviour, and — the big one — **a modified
   generator only has to run on one machine.**
4. **An exit teleporter whose whole job is "return to lobby"** (`Teleporter.ReturnToOutpost`
   → `GameManager.ReturnToLobby`). Redirecting it to "load the next floor" is a single hook.

The one real design constraint: **a floor is a scene load, not a staircase.** Rooms are Photon
room objects tied to the `dungeon_builder_v1` scene lifecycle (occlusion, view-ID cache,
encounters, hazards all assume one dungeon per scene), and no vanilla room set has a vertical
connector — doors sit on one floor plane. "Going down" is therefore the exit teleporter
dissolving you into the next floor, with a new seed, realm and difficulty. That is exactly how
the game already dresses the lobby → dungeon transition, so it reads naturally.

What we cannot get cheaply: Daggerfall-style multi-storey labyrinths and Diablo's open,
loop-heavy grids. The generator builds trees with doors on a plane; loops only appear when two
branches happen to meet door-to-door.

## What the game already has

### 1. The generator (`DungeonBuilder`, dump.cs:34643)

Inputs, collected in `DungeonBuilder.References` (dump.cs:34209):

| Input | Where it comes from | Values |
|---|---|---|
| `gameMode` | mission | `DungeonRaid` 100, `CrystalHunt` 200, `SoulHarvest` 300, `Challenge` 400, `Sandbox` 500 |
| `realm` | mission | `Underworld` 0, `Sandstorm` 1, `Vilehalls` 2, `LavaForge` 3 (+ `FrostBound` 4, `Stormgrave` 5 in the enum, with no layout lists — unreleased) |
| `difficulty` | mission | `Easy` 100, `Medium` 200, `Hard` 300, `Nightmare` 400 |
| `hazards` | mission | `GameManager.HazardModifier` list: `CreatureSwarms`, `Damage150Pct`, `Damage200Pct`, `Death`, `NoPotions`, `NoRevives`, `BossBattle`, `MiniBossBattle`, `DragonBattle`, weapon-only modes … |
| `missionLength` | mission | `Short` 100, `Medium` 200, `Long` 300 |
| `RandomSeed` | mission | `int`; seeds an `OtherRandom` (System.Random wrapper) — fully reproducible |

`InitBuilder` resolves those to a `DungeonLayoutDef` (dump.cs:34950; one per game mode, plus
hazard-filtered variants via `useOnlyForHazards`/`hazardFilters`), then:

- `GetGenSettings(difficulty)` → one of `easy` / `medium` / `hard` `GenSettings` (dump.cs:34912):

  ```
  maxDepth, maxSecretRooms, maxTrapRooms, maxPuzzleRooms,
  chanceForLockedRooms, distanceBetweenKeyAndLock (Closest/Farthest),
  chanceForHallwayBeforeDeadEnd, chanceForSecretRooms,
  chanceForTrapRoomsInSidePath, chanceForPuzzleRoomsInSidePath,
  chanceForTrapRoomsInMainPath, chanceForPuzzleRoomsInMainPath, chanceForArenaRoomsInMainPath,
  baseXP, xpPerRoom, baseGold, goldPerRoom
  ```

- `GetMainPath(missionLength)` → `shortMainPath` / `mediumMainPath` / `longMainPath`, a
  `List<RoomSet.RoomType>` — literally the spine of the dungeon as a list of room kinds:
  `StartRoom, DeadEnd, Hallway, ConnectorHallway, Room, Mega, Arena, Secret, KeyRoom, Trap,
  Puzzle, EndRoom, SoulHarvestHub, CrystalHuntStart/Pyramid/FallbackRoom, SpecialRoom`.
- `GetRealmDef(realm, seed)` → picks one `DungeonRealmDef` from the per-realm list
  (`underworldRealms`, `sandstormRealms`, `vilehallsRealms`, `lavaForgeRealms`). A realm def is
  a weighted list of `RoomSet`s (`roomSetAllocations` + `commonRoomSets`), and a `RoomSet` is
  the per-kind prefab lists (`startRooms`, `hallways`, `rooms`, `megaRooms`, `arenaRooms`,
  `secretRooms`, `trapRooms`, `puzzleRooms`, `endRooms`, `deadEnds`, `connectorHallways`, …),
  each `RoomDef` = `{prefabName, roomBounds, doors[], features}`.
- Layout-level knobs: `enableConnectorRooms`, `boundsAdjust`,
  `secondaryRoomBoundsAdjustMultiplier`, `maxRetries`.

The algorithm, from the `BuildDungeon` coroutine's strings and helpers (dump.cs:34796–34860):

1. Walk `MainPath`. For each entry `FindNextRoom` asks the realm def for a random room of that
   type whose door type can mate with the open door (`DungeonDoorType`: `SquareDoor`,
   `RoundDoor`, `CaveDoor`, `SecretDoor`, `PyramidDoor`), `ConnectRooms` computes the placement
   matrix door-to-door, `OverlapsOtherRooms` rejects collisions against every placed
   `roomBounds`. On failure it backtracks (`BackupOneRoom`, `MainPathFailureRooms`, up to
   `maxRetries`) — log line `"Main path FAILED at path index {0} of {1}"` when it gives up.
2. `"Main path finished."` → `"Adding minor paths, OpenDoor Count = {0}"`: every unused door
   is a growth point; `AddSecondaryRoom` extends it with `GetSecondaryRoomType` (hallway /
   room / trap / puzzle / dead end by the chance fields) up to `maxDepth`.
3. `"Adding secret rooms"`: `AddSecretRoom` off remaining doors up to `maxSecretRooms`.
4. Unused doors go to `WalledOffDoors`; `ConnectDoors` pairs up `DungeonRoomDoor`s that ended up
   coincident (this is where incidental loops come from) and sets each door's `DoorState`
   (`Wall`, `Door`, `DoorOneWay`, `DoorLocked`).
5. `LayoutGameModeFeatures` stamps `DungeonFeature`s onto rooms: `DungeonRaidExit` on the
   farthest room (`FindRoomWithGreatestDepth`), `LockPedestal`/`SkeletonKeyPedestal`/`KeySpawn`
   via `LayoutLockedRoom` and `distanceBetweenKeyAndLock`, `MiniMapLocation`, `DeathHazard`,
   `BossFightFeature`, Soul Harvest / Crystal Hunt variants.
6. `"Generated {0} Rooms | Checksum = {1}"`.

The builder has debugging built in that we can switch on from a mod (public fields on the
instance): `enableLogging`, `enablePathDebugging`, `showPathDepth`, `singleStepRooms`,
`repeating`, `numDungeonsForMetrics`, `stopOnRoom`, `stopOnRoomWithOverlap`. And
`GenerateLayout(dungeon, enableLogging)` runs the whole thing in **layout-only mode** with no
instantiation — the lobby uses it to draw the hologram map. That means we can pre-validate a
floor's seed headlessly in the lobby before launching it.

### 2. The room prefabs

The bundle is LZ4-chunked, so a raw scan only recovers fragments, but combined with the recon
dumps (`NetworkObjectPool.UnpooledPrefabs` after a run) the families are:

| Family | Realm | Seen | Notes |
|---|---|---|---|
| `Cave_*` | Underworld | Room_02/07/11, Hallway_01/02/04/12, DeadEnd_01/02/05, Mega | cave doors |
| `Buried_*` | Sandstorm | Room_04..17, Hallway_02..12, DeadEnd_05..10, Connector_04..09, Trap_03..09, Mega_03 | the biggest set; has connectors and trap rooms |
| `Sewer_*` | Vilehalls | Room, Hallway_03/06, DeadEnd_01/06, Mega, Connector | |
| `Pyramid_Hub_01/02`, `Pyramid_Hallway` | Crystal Hunt | | `PyramidDoor` |
| Lava Forge | LavaForge | only `Forge_Start` recovered | naming unknown; needs the AssetRipper pass |

Two things worth knowing about the art: the mesh names (`Dwarf_Stairs_*`, `Castle_Tower_*`,
`Minetrack_Bridge`, `Ice_End`) and the string `PolygonDungeonRealms_Global` in the bundle say
the rooms are assembled from Synty's *POLYGON Dungeon Realms* kit, which is licensable. So new
rooms in the same style are possible without touching game assets — but "a room the generator
accepts" is a `Room` component with `DungeonRoomDoor`s, `DungeonRoomModule` bounds, spawn
points, `EncounterSet`s, a baked `NavMeshSurface`, Bakery lightmaps and `HiddenPolyTool`
occlusion data. That is a content pipeline, and it is why direction 1 is a bad fit.

Rooms carry vertical size (`DungeonRoomModule.numChunks` is a `Vector3Int`, and the occlusion
presets include `HighVerticalRoom` / `PitVerticalRoom`), but door placement uses one
`doorOffsetY`; nothing suggests a room whose exit door is a storey below its entry.

### 3. Launching a dungeon — the exact chain

```
lobby:   DungeonScanner (mission list from a PlayFab server-time seed; regenerates per
         time block → UIDungeonUpdateTimer. Custom seeds bypass this entirely.)
         AddCustomMission(realm, gameMode, seed, difficulty, hazardLevel, ...)   dump.cs:36642
           → FinishCustomMission: GenerateDungeonLayout (layout-only) + map model
             + SyncAllPlayers (peers get the mission)
         players on TeleporterPads → countdown → RPC_SetLaunchState
           → GameManager.LoadDungeon(callback, Dungeon, dissolveTime)          RVA 0x511E10
             → TeleportToDungeonLevel: LockRoom, dissolve, SetRespawnAvatarOnLevelLoad,
               [master] NetManager.SetRandomRoomSeed(seed, questType, questName)
                        → room props dungeon_seed / quest_type / quest_name,
               PrepareForSceneChange, [master] PhotonNetwork.LoadLevel("dungeon_builder_v1")

dungeon: GameManager.OnSceneLoaded → InitializeLevel
           [master] DungeonBuilder.GenerateDungeon(CurrentDungeon)
             rooms: DungeonRoom.InstantiateRoom → PhotonNetwork.InstantiateRoomObject(prefab,
                    pos, rot, data)  — replicated to every client and to late joiners
             clients: DungeonRoomModule.OnPhotonInstantiate → Room.SetRoomType(type,
                    featureSeed, features)  — the room enables its own features/encounters
           OnRandomDungeonGenerated → SceneOcclusion.InitializeForRandomDungeon,
             InitRandomDungeonDoorsForRemotes, InitHazardsForLevel,
             InitRandomDungeonNetworking (view-ID hash cache) → SyncDungeonData (RaiseEvent)
             → EV_SyncDungeonData on peers ("SYNCING DUNGEON DATA | CurrentDungeon = Valid")

exit:    Room.EnableFeature(DungeonRaidExit) spawns "DungeonRaid_Exit_01": a Teleporter
         (NetworkObject) with pads. Everyone on pads → RPC_MissionLaunch → missionLaunched
         UnityEvent (→ MissionSuccess: LockRoom + SetMissionEndState(Success))
           → ReturnToOutpost coroutine → GameManager.ReturnToLobby        RVA 0x51BB00
             → ReturnToLobbyInternal: AI ownership hand-off, UIEndMission.SetCurrentDungeon,
               ClearLastDungeon, ResetHazards, SetRewardStats, LoadLevel("lobby")
```

Evidence that clients do not generate: `InitializeLevel` tests `IsMasterClient` immediately
before `GenerateDungeon`; the only caller of `Room.SetRoomType` is
`DungeonRoomModule.OnPhotonInstantiate`; `Player.log` shows the master's
`"SYNC'ing dungeon PhotonView data"` and the receiver's `"EV_SYNC_DUNGEON_DATA"`. The game also
already has **saved dungeons**: `PlayerData.SavedDungeonDTO {r, gm, s, d, n}` (realm, game
mode, seed, date, name-word ids) — a run is five ints and a string.

### 4. Difficulty levers, all independent of the layout

| Lever | Mechanism | Range |
|---|---|---|
| `Difficulty` | chooses `GenSettings` (more trap/puzzle/locked rooms) and `EncounterSet.ShouldOverrideOtherSets` chances (`easy/medium/hardChanceForOverride`) | Easy → Nightmare |
| Difficulty tier | `DungeonScanner.ScannerDifficulty` Tier1–7 → `GameManager.DifficultyTier` (`TierOverride` 0–6, log `"Using DifficultyTier: 3"`) → `CombatRunner.EnemyTier` → `AIRealm.GetAIPools(class, tier, room)` picks stronger pools; `CalculateLootTierForLocalPlayer(includeDifficultyBonus)` raises chest loot | 0–6 (`MaxEnemyTier = 6`) |
| Hazards | `HazardLevel` None..Level3 + explicit `HazardModifier` list, applied by `InitHazardsForLevel`; hazard layouts via `DungeonLayoutDef.hazardFilters`; `CombatRunner.swarm/miniBoss/bossEncounterSets` | swarms, +50/+100 % damage, boss/mini-boss/dragon battles, no potions/revives |
| Length | `MissionLength` → main-path list | Short / Medium / Long |
| Player count | every `Encounter.CombatWave` has `_1Player.._4Player` spawn counts | automatic |

A gentle per-floor ramp is a table over floor index, not new code.

### 5. Multiplayer, as vanilla already does it

Private rooms are `IsVisible == false` (GAME-INTERNALS.md); `IsOpen` flips false mid-run but
`JoinInProgressGames` exists and late joiners are rebuilt from Photon room objects plus
`EV_SyncDungeonData`. Master switch is handled (`DungeonScanner.OnMasterClientSwitched`,
`CanTransferHost`). Room `CustomProperties` (`dungeon_seed`, `quest_type`, `in_progress`,
`curScn`, `lvl_tier`) survive a master switch, which is where the mod's run state should live
too.

Event-code map for a fourth mod: CustomAvatars 140–149, LootOverhaul 150–159, VisualCues
160–169 → **Descent takes 170–179.**

## The three directions

### 1. Daggerfall — a content project, not a mod

Daggerfall dungeons are grids of *RDB blocks* (`BLOCKS.BSA`): pre-modelled 3D chunks with
internal corridors, rooms and multiple storeys, connected at fixed edge points, chosen per
dungeon from a fixed block table plus a random pool. Daggerfall Unity reads them and rebuilds
the meshes from the original textures — so its output is Daggerfall's art, which we cannot
ship into DoE, and its block structure (vertical, labyrinthine, 30–60 m chunks) has no analogue
in the game's room kit. Reproducing the *shape* would need us to model every block in the
Synty kit and give each one the full `Room` treatment listed above (doors, bounds, spawn
points, encounters, navmesh bake, lightmaps, occlusion). Nothing in the generator is the hard
part there; the assets are. Worth keeping only as inspiration for graph shapes (dead-end
mazes, key hunts — which `LayoutLockedRoom` already does).

### 2. Diablo — a spec for direction 3

Diablo 1's level generator (DRLG) is a family of 2D tile-grid algorithms, one per tileset:
Cathedral (recursive room subdivision + corridors), Catacombs (random rooms joined by halls),
Caves (cellular growth), Hell (mirrored halls), each with quest set-pieces dropped in, a
staircase down, a town portal back, 16 levels in 4 tilesets. Diablo 2 is the same idea with
preset tiles and a "maze" of room-and-corridor tiles. DoE's generator is squarely in that
family: a spine of rooms, side branches, special rooms placed by rules, four realms ≈ four
tilesets. What Diablo has and DoE lacks is precisely the descent loop — stairs down to a
harder level, portal to town, levels that persist for the game — which is the feature being
asked for. So "recreate Diablo" is the design brief for direction 3. What we cannot match:
Diablo's open, loop-rich layouts (the generator builds trees on a plane) and its per-tileset
tile density (rooms here are whole prefabs, so variety comes from the room count in each set).

### 3. Bend the existing generator — recommended

Everything needed is a hook or a field write. The design that follows is direction 3.

## Proposed design: the Descent mod

**A run** is `{runId, floors[], floorIndex, clearedRooms{}}` where each floor is
`{seed, realm, gameMode=DungeonRaid, difficulty, tier, hazards[], length}`. It lives in a local
JSON like LootOverhaul's inventory, and — while a room is live — in a room custom property
(`dd_run`) so any master can continue it. Floors are generated lazily from `seed0` so a run is
reproducible from one number plus the ramp table.

**Start / resume.** A mod console in the lobby (LootOverhaul's `UiKit` already builds in-world
panels) listing "New descent" and "Resume: floor N". Launch = `AddCustomMission(realm,
DungeonRaid, floorSeed, difficulty, hazardLevel)` then the vanilla pads, or directly
`GameManager.LoadDungeon(callback, dungeon, dissolveTime)` with a `DungeonScanner.Dungeon` we
construct (verify item 2). Before launch, run `DungeonBuilder.GenerateLayout` in layout-only
mode on the candidate seed; if the builder reports `Failed`, step the seed — so a floor never
fails to build in front of players.

**Descend.** Harmony prefix on `GameManager.ReturnToLobby` (RVA 0x51BB00, unique): if a
descent run is active and the trigger was the exit teleporter (not death, forfeit or a
`TeleportTrigger`), the master advances `floorIndex`, writes the next floor to room props, and
calls the same `TeleportToDungeonLevel` path with `dungeon_builder_v1` as the target instead of
`lobby`. `GameManager.MissionSuccess` (0x512840) is suppressed on intermediate floors so the
end-mission screen and XP tally fire once, on surfacing (no PlayFab writes by us; the game's own
reward path runs exactly once, as in vanilla). A minimal first version can even skip the
teleporter hook and let the exit read "Descend" by re-labelling the pad text
(`TeleporterPad.detailText`).

**Surface.** A second pad — or a mode toggle on the exit — that calls vanilla `ReturnToLobby`
with the run left intact. Resume relaunches `floors[floorIndex]`. Encounters in a resumed floor
respawn fresh (encounters come from the room prefab and seed); optionally the mod keeps
`clearedRooms` by `roomIndex` and calls `Room.DisableAllEncounters` on those rooms after
`OnRandomDungeonGenerated`, since the same seed reproduces the same room order.

**Ramp.** Floor *n*: `tier = min(6, baseTier + n/2)`, `Difficulty` steps every few floors,
`hazards` drawn from a depth-indexed list (`CreatureSwarms` early, `MiniBossBattle` mid,
`BossBattle`/`Damage150Pct` deep), `MissionLength` Short → Long. **Realm rotates per floor**
(Underworld → Sandstorm → Vilehalls → LavaForge → …) which delivers "all four realms in one
dungeon" for free; each floor also gets that realm's enemies and biome (`BiomeManager` and
`CombatRunner.currentRealmPools` are per scene).

**Longer, snakier, branchier floors.** Because only the master generates, the mod can mutate
the layout assets on the master right before `GenerateDungeon` with no cross-client
determinism burden: raise `maxDepth`, `chanceForHallwayBeforeDeadEnd`, `maxSecretRooms`,
`chanceForSecretRooms`, `chanceForArenaRoomsInMainPath`; and replace `longMainPath` with our
own list (e.g. `Start, Hallway, Room, Hallway, Arena, ConnectorHallway, Room, Mega, Hallway,
Trap, Room, Hallway, End`). Longer spines raise the overlap-failure rate (`maxRetries`,
`boundsAdjust`), which the lobby-side layout validation absorbs.

**Mixing realms inside one floor** is possible at the room-set level — build a
`DungeonRealmDef` at runtime whose `roomSetAllocations` draw from several realms — but only
where door types mate (`CaveDoor` rooms will not join `SquareDoor` rooms), and enemies/biome
stay one realm per scene. Treat it as a later experiment after the AssetRipper pass shows the
door types per set; per-floor rotation is the version to ship.

## Verify first (one recon session, before any code)

1. **Scene → same scene.** Does `PhotonNetwork.LoadLevel("dungeon_builder_v1")` from inside
   `dungeon_builder_v1` reinitialise cleanly? Watch `LevelInitialized` /
   `LastLevelInitialized` (`"SCENE Initialized twice"`), `PrepareForSceneChange`,
   `NetworkObjectPool.ReleaseRoomPrefabs`, `FlushCachedDungeonData`, and — per LootOverhaul's
   `SceneExit` lesson — that floor N's Photon room objects are gone from the event cache before
   floor N+1 instantiates (a late joiner would otherwise receive both). Solo first, then two
   clients, then a late joiner.
2. **Launch without the scanner UI.** Whether `GameManager.LoadDungeon` accepts a
   `DungeonScanner.Dungeon` we build ourselves, or whether it needs to go through
   `AddCustomMission` (which assigns the name via `NameData.Generate`, XP/gold bonus, and sets
   `isValid` from the layout pass).
3. **Asset inventory.** One AssetRipper pass over `data.unity3d` for the ScriptableObjects:
   every `DungeonLayoutDef` (actual `GenSettings` numbers and main-path lists),
   `DungeonRealmDef` and `RoomSet` (room counts and door types per realm). This turns the
   partial table above into facts and tells us how much room variety each floor has.
4. **Generator failure rate vs. spine length.** Loop `GenerateLayout` in the lobby over 200
   seeds for each candidate main-path list with `enableLogging` on; count `Failed`.
5. **Memory across floors.** `UnpooledPrefabs` grew 5 → 22 in one run; confirm
   `ReleaseRoomPrefabs` + `UnloadUnusedAssets` actually free room prefabs between floors over a
   ten-floor run (PC only, so this is about leaks, not headroom).
6. **Hook points are unique RVAs** (not the 0x35FC20 shared stub): `ReturnToLobby` 0x51BB00,
   `MissionSuccess` 0x512840, `LoadDungeon` 0x511E10, `Teleporter.EV_LaunchMission` 0x48E150,
   `DungeonSeedConsole.LaunchDungeon` 0x46FD30, `DungeonLayoutDef.GetMainPath` 0x451EA0,
   `GetGenSettings` 0x451E70. Coroutine `MoveNext`/`Dispose`/ctors must not be patched
   (dump.cs count check first, as always).

## Phases

| Phase | Deliverable | Depends on |
|---|---|---|
| 0 | Recon session: items 1–6 above; a vanilla run logged with `DungeonBuilder.enableLogging = true` | — |
| 1 | Descent MVP, solo: seed chain, exit → next floor, surface, resume console | 0 |
| 2 | Multiplayer: run state in room props + event 170 block, late join, master switch | 1 |
| 3 | Ramp table, realm rotation, longer/branchier floors, lobby-side seed validation | 1 |
| 4 | Polish: cleared-room memory, loot-tier bonus per depth (LootOverhaul hook on `CalculateLootTierForPlayer`), waystone/portal dressing, minimap | 2, 3 |

Phase 1 is the size of VisualCues 0.1.0; the whole thing is smaller than CustomAvatars' FBT
work, because every risky subsystem (generation, replication, encounters, late join) stays
vanilla.

## Recon tooling added during this investigation

`tools/disasm.py` (existing) annotates one function. To answer "who calls X" the investigation
used a small scan of `GameAssembly.dll` for `E8 rel32` calls whose target is a named method in
`dump/script.json`, mapping each call site back to its containing method. It now lives at
`tools/xref.py`; it answered in seconds questions that the dump alone cannot (which coroutine
calls `PhotonNetwork.LoadLevel`, who calls `MissionSuccess`, that `Room.SetRoomType` has
exactly one caller). Caveat: coroutine `Dispose`/ctor entries resolve to the shared stub and
produce noise — filter them out.

## Decisions from the 2026-09-07 conversation

- Direction 3. Finite run: 16 floors, four per realm in seed-shuffled bands, boss battle on
  the last floor. No permadeath; a wipe or a quit keeps the run at the floor it reached.
  Anyone in the party can start or resume; cancelable countdown; the host's client loads.
- Entrance: a board in the spawn room (`Lobby_Player_03_NEW`) for now; later a doorway with a
  staircase and a trigger volume. Floor 1 is tier 1 and ramps from there.
- Resume = top of the floor you stopped on, same layout, mobs and chests respawned.
  "Restart" keeps the seed and resets the floor counter.
- **Rewards are real.** The user explicitly relaxed the no-PlayFab-writes rule for this mod:
  a descent floor is a vanilla-generated dungeon and should pay like one, through the game's
  own reward code. Future flavour: quest runs (retrieve an artifact/battery, kill a named boss
  on a target floor).

### Where the game commits XP and gold (traced 2026-09-07)

```
dungeon, in ReturnToLobbyInternal (before LoadLevel("lobby")):
  GameManager.SetRewardStats → CalculatePartyStats      per-player PlayerStatDef from
      CurrentDungeon.XPBonus/GoldBonus, CalculateAITierForPlayers, SumHazardBonuses
  UIEndMission.SetCurrentDungeon(dungeon, tier)

lobby, when the stat holograms come up:
  UIEndMission.ActivateAvatarHolograms → GameManager.SaveLoot (public, RVA 0x51BF20):
      GetEarnedLoot, PlayerProfile.IncrementCharacterData (XP, gold), level-up via
      PlayerXPTable, IncrementData (stats, by MissionEndState), CheckAchievements,
      PlayerProfile.SavePlayerProfile (whole encrypted save uploaded to PlayFab),
      SubmitLeaderboards / SubmitIncrementalLeaderboards
```

So nothing is banked until the party is back in the hub. Per-floor banking on descent means
calling `SetRewardStats` then `SaveLoot` ourselves before loading the next floor (both are
the game's own functions; `SaveLoot` is public, `SetRewardStats` private but reachable through
the interop wrappers). Verify: that `SaveLoot` runs correctly outside the lobby (it is
normally guarded by `UIEndMission.ShouldSaveLoot`), that per-player stats reset between floors
(`FlushCachedDungeonData` / `ClearLastDungeon`), and that the final surfacing does not
double-count the last floor. Interaction with LootOverhaul's own loot save path
(`GetEarnedLoot`) needs a look once both mods are on.

## Build 0.1.0 (2026-09-07, untested)

`src/Descent/` implements the design above in one pass, ahead of the recon session, because
each test is a headset session. What it commits to, and where each decision lives:

| Piece | File | Mechanism |
|---|---|---|
| Run model, ramp, realm bands | `Run/RunRecord.cs` | seed → per-floor `FloorSpec` (seed hash, realm band, tier, difficulty, length, hazard level, boss) |
| Persistence | `Run/RunStore.cs` | `UserData/Descent/runs.json`, written on every change |
| Mission object | `Dungeon/FloorPlan.cs` | `new DungeonScanner.Dungeon(...)` (hazards rolled by the game's `GenerateDeterministicHazards`), name from `GameManager.nameGenerator`; lobby validation via `DungeonBuilder.GenerateLayout` |
| Floor length | `Dungeon/InitBuilderHook.cs` | Harmony prefix on `DungeonBuilder.InitBuilder` rewriting `_missionLength` (its `GetMainPath` call is inlined, so the layout asset cannot be patched by method) |
| Launch | `Dungeon/Launcher.cs` | `GameManager.DifficultyTier`, `GameManager.CurrentDungeon`, then `GameManager.LoadDungeon(callback, dungeon, dissolve)`; manual copy of `TeleportToDungeonLevel` as fallback |
| Descend | `Dungeon/Descender.cs` | prefix on `GameManager.ReturnToLobby`: Success and not last floor → bank, advance, `descend` event, launch; Failure/Forfeit → vanilla |
| Banking | `Dungeon/Rewards.cs` | `SetRewardStats` → `SaveLoot` → `InitLocalPlayerShareableStats` → `ClearPlayerStatsLUT`, on every client for itself |
| Party state | `Dungeon/RunSync.cs` | room property `dd.run` (host writes before every load) + event 171 ops `state/arm/cancel/launch/descend` |
| Watching | `Dungeon/FloorWatch.cs` | logs the generated floor; resets a stale `missionEndState` / `IsEndMissionMode` on a descended-into floor; relabels the exit pads |
| Hub | `Hub/Board.cs`, `Hub/UiKit.cs` | panel beside the kobold; NEW / RESUME / RESTART / CANCEL |

Two defensive resets are in the code because vanilla's own are inlined and could not be read
from the disassembly: `GameManager.LevelInitialized`/`LastLevelInitialized` before a
same-scene reload, and `missionEndState`/`IsEndMissionMode` on arriving at a descended-into
floor (readers: `CombatRunner.DeactivateEncounter`, `RespawnTrigger.Activate`,
`AvatarPlayer.ReviveLocalDelayed`, `HazardHandler_RespawnPlayersAfterEncounters`). The
transcript says whether either fired.

The session checklist is in `src/Descent/README.md`.
