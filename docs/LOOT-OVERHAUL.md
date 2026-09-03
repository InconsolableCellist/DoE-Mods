# LootOverhaul — feasibility investigation

Date: 2026-09-01. Source: `dump/dump.cs` (game build 1.2.3849), the CustomAvatars source, and
the recon findings in [GAME-INTERNALS.md](GAME-INTERNALS.md). Nothing here has been verified at
runtime yet; the "verify first" list at the end is the recon session this needs before code.

## Verdict

**Feasible, and much cheaper than it looks, because the game already ships a Borderlands-style
procedural weapon generator.** The mod's job is orchestration, not content: decide *when* a
weapon drops, keep a mod-side inventory, draw a bag and a shop, and re-spawn weapons through the
game's own networked instantiation path. The two genuinely hard items are the overworld town
(4, future) and stat-bearing armor (6), and both have a workable reduced form.

The one hard wall is unchanged from PLAN.md ground rule 2: **every vanilla notion of "owning" a
weapon or coin lives in PlayFab**, and we don't write there. So LootOverhaul's loot, inventory,
currency and shops form a closed economy that lives next to the vanilla one, in a local JSON,
gated to modded private rooms. That is a design constraint, not a blocker, but it shapes
requirement 4 (what selling buys you) more than anything else below.

## What the game already has

### 1. A complete random-weapon generator (`WeaponFactory`, dump.cs:80780)

A generated weapon is fully described by six values — this is the `PlayerData.WeaponModuleDTO`
(dump.cs:64137) the game itself saves:

| Field | Space |
|---|---|
| `prefabName` | `Axe_Gen1`, `Crossbow_Gen1`, `Longsword_Gen1`, … (11 types: Sword, Axe, Bow, Crossbow, Dagger, Staff, Hammer, Shield, LongSword, Spear + mythics) |
| `weaponClass` | Common / Unique / Rare / Legendary / Mythic (`WeaponFactory.WeaponClass`) |
| `weaponTier` | Tier1–Tier7 |
| `weaponStyle` | Style0–Style9 (mesh variant via `WeaponBuilder`/`WeaponPart`) |
| `randomSeed` | int — drives perks, elemental, superior flag, damage roll, name |
| `genV` | generator version v1–v5 |

Derived from the seed, deterministically on every client: two perks from a pool of **50**
(`WeaponFactory.WeaponPerk`), an elemental (Fire/Ice/Poison), `isSuperior`, a damage roll from
`WeaponDamage` (per type × tier × class), a cost and a **salvage value** from `WeaponCost`, a
material from `TierMaterial`/`StaffMaterial` tables, and a generated name from `WeaponNameData`
word lists. Rarity colours are in `WeaponModule.rarityColors`. Seasonal drops exist
(`SeasonalKey` Christmas/Spring/Halloween).

Useful public entry points:

- `WeaponFactory.GenerateRandomWeaponModuleForLocalPlayer(class, type=-1, tier=-1, style=-1, seed=-1, seasonal=0)` → `WeaponModule`. The name says "for local player" — verify it only reads the local level and does not touch the profile; the safe alternative is to build a `WeaponModuleDTO` by hand and call `new WeaponModule(dto)`.
- `WeaponFactory.GetRandomWeaponClass(playerLevel, realm, …)` — the vanilla realm-weighted rarity roll (`RealmDrop` tables per Underworld / Sandstorm / Vilehalls / LavaForge).
- `WeaponFactory.GetRandomWeaponStats(type, class, tier, style, seed)` → `WeaponDef` (damage, perks, cost, salvageValue) without spawning anything.
- `WeaponFactory.GenerateLootWeapon(WeaponModule)` → `(Mesh, Material)` — the mesh the chest hologram uses (`ChestLoot.ShowWeapon`). This is how a bag UI or shop shelf renders an item without instantiating a networked prop.
- `WeaponCost.GetSalvageValue(type, tier, class, seed, perkA, perkB, elemental)` — a ready-made **sell price**. `WeaponFactory.Instance.weaponCostTable` holds the asset.
- `WeaponModule.GetSaveString()` / `new WeaponModule(WeaponModuleDTO)` — serialisation for our JSON.
- `WeaponModule.GetWeaponPrefabData(module, out object[] data)` — prefab name + the **5-value** instantiation packet (`randomWeaponNetworkedDataCount = 5`; mythic 8, manual 11).

