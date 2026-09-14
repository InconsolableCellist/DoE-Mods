# Changelog — LootOverhaul

Versions are the mod's `Version` constant in `src/LootOverhaul/Core.cs`. Notes for 0.9.9
and earlier are in the version sections of [README.md](README.md).

## 0.9.18 (2026-09-13)

### Fixed
- The ARMOR tab's comparison lines were cut off: three stats with their differences, plus a "loses" entry each, in full-sentence labels outran the row. Comparisons and the worn piece's line now use two-or-three-word stat labels ("stamina drain ÷1.09 (+0.02)", "poison taken", "weapon dmg"), wrap onto two lines before shrinking, and the tab shows four taller rows per page instead of five.

## 0.9.17 (2026-09-12)

### Fixed
- Floor labels on armor said "trinket" / "curio" / "artifact": the caption used the junk tier name for the item's rarity number, and armor's placeholder body is the small reliquary chest, so a Rare piece was a "little artifact thingy" that the walk-over pickup (junk only) ignored (report 2026-09-12; the transcript shows every junk tier taken by walking over it and every armor bundle grabbed by hand). Armor is captioned "head armor" / "chest armor" / "legs armor", tonics "tonic".
- The walk-over height check is measured from the head (within 2.2 m below the eyes) instead of from the rig root, which is not a reliable floor on every platform.

### Added
- `AutoPickupArmor` (default on): armor bundles are picked up by walking over them too, tossing trinkets to make room if the bag is full. Weapons remain a hand grab.

## 0.9.16 (2026-09-12)

### Changed
- The walk-over pickup takes everything that is not a weapon or armor: junk of every tier (trinkets, curios, artifacts; it already did, the wording said "trinkets") and now tonics too. The settings text says so.

## 0.9.15 (2026-09-12)

Fifteen points of feedback from the 2026-09-12 report. Untested in headset.

### Fixed
- An equipped weapon can be enchanted. The ENCHANT list hid equipped weapons and the table refused them ("unequip it at the pedestal first"); now every bag weapon is listed, equipped ones marked, and the enchanted record takes the old one's battle slot and goes into the hand at once through the same `AssignWeapon` the pedestal uses. Its lock carries over.
- Beams are the item's own colour. The beam wore the game's rarity hologram material indexed by `WeaponClass`, which for junk is the junk tier, so an artifact (tier 2) stood under the Rare weapon hologram; the hologram tint is not the name colour either. The hologram material is now cloned for its look and every colour property on it set to the colour the game puts on the item's name (junk: its tier colour); with no hologram material, a plain emissive one in that colour. The transcript's `beam for …` line says which shader and properties were used.
- Text at the kobold no longer runs under buttons. The WEAPONS header ran under RESTOCK, the TONICS rows under the third price button, and the enchant and armor rows were fixed-width; every line now stops where its button column begins and shrinks to fit.
- Tokens refresh after buying a tonic or enchanting: both panels rebuild, and the balance is shown in the header of every buy page, next to where it is spent.
- Equipped and worn items are never sold, swept, dropped, tossed or salvaged. One check (`LootInventory.InUse`, from the loadout table the holster reads) replaces the scattered `EquippedSlot` tests; the armory's SALVAGE now refuses an equipped loot weapon as it refuses a locked one.
- Two candidate causes of an equipped loot weapon missing from the pedestal until the next dungeon: hidden armories are rebuilt a second after the gate reopens (a player joining closes and reopens it) and never while it is shut (a rebuild asked for then waits); and the bag no longer switches to an empty `nick-…` file when PlayFab's player id is momentarily unreadable, which would have made every list built meanwhile show no loot. Not reproduced; the transcript logs `armory rebuild deferred` and `Bag: account changed` when either fires.
- The dice bodies are out of the junk pool: they are the lobby's map-table dice and play that object's sound. Dice already bagged still drop and sell.

