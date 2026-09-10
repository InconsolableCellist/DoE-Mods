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

## 0.9.14 — buffs never compound, damage-reduction stats divide, sold loot leaves the armory

- **"Take less damage" no longer means more.** `AvatarPlayer.OnDamaged` multiplies incoming
  damage by the chest stat for the damage type (melee × `Chest_Armor`, projectile ×
  `Chest_Ricochet`, magic × `Chest_Dispel`, fire × `Chest_Blast`); the game's perks push those
  below 1, the mod pushed them above. Every "less is better" stat now divides (those four,
  poison, freeze duration, fall damage, stamina drain), shown as ÷ on armor.
- **Sold loot leaves the armory.** The customizer builds its weapon lists once per profile
  load, so a weapon sold at the kobold stayed on its pedestal looking vanilla and the trash can
  paid coins for it. Weapons that leave the bag are remembered by GUID (`RetiredGuids`),
  hidden armories rebuild at once, stale entries read `[SOLD]` and refuse salvage, trash and
  unlock with the coin write blocked. Newly bagged weapons appear without a relog.
- **Stat labels** say what the game does; nine stats nothing reads are gone.

- **Armor and tonics multiply the game's value once.** The game's own stat values are kept
  per exosuit instance and put back before every application and every game recompute
  (`Exosuit.Update(module)` writes only its module's stats). Two 1.14/1.16 pieces are 1.32×,
  not several times.
- **First lobby of a session.** The holster fills before the gate opens; a fill seen with the
  gate shut is re-applied 1.5 s after it opens, and worn armor is applied on gate open and a
  second after every local respawn.

## 0.9.13 — tester feedback: outlines, armory salvage, sell-all, bigger piles

- **Rarity outlines.** The game's weapon outline is a constant cyan (`Weapon.outlineColor`);
  loot now sets the glow and the outline shader colour to the rarity colour from the weapon's
  own name tag.
- **Armory SALVAGE** on a loot weapon sells it for tokens and refuses the game's coins; a
  locked one is refused outright. The pedestal trash can already behaved this way.
- **Stats lines fit.** Compacted stats text, rows shrink to fit.
- **Sandbox kills drop nothing** (`SandboxDrops`); a body the game destroys takes its
  sparkle and label with it.
- **Frame instead of the "LOOT" word** on fabricator and armory tiles (`PedestalFrame`,
  `PedestalFrameColor`).
- **SELL ALL by rarity** at the kobold: JUNK / COMMON / UNIQUE / RARE, never locked, equipped
  or worn items, never a Legendary.
- **Elites and Legends always drop** (`EliteDrops` 1, `LegendDrops` 2).
- **Boss piles scale** with players (`BossDropsPerExtraPlayer`), health bars
  (`BossDropsPerExtraHealthBar`) and strength type (`EliteBossExtraDrops`).
- **Loot goblin**: its own pile (`GoblinDrops`, `GoblinGuaranteedClass`, `GoblinJunkRolls`)
  and `LootGoblinScale` (1.5) on every client.

## 0.9.12 — bosses drop a pile

- **Bosses and mini-bosses drop several pieces.** `BossDrops` (3) for a boss, `MiniBossDrops`
  (2) for a mini-boss, weapons or armor by `ArmorShare`. The first piece always drops and is
  at least `BossGuaranteedClass` (2, Rare; pity still makes it Legendary); every further
  piece drops with `BossDropChance` (1.0) on the boss rarity curve. The pieces are kicked out
  around a circle so they land apart, and the boss still rolls its trinket on top.
- **Mini-bosses count.** The game's `AI.IsBoss` is only the full Boss class; the rank now
  comes from the class the enemy was spawned as (Miniboss 4, Boss 5). The drop line says
  `boss` or `mini-boss` and `piece=n/m`.

## 0.9.11 — locks, party scaling, tidier bodies

- **LOCK / UNLOCK** per row on the kobold's sell panel. A locked item cannot be sold (SELL
  ALL JUNK skips it), dropped from the bag, or trashed at the pedestal. Saved in the
  inventory file; the bag panel marks locked rows.