### 2. Networked spawning is one call

`NetworkObjectPool.Instantiate<T>(string prefabName, Vector3, Quaternion, object[] data)`
(dump.cs:61093) is the game's public wrapper over `PhotonNetwork.Instantiate`. Feed it the
prefab name and data from `GetWeaponPrefabData` and every client rebuilds the same weapon from
the seed in `Weapon.OnPhotonInstantiate` → `InitRandomProp`. **No custom prefabs, no
AssetBundle, no `dfm:` prefix work is needed for requirements 1–5.** The result is a real
`Weapon : Prop` with vanilla grab, holster, outline colour, dissolve and ownership transfer.

### 3. The vanilla loot flow, and where to hook it

- **Enemy death:** `AI.OnKilled(int killerActorNr, int damageType)` is a `[PunRPC]` on
  `AI : Agent` (dump.cs:93316); each AI has `pctChanceToDropLoot`. Death loot goes
  `CombatRunner.SpawnLoot(pct, pos, rot, enemy)` → `LootSpawner` (dump.cs:43573), whose pools
  are gold / health / mana / key / bone / bomb / powerup. **Enemies never drop weapons in
  vanilla** — the mod's drop roll is additive, not a replacement.
- **Chests:** `Chest : Operable` (dump.cs:39307) with `AvailLootTypes` Coins / PowerGem /
  Consumable / CosmeticModule / WeaponModule. A chest "weapon" is a **blueprint**: it is added to
  the PlayFab uncrafted list (`PlayerProfile.AddUncraftedWeapon`) and must be fabricated for
  coins at a `Fabricator`. Hook: `Chest.OnLootCollected()` (protected virtual — patchable;
  the `CollectLoot` coroutine itself is not a good target).
- **Room/dungeon spawn:** `LootSpawn` (dump.cs:40221) places chests/barrels/keys when the host
  connects; `Room` (dump.cs:67840) and `GameManager.CurrentRealm` tell us where we are.
- **Fairness is free:** because the generator *is* the damage table, a mod weapon built from
  a seed is by construction a vanilla-legal weapon. Only the `Manual` serialized type
  (`ManualWeaponDTO`: explicit damageMin, damageType, superior, three perks) can exceed vanilla;
  that is the "sandbox night" lever, and the game already networks it (11 values).

### 4. Vendors and the hub

- `Fabricator : NetworkObject, IPointable` (dump.cs:52738) is the crafting/vendor station:
  hologram pedestal, gear/consumables/vendor/perk-info menus, `EV_Fabricate`, `EV_EquipLeft/
  Right/Back`, `EV_StoreInArmory`, `EV_TrashModule`, salvage text. `WeaponVendor : Fabricator`
  (dump.cs:57083) is the goblin's shop: per-tier `TierGen` rarity ranges and cost multipliers,
  PlayFab server-time seeded stock that rotates every `refreshMinutes`, and a held-weapon
  comparison panel (`vsBetter/vsSame/vsWorse`).
- `Vendor : Idler` (dump.cs:30152) is the animated NPC: `Location[]` waypoints, idles, IK hand
  targets, welcome VO. `Vendor_Goblin` (weapons), `Vendor_Merchant`, `CosmeticsVendor`.
- Scenes: `GameManager.LOBBY_SCENE = "lobby"`, `MEGALOBBY_SCENE = "megalobby"`,
  `DUNGEON_SCENE = "dungeon_builder_v1"`, `SANDBOX_SCENE = "sandbox"`, plus
  `GameManager.IsLobbyScene` / `IsMegaLobbyScene` and `PreSceneLoadCallback`. Lobby sub-areas
  appear as string literals `lobby_vendor`, `lobby_tower`, `lobby_crypt`, `lobby_combat`,
  `pyramid_hub`.

### 5. The player-side inventory that already exists

