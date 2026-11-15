# HazardTweaks 0.1.0

A mod for **Dungeons of Eternity** that changes the dungeon hazards (the modifiers on the
mission board):

- **Keep your own weapons.** Every hazard that takes them away is removed: Bows Only, Axes
  Only, Swords & Shields, Daggers Only, Fireball Staffs Only and Random Weapons.
- **Remove any other hazard you choose**, such as Death, by name in the settings.
- **Bigger rewards.** Every hazard's XP and gold bonus is doubled by default, so Death goes
  from +25% to +50% at tier 7 and the +40% hazards go to +80%.

The mission board, the "+X% Bonus" lines and the end-of-run total all show the new numbers.

## Vanilla hazard bonuses

Read from the game's `HazardData` asset. The bonus grows with the dungeon tier; bonuses of
several hazards add up.

| Group | Hazards | Tier 1 → 7 |
|---|---|---|
| +25% | Death Encounter, Half Power Potions, 1.5x Damage Taken, Bows / Axes / Daggers Only, Swords & Shields, Random Weapons, Miniboss Battle | 5, 7, 10, 13, 17, 21, 25 % |
| +40% | No Potions, No Revives, Enemy Swarms, 2x Damage Taken, Fireball Staffs Only, Boss Battle, Dragon Battle | 8, 12, 17, 22, 28, 34, 40 % |

## Install

Needs MelonLoader 0.7.3 like the other mods here. Copy `HazardTweaks.dll` into the game's
`Mods` folder. **Everyone in the lobby should run it with the same settings:** a dungeon's
hazards are rolled from its seed on every player's machine, so a friend without the mod (or
with other settings) sees and plays a different hazard list and gets a different bonus.

## Settings

`UserData\MelonPreferences.cfg`, section `[HazardTweaks]`. F9 in the game window reloads them;
they apply to the next mission board.

| Setting | Default | |
|---|---|---|
| `Enabled` | `true` | |
| `RemoveWeaponRestrictions` | `true` | Drop every weapon-limiting hazard. |
| `AlsoRemove` | empty | More hazards to drop, comma separated: `Death`, `NoPotions`, `Potions50Pct`, `NoRevives`, `CreatureSwarms`, `Damage150Pct`, `Damage200Pct`, `DragonBattle`, `MiniBossBattle`, `BossBattle`. The log lists every name at startup. |
| `BonusMultiplier` | `2` | Multiplies every hazard's bonus. `1` is vanilla. |
| `PrivateLobbyOnly` | `true` | In a public lobby the game stays vanilla. |
| `VerboseLogging` | `true` | Log each hazard list the mod changes. |

A removed hazard also takes its bonus with it: a dungeon whose only hazard was Bows Only
becomes a plain dungeon with no hazard bonus.

## How it works

`GameManager.GenerateDeterministicHazards` (where a mission gets its hazard list) and
`GameManager.InitHazardsForLevel` (where a run switches them on) both get the list filtered.
The bonus percentages are scaled in place in the `HazardData` asset's per-tier table, with the
originals kept so the multiplier can change or be undone without a restart. Nothing is sent
over the network.

**Your account.** The XP and gold are paid by the game's own end-of-mission code, so a higher
multiplier means more XP and gold saved to your profile than vanilla would give. This mod
never writes to the profile itself.