- **Party scaling.** Weapon and junk chances are multiplied by `1 + DropChancePerExtraPlayer
  × (players − 1)` (default 0.35 per extra player). The drop line logs the player count.
- **Pity** (`LegendaryPityKills`) is counted in the host's own file from every kill in the
  room; a non-host's counter does not move. Documented in the setting.
- **Bag full leaves the item on the floor.** The master's grant on the game's pickup RPC
  (0.9.8) bypassed the picker's bag-full check and bagged it anyway; that path is gone.
- **The rock body is retired** (spawned with nothing visible). **Bones are tinted** by tier
  (brown, ivory, gold with a glow) so they read differently from the wolf's treats.
- **Glow stays behind walls.** Some bodies ship with an always-on-top outline (mug, dice);
  loot outlines are set to normal visibility.
- **Beams** are narrower and only over items of `BeamMinClass` rarity or better (default 2:
  Rare and Legendary weapons and armor, artifact junk). Beams are still off unless
  `DropBeams` is on.
- The pickup toast says `T` instead of `tokens`.

## 0.9.10 — other players can grab loot, the kobold traveler, tokens

- **Anyone can pick loot up.** The hand's grab rule, read from the game's code: a prop is
  grabbable only when it is a *room object* (a scene view, owned by the room) or the
  grabber's own. Every drop was a plain `PhotonNetwork.Instantiate` by the master, so the
  master's personal property, and no other player could ever grab one (two runs, one with
  the remote present from the start, one joining late: not one claim from the remote). Drops
  are now `PhotonNetwork.InstantiateRoomObject`, the call the game's own `LootSpawner`
  makes, with the game's room data for junk bodies. Only the master may create room objects,
  so dropping an item from your bag on a non-master client asks the master to spawn it.
- **Floor loot dies with the dungeon.** The pooled bodies survive a scene load and an
  undestroyed room object stays in the room's event cache: the previous dungeon's drops
  turned up in the next one, and a rejoining player got them all re-created upright in the
  air. The master now network-destroys every unclaimed drop it controls on the game's
  pre-scene-load event (fallback: scene initialised, gate closed, quit).
- **Late joiners get the resting pose.** The master's re-send to a late joiner carries each
  item's position and rotation, and the joiner's copy is moved there.
- **The kobold traveler.** The stall is signed KOBOLD TRAVELER and the mod's currency is
  *tokens* everywhere (the inventory file keeps its `Gold` field; nothing is lost). Tokens
  are never the game's gold, by design.
- **Trash pays nothing.** The pedestal's trash can removed a bag weapon *and* credited its
  salvage value as real coins. The coin write is refused while a bag weapon is trashed.