`Holster` (dump.cs:9296) is richer than "a place weapons hang": one holster is flagged
`isInventoryHolster` — the consumables ring (`VR Controller/Holsters/HolsterInventory` in the
recon hierarchy) with `ExpandInventory`, per-type capacity, count labels, a fallback scroll
mode, and static `Holster.AddToInventory(prefabName, count)` / `GetInventoryItem(slot, out
count)`. `VRControllerProps.AddPropToInventory(Prop)` and `ShowInventoryFullNotif()` exist.
Weapon slots are `SaveSlot` WeaponL / WeaponR / WeaponB (back) / WeaponS (shield), loaded by
`Holster.InitHolsterContents(bool isLobby)` from the PlayFab loadout and refilled on respawn.
`AvatarPlayer.AssignWeapon(LoadoutValues, WeaponModule, List<Prop>)` is public.

### 6. VR UI primitives we can borrow

- `IPointable` (dump.cs:64835): `CanPoint`, `MaxTraceRange`, `Colliders`, `OnHover`,
  `OnIndexTriggerDown/Up`. The local hands keep a registry: **`VRControllerHands.AddPointable(
  IPointable)` / `RemovePointable` are public**, so a custom panel gets the game's laser
  pointer and trigger for free.
- `InteractableButton : MonoBehaviour, IPointable` (dump.cs:53418): `onPressed`, `onHover`,
  hold-to-press, `SetLabel`, `ShowButton`, tooltip hooks, sounds. Cloning a vanilla button
  (`Fabricator.fabricateButton` is a public reference) avoids implementing an Il2Cpp interface
  from managed code — CustomAvatars has not needed class injection so far and this keeps it
  that way.
- `FXNotifications.AddNotification(text)`, `AddQuickNotification`, `ShowTooltip(text, secs)`,
  `ShowArrow(target, …)` — toasts for "picked up Rare Longsword", "bag full".
- `WeaponFactory.GenerateLootWeapon` + a plain `MeshFilter`/`MeshRenderer` for item previews;
  the hologram materials (`commonHologramMaterial` … `legendaryHologramMaterial`) are public
  fields on `WeaponFactory`.

### 7. Economy boundary — the wall

`PlayerProfile` (dump.cs:62977) is the only writer to PlayFab and it is exactly the surface we
must not call: `AddUncraftedWeapon`, `AddUnlockedWeapon`, `UnlockWeapon`, `SetLoadoutData`,
`IncrementData(Coins)`, `SetData`. Read-only use is fine: `GetUnlockedWeapons()`,
`GetUncraftedWeapons()`, `GetLevel()`, `GetWeaponModule(guid)`, `FindLoadoutWeapon(weapon)`.

Two non-obvious consequences:

- **Don't spawn vanilla coin piles as loot.** `Coin_Pile_01/05/10` are in the prefab pool, but
  picking one up fires `Chest.LocalPlayerReceivedCoins` → profile coin increment. Mod currency
  needs its own pickup (or no pickup at all: currency only from selling).
- **Mod loot can never enter the vanilla armory or loadout save.** Equipping a bag weapon is a
  session-scoped, holster-level operation (see requirement 5), re-applied on respawn by us.

## Requirement-by-requirement

### 1. Loot drops (weapons, later armor) — *easy*

- Master client only (mirrors vanilla: `LootSpawn` "spawned when the host connects").
  Harmony **postfix on `AI.OnKilled(int, int)`**, guarded by `PhotonNetwork.IsMasterClient` and
  `ModGate.Active`. Roll against a mod drop table (realm, `AI.Family`/elite flag, boss). Build
  a `WeaponModuleDTO` (class from `GetRandomWeaponClass(level, realm)`, random type/tier/style/
  seed) and `NetworkObjectPool.Instantiate<Weapon>(prefab, pos, rot, data)`.
- Postfix on `Chest.OnLootCollected()` for a bonus roll on chests; `Mimic` and `GiftChest` are
  `Chest` subclasses and come along.
- Broadcast a small "this PhotonView is loot" record on the mod's own event code so every
  modded client tags the object (rarity, value, weight) before anyone touches it.
