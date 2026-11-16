# Changelog — LootOverhaul

Versions are the mod's `Version` constant in `src/LootOverhaul/Core.cs`. Notes for 0.9.9
and earlier are in the version sections of [README.md](README.md).

## 0.15.2 (2026-11-16)

### Fixed
- **The kobold showed 0 gold, so nothing could be bought.** The profile hands the gold back as an Il2Cpp box, and its managed `ToString()` did not give the number, so every read came out 0. It is now unboxed by its Il2Cpp type (Int32, Int64, Single, Double, else the game's `Convert.ToInt64`). If that still reads 0, the character's own key (`C1Coins` ... `C4Coins`) is tried. The first read logs what the profile returned (`Wallet: gold is stored as ...`).

## 0.15.1 (2026-11-16)

New kobold prices for gold.

### Changed
- **Tonics** cost 400 / 1,000 / 2,000 gold for Minor / Major / Grand (were 200 / 600 / 2,000), the Light Potion too.
- **Enchanting costs a flat 20,000 gold** on any weapon, whatever its rarity and whether it is a bag or an armory weapon (bag weapons were 750 to 6,000 by rarity, armory weapons 500 to 4,000). Taking a perk, the element or Boomerang off costs 10,000. Boomerang costs one enchantment. The price no longer looks at rarity.
- Armor upgrades and bag upgrades are unchanged.

### Removed
- `StockEnchantCostMultiplier` (there is no separate armory price any more); it is removed from settings files that hold it. `EnchantCostMultiplier` scales every enchantment.

### Fixed
- Removing the old settings read `StockEnchantCostMultiplier` as a whole number although it is stored as a decimal.
- A `|` in a player, avatar or item name no longer breaks the loot sync (armor arriving as a weapon, a dropped item lost on its way to the host).
- The TONICS summary shows what REVERT ALL pays back; tonics paid in old tokens are no longer counted as gold.
- The booth reads your gold from the profile once per frame instead of once per row.
- Salvaging an armory weapon that had already left the bag no longer makes the game's next gold payout (run gold, a merchant sale) get refused as "trashed loot".
- Armor stats, tonics and bomb stats only apply while the mod is active (a private room where everyone runs this same build). In a public room or with a player without it, they stayed on.

## 0.15.0 (2026-11-16)

The kobold moves to the game's own gold. Tokens, the weapon stock and the token payout are gone.

### Changed
- **Gold instead of tokens.** Selling (at the kobold or a bag weapon at the armory), tonics, bag upgrades, enchanting and armor upgrades all use the character's gold (`Coins` in the player profile), read with `PlayerProfile.GetCharacterData` and changed with `IncrementCharacterData` (a negative count for a price, as the game's own fabricator does). The profile is saved after each change. The armory's refusal of salvage gold for bag weapons stands aside while the kobold pays its own price. Prices are unchanged; the numbers that were tokens are now gold. New `Loot/Wallet.cs`.
- **Selling pays half.** `SellMultiplier` defaults to 0.5 (was 1.0). Config layout 14 halves the value in existing files once, whatever it was.
- **Tonics are 5 / 10 / 15%** for Minor / Major / Grand (were 15 / 30 / 50%). Tonics already in a bag are set to the new strength for their tier when the bag loads; prepared tonics use the new strength when drunk. The Light Potion is unchanged.
- **The token panel is the run panel.** It shows THIS RUN only (damage dealt and taken per player, and bites from StayPutVR, which it reads by reflection as before). `Loot/TokenPanel.cs` is `Loot/RunPanel.cs`; its settings are `[LootOverhaul_RunPanel]` `RunPanelEnabled` / `RunPanelSide` / `RunPanelHeight` / `RunPanelAhead` / `RunPanelAngle`, carried over from `[LootOverhaul_TokenPanel]` (either old switch off turns the new one off).
- **`Loot/Tokens.cs` is `Loot/RunTracker.cs`.** It still tracks run start and end, counts bosses and mini-bosses, opens the run panel at the end screen and writes `runs.csv`, but pays nothing. `runs.csv` drops `mult`, `payout`, `applied`, the three token balance columns and the rate and bonus columns, and adds `gold_at_run_start` and `gold_now` (the character's gold when the dungeon loaded and at the end screen). The old file is kept under a dated name.

### Removed
- **The WEAPONS tab and its stock.** Weapons come from the game's vendor; the kobold enchants them. The shop's tabs are TONICS, ENCHANT, ARMOR, RINGS. The bag file's `ShopStock` is dropped on its next save.
- **The token payout** at the end screen, and the token balance. The bag file keeps the old balance in its `Gold` field so it is not lost, but nothing reads it.
- **Settings:** `[LootOverhaul_Tokens]` (`GoldToTokenRate`, `BossKillBonus`, `MinibossKillBonus`, `TokenPlayerBonusPerExtraPlayer`, `TokenChestBonusPerChest`) and `[LootOverhaul_TokenPanel]` are removed from the settings file, and so are `ShopSlots` and `ShopRefreshMinutes`.

### Notes
- A tonic prepared with tokens before 0.15.0 is still drunk on the next run, but REVERT gives no gold back for it.

## 0.14.3 (2026-11-15)

#28 (written as 0.13.9) on top of 0.14.2.

### Added
- **FRIENDS page on the ring tab.** Next to LEFT RING and RIGHT RING: show or hide other players' rings (moved here from the toolbar) and a SIZE for them (`RingsOthersSize`, needs CustomAvatars 0.42.22). RESET on that page puts the size back to x1. With CustomAvatars 0.42.22 your own two rings' settings also reach your friends, so they see your rings as you do.

## 0.14.2 (2026-11-15)

Two new armor stats for bombs. #30 (written as 0.13.8) on top of 0.14.1.

### Added
- **Bomb damage** (head armor) and **bomb radius** (leg armor). They roll on armor like the other stats, with the same ranges (Common 1-5%, Unique 4-8%, Rare 7-14%, Legendary 13-25%) and upgrade ceilings (10 / 16 / 28 / 50%), and show in the ARMOR and upgrade pages. They are never tonics.
- **How it works.** A bomb's blast is the game's area-damage effect, run on every client with the bomb's damage and radius. The mod makes that blast hit harder and reach further on enemies and mimics (the radius also reaches more barrels, crates and crypt lids). Damage to players is unchanged, the thrower included. The explosion looks the same size.
- **Multiplayer.** Enemies take damage on the client that runs them (usually the host), so each player's bomb stats are put on their Photon player while the room is gated, and the host uses the thrower's. The thrower is whoever owned the bomb when it broke. Each boosted blast is logged (`bombs: blast radius ... damage ...`).

## 0.14.1 (2026-11-15)

#29 (written as 0.13.6) on top of 0.14.0.

Fewer tokens per run, and the token panel shows its numbers at once.

### Changed
- **No hazard-level or tier bonus any more.** A run's payout is now gold × `GoldToTokenRate` × (1 + bosses + mini-bosses + extra players + chests). The two bonuses added little meaning and too many tokens. `HazardBonusPerLevel` and `TierBonusPerLevel` are gone from `[LootOverhaul_Tokens]` and are removed from settings files that still hold them. The log still reports the run's tier and hazards, and `runs.csv` keeps its `hazard_bonus` / `tier_bonus` columns (always 0) so old and new rows line up.

### Fixed
- **The token panel was empty until an arrow was pressed.** Its text was laid out while the panel was still hidden. It is now built after it appears, so the numbers show straight away.

## 0.14.0 (2026-11-15)

Boomerang fixes from the first playtest, a trail, and run stats on the token panel.

### Fixed
- **Boomerang stuck in an enemy.** 0.12.0 sent a stuck weapon to the game's auto-holster, which does not take a weapon still in a stab, so it stayed in the enemy. A stab now counts as the top of the flight: a gripping empty hand or a pull frees it with the game's own `DynamicAnchorStab.Break` and calls it back. With no call for 5 s it is freed and sent to the holster. Every fallback frees a stuck weapon first.
- **Boomerang stealing holster grabs.** Any gripping empty hand after the apex summoned the weapon, including a hand reaching for a holster. A hand can now call only if its grip began away from every holster (`VRControllerProps.holsters`, each one's pick-up radius + 12 cm) and has not been near one for 0.4 s, it is in front of the head and no more than 55 cm below it, and the inventory is closed. A pull must also move back level: down to the hip or up over the shoulder is a holster grab.
- **Long throws.** The longest return is 25 m (was 15 m). The first playtest fell back to the holster five times at 16-20 m.

### Added
- **Boomerang trail.** A glowing blue ribbon follows the weapon out and back, then fades. Modded friends draw it too (new event 152, the prop's view ID).
- **THIS RUN on the token panel.** Damage dealt, damage taken and (with StayPutVR) bites received, for every player. Each client counts only itself: its own hits from `AI.Net_Damage` with its own actor number, its own damage from `AvatarPlayer.OnDamaged` on the local avatar, both capped at the health left so overkill does not count. It shares the totals as the player property `lo.run` while the gate is open, and the panel redraws when friends' numbers arrive. Bites come from StayPutVR's `BiteSense.Received`, read by reflection, so neither mod needs the other. `TokenPanelShowStats` (default on) in `[LootOverhaul_TokenPanel]`.

## 0.13.5 (2026-11-15)

Less glow at the kobold and in the bag. (0.13.4 is the token panel, a separate change.)

### Changed
- **No white blocks.** A selected tab or button (and a locked padlock) is a blue plate with white text instead of a white plate with black text, which was blinding on an Index.
- **Less glow.** Frames glow at 0.6 (was 1.5) and lit plates at 0.55 of their colour (was 1.0). The armor upgrade bar fills blue instead of near-white.

## 0.13.4 (2026-11-15)

The run payout gets its own panel in the end room; the toast is gone.

### Added
- **Token panel.** After a run, a panel stands in front of you, a little to the right and turned to face you, while the game's gold and XP screen is up: the tokens earned in big gold numbers, then the gold, tokens per gold, each bonus that applied (bosses, mini-bosses, hazard level, tier, party, chests), the multiplier and your new token balance. Same look as the kobold stall and the bag. Only you see your own panel.
- **Arrows to place it.** Left, right, up, down, NEARER, FARTHER (0.1 m a press) and TURN LEFT / TURN RIGHT (10°), plus RESET. The spot is saved right away in the new `[LootOverhaul_TokenPanel]` section (`TokenPanelSide`, `TokenPanelHeight`, `TokenPanelAhead`, `TokenPanelAngle`), measured from your eyes looking toward the end screen's total gold, so the panel comes back to the same place after every run. `TokenPanelEnabled = false` turns it off.
- **Out of walls.** Before it shows, the mod casts from your eyes to the panel's centre and edges; a wall or the end screen in the way pulls the panel in front of it (15 cm clear, never nearer than 0.5 m). It closes with the end screen, with its CLOSE button, or when the scene changes. If no end screen can be found within a few seconds, it stands where you look instead (logged).

### Removed
- **The payout toast and `TokenToastSeconds`** (0.12.1). The game's notifications vanish after a moment whatever the mod asks, and reposting them only made the message flicker. The setting is removed from settings files that still hold it. The payout itself is unchanged and still written to the log.

## 0.13.3 (2026-11-15)

Smaller hazard and tier token bonuses.

### Changed
- **`HazardBonusPerLevel` 0.25 → 0.20** and **`TierBonusPerLevel` 0.1 → 0.05**, in `[LootOverhaul_Tokens]`. A hazard level now adds +20% to the run payout instead of +25%, and each tier above the first +5% instead of +10%. A Tier 7 run with one hazard level goes from ×1.85 to ×1.50 on these two parts.
- **Config layout 13.** A settings file that still holds the old defaults (0.25 / 0.1) gets the new ones on first start; a value you set yourself is kept. Payout settings are local to each player and not part of the room gate, so a friend on other values only gets a different payout.

## 0.13.2 (2026-11-15)

Fixes from the first headset playtest of the 0.13.0 look, and a third perk that never worked.

### Fixed
- **A Legendary weapon's third perk did nothing.** The game's weapon holds two perks: `Weapon.ApplyManualParameters` stores perkA and perkB and drops perkC, and `GetStatsText` lists two. A third perk added at the table was paid for, saved, and offered under TAKE OFF, but never shown in the weapon's stats and never applied. Legendary weapons now have 2 perk slots like Unique and Rare. A third perk already saved is shown grey with "(no effect)" on the enchant page; its TAKE OFF button is green and free (no tokens back). Taking off one of the two working perks still moves the third one up, where it works.
- **The ARMOR page pointer.** Hit boxes were the game's button scaled by a guessed button size, so where the game's collider is bigger than that guess, a box reached past its plate into the buttons next to it. On the ARMOR page (WEAR and SELL 0.02 m apart, the arrows just above them) the laser hopped between overlapping boxes every frame, played the hover sound over and over and stuck to the middle of the panel; the same overlap was the 0.10.4 WEAR / SELL flicker. Box colliders are now set to 90% of the drawn plate, so two buttons a gap apart never touch. The first hit box logs the collider's old size.
- **Weapon names in rarity colours.** The game colours Rare and Legendary weapon names in the same dark blues as old armor, which read as dark text on the panel. Weapon names now use the panel's rarity colours, like armor.
- **Tonic prices** are centred under the MINOR / MAJOR / GRAND buttons, and a row's text runs up to the REVERT column only on a prepared row, so the effect is no longer cut off. The effect and the three tier values are a line each.
- **SELL ALL** goes back to the first page after a sweep instead of staying on a page that may now hold one row.
- **Centred labels sat half their width too far left**: the WEAPON / ARMOR card titles at the card's edge, "none" under SELL ALL off its button, "locked, can't be sold" over the SELL button on the ARMOR page, the page number on top of the < button, and the CURRENT / NEXT / MAX values. `UiKit.Label` now centres them in their box.
- **Armor upgrade page:** MAX no longer shows less than CURRENT on a piece rolled under the older, higher caps, and the piece's name runs up to "sells for" instead of cutting off its rarity.
- **Only perks a weapon can roll.** ADD offers only the perks the game's own generator can roll on that weapon type (its four rarity tables in `WeaponFactory`, read once per type and written to the log), so no more Criticals on a shield. Perks already on a weapon stay.
- **Enchant weapon list:** each row says "slots 2 / 2 · fire" on one line ("price per enchantment" is gone; the price column says it).

### Changed
- **Taking something off costs half of adding it** to that weapon: 375 / 750 / 1,500 / 3,000 tokens for a bag weapon, 250 / 500 / 1,000 / 2,000 for an armory weapon (Common to Legendary). Was 200 / 400 / 800 / 1,600 for both. The same for the element and Boomerang. `DisenchantCostMultiplier` now multiplies that half.
- **The enchant page shows the weapon like the armor page:** the name in its rarity colour with rarity and type in grey after it, then damage, element (in its colour) and each perk by name, instead of the game's own stats text.
- **Less text in the bag and on SELL, and it fits.** The status line under the bag's tabs is one short line ("Wearing 3 / 3 armor · 2 tonics this run") instead of every worn piece's full name and every tonic's effect. Stats are one line per row. Names and stats shrink down to the 0.30 floor before anything is cut, and what still does not fit ends in "…" (it was cut mid-word). EQUIPPED, WORN and ARMORY sit under the type tag instead of in front of the stats, so names and stats get the full width. LOCKED is gone (the padlock on SELL says it; DROP refuses a locked item with a message).
- **Armor stats show in full on SELL and in the bag.** An armor row puts its slot word under the icon instead of a type-tag column, starts its text right after the icon, and gives the stats two lines split between stats ("weapon dmg +28% · leap +26%" over "run speed +26%"), so three stats fit at the 0.30 floor or larger. WORN leads the name.
- **Prices are never cut** ("60,00" for the 60,000 bag upgrade): a number shrinks to the floor and, if still too wide, runs left with the coin in front of it.
- **The stall's panels are 15 cm higher** (centres at 1.55 m, was 1.4; the sign moves with them). Since 0.13.0 made them taller, the pager and the bag row sat behind the counter.
- **SELL ALL buttons are compact** with a smaller label, the total centred under each.

## 0.13.1 (2026-11-15)

The run payout message stays up long enough to read. This is #20 (written as 0.12.1) on top of 0.13.0.

### Changed
- **`TokenToastSeconds` is a setting again**, in `[LootOverhaul_Tokens]`, and defaults to 30 seconds instead of a fixed 5. The "+N tokens (gold x mult)" message after a run used to vanish while the end screen was still up. It is still reposted every 2 seconds, because the game's notifications ignore their own display time. 0 shows it once for the game's own short time; values above 300 are treated as 300. Display only: it does not change the payout and is not part of the room gate.

## 0.13.0 (2026-11-15)

One look for the whole kobold stall and the bag, made to be read in a headset. Prices, rules, what each button does, saved data and network messages are unchanged.

### Changed
- **One style, in one place.** The new `Loot/UiStyle.cs` holds the text sizes, colours and panel grid; the shared components are in `UiKit.cs`. `Booth.cs` and `BagPanel.cs` set no colour, size or button scale of their own.
- **Four text sizes**, all larger: Title 0.56, Row 0.46, Label 0.42 (buttons), Body 0.36, plus the sign at 0.9. Auto-fit shrinks to 80% at most and never below 0.30 (was down to 60%, so the SELL stats could end up at 0.16). Stats wrap to a second line before they shrink.
- **One button.** A lit plate with a frame and a bold label, with the game's button invisible on top for the pointer. The plate lights up while the pointer is on it. Blue for buy, sell, wear, select and add; green for SELL ALL; grey for back, restock, sort and take off; red for undo and loss (drop, revert, take a perk off, reset). A button you can't use is dimmed with the same label; the reason is in the price (red) or a status word, never in the label ("NEED 120 tk" and "MINOR locked" are gone).
- **Tabs are a tab strip**, with the open tab filled white: the main tabs, the armor slots, LEFT RING / RIGHT RING, BAG / ARMORY on the enchant list, and the bag's sort. The cogwheel is now the RINGS tab.
- **Same grid on every page and on the bag:** header with the token balance always top right, tab strip, a toolbar row, rows 0.14 m apart (0.105 for help, compare and ring rows), and the pager in the same place, hidden when there is one page. Panels are 1.26 m tall (was 1.20); the stall stays where it is.
- **Prices** are a gold coin (drawn, no font glyph) right before the number, with a thousands separator, in a price column; red when you can't afford it. In sentences and toasts the unit is always "tokens" ("T" and "tk" are gone).
- **Status words** (LOCKED, EQUIPPED, WORN, ARMORY) are small grey capitals in front of the stats.
- **SELL:** name over stats like every other list. SELL ALL is four green buttons at the top with the total each one pays under it. The bag upgrade is a row lined up with the price and button columns.
- **TONICS:** six per page (was four). Tier buttons say only MINOR / MAJOR / GRAND, with the price under each; the prepared tier is the white button with PREPARED under it.
- **ENCHANT, one weapon:** the weapon and all its stats on top, then ADD, BOOMERANG and TAKE OFF, each with the price per change on the right. Perk buttons are four to a row and no longer touch.
- **Armor upgrading:** an UPGRADE button with the price beside it replaces the drawn arrow.
- **RINGS:** values in one column; the + and - steppers are drawn from bars.
- **Bag:** DROP is always the small red button on the right, dimmed with a status word for an equipped weapon or worn armor.
- **Colours:** one meaning each, gold for tokens only. Rare and Legendary armor names are lighter (they were dark blue on dark blue), also for armor rolled earlier; valuable junk is orange instead of gold. Weapon name colours come from the game and do not change.
- **Floor labels** over dropped loot are 0.36 and bold (were 0.32, with the caption at 0.22).
- **Wording:** MENU is BACK, "item(s)" is "1 item" / "3 items", and the empty pages say "Your bag is empty.", "Sold out. New stock in 12 min.", "No weapons in your bag." and "Unlock exosuit perks to buy tonics for them."

## 0.12.0 (2026-11-14)

Enchant your armory weapons, take enchantments off again, and Boomerang for throwing weapons.

### Added
- **ARMORY at the enchanting table.** The ENCHANT weapon list has an ARMORY button. It lists your armory weapons (generated ones only; no mythics), marked with the slot they are equipped in. Each can be enchanted like a bag weapon:
  - The first change binds the weapon. A bag record is made with its own GUID. The record is always locked (no sell, drop, trash or salvage, and no unlock) and weighs nothing.
  - While the record exists, the armory copy is hidden from the game's gear list. An equipped weapon keeps its slot and is swapped in hand.
  - Nothing is written to the profile.
- **De-enchanting.** The weapon page has a REMOVE row with one button per perk (rolled or added) and one for the element. It works on bag and armory weapons. An armory weapon taken back to exactly what it had goes back to the armory: the record is removed, the armory weapon shows again, and an equipped slot gets it back in hand. If the armory no longer has the original, the record stays as ordinary loot.
- **Boomerang**, for axe, dagger, spear, hammer, long axe, and a sword with Spire. It does not use a perk slot.
  - **Price.** One enchantment on that weapon × `BoomerangCostMultiplier`. Removing it costs the de-enchant price.
  - **Triggers.** A thrown weapon flies back to an empty hand:
    - at the top of its flight, or when it hits something and stops, if that hand is gripping;
    - or when an empty hand is pulled back sharply while gripping.
  - **The return** is the game's own force grab. Hold grip to catch.
  - **Fallback.** When it cannot come back to the hand, the game's own auto-holster returns it to its holster: stuck, both hands full, over 15 m away, the force grab refused, grip let go, or the player dead.
    - In flat-screen mode the game's own pick-up returns it to the hand.
    - With no trigger, the usual 1.5 s return to the holster is untouched.
  - **No duplicates and no lost weapons.** Nothing is spawned or destroyed, and a weapon destroyed or taken over in flight is left alone.
  - **Where it works.** Gated like everything else. Only weapons in your loadout are tracked.
  - **Settings.** New section `[LootOverhaul_Boomerang]`: `BoomerangTrigger` (both / apex / pull / off) and `BoomerangPullSpeed` (1.5 m/s).
  - **Log lines.** Each one starts with `Boomerang:`: the throw, apex or stop, the return and which hand, the catch, every fallback and its reason, and flights dropped on a scene change or gate close.
- **Settings** in `[LootOverhaul_Shop]`: `StockEnchantCostMultiplier` (1.0), `DisenchantCostMultiplier` (1.0) and `BoomerangCostMultiplier` (1.0).
- **Log lines** for the playtest. Each one starts with `Enchanting:`: every enchant and de-enchant with the price paid and the new balance, every binding ("stock, now bound"), every return ("back to stock"), the fallback when the original is gone, and each armory weapon left off the table and why.

### Prices and the economy behind them
Tokens now come only from chest armor sold at the kobold and from the end-of-run payout (enemy drops are off since 0.11.0). Defaults:
- **Payout.** Gold × 0.5 × (1 + 0.5 per boss + 0.1 per mini-boss + 0.25 per hazard level + 0.1 per tier above the first + 0.5 per extra player + 0.02 per chest).
- **Armor.** Chests drop armor 10% (small, Common), 25% (medium, Unique), 100% (epic, Rare) and 100% (boss, Legendary). Armor sells for about 40 / 144 / 456 / 1,700 tokens.
- **A typical solo mid-tier run (estimate).** About 600 gold, 1 boss, 2 mini-bosses, tier 5, no hazard and about 10 chests:
  - The payout multiplier is 2.3, so the payout is about 690 tokens.
  - Selling the armor except the Legendary adds about 590 tokens.
  - That makes about **1,300 tokens a run**, or about 3,000 if the Legendary is sold too.
  - The gold figure is not measured: no run log was available. `RunLogging` writes one to check it against.
- **Chosen prices** (all × `ShopPriceMultiplier` 2.5):
  - An armory enchantment costs **500 / 1,000 / 2,000 / 4,000** (Common to Legendary). That is about half a run to three runs. It is two thirds of the bag price (750 / 1,500 / 3,000 / 6,000, unchanged), because an armory weapon was earned already and cannot be sold.
  - Removing a perk or element costs **200 / 400 / 800 / 1,600**. That is about 40% of an armory enchantment: undoing a mistake should cost, but less than making it.

## 0.11.3 (2026-11-14)

### Changed
- The ring page's SIZE goes from x0.05 to x10 (was x0.5 to x4). At a playtest a left ring started far too big and could not be shrunk enough. CustomAvatars 0.42.19 (#18) fixes the starting size and accepts the same range; an older CustomAvatars still holds the ring to 0.5–4 whatever the page shows.

## 0.11.2 (2026-11-14)

A cogwheel at the kobold, for the ring settings of CustomAvatars.

### Added
- **A cogwheel button** at the right end of the kobold's tab row, smaller than the shop tabs. It opens the ring settings of CustomAvatars for your own avatar:
  - LEFT RING / RIGHT RING picks which ring the page edits.
  - SHOW … RING turns that ring on or off. Rings start off.
  - SIZE, UP/DOWN, SLIDE, TILT, TURN and ROLL each have large `--` `-` `+` `++` buttons. These are drawn by the mod, because the game's buttons left "+" and "-" too small to read.
  - RESET puts the selected ring's size, position and angles back.
  - "friends' rings" SHOW/HIDE switches other players' rings on or off.
- The values are written straight into the `[CustomAvatars]` section of MelonPreferences.cfg and saved at once. CustomAvatars (0.42.14 or later) re-reads them twice a second, so the ring moves as you press. They are local: each player sets their own, and nothing is sent. LootOverhaul finds the settings by name, so neither mod needs the other. Without CustomAvatars the page says so.

## 0.11.1 (2026-11-13)

An escaped loot goblin no longer drops armor.

### Fixed
- **No pile from an escaped goblin.** The game removes a goblin that runs off with the same death event as a kill (damage type `Other`), so the goblin pile dropped where it vanished. The pile is now skipped when the goblin escaped, and the log says "goblin escaped, no pile".

## 0.11.0 (2026-11-13)

Far fewer settings, armor from every perk, and only armor drops from the mod.

### Changed
- **Settings are grouped and trimmed.** About 90 settings in one flat `[LootOverhaul]` list became 34 player settings in their own sections: `[LootOverhaul]` (the master switch), `[LootOverhaul_ChestArmor]`, `[LootOverhaul_Goblin]`, `[LootOverhaul_FloorLoot]`, `[LootOverhaul_Bag]`, `[LootOverhaul_Shop]`, `[LootOverhaul_Tokens]`, plus `[LootOverhaul_Booth]` (set by the = key) and a small `[LootOverhaul_Dev]`. Every setting has a plain description. Fine tuning that nobody needs to change (chest drop distance, delay, spread and stagger, pickup radius, bag auto-close distance, gesture hold time, payout toast timing, pedestal frame colour) is now fixed in code at the old defaults, and the floor outline, armor tint, panel laser, pedestal frame and toss-trinkets-when-full are always on.
- **Merged switches.** `DropLabels` is now `off`, `hover` or `always` (was `DropLabels` + `DropLabelsOnHover`). `DropSound` is `start`, `finish`, `collected` or `off` (was `DropSounds` + `DropChime`). `AutoPickup` covers tonics, trinkets and armor (was `JunkAutoPickup` + `AutoPickupArmor`). In `[LootOverhaul_Dev]`, `DevTools` replaces `ReconEnabled`, `ProfileWatchEnabled` and `HotkeysEnabled`, and `VerboseLogging` also covers the old console mirror. The player hotkeys ([ ] =) always work.
- **New defaults:** floor labels off, drop sound "finish", auto pickup off, goblin pile of 2 at size 1.1, dev tools and logging off.
- **One-time migration.** The first start of 0.11.0 moves every value you changed from its 0.10.10 default into its new section, works out the merged switches from the old pairs, removes the keys that no longer exist, and logs how many values it carried. Values you never changed are left behind, so the new defaults apply. Nothing you tuned is lost. A settings file already migrated by an earlier 0.11.0 build gets the new defaults back where it still holds an old default (goblin pile of 3 at size 1.5, labels on hover, drop sound "start", auto pickup on, dev tools on).
- **Armor rolls every exosuit perk.** A piece no longer needs the perk unlocked in the exosuit. Each stat is measured against what its perk gives at the top level in the game: a drop rolls 1-5% (Common), 4-8% (Unique), 7-14% (Rare) or 13-25% (Legendary) of that, and upgrades at the kobold can take a stat to twice its rarity's best drop, so only a Legendary reaches 50% of the perk's top level. Power, for example, is +50% at level 5, so a Legendary Power stat drops at +7 to +13% and upgrades to +25%.
- **Whole percents.** Armor stats are whole percents and show as +16% or -12% everywhere (were ×1.16 and ÷1.14). Every upgrade step is +1% and costs the same per rarity (750 / 1,500 / 3,000 / 6,000 tokens at the defaults). Pieces you already own keep their values, shown rounded; a piece above its new maximum cannot be upgraded further.
- **The loot goblin drops armor only.** Its pile no longer contains weapons; the first piece is at least `GoblinGuaranteedClass` and the rest roll Common 70 / Unique 22 / Rare 7 / Legendary 1.

### Removed
- **Enemy weapon drops, boss and mini-boss piles, Elite and Legend drops, the Legendary pity counter, and junk drops**, with their settings (`EnemyDropsEnabled`, `BaseDropChance`, the `Boss*` / `MiniBoss*` / `Elite*` / `Legend*` piles, `LegendaryPityKills`, `DropChancePerExtraPlayer`, the four rarity weights, `TierUpChance`, `ArmorShare`, `JunkDropChance`, `SandboxDrops`, `GoblinJunk`, `GoblinJunkRolls`). Enemy drops were already off by default since 0.10.0. Trinkets already in a bag can still be sold.

## 0.10.10 (2026-11-13)

Tonics are ordered for the next run instead of carried in the bag, and they cost more.

### Changed
- **Tonics are prepared, not bagged.** Buying a tonic at the kobold no longer puts a bottle in the bag. It is prepared for your next run, and every prepared tonic is drunk on its own when the next dungeon starts, with a short toast ("Tonics active: Grand Light Potion, Major Bruiser's Brew"). Nothing is drunk in the lobby, the practice arena or the tutorial. Prepared tonics are saved with the bag, so they survive a game restart; if the next room is not modded they stay prepared for the run after.
- **One tonic per kind, the strongest wins.** Only one strength of each tonic can be prepared. Tonics never multiply with each other: a stat takes the strongest tonic active on it. Drinking a bag tonic (bottles from earlier versions, or a Minor Light Potion found in a crypt) that is not stronger than what is already active now leaves the bottle in the bag instead of using it up for nothing.
- **Booth TONICS tab.** A prepared tonic's row is lit green and its strength shows as a framed "PREPARED" plate; the other strengths of that kind are locked. REVERT on the row gives back everything that was paid for it and unlocks the row; REVERT ALL in the header takes back every prepared tonic. The header shows how many are prepared and what they cost.
- **Tonic prices.** Minor / Major / Grand now cost 80 / 240 / 800 × `ShopPriceMultiplier`, which is 200 / 600 / 2,000 tokens at the default 2.5 (were 150 / 400 / 1,000). The Light Potion costs the same (was 200 / 500 / 1,125). Token payouts are unchanged.

### Fixed
- **An armor upgrade cut short by the cap costs only its share.** When the last step of a stat is clipped by the rarity's maximum (1.275 → 1.28 is half a step), it used to cost the full step; it now costs half, and the booth shows that price.

## 0.10.9 (2026-11-13)

No more junk from chests and the loot goblin. Weapons and armor drop exactly as before.

### Changed
- **The loot goblin drops weapons and armor only.** It used to leave `GoblinJunkRolls` trinkets on every pile, and at least one even with `GoblinJunkRolls = 0`. The trinkets now have their own switch, `GoblinJunk` (default off); with it on, `GoblinJunkRolls` trinkets land on each pile and 0 really means none.
- **Chest armor no longer looks like junk.** Chests never dropped junk, but their armor lies on the floor as a small reliquary chest, the same body as the "Reliquary Chest" artifact, and below Rare it has no beam. Armor on the floor is now tinted in its rarity colour (`ArmorTint`, default on), and the Reliquary Chest artifact is gone from the junk table, so that body always means armor. Reliquaries already in a bag stay as they are.

## 0.10.8 (2026-11-11)

Performance only: nothing about what drops, what it is worth or how it plays has changed.

### Changed
- **The recon transcript is written to disk once a second** instead of after every line. Recon stays on by default and logs exactly what it did before; only the per-line disk write on the game's main thread is gone. At most the last second is missing after a hard crash. The PlayFab write watchdog now describes a call's arguments only when it writes the line, not on every profile write.
- **The bag file is saved at most every half second.** Every change used to rewrite the whole inventory file at once: each bagged item, each kill (the legendary pity counter), each trinket tossed to make room. Changes are now collected and written together half a second later, and at once on scene change, account change and quit.
- **One armory rebuild per frame.** Bagging, buying, enchanting or selling several weapons in a row (SELL ALL) rebuilt the game's armory lists once per weapon; all of them now share one rebuild on the next frame.
- **Boss and goblin piles appear over about a second** instead of all in one frame (a goblin in a four-player room is up to two dozen pieces). The pieces are rolled at the kill exactly as before; only their spawning is spread 0.06 s apart. Queued pieces are dropped on scene change.
- **Tossing trinkets to make room rebuilds the bag panel once,** not once per trinket.
- **Opening the bag writes its contents to the transcript only when something changed** since the last time.

### Fixed
- **Bag, booth and floor-loot materials no longer leak.** Every panel rebuild made new copies of its frame, bar and backdrop materials that were never freed; tinted elements now share one material per colour. A drop beam's material is freed with the beam.
- Smaller per-call costs: bag weapons' GUIDs are normalised once instead of on every armory display-name call, the retired-weapon check is a set lookup instead of a scan of up to 500 entries, and the roster poll and junk walk-over pickup reuse their buffers.

## 0.10.7 (2026-11-06)

### Changed
- **Chest armor scales with the number of players.** A chest used to roll one armor piece for the whole room. It now rolls once for every player in the room, each roll on its own at the full chance: two players at an epic chest give two Rare pieces, and a small chest makes two separate 10% rolls (so 0, 1 or 2 pieces). The pieces are shared floor loot as before: everyone sees all of them and anyone can pick any of them up. Several pieces fan out in front of the chest (`ChestDropSpread`, default 35° apart) and appear a moment apart (`ChestDropStagger`, default 0.35 s) so they do not stack or collide. `ChestArmorScalesWithPlayers = false` brings back one roll per chest.
- **The loot goblin drops again, and its pile scales with the number of players.** Its pile was switched off together with all enemy drops (`EnemyDropsEnabled = false`). It now has its own switch, `GoblinDropsEnabled` (default on), and drops its whole pile (`GoblinDrops` pieces, the first at least `GoblinGuaranteedClass`, plus `GoblinJunkRolls` trinkets) once for every player in the room, each pile rolled on its own and all of them spread around one circle, shared like any floor loot. Other enemies still drop nothing. `GoblinDropsScaleWithPlayers = false` brings back one pile.

### Added
- `DropPlayersOverride` (dev section, default 0): counts that many players for the chest and goblin rolls that scale with the party, to test them alone.

## 0.10.6 (2026-11-02)

### Fixed
- **A weapon bought from the kobold shows up in the armory at once.** It used to sit in the bag but stay out of the game's own inventory, so it could not be equipped until the next lobby. The purchase now rebuilds the armory lists the way a pickup or an enchant already did. An armory that is open at that moment is rebuilt as soon as it closes (this covers pickups and enchants too).

## 0.10.5 (2026-10-13)

### Changed
- **Chest armor appears after a short delay.** The piece used to appear the moment the chest opened, right under the hand of whoever turned the key, and was grabbed by accident. It now appears `ChestArmorDelay` seconds later (default 2.0; 0 = at once), tossed out the same way. A delayed piece is dropped if the scene changes or the room closes before it appears.

## 0.10.4 (2026-10-08)

### Changed
- **Armor has one perk, a Legendary two.** Common, Unique and Rare pieces roll one stat, a Legendary two. The strength bands are unchanged. Rolls are now whole hundredths (×1.12, not ×1.123) so every upgrade step lands cleanly.
- **Stats follow the slot.** Head is offense (weapon damage, crits, knockback, throw range, shot range), chest is defense (melee, arrows, magic, fire and poison taken, freeze time, potion heal, regeneration, standing-still regeneration, self-effects, enemy aim), legs are utility (run speed, jump, leap, fall damage, run coins). No stat is on two slots, so worn pieces can no longer stack the same stat. Armor already in a bag keeps its old stats.
- **Five stats retired.** Axe/spear damage, pierce damage, crossbow/staff damage (general weapon damage covers them), impales and stamina drain are no longer rolled on armor or sold as tonics. Tonics and armor already in a bag keep working.

### Added
- **ENCHANT now opens on a menu: WEAPON or ARMOR.** WEAPON is the table as it was (a MENU button leads back). ARMOR shows the piece worn on head, chest or legs (name and rarity) and a card per stat: current, next and max as +xx%, a slim bar of the rarity's range with "5/13 Upgrades", the price and an up-arrow button that raises that one stat by 0.01. A stat at its maximum shows MAXED.
- **A fixed price per upgrade step**, set by the piece's rarity and the same for every step and every stat: 750 Common, 1,500 Unique, 3,000 Rare, 6,000 Legendary at the defaults (the enchanting base price × ShopPriceMultiplier). It never rises with the number of upgrades. A luxury and a token sink. Setting: `ArmorUpgradeCostMultiplier` (default 1.0).
- **Upgrading never changes the sell price**; the item's value is not touched.

### Fixed
- **WEAR and SELL flickered on the armor page.** The two buttons sat so close that their glow edges touched, and the pointer flipped between them. They are now slightly smaller with a clear gap between them.
- Review fixes from 0.10.2 are included: a stat only one piece has counts from the neutral 1.0 in the armor comparison, the shop button shows the missing price, and the enchant price colour matches.

## 0.10.3 (2026-10-08)

### Changed
Armor now comes from chests. Enemies still drop nothing.
- **Armor drops when a chest opens.** The moment the chest opens (the key turning, not the A press on the loot) the host rolls once for that chest and the piece lands on the floor as a normal item, just in front of the chest (a gentle toss; `ChestArmorDistance` sets the start point, default 0.2 m). A chest can never roll twice, however many players are in the room.
- **Chest size sets chance and rarity.** Small 10% Common, medium 25% Unique, epic 100% Rare, boss chest 100% Legendary. Seasonal variants follow their size. Settings: `ChestArmorEnabled`, `ChestArmorSmall`, `ChestArmorMedium`, `ChestArmorEpic`, `ChestArmorBoss`.
- **Mimics drop armor too**, as a chest of their size (medium or epic), in front of them.
- **Never from:** the crypt (realm Crypts or a dark dungeon, and any chest holding rings or crypt loot), the sandbox, and tutorial, respawner or soul-harvest chests.
- The chest-open hook now runs even with recon off (it used to be recon only).
- **A chest can roll again in a later run.** The list of chests that already rolled is cleared on every scene change (it lived for the whole session, so a reused chest object would never have dropped again).
- The README title and the source index show the current version (they still said 0.9.20).

## 0.10.2 (2026-10-08)

### Changed
The Kobold Traveler booth and the bag panel are easier to read and steer (report: tiny text, cramped rows).
- **Bigger and roomier.** Panels are wider and scaled up, rows are taller, names, stats and headers use larger type, and grey text is lighter.
- **Price column.** The price sits in its own right-aligned column beside the button (red when you cannot afford it), instead of at the end of the name. Buy buttons say `BUY` or `NEED MORE`.
- **One state label.** On the sell counter, equipped / worn / locked shows once, in the button column.
- **Striped rows.** Every other row has a lit stripe so the eye can follow a row from name to button.
- **Clear tabs.** The open shop tab has a gold underline and a divider under the tab row.
- The bag panel got the same width, type sizes and stripes, and opens slightly larger.
- **Price on the name line.** The stats line under each weapon has the full row width, so long perk lists are no longer cut off.
- **Every booth tab matches.** Tonics, enchant, enchant help and armor use the same larger type, lighter grey text and striped rows. The help page has more room between entries, and the sell counter's footer text is larger.
- **Armor page rebuilt as a comparison table.** One compact row for head, chest and legs (each with the piece worn). Below, three columns, one stat per row: what you wear, the bag piece, and what a swap changes (only the green or red number, e.g. `+0.04`, `-0.15`; a stat only one piece has counts from the neutral 1.0). `TAKE OFF` sits under the worn piece; `WEAR`, `SELL` and the `<` `>` pager belong to the bag piece, so you can sell the piece you just compared (`SELL` is greyed when it is locked).
- **Type tag between icon and name.** Item rows (bag and booth) show the type and tier centred in two small lines between the icon and the name (Sword over T7), so the stats line starts with the numbers that matter.
- **Price centred** in its own column on the sell list and the weapon shop.
- **Sell list shows one stat per line** (`44 Fire Damage`, `Elite Damage`, no bullets) next to the name, in slightly smaller type.
- **Padlock instead of a LOCK button.** Locking a sell-counter item is a small padlock icon (gold and closed when locked, grey and open when not).
- **No more pop-up messages** when you buy, sell, lock, unlock, wear, take off or enchant (warnings and errors still show).
- **Fixed:** unlocking an armor piece on the sell list now updates the armor page right away (it kept showing locked until you changed page).
- **Fixed:** the `equipped` / `worn` / `locked` text on the sell counter sat under the lock button; it is now centred on the sell column.

## 0.10.1 (2026-10-08)

### Performance
Fewer checks per frame, aimed at the stutter in full 4-player parties when enemies spawn or die
and when a lot of loot lies on the floor. Nothing a player sees or plays changes.
- **The co-op gate re-checks only when the roster refreshes** (twice a second) instead of every
  frame. Between refreshes a cheap live check still fails closed at once: room went public, room
  left, or a player the roster has not vetted yet.
- **Network events are filtered by code first.** Every Photon event from every player used to have
  its sender and payload read; now only events in our own block 150–159 do. The recon tally still
  counts every code.
- **Floor loot does less work per item.** The player's head and hand are read once per frame
  instead of once per label; items farther than 15 m have their label, sparkle and glow moved four
  times a second instead of every frame; an object that has not replicated yet is looked up four
  times a second instead of every frame.
- **Settings are parsed once.** `BagGesture` and `DropChime` were trimmed and lower-cased on every
  read, and the bag gesture read the input system's type name every frame for a one-time log line.

## 0.10.0 (2026-10-07)

### Changed
- **The drop roll is off by default.** `EnemyDropsEnabled` now defaults to `false`: nothing spawns on
  an enemy death and nothing past the early return in `DropRoller.OnKilled` is calculated. The drop
  system itself is **untouched** — its 25 settings, the boss and mini-boss piles, elites and legends,
  the loot goblin's pile, party scaling, the legendary pity counter and the rarity weights are all
  still there, and `EnemyDropsEnabled = true` brings the behaviour back exactly as it was. It is off
  because playtesting reported stutter that grew with more piles, more enemies and more players,
  alongside a floor full of items to pick up — reported, not measured; the setting is one line away
  from back on.
- **The end screen pays tokens.** The gold the end screen already reports is converted with
  `GoldToTokenRate` and the bonuses in `[LootOverhaul]` (bosses, mini-bosses, hazards, tier, extra
  players, chests). Bonuses only ever add. A completed run and a wipe both pay, because both report
  gold. Weapons now come from tokens and the Shopkeeper as well as from drops.
- **No death malus.** Deaths and last stands are still *recorded* — per player in the end-screen
  breakdown and in `runs.csv` — but nothing is charged for them. The run's own gold already reflects
  how it went: a wipe, or dying early, costs the quest reward, and charging deaths on top would take
  the same failure out of the payout twice. It also cannot work in solo, where a death is a last
  stand the game files under `revives` rather than `deaths`. `DeathMalusPerDeath`, `DownMalusPerRevive`
  and `MinPayoutFraction` are gone.
- **`TokenPlayerBonusPerExtraPlayer` is now 0.5** (was 0.25): +50% per party member beyond the first,
  which is what makes the lost last stand worth trading.
- The payout message is reposted on a timer for `TokenToastSeconds`: the game's notifications have a
  short fixed life and ignore their own display-time field, so raising it changed nothing.
- Boss and mini-boss kills are counted in one place: `Recon/GameplayHooks.cs` owns the `AI.OnKilled`
  patch, and one postfix feeds the payout counter, the drop roll and the recon transcript.
  (`Hooks.Patch` only refuses a *different* method that shares a native address, so a second patch
  would have been permitted — one owner simply keeps the three consumers on exactly the same kills.)

- **Upgrades migrate themselves once.** MelonPreferences keeps whatever value is already saved, so a
  changed default never reaches an existing `MelonPreferences.cfg`. A `[LootOverhaul_Dev]
  ConfigVersion` entry drives a one-off migration that sets `EnemyDropsEnabled = false` on an older
  config and bumps to 10.

### Added
- `Loot/Tokens.cs`: the run payout.
- `[LootOverhaul_Dev] RunLogging`, default off: one row per run in `UserData/LootOverhaul/runs.csv`.

## 0.9.20 (2026-10-02)

### Changed
- The Light Potion is local only: the `L|radius` loot message and the copy of the light on other players' heads are gone. Only the drinker sees it.

## 0.9.19 (2026-09-28)

After the 2026-09-27 game update. Untested in headset.

### Fixed
- Armor and tonics did nothing. The exosuit stats became getter-only properties over a four-slot perk table; every write threw `ArgumentException` (`buffs applied (…): Arms_Power: ArgumentException, …`). The multiplier is now a postfix on `Exosuit.GetExosuitStat`, which every stat getter jumps to, for the local exosuit only. The base-value restore and the `ResetAll`/`Update` hooks are gone.
- Run speed: the game computes base × max(1, 0.9 × haste × …); the mod multiplies from at least 1/0.9 so the label's factor is the speed change.
- Keyboard hotkeys through the Input System package (`Keyboard.current`); a failure logs once.
- Enchanting self-test: `GenerateMythicWeaponModuleForLocalPlayer` gained a `randomSeed` parameter (−1 = random, as before).
- Stat descriptions for Stillness, Mystify and Fortune match what the game reads them for.

### Changed
- The bag gesture reads the game's `XRInput` singleton before the SteamVR actions and logs each toggle.

### Added
- Light Potion (TONICS tab, three reaches; a share of crypt trinket drops): a realtime light and a crypt darkness reveal on your head until the lobby, shown on modded peers too (`L` opcode).

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
