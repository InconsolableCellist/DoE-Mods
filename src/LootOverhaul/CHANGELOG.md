# Changelog — LootOverhaul

Versions are the mod's `Version` constant in `src/LootOverhaul/Core.cs`. Notes for 0.9.9
and earlier are in the version sections of [README.md](README.md).

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