- **"Collect, don't use":** the dropped object is a real weapon, so grabbing it would wield it.
  Cheapest fix: prefix on `Prop.PickUp(PropRoot)` (or postfix on `Prop.OnPickUp`) — if the view
  ID is tagged loot, cancel the pickup, `PhotonNetwork.Destroy` it (owner side), add it to the
  bag, toast the name in its rarity colour. The vanilla outline colour by rarity (`Weapon.
  outlineColor`) and the coin-style force-grab feel come for free. Later, a "loot bag" prop
  with its own mesh is a nice-to-have, not a requirement.

### 2. A new inventory — *easy*

`LootItem { saveString, propType, class, tier, weight, value, realm, foundAt }` serialised as
JSON under `UserData/LootOverhaul/<playfabId>/inventory.json`, written on every change and on
`OnApplicationQuit`. `value` = `WeaponCost.GetSalvageValue(...)`, `weight` = a mod table by
`Prop.Type` × tier (spears and shields heavy, daggers light). The `WeaponModule` round-trips
through `GetSaveString()` / `new WeaponModule(dto)`, so an item is ~60 bytes and re-spawnable.

### 3. Weight cap and bag management — *medium (UI work, not game-internals work)*

- World-space bag panel anchored to the left hand or a wrist "look" gesture, opened by a
  controller chord (never F12: Steam takes it — see memory). Rows: mesh preview via
  `GenerateLootWeapon`, name, rarity colour, value, weight; sort by value / weight / rarity /
  time; "drop" re-instantiates the weapon on the ground (still tagged loot, so it can be
  re-bagged or left for a friend); a weight bar; `ShowInventoryFullNotif`-style toast when a
  pickup would overflow, with the item left on the floor.
- Buttons: clone a vanilla `InteractableButton` and bind `onPressed` through
  Il2CppInterop's `DelegateSupport` (managed `Action` → `UnityAction`). Registering the panel
  with `VRControllerHands.AddPointable` gives it the pointer.
- Multiplayer: the bag is per player and local; peers only ever see the ground object.

### 4. Selling — hub stall now, town later — *stall medium; town hard*

- **v1 "Loot Broker" stall in the lobby:** on `GameManager.IsLobbyScene`, spawn our own
  table/pedestal (AssetBundle, same pipeline as avatars) at a recorded lobby position, with
  buttons and a preview pedestal. Selling converts bag items to mod currency at `salvageValue`
  (× a mod multiplier). The currency is local JSON plus a `lo.gold` player custom property so
  friends can see it.
- **What selling buys you** — the design question the PlayFab wall forces. Options that stay
  inside the closed economy: (a) a mod weapon shop with rotating seeded stock (reuse the
  `WeaponVendor.TierGen` idea; stock is just DTOs) — pays off once requirement 5 lands;
  (b) prestige: a wealth rank on the nameplate, trophies/decor spawned in the hub from your
  hoard; (c) house rules: buy a "mutation night" drop-rate boost for the next run. Recommend
  shipping (b) with the stall so selling has *some* payoff before 5 exists.
- **Overworld town (future):** loading our own scene from an AssetBundle is possible under
  MelonLoader, but `GameManager` owns scene lifecycle (`PreSceneLoadCallback`, `curScn` room
  property, `Prop.DestroyOnSceneChange`, `IsNukingSceneProps`), Photon room properties name the
  current scene, and the lobby is where the game expects everyone to be between runs. The
  realistic shape is an **additive scene layered onto the lobby** with lobby renderers hidden
  and the player teleported into it, entered/exited through a mod doorway; a standalone town
  scene that the game treats as "the lobby" is a much deeper patch of `GameManager`. Park it
  behind 1–5; do not size it yet.

### 5. Usable, randomised weapons — *medium; the generator does the heavy lifting*

- **Equip from bag (vanilla-legal):** instantiate the item via the same networked call and
  holster it into a chosen `SaveSlot` (`Holster.OnHolster` / `ParentToAvatarHolster`). The
  vanilla loadout re-spawns on respawn and lobby return (`Holster.InitHolsterContents(isLobby)`,
  `RefillHolster`, `OnAvatarRespawn`), so a postfix on `InitHolsterContents` re-applies the mod
  weapon. Nothing is written to PlayFab; the weapon vanishes in vanilla lobbies, as PLAN.md
  Phase 4 already specifies. Damage is automatically within vanilla tables.