- **Rows without weight.** Bag, sell and shop rows show the weapon's stats on the second
  line (type, tier, the game's stats text) and the price on the first; no `wt` anywhere but
  the bag capacity line.
- **Pedestal thumbnails** for bag weapons carry the LOOT tag again: the pedestal fills its
  tiles through `SetCustomItemType`, which was not patched, and the tag is now placed on
  the tile's own icon bounds.
- **Drops spin** (a random tumble) and play the prop's throw/spin audio, which the game
  networks; weapons and armor add the coin pile's chime when they land (`DropChime`).
- **The pickup glow** the coins and vanilla drops have (`Prop.Outline`) is kept lit on floor
  loot (`DropOutline`).
- **The bag closes by itself** when you walk `BagAutoCloseMeters` (2 m) from it.
- **Rates:** weapons 1.5% base (was 3.5%), junk 18% (was 35%). Bag base 30 wt; your
  settings file still said 60 and was updated. SELL ALL JUNK is one and a half times bigger,
  on its own band above the bag line.
- **Force grab ended before the drop-back.** The best candidate for the "arm stretched out"
  report: a force-grabbed drop ends in the same pickup we cancel a frame later. The pickup
  hook now ends the force grab first and logs when it did (`pickup: force grab was in progress`).
- A player whose grab reaches the master twice (the game's pickup RPC and our claim) no
  longer gets a spurious "Taken." toast.

## 0.9.9 — armor at the shopkeeper, dungeon tier cap, smaller bag

- **ARMOR tab** at the shopkeeper: the three slots with what is worn and TAKE OFF, then the
  pieces in your bag with WEAR. (The bag panel's WEAR button remains; armor has no vanilla
  screen, so this is its home.)
- **Tier cap.** A drop's tier is never above the tier the dungeon was entered at (the room's
  `lvl_tier`, else the game's difficulty tier). The transcript's `loot tier` line shows both.
- **Bag halved:** base 30 wt; upgrades +15 / +30 / +55. Your settings file was updated.

## 0.9.8 — first two-player fixes

- **Other players can pick loot up.** Spawned loot now gets the game's own post-spawn state
  sync and pickup-enable, which a bare Photon instantiate skips; remote copies were inert.
  As a second path, the master grants the claim itself when another player's pickup RPC
  arrives for a tagged object.
- **One shopkeeper spot for everyone.** The mod carries a built-in lobby position (the
  author's chosen spot); `=` still moves it for you alone and sets `BoothPlaced`.
- Other players no longer get a "loot dropped" HUD message for every drop; the floor label
  is the announcement.

## 0.9.7 — copy pass, greyed buttons

The sign reads SHOPKEEPER. Unaffordable buys show a greyed `NEED 2510g` instead of a live
button (tonics and the bag upgrade too). Bag tiers are Satchel, Backpack, Bag of Holding,
bought with `BUY SATCHEL` and so on. Tonics are "good for one excursion". The developer
phrasing in empty states, the cold enchanting table, lost claims ("Taken.") and the five
"gone" messages is replaced with plain English.

## 0.9.6 — armor

Armor drops out of the weapon budget (`ArmorShare`, 35% of successful weapon rolls) as a
bundle on the floor (placeholder body, the reliquary chest, until cosmetic meshes are wired
in). A piece has a slot (head, chest, legs), a rarity, a rolled name ("Scale Hauberk of the
Owl", "Mithril Sabatons of Warding"), and one to three exosuit-stat bonuses — Common one,
Rare two, Legendary three — drawn only from perks you have unlocked, in bands from ×1.04 to
×1.28. WEAR it from the bag: one piece per slot, permanent while worn, combined with any
tonic (armor multiplies together, the best tonic on top), re-applied after every recompute,
kept in the inventory file so it is on you next session. TAKE OFF to change; worn pieces
cannot be sold or dropped. The bag header shows what you are wearing.

## 0.9.5 — hover labels, bag panel spacing

- Floor labels show only while you look at the item (within about four metres, a narrow
  cone) or reach a hand toward it (`DropLabelsOnHover`, on by default).
- Bag panel: sort buttons spaced for their glow, tonic rows shortened so DROP and DRINK no
  longer sit on the text, and any line too long for its space is cut rather than drawn under
  the buttons.
- Confirmed on the 2026-09-03 run: the enchanting self-test passed with the name `manual`
  (Enchant tab opens by itself now), tonics apply (Arms_Distance 1.3 → 1.495 on a Minor
  Long Arm Liniment), and the stick-hold gesture opens the bag.

## 0.9.4 — a bigger bag, and the real button width

- **Bigger bag.** The sell panel's bottom line offers the next bag upgrade for mod gold:
  Satchel (+30 wt), Traveller's Pack (+60), Porter's Harness (+110), on top of
  `BagWeightCapacity`. The level is kept in the inventory file.
- The button template measured 5 cm wide on the last run (the label, not the glowing
  frame), which is why every button-relative layout was off. Implausible measurements are
  ignored now and the known width used.

## 0.9.3 — fixes from the 0.9.2 run

- The broker's BUY panel now has a tab row (WEAPONS · TONICS · ENCHANT) under the header
  instead of three buttons stacked on one spot.
- The enchanting self-test tries `manual` first: the run showed random modules are named
  `random` and mythics `mythic`.
- Tonics now exist for every exosuit stat (Long Arm Liniment, Antidote Tonic, Mystic
  Draught, Marathon Brew for your current four), so the page is never empty once a perk is
  unlocked. Reduction stats (antidote, antifreeze) divide instead of multiply.
- Junk bodies: the hockey puck (a flat disc that sinks into the floor) and the Christmas
  ornament (a breakable, not grabbable) are retired. Previews are clamped so a bad mesh
  bound can no longer draw a spear across the booth.

## 0.9.2 — the enchanting table (self-testing)

The broker's BUY panel has an ENCHANT page. Pick an unequipped bag weapon, see its perks,
element and free slots (Common 1, Unique 2, Rare 2, Legendary 3), and add a perk valid for
its type or an element (fire, ice, poison) for gold plus a reagent from your junk: a curio
for Common/Unique, an artifact for Rare/Legendary. The result is a new weapon record on the
game's own "manual" module, which carries chosen perks explicitly and which the game already
networks for mythics.

How the game names a manual module is the one unknown, so the table **self-tests** when the
lobby gate opens: it builds a manual module from a generated sword under six naming
conventions and keeps the first the game serialises with the manual packet length. Until
one passes the page reads "table is cold" and the transcript's `Enchanting self-test`
section shows what each candidate produced, plus the real module names of a random and a
mythic module. That section is the thing to read after the next run.

## 0.9.1 — tonics, and lock-step with the game's gating

- **Tonics.** The broker's BUY panel has a TONICS page: one-run brews on the game's own
  exosuit stats (Ironskin = armor, Bruiser's Brew = power, Quicksilver = haste, Lucky Coin
  Tea = fortune, and eleven more), in Minor / Major / Grand (×1.15 / ×1.30 / ×1.50).
  DRINK one from the bag; it lasts until the lobby loads again, so drink in the lobby for
  your next run. The bag header lists what is active. Nothing is written to the profile:
  the multiplier is re-applied a frame after every exosuit recompute.
- **Lock-step.** The broker only brews for stats whose perk you have already unlocked at
  the exosuit station. Weapon drops and shop stock use only weapon types your armory (or
  loadout) already contains, and staff drops only styles you own. Seasonal styles are
  never generated. The transcript logs the exosuit table and the unlock sets at lobby
  entry and at quit so the gating can be checked.

## 0.9 — endless junk, the hand, the gesture

- **Junk names are rolled** from adjective × material × noun × provenance pools per tier
  and per body ("Tarnished Pewter Tankard", "Engraved Silver Goblet of the Vile Halls",
  "Runic Moonstone Reliquary of the Bone King"); value follows the material and adjective.
  All ten bodies probed on 2026-09-02 spawn, so the table is live.
- **Hand fix, third attempt.** The release now goes through the hand (`PropRoot.Drop`),
  which is what opening your fingers does; the earlier prop-side drop left the hand attached,
  and the claim then moved the object fifty metres down with the hand still on it. Claimed
  objects are now hidden in place (renderers, colliders, physics off) and destroyed 2.5 s
  later; nothing is moved.
- **Gesture default is `stick-hold`:** right stick straight up for 0.7 s. SteamVR's action
  set is null in the main menu, which 0.8 mistook for "no SteamVR" and gave up; it now
  retries. `back-grip` remains an option but the back holsters' grab zones fight it.

## 0.8 — playtest fixes

- **Open the bag in VR.** `BagGesture = back-grip` (default): reach your right hand behind
  and below you, squeeze the grip and push the right stick up for half a second. Or
  `stick-hold`: right stick held up. Reads the game's own SteamVR actions, so it works on
  the OpenVR rig; elsewhere it logs once and the `[` key remains.
- **Loot marking.** The beam is off (also switched off in your existing settings file);
  the coin pile's own sparkle is borrowed and placed over each drop, plus the floating name.
- **Laser across the whole window.** An invisible pointer target sits behind each panel so
  the laser shows wherever you aim on it (`PanelLaser`).
- **Hand freeze.** The drop-back now happens one frame after the game's pickup, the hand's
  memory of the prop is cleared, and the claimed object is hidden at once but destroyed
  1.5 s later. If the hand still freezes, the next suspect is the CustomAvatars hand rig.
- **Drop rates** now 3.5% base for weapons (was 8), 35% for junk (was 18); both were also
  written into your settings file since MelonPreferences keeps old values. Shop prices
  × 2.5.
- **Junk names match their models.** `Bone` and `Bones` refused to spawn; the table now
  uses dice, the dog treat and the two lobby trophies with names to suit (bone dice,
  weighted dice, marrow charm, skull crown, guild trophy, religious icon, gilded skull
  crown, gambler's relic). Press **-** (minus) in a private room to try further candidate
  props one at a time; each is described in the transcript with its size and components.
  **;** (semicolon) runs a census of every GameObject the game can load by name from its
  Resources folders that carries a Prop or PhotonView — the authoritative list of possible
  loot bodies — and the Backslash lobby survey now also lists decoration meshes (cups,
  bottles, skulls, candles…) with sizes, as candidates for mesh-swapped loot bodies.
- The junk button now reads `SELL n JUNK · value g`. Junk items are also listed as rows.

## 0.7 — the shop

The broker's right-hand panel sells weapons. Six (`ShopSlots`) generated at your loot
tier, the last two leaning a tier up, rarity 50/30/15/5, priced at the game's own cost
figure × `ShopPriceMultiplier` in mod gold. Stock is yours alone, saved in the inventory
file, rolled again for free once it is `ShopRefreshMinutes` old (60), or on the spot with
RESTOCK for a tenth of the stock's asking prices. BUY puts the weapon in your bag as
ordinary loot, so it sells back at salvage and equips at the pedestal like anything else.
TOO DEAR means exactly that.

## 0.6 — tuning, junk, labels

- **Drops are reasonable now.** Tier comes from the game's own loot tier for your level
  (`TierUpChance` for one tier up), rarity is weighted `WeightCommon/Unique/Rare/Legendary`
  (70/22/7/1 by default) with bosses and the pity counter pushing upward, and critters
  (scorpions, wasps) never drop weapons. Bosses always may.
- **Junk.** A failed weapon roll can drop a trinket (`JunkDropChance`): gnawed bones, bone
  dice, silver mugs, skull crowns, religious icons, gilded skulls and the like, riding on
  harmless vanilla props for their floor body. Three tiers, trinket / curio / artifact,
  priced accordingly. The broker has a SELL ALL JUNK button. Which vanilla props actually
  spawn is learned at runtime and logged; a prefab that refuses is retired for the session.
- **Floating name labels** over floor loot instead of the beam (`DropLabels` on,
  `DropBeams` off by default). They face you and use the game's font.
- **Pedestal thumbnails** for bag weapons carry a small gold LOOT tag.
- **Pickup hand freeze fixed:** the game's pickup now completes and the item is dropped
  straight back out of the hand before the claim, instead of the pickup being cancelled.
- Copy trimmed, previews scaled to fit whatever the weapon is, page buttons spaced.

## 0.5 — loot in the game's own fabricator; the booth is a sell counter

Equipping moved into the vanilla fabricator, which is where you already choose your three
weapons. With the gate open, every bag weapon appears in the fabricator's gear list with a
`[LOOT]` prefix on its name, and its hologram, stats and equip buttons work as for any
weapon. Equip writes go to the inventory file instead of the profile; the holster fill
resolves them natively on every spawn, so the chosen weapon is simply there, in the lobby
and in the dungeon. Equipping a vanilla weapon into a slot clears the loot choice. Trashing
a `[LOOT]` weapon at the fabricator removes it from the bag. Storing it in the armory is
refused silently. The watchdog summary at quit must still say no profile write happened.

The booth now only sells. `=` still places it. UI text is sized for VR now, buttons are
laid out from their measured width, and weapon previews are larger.

## 0.4 — milestone L3, the booth (superseded by 0.5)

The Loot Broker stands in the lobby, built one second after the gate opens there. Its
position comes from `[LootOverhaul] BoothX/Y/Z/Yaw`; the defaults put it 2.5 m in front of
where the lobby spawned you on 2026-09-02. Press **=** (equals) in the lobby to move it to
1.5 m in front of where you stand, facing you; the setting is saved.

- **SELL** (left panel): bag items by value, the game's salvage value × `SellMultiplier`
  in mod gold. Equipped items show their slot instead of a SELL button.
- **BATTLE LOADOUT** (right panel): the three stock slots. CHOOSE lists every weapon that
  holster accepts, from your vanilla armory (read-only) and your bag, with the game's
  stats line; USE picks it; VANILLA hands the slot back to the game. The choice lives in
  the inventory JSON. After every holster fill (lobby entry, dungeon entry, respawn) the
  mod waits 1.5 s and swaps the chosen weapons in with the game's own `ResetWeapon` +
  `AssignWeapon`, with the PlayFab watchdog armed around the call. Dungeon hazard weapons
  win over the loadout. `LoadoutEnabled = false` turns the swap off.

Solo test: in the lobby walk up to the booth; CHOOSE a slot, USE a bag or armory weapon,
check your hip or back after the swap (about two seconds), then enter a dungeon and check
it is still there. SELL something and watch the gold. The transcript's `loadout:` lines say
what was applied and how many props `AssignWeapon` spawned; the watchdog summary at quit
says whether any of it wrote to the profile (it must not).

## 0.3 — milestone L2, the bag panel

`[` opens and closes a world-space panel in front of you (it stays where it was opened;
close and reopen to move it). Each row shows the weapon's generated mesh, its coloured
name, type and tier, the game's stats line, weight and value, and a DROP button that puts
that item on the floor as tagged loot. Sort buttons: NEW, VALUE, WEIGHT, RARITY; pages of
six. The buttons are clones of the fabricator button with every inherited listener off,
and the font is borrowed from the game, so both templates are captured in the lobby
(three seconds after it loads) and kept across scene loads. If you open the bag in a
dungeon before ever loading the lobby with this build, you get a toast and the summary
instead.

## 0.2 — milestone L1, the core loop

What runs, gated to a fully modded private room (solo counts):

- **Drops.** On the master, every real kill (killer actor ≥ 0; the run-end cleanup is
  filtered out) rolls `BaseDropChance × enemy multiplier` (Light 1, Medium 1.5, Heavy 2.5,
  Elite 4, Legend 6, elementals 2, Gold 3) or `BossDropChance` for bosses. Rarity is the
  game's own realm-weighted roll; bosses never drop below Rare; after `LegendaryPityKills`
  kills without one, the next drop is Legendary. The weapon is generated by the game and
  spawned through the networked path, kicked out of the corpse, tagged, and broadcast.
- **Tags and beams.** Every modded client keeps a registry of loot by PhotonView id and
  puts a rarity-coloured beam (the game's hologram materials) over each. A toast names the
  drop in its rarity colour. Late joiners get the master's tag list.
- **Bag on pickup.** Grabbing a tagged weapon cancels the wield and sends a claim to the
  master; the first claimant gets it, the master destroys the object, everyone untags. A
  full bag refuses the claim with a toast and leaves the item on the floor.
- **Bag.** `UserData/LootOverhaul/inventory/<playfab id>.json`, saved on every change.
  `[` toasts a summary and dumps the contents to the transcript; `]` drops the most
  recently bagged item at your feet as tagged loot again (that is also how trading works).

Solo test: private lobby, run a dungeon, watch for beams; grab one (it bags instead of
wielding); `[` to check; `]` to drop it back; grab it again. The transcript's `DROP #n`
lines carry the roll numbers. LongAxe never drops (generator bug, see the design doc).

## Running the 0.1 recon build

Everything v0.1 does is read-only: logging hooks, a PlayFab write watchdog, and four hotkey
probes. It writes a transcript to `UserData/LootOverhaul/recon/recon-<timestamp>.md`; the
MelonLoader console and `MelonLoader/Latest.log` get headlines only, each prefixed
`[LootOverhaul]` (`grep '\[LootOverhaul\]' Latest.log` separates them from CustomAvatars,
which shares the same file — MelonLoader has one log, one prefix per mod).

Suggested session, solo is fine (a private room with only you counts as gated):

1. **Main menu or lobby, before joining a room** — press **Insert**. Generator survey: one
   weapon per type × rarity through the game's own generator, with prefab names, coloured
   names, stats, cost, salvage, save-string round-trips, and 800 rarity rolls. The headline
   `VERDICT` says whether the generator touched your profile weapon lists; the watchdog
   flags any `PlayerProfile` write that lands during the probe with `!!! ... DURING PROBE`.
2. **In a private lobby** — press **Backslash** (`\`). (End is a game key: it sends you to the main menu.) Lobby survey: scene flags, fabricator and vendor
   positions, every holster and what each slot accepts, your current loadout and armory
   (read-only), then two marker cubes near your head: cyan is plain, magenta is
   DontDestroyOnLoad. Leave for a dungeon and come back; the transcript reports which cube
   survived each scene change.
3. **Same lobby** — press **Scroll Lock**. A vanilla fabricator button is cloned in front of
   you labelled `LOOT TEST` and wired to a managed handler, with every inherited listener
   switched off (the first version left them on and two presses reached the fabricator's
   coin write). Point at it and pull the trigger;
   each press logs a headline. If nothing logs, the transcript says whether registration with
   the hands' pointable list succeeded.
4. **Same lobby** — press **Delete**. Spawns one Rare weapon in front of you through the same
   networked path drops would use (a different type each press). Pick it up, holster it,
   drop it. The `Prop.PickUp` / `Prop.Drop` lines show what fires and on which client. Go
   into a dungeon: the transcript says whether the spawned weapon survived the scene change.
5. **Run a dungeon** — kill things, open a chest, take damage, die and respawn if you can.
   `AI.OnKilled`, `Chest.OnLootCollected`, `Holster.InitHolsterContents` /
   `RefillHolster` / `OnAvatarRespawn`, `AvatarPlayer.RespawnAvatar` and `OnDamaged` lines
   land with timestamps, so the ordering questions answer themselves.
6. **Quit normally.** The transcript closes with the Photon event-code tally (is 150–159
   clear?) and the watchdog summary of every profile write the game made.

With a friend on the same build, repeat steps 4 and 5 together: who sees the spawned
weapon, whose `Prop.PickUp` fires when they take it, and what `AI.OnKilled` logs on the
non-master client.

Switch it all off with `ReconEnabled = false` in `[LootOverhaul_Dev]`; `HotkeysEnabled` and
`ProfileWatchEnabled` are separate switches.

## Layout

```
LootOverhaul/
├── Core.cs           MelonMod entry: config, self-hash, hook, roster, gate, handshake, hotkeys
├── ModConfig.cs      [LootOverhaul] / [LootOverhaul_Dev] settings
├── ModPaths.cs       UserData/LootOverhaul/, recon dir, per-account inventory path
├── SelfCheck.cs      DLL SHA-256 for the roster
├── Gate/             ModCaps, ModPeer, ModRoster, ModGate, ModHandshake
├── Net/              PhotonHook (the one Harmony patch), ModNet (send/receive, 150–159)
├── Loot/             LootItem, LootInventory — the JSON model, no game types
└── Recon/            ReconLog, Interop, Hooks, ProfileWatch, GameplayHooks, EventTally,
                      GeneratorProbe, LobbyProbe — the 0.1 build
```

Still to build, in the order the design doc estimates: drop roll on `AI.OnKilled` (master
only) + loot tag + bag-on-pickup; the bag panel; the lobby booth; equip-from-bag with respawn
re-apply; the shop.