### Added
- HELP on the ENCHANT tab: every perk and element the table can add, with the game's own name and description for it (from its language pack, in the current language; `perk.<id>.name` / `.description`) and the weapon types it fits.
- The ARMOR tab compares. One slot at a time (HEAD / CHEST / LEGS tabs with counts): the piece worn there with TAKE OFF, then every bag piece for that slot with WEAR, each stat shown with its difference against the worn piece — green better, red worse, blue new, and in red any worn stat the candidate lacks. Paged.
- Trinkets are picked up by walking over them (`JunkAutoPickup`, default on; `JunkAutoPickupMeters`, 0.6 m flat from the head): a settled, unclaimed trinket close enough and below head height is claimed as a hand grab would claim it. Weapons and armor are never taken this way. Something you dropped yourself is left alone until the scene changes.
- A weapon or armor that does not fit tosses trinkets to make room (`TossJunkWhenFull`, default on): the cheapest unlocked ones, fewest tokens first, until it fits; never anything equipped, worn, locked, or a weapon, armor or tonic. The toss says what it threw out.
- Three more bag tiers above the Bag of Holding: Traveller's Pack (+85 wt, 6000), Porter's Harness (+120, 12000), Caravan Trunk (+160, 24000). Saved levels keep their meaning.
- Bag rows show what the kobold pays and the weight on the name line.

### Changed
- Enchanting consumes no reagent; the price is doubled (300 / 600 / 1200 / 2400 by rarity, × `ShopPriceMultiplier`, × the new `EnchantCostMultiplier`).
- The SELL ALL buttons read `SELL 3 JUNK`, `SELL 2 COMMON` and so on, under a caption that says they sell and what they never touch.
- The bag-upgrade button is just BUY; the line beside it names the tier and price.
- The pedestal frame is a hairline (3% of the tile, at most 6 mm) with a softer glow.

## 0.9.14 (2026-09-09)