- **Borderlands-style variety** is already present: 11 types × 7 tiers × 10 styles × 5 rarities ×
  50 perks × 3 elementals × superior × seeded names. New *meshes* would mean feeding
  `WeaponBuilder`/`WeaponPart` from our bundle or the `dfm:` prefab route — real work, and
  cosmetic only. Mod-only affixes beyond vanilla ride on the existing `Manual` serialized type.
- Open question here is grind ethics, which the user already flagged: equipping loot bypasses
  the coin-gated fabricator. Suggest the room-wide "loot weapons usable" toggle so a night can
  be played either way.

### 6. Armor — *cosmetic part easy (this repo already does it); stat part medium, unverified*

The game has no armor stat. "Armor" is cosmetic slots on `CharacterPrefab.AvatarDef` (torso,
legs, boots, headwear, capes …) plus Exosuit perks (`ExoChestArmor`, `ExoChestResilience`).
So mod armor is two things:

- **Look:** a garment attached to the avatar skeleton — exactly what the FBT/garment-rig work in
  this repo already does for custom avatars (the armature-link and bindpose findings apply).
- **Effect:** `AvatarPlayer.OnDamaged(float damage, float knockBackDist, Vector3, DamageType)`
  is public and runs on the local player, so a prefix can scale `damage` by the worn set's
  mitigation. Needs runtime confirmation that this is the single entry point (there is also
  `ApplyRemoteDamage` / `RPC_RemoteDamage` for player-inflicted damage) and that `health` is
  not server-validated (nothing suggests it is; there is no anti-cheat).

## Architecture notes

- **Separate DLL, shared gate.** `LootOverhaul.dll` as its own MelonMod referencing
  `CustomAvatars.dll` for `ModGate` / `ModNet` / `ModRoster` (MelonLoader resolves mod-to-mod
  references from `Mods/`). Cleaner long-term: lift `Gate/` + `Net/` into a shared assembly,
  but that is a refactor the avatar work does not need today.
- **Capability flag** already exists: `ModCaps.Items = 1 << 2` is reserved and unadvertised.
  Loot features gate on *every* peer advertising it, not just the room being modded.
- **Event codes:** `ModNet` hard-limits sends to 140–149 and `CodeItems = 143` is reserved. Use
  143 with a one-byte sub-opcode (LootSpawned / LootTaken / InventorySummary / Sold) rather than
  widening the block; the game's own codes are 1, 2, 50, 70 and the block is clear.
- **Master authority for drops, owner authority for pickup** — same split the game uses.
- **Never patch a method whose native address is shared.** IL2CPP folds identical bodies
  into one function: every empty method in the game sits at dump.cs RVA `0x35FC20`
  (about 3,200 of them). `Chest.OnLootCollected` is one; patching it patched all of them and
  crashed 0.1.0 at startup. Rule: check the RVA's occurrence count in `dump.cs` before
  choosing a hook, and let `Recon/Hooks.cs` refuse shared addresses at runtime. Hook
  `EV_CollectedLoot` / `EV_ChestOpened` on chests, or the per-subclass overrides.
- **Il2Cpp friction to expect:** `object[]` instantiation data crosses the interop boundary as
  `Il2CppSystem.Object[]`; `WeaponModule` `ValueTuple` fields showed garbage in the recon
  dump (GAME-INTERNALS "still open") — read via the DTO instead of the tuple dictionary.

## Estimates (hobby pace, after the recon session)

| Piece | Estimate |
|---|---|
| Recon session (below) | 1 evening |
| L1 Drops + tag + bag-on-pickup, JSON inventory, toasts | 3–4 evenings |
| L2 Bag panel (sort, drop, weight cap) | 1 week |
| L3 Hub stall: sell, currency, wealth rank | 1 week |
| L4 Equip-from-bag with respawn persistence + room toggle | 3–4 evenings |
| L5 Mod weapon shop (seeded stock) | 3–4 evenings |
| L6 Armor: garment + `OnDamaged` mitigation | 1–2 weeks |
| Overworld town | not sized; additive-scene spike first |

