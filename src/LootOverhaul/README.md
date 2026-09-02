# LootOverhaul — building

A second, independent MelonLoader mod for Dungeons of Eternity. Design and game-internals
findings: [docs/LOOT-OVERHAUL.md](../../docs/LOOT-OVERHAUL.md).

## Prerequisites

Same as CustomAvatars (see [../README.md](../README.md)): MelonLoader 0.7.3 installed into the
game folder, the game launched once so `MelonLoader/Il2CppAssemblies/` exists, and a .NET SDK
that can target `net6.0`. `GameDir` resolves through `../Directory.Build.props`.

## Build

```
cd src/LootOverhaul
dotnet build
```

The build copies `LootOverhaul.dll` into `<GameDir>\Mods\`. Pass `-p:NoDeploy=true` to skip.

## Independence from CustomAvatars

The two mods share no assembly. `Gate/` and `Net/` are copies of the CustomAvatars gate and
transport, re-identified so the mods never confuse each other and either installs alone:

| | CustomAvatars | LootOverhaul |
|---|---|---|
| Photon player properties | `ca.ver` / `ca.sha` / `ca.caps` | `lo.ver` / `lo.sha` / `lo.caps` |
| Event codes | 140–149 | 150–159 (`150` handshake, `151` loot with sub-opcodes) |
| MelonPreferences sections | `[CustomAvatars*]` | `[LootOverhaul]`, `[LootOverhaul_Dev]` |
| UserData folder | `UserData/CustomAvatars/` | `UserData/LootOverhaul/` |
| Harmony | prefix on `LoadBalancingClient.OnEvent` | its own prefix on the same method |

Both prefixes coexist: Harmony chains them and each mod ignores the other's code block. The
gate rule is the same: private room, every occupant on the identical version and DLL hash of
*this* mod, own self-checksum OK. A friend running CustomAvatars but not LootOverhaul keeps
LootOverhaul inert for the whole room, by design.

## Layout

```
LootOverhaul/
├── Core.cs           MelonMod entry: config, self-hash, hook, roster, gate, handshake
├── ModConfig.cs      [LootOverhaul] / [LootOverhaul_Dev] settings
├── ModPaths.cs       UserData/LootOverhaul/, per-account inventory path
├── SelfCheck.cs      DLL SHA-256 for the roster
├── Gate/             ModCaps, ModPeer, ModRoster, ModGate, ModHandshake
├── Net/              PhotonHook (the one Harmony patch), ModNet (send/receive, 150–159)
└── Loot/             LootItem, LootInventory — the JSON model, no game types
```

Still to build, in the order the design doc estimates: drop roll on `AI.OnKilled` (master
only) + loot tag + bag-on-pickup; the bag panel; the lobby booth; equip-from-bag with respawn
re-apply; the shop. Run the "verify first" list in the design doc before the first of these.