### Fixed
- Armor and tonic multipliers no longer compound. Every application multiplied whatever value was live, and the game's `Exosuit.Update(module)` writes only that module's stats (absolutely, read from the assembly), so after the postfix re-apply every other stat was multiplied again; wearing a second piece, drinking a tonic, or re-equipping a perk did the same. Two "leap farther" pieces at 1.14 and 1.16 reached several times the distance within a session (report 2026-09-09). The game's own value of every touched stat is now kept per exosuit instance and put back before each application and before each of the game's recomputes, so the result is always the game's value times the combined multiplier, however many times it runs. The transcript's `buffs applied` line shows the multiplier next to each stat.
- Loot weapons are in the hands from the first lobby of a session, and worn armor is active there, without re-equipping. The holster fills during the lobby load, before the gate has opened (0.7 s in the tester's log), so the bridge handed the game its vanilla loadout while the hologram, read later, showed the loot weapons; the bag loaded after the avatar's exosuit was computed, so the worn set was empty when it was applied. A holster fill seen with the gate shut is remembered and the loot slots are re-applied 1.5 s after the gate opens; the worn armor is rebuilt and applied when the gate opens and a second after every local respawn.

### Added
- The `buffs applied` line goes to the MelonLoader log as well as the transcript, with each stat's before, after and multiplier, the scene, and the game's own switch on the leg multipliers: `VRPlayerControl.UpdateJumping` and `GetInputVelocity` multiply by `Legs_Jump` / `Legs_Leap` only while `GameManager.FriendlyFireEnabled` is false (read from the assembly), and the sandbox sets that flag from its hazard choice. With it on, no jump or leap perk works, the game's own included; wearing leg armor there says so.

### Fixed (same day, second pass)
- "Take less damage" armor and tonics made you take MORE damage. `AvatarPlayer.OnDamaged` multiplies incoming damage by the chest stat for the damage type (melee × `Chest_Armor`, projectile × `Chest_Ricochet`, magic × `Chest_Dispel`, fire × `Chest_Blast`), so a value above 1 hurts more; the game's own perks push these below 1. The mod multiplied them up. Every "less is better" stat now divides: those four, plus poison (`Chest_Antidote`), freeze duration (`Chest_Antifreeze`), fall damage (`Legs_Absorb`) and stamina drain (`Legs_Endurance`). The directions come from the perk table in the game's bundles (`ExosuitModuleContainer`: multiplier and per-level increase) and from the readers in the assembly. Existing armor pieces are corrected on the next apply; their stat line shows ÷ instead of ×.
- Stats the game never reads are gone from the catalogue (`Arms_Stun`, `Legs_Airtime`, `Legs_Shockwave`, `Legs_Swift`, `Mind_Crafter`, `Mind_Lucky`, `Mind_Perception`, `Mind_Predator`, `Mind_Grounded`).

### Changed
- Stat labels say what the game does with the stat: "stronger critical hits", "throw farther", "shoot farther", "stronger impales", "stronger knockbacks", "more axe/spear damage", "more pierce damage", "more weapon damage", "more crossbow/staff damage", "poison does less damage", "melee hits do less damage", "fire does less damage", "magic does less damage", "potions heal more", "self-effects last longer", "arrows and bolts do less damage", "health regenerates faster", "freezing wears off sooner", "less fall damage", "stamina drains slower", "run faster", "jump higher", "leap farther", "more coins from piles", "mystify (staff magic) stronger", "stillness (slowed time) stronger". Inverted stats show ÷ on armor.
- A weapon sold at the kobold (or dropped, trashed, enchanted) no longer lingers in the armory as a vanilla-looking item that the trash can pays coins for. The customizer builds its weapon lists once per profile load (`AvatarCustomizer.InitWeaponModules` from `OnPlayerProfileLoaded`, read from the assembly), so it kept the module object. Now every weapon that leaves the bag is remembered by GUID in the inventory file (`RetiredGuids`), hidden customizers rebuild their lists at once (a showing one on its next open), a retired weapon is labelled `[SOLD]`, and salvage, trash and unlock on it are refused with the coin write blocked. A newly bagged weapon also triggers the rebuild, so it appears in the armory without a relog.

## 0.9.13 (2026-09-08)

Tester feedback pass (bugs 1–5, suggestions 1–5 of the 2026-09-08 report). Untested in headset.

### Fixed
- Floor loot glows in its rarity colour. The game's own pickup outline is not rarity at all: `Weapon.outlineColor` returns a constant cyan for every weapon (read from the assembly), and the FXOutline glow colour is whatever the prefab was saved with, so every drop glowed blue while the beam was right. The glow (`FXOutline.SetGlowColor`) and the prop's outline shader parameter are now set to the colour the game itself puts on the weapon's name; junk keeps its tier colour.
- Long stats lines are no longer cut off in the bag and at the kobold. The stats text is compacted (the game's empty colour tags removed, lines joined with a dot) and the row texts shrink to fit their width, down to 60% of their size.
- The armory's SALVAGE button no longer turns a loot weapon into the game's coins. The path (`AvatarCustomizer.EV_Salvage`, read from the assembly) removes the weapon with the result ignored and then credits salvage coins, so a loot weapon vanished from the pedestal, stayed in the bag, and paid coins; a locked one did the same. Salvaging a loot weapon now sells it for tokens at the kobold's price and the coin write is refused. A locked loot weapon refuses the salvage before the game touches anything (the pedestal selection is read from `EV_SelectModule`), or mid-call if that read failed.
- Sandbox kills roll nothing (`SandboxDrops`, default off). The practice arena despawns its enemies and their bodies; the loot was free and the despawn left the sparkle and label hanging in the air.
- A floor item the game destroys without a claim is forgotten: its sparkle, label, beam and outline go with it.

### Added
- A bright frame around loot weapons on the fabricator and armory tiles instead of the small "LOOT" word (`PedestalFrame`, colour `PedestalFrameColor`, default `#FFD24A`). `PedestalFrame = false` brings the word back.
- SELL ALL by rarity at the kobold: JUNK, COMMON, UNIQUE and RARE buttons with the count and token total of each. Locked, equipped and worn items are never swept; a Legendary is only sold one at a time.
- Elites always drop `EliteDrops` (1) pieces and Legends `LegendDrops` (2), on the normal rarity curve; pity applies to the first.
- Boss piles scale: `BossDropsPerExtraPlayer` (1 per extra player; mini-boss 0.5), `BossDropsPerExtraHealthBar` (1 per health bar beyond the first, from the game's `Specs.stages`, the multi-bar bosses), and `EliteBossExtraDrops` (1 for a boss of the Elite strength type, 2 for Legend). Piles are capped at 12.
- The loot goblin (the game's `SauronKeys.IDHashLootGoblin`) drops `GoblinDrops` (3) pieces, the first at least `GoblinGuaranteedClass` (Unique), plus `GoblinJunkRolls` (3) guaranteed trinkets, and is `LootGoblinScale` (1.5) times its size on every modded client. The game's coins are not touched (design decision 1), so "more gold" is more trinkets.
- The drop log line carries the enemy's type name, its health bar count and the player count.

## 0.9.12 (2026-09-07)

### Added
- Bosses and mini-bosses drop a pile. A boss drops `BossDrops` pieces (default 3) and a mini-boss `MiniBossDrops` (default 2), weapons or armor by the usual `ArmorShare`. The first piece always drops and is at least `BossGuaranteedClass` (default 2, Rare); pity still forces it Legendary. Each further piece drops with `BossDropChance` (default 1.0, so all of them) on the boss rarity curve (never Common, a 35% bump). Pieces are kicked out around a circle so they land apart. Bosses also roll their trinket (three times the normal `JunkDropChance`) on top of the pile; before, a boss's guaranteed weapon meant it never reached the junk roll.

### Fixed
- Mini-bosses are treated as bosses. The game's `AI.IsBoss` is exactly "spawned as the Boss class" (read from the assembly), so a mini-boss rolled like a regular enemy of its type. The rank now comes from the class the game spawned the enemy as: Miniboss (4) or Boss (5), with `IsBoss` as the fallback.

### Changed
- `BossDropChance` now means the chance of each extra piece, not whether a boss drops at all. The drop log line for a boss says `boss` or `mini-boss` and `piece=n/m`; the junk line carries `boss=`.

## 0.9.11 (2026-09-05)

### Added
- LOCK / UNLOCK per row on the kobold's sell panel. A locked item cannot be sold, swept by SELL ALL JUNK, dropped from the bag, or trashed at the pedestal. Saved in the inventory file; the bag panel marks locked rows.
- Weapon and junk chances scale with the party: `1 + DropChancePerExtraPlayer × (players − 1)`, default 0.35 per extra player. The drop log line shows the player count used.
- `BeamMinClass`: beams only over items of at least this rarity (default 2, Rare; junk 2 is an artifact). Beams stay off unless `DropBeams` is on.

### Changed
- Beams are narrower.
- Bones (the wolf treat body) are tinted by junk tier: brown trinket, ivory curio, gold artifact with a glow.
- Loot outlines are set to normal visibility. Some bodies (mug, dice) ship with an always-on-top glow that showed through walls.
- The pickup toast says `T` instead of `tokens`.
- `LegendaryPityKills` is documented as counted in the host's own file from every kill in the room; a non-host's counter does not move.

### Fixed
- A full bag leaves the item on the floor. The master's grant on the game's pickup RPC (from 0.9.8) bypassed the picker's bag-full check and bagged the item anyway; that path is removed, since the picker's own pickup now fires.
- The rock body is retired: it spawned with nothing visible.

## 0.9.10 (2026-09-04)

### Fixed
- Other players can pick loot up. The hand's grab rule, read from the game's code, only accepts a prop that is a room-owned scene object or the grabber's own. Every drop was a plain `PhotonNetwork.Instantiate` by the master, so the master's personal property. Drops are now `PhotonNetwork.InstantiateRoomObject`, the call the game's own `LootSpawner` makes, with the game's room data for junk bodies. Only the master may create room objects, so dropping an item from the bag on a non-master client asks the master to spawn it.
- Floor loot no longer survives into the next dungeon or turns up upright in the air for a rejoining player. The pooled bodies survive a scene load and an undestroyed room object stays in the room's event cache; the master now network-destroys every unclaimed drop it controls on the game's pre-scene-load event, with scene initialisation, gate closing and quit as fallbacks.
- Late joiners get each item's resting position and rotation with the master's re-send.
- Trashing a bag weapon at the pedestal no longer credits its salvage value as real coins; the coin write is refused while a bag weapon is trashed. The weapon still leaves the bag.
- Pedestal thumbnails for bag weapons carry the LOOT tag again. The pedestal fills its tiles through `SetCustomItemType`, which was not patched; the tag is placed on the tile's own icon bounds.
- A player whose grab reaches the master twice (the game's pickup RPC and the mod's claim) no longer gets a spurious "Taken." toast.
- A force grab that ends in a loot pickup is ended before the drop-back; the transcript logs `pickup: force grab was in progress` when it happened. Best candidate for the stretched-arm report; not reproduced.

### Changed
- The stall is signed KOBOLD TRAVELER and the mod's currency is called tokens everywhere. The inventory file keeps its `Gold` field, so balances are intact. Tokens are never the game's gold.
- Bag, sell and shop rows show the price on the first line and the weapon's type, tier and stats text on the second. Weight appears only on the bag capacity line.
- Rates: weapons 1.5% base (was 3.5%), junk 18% (was 35%). Bag base 30 wt; the saved settings file still said 60 and was updated along with the rates.
- SELL ALL JUNK is one and a half times bigger, on its own band above the bag line.

### Added
- Drops tumble and play the prop's own throw and spin audio, which the game networks; weapons and armor add the coin pile's chime when they land (`DropSounds`, `DropChime`: start / finish / collected / off).
- The game's pickup glow is kept lit on floor loot (`DropOutline`).
- The bag panel closes by itself when you walk `BagAutoCloseMeters` (2 m) from it.