## Recon results (LootOverhaul 0.1, 2026-09-01/02, solo, private lobby)

Transcripts: `UserData/LootOverhaul/recon/recon-20260902-002755.md` and the two before it.

**Confirmed**

- **The generator is clean.** 44 weapons (11 types × 4 rarities) through
  `GenerateRandomWeaponModuleForLocalPlayer` plus 800 rarity rolls: uncrafted and armory
  counts unchanged, no `PlayerProfile` writer called during the probe. Prefab names are
  `<Type>_Gen1`; staves are style-specific (`Staff_Heal_Gen1`). Instantiation data is 5
  values for every type. **LongAxe throws a NullReferenceException inside the generator**
  — leave it out of the drop table until understood.
- **`GetSaveString()` is not self-describing.** It returns `#<guid>`, a key into the PlayFab
  armory, so `WeaponModule.GetWeaponModule(save)` cannot rebuild an unsaved weapon. The
  **DTO round-trip is exact** (`new WeaponModuleDTO(wm)` → `new WeaponModule(dto)`): the bag
  stores DTO fields, not save strings. `LootItem` was changed accordingly.
- **Networked spawning works.** `PhotonNetwork.Instantiate(prefab, pos, rot, 0, data)` for
  every type except LongAxe: the result carries `WeaponMelee`/`Weapon`, `PhotonView`,
  `Rigidbody`; `Weapon.RandomSeed` matches the module; Rare outline is cyan; owner is the
  spawner. Picking the spawned weapons up and dropping them logs normally.
- **Cost and salvage come straight from the tables.** At level 15: Common salvage ~20,
  Unique ~120–170, Rare ~180, Legendary ~360–390; costs 200–1300.
- **Rarity rolls at level 15 never produced Legendary** in 800 rolls (Common/Unique/Rare
  only, realm-weighted: Vilehalls leans Rare). Legendary is probably level-gated in
  `RealmDrop`; the mod's own pity/boss rules will have to supply the top end.
- **Buttons work without registration.** A cloned `InteractableButton` is found by the
  game's pointer on its own (the `AddPointable` cast fails — interop proxies don't carry
  `IPointable` — and it doesn't matter). A managed handler bound via `DelegateSupport` fires
  on every trigger pull.
- **A cloned button keeps its serialized listeners.** Two presses of the test button reached
  the fabricator's own `EV_Fabricate` path and wrote `IncrementCharacterData(Coins, -9999)`
  to the profile before our handler ran — the one PlayFab write the recon caused. Every
  persistent listener on a clone must be switched `Off` (`SetPersistentListenerState`);
  `RemoveAllListeners` does not touch them. The probe now does this and refuses to place a
  button that still has one active.
- **Holster fill order on lobby entry:** `InitHolsterContents(isLobby=true)` for all six
  holsters (sidearm L/R, arrows, back, shield, inventory ring) → `RespawnAvatar` →
  `RespawnLocalPlayer(initialSpawn=true)` → `Prop.PickUp` of the loadout weapons. The
  loadout weapons are seeded generator weapons themselves (`Dagger_Gen1`, `random=True`).
  `hazardWeapons` is empty in the lobby.
- **Profile write baseline:** a burst of ~116 `SetData` settings writes at boot, one
  `SavePlayerProfile` on lobby join, nothing else. The watchdog is quiet enough to use.
- **Identity and transport:** `lo.*` properties replicate ~2 s after join; gate goes ACTIVE
  solo; event block 150–159 clear (the game used code 70).
- **Hooks are safe:** 36 read-only patches installed, none failed, game stable.

**Dungeon run (recon-20260902-003604, solo Underworld, died)**

- **`AI.OnKilled` is the drop hook.** Fires on the master for every kill with
  `killerActor` (1 = the player), `damageType`, `AI.type` (Light/Medium) and `armorTier`
  (1–3) — enough to weight drops. **Filter out `killerActor == -1, damageType == 8`**: that
  is the run-end cleanup killing the remaining wave, not a player kill. Each real kill is
  followed by vanilla achievement/stat writes (Kills, UndeadKills, Blademaster).
- **`AvatarPlayer.OnDamaged` is the damage entry point.** Melee hits arrive as `dmg=2
  type=Melee`, ticks as `0.4 type=Other`, and death as `dmg=100 type=LastChanceFailed`;
  it returns `true` and `health.normalizedHP` updates in step. `ApplyRemoteDamage` never
  fired solo. An armor prefix scales `damage` here and must leave `LastChanceFailed` alone.
- **Scene survival.** Plain objects die on every scene change; a `DontDestroyOnLoad`
  object survived lobby → dungeon → lobby. Networked weapons spawned in the lobby were
  destroyed on leaving it. So: the booth is re-placed (or DDOL and toggled) on each lobby
  load, and floor loot must be bagged before the scene changes.
- **Dungeon holster order:** `RefillHolster` then `InitHolsterContents(isLobby=false)` per
  holster, then `RespawnAvatar` → `RespawnLocalPlayer(initialSpawn=true)`. Solo death goes
  straight back to the lobby (no in-dungeon respawn observed). On lobby return the game
  writes the run rewards itself: `IncrementCharacterData(Coins, 40)`, `(XP, 822)`,
  `SetLevel`, then `SavePlayerProfile`. **The vanilla economy writes coins client-side.**
- **Lobby geometry** (world units): four player fabricators at (65,-2,33), (43,-2,7),
  (55,-2,7), (53,-2,33); the goblin `WeaponVendor` at (97,-7,15); the merchant at
  (106,-7,29); the player spawned at about (54,0,20). `CurrentRealm` reads Underworld in
  the lobby. Fabricator buttons are inactive until walk-up (only the door button is found
  active).
- **Slot acceptance:** back = Bow, Crossbow, Staff, KineticStaff, Spear, Longsword,
  LongAxe, Shield; both hips = Axe, Hammer, SmallAxe, Sword, Dagger, Blunt; shield slot =
  Shield; arrows ×10; the inventory ring is consumables only. Loadout slots in PlayFab are
  `#guid` references into the armory, which is why the booth swaps at holster level.
- **Photon codes seen over a run:** 1, 2, 50, 70, 92. Block 150–159 clear.

**Still open:** chest hooks (no chest was opened), in-dungeon respawn with a partner alive,
and every two-player question (who sees the spawn, non-master `OnKilled`, claims).

## Implementation state

- **0.1 (2026-09-01):** recon build. Findings above.
- **0.2 (2026-09-02):** milestone L1 built — master drop roll on `AI.OnKilled`, tagged
  networked spawns with rarity beams and toasts, master-arbitrated claims on `Prop.PickUp`,
  JSON bag, `[`/`]` hotkeys. Untested in game as of writing; see `src/LootOverhaul/README.md`
  for the solo test script. Tested 2026-09-02: the full loop worked solo.
- **0.3 (2026-09-02):** milestone L2 built — world-space bag panel from cloned game buttons
  and the game's font, mesh previews via `GenerateLootWeapon`, sort and paging, per-row
  drop. Untested.
- **0.4 (2026-09-02):** milestone L3 built — the Loot Broker in the lobby: sell counter into
  mod gold, three-slot loadout picker from armory or bag, re-applied after each holster fill
  via `AvatarPlayer.ResetWeapon` + `AssignWeapon` with the watchdog armed. Untested; the two
  things to confirm are that `AssignWeapon` makes no profile write and that it replaces the
  vanilla weapon rather than adding a second. Next: shop (L5), armor (L6).

## Verify first (one UnityExplorer/recon session, no headset-heavy iteration)

The LootOverhaul 0.1 recon build covers this list with read-only hooks and hotkey probes; see
`src/LootOverhaul/README.md` for the session script.

1. Call `WeaponFactory.GenerateRandomWeaponModuleForLocalPlayer(Rare)` in the lobby, then check
   `PlayerProfile.GetUncraftedWeapons()` did **not** grow and `PlayerData.Unsaved` is false.
   If it did, fall back to hand-built DTOs.
2. `NetworkObjectPool.Instantiate<Weapon>("Longsword_Gen1", …, data)` from
   `GetWeaponPrefabData` — confirm the prefab name format for every type (only `Axe_Gen1`,
   `Crossbow_Gen1`, `Longsword_Gen1` appear as literals; the rest are probably composed) and
   that a second client rebuilds the same perks/name.
3. Prefix `Prop.PickUp` on a tagged weapon: confirm the cancel path leaves the hand in a sane
   state (`PropRoot.ClearLastProp`).
4. `AI.OnKilled(int,int)` fires on the master for every death, including `Kill(DamageType)`
   scripted deaths and swarm enemies.
5. Whether objects we place in `lobby` survive a run and return (`Prop.DestroyOnSceneChange`,
   `NetworkObjectPool.ReleaseRoomPrefabs`), or must be re-placed on every `IsLobbyScene` entry.
6. `Holster.InitHolsterContents` timing relative to `RespawnAvatar`, for the equip re-apply.
7. `AvatarPlayer.OnDamaged` is the only path enemy damage takes to the local player's `health`.
8. `VRControllerHands.AddPointable` accepts a cloned `InteractableButton` and its `onPressed`
   fires through a `DelegateSupport`-converted managed handler.

## Design decisions (2026-09-01)

Settled in discussion after the investigation above. These are the rules the code is built to;
change them here first.

1. **The PlayFab wall stays.** No profile writes, ever. A "modded character slot" was considered
   and rejected: PlayFab data follows the account into every room, so it cannot be quarantined,
   and it adds nothing to the feel. The alternative, "items are removed when the room isn't
   fully modded", is what the local design already does and does more strongly: mod items never
   exist outside the gate, so there is nothing to remove. Read-only use of the profile is fine
   (character level for drop tiers, the armory list for the booth picker).

2. **Diablo-feel is the bar for drops.** Rarity-coloured beam or glow and a sound sting that
   scales with rarity, the item arcing out of the corpse, the game's own coloured name and stat
   text on hover, better/same/worse comparison against the current loadout, a low legendary
   chance with a pity counter, elite and boss multipliers, realm-weighted rarity from the
   vanilla `RealmDrop` tables, and a loot filter that auto-bags commons.

3. **Shared drops, master-arbitrated claims.** The master spawns one networked object and every
   client regenerates the identical weapon from the seed. A pickup is a claim request to the
   master; the first claimant wins, the master destroys the object, everyone else's attempt
   fails cleanly. Late joiners receive the master's loot table on join. Pickup is free-for-all
   in version 1; a "reserved for the killer, then anyone" window is a later option.

4. **Trading is drop-and-bag.** A dropped bag item is a tagged loot object again, so a friend
   simply bags it. A "give to nearby player" convenience can come later.

5. **Three weapons into combat, as stock.** The booth is the loadout screen for modded play:
   each of the three slots (left hip, right hip, back) is filled from either the vanilla armory
   or the loot bag. The local JSON stores one entry per slot, either "whatever PlayFab says" or
   a specific weapon, and the mod swaps the chosen weapon in at holster-fill time. Loot can be
   equipped only at the booth, which exists only in the lobby, and a tagged loot object on the
   floor cannot be wielded, so the three-weapon limit holds without any in-dungeon policing.
   The bag in a dungeon does two things: look, and drop. The fabricator remains the place you
   own and craft vanilla weapons. Stock's fourth slot (shield) is left vanilla-only until loot
   shields exist. The holster's hazard-modifier path, which replaces weapons for some dungeon
   modifiers, must win over the re-apply.

6. **Booth first, fabricator injection second.** Version 1 uses our own pedestal, built from
   the public hologram mesh and material calls plus cloned interactable buttons, with three
   verbs: sell, equip, drop. Once that works, surfacing bag items in the vanilla fabricator's
   gear list with its write paths intercepted is the version 2 merge, after which the booth
   shrinks to a shop and sell counter.

7. **Separate mod, own identity.** `LootOverhaul.dll` is independent of CustomAvatars: its own
   Photon player properties (`lo.ver`, `lo.sha`, `lo.caps`), its own event-code block
   (150–159; CustomAvatars owns 140–149), its own MelonPreferences category and UserData
   folder. The gate and transport code is carried over rather than shared, so either mod can
   be installed without the other. See `src/LootOverhaul/README.md`.
