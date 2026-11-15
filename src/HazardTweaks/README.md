# HazardTweaks 0.1.0

A mod for **Dungeons of Eternity** that changes the dungeon hazards (the modifiers on the
mission board):

- **Keep your own weapons.** Every hazard that takes them away is removed: Bows Only, Axes
  Only, Swords & Shields, Daggers Only, Fireball Staffs Only and Random Weapons.
- **Remove any other hazard you choose**, such as Death, by name in the settings.
- **Bigger rewards.** Every hazard's XP and gold bonus is raised by half by default, so Death goes
  from +25% to +40% at tier 7 (37.5%, rounded up to the next 5%) and the +40% hazards go
  to +60%. No decimals: every bonus is rounded up to a multiple of 5%.

| Group | Vanilla, tier 1 → 7 | HazardTweaks default (x1.5, rounded up to 5%) |
|---|---|---|
| +25% hazards | 5, 7, 10, 13, 17, 21, 25 % | 10, 15, 15, 20, 30, 35, 40 % |
| +40% hazards | 8, 12, 17, 22, 28, 34, 40 % | 15, 20, 30, 35, 45, 55, 60 % |

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
`Mods` folder.

## When it is on

A dungeon's hazards are rolled from its seed on every player's machine, so players only agree
on a dungeon's hazards and bonus when all of them run the same build with the same settings.
The mod therefore only changes anything:

- **offline**, or
- in a **private lobby** where **every player** runs the same HazardTweaks version and DLL,
  with the same `RemoveWeaponRestrictions`, `AlsoRemove`, `BonusMultiplier` and `RoundUpTo`.

In a public lobby, or with a friend who has no HazardTweaks, another build or other settings,
the game stays vanilla and the log says why, for example `INERT: Sam has no HazardTweaks` or
`INERT: settings differ from Sam's`. The check runs again twice a second and every time a
mission board is rolled, so a join, a leave or an F9 settings reload is picked up before the
next board. Each player publishes `ht.ver`, `ht.sha` and `ht.cfg` (a hash of those
settings) as Photon player properties, in private lobbies only.

## Settings

`UserData\MelonPreferences.cfg`, section `[HazardTweaks]`. F9 in the game window reloads them;
they apply to the next mission board. Everyone in the lobby needs the same values.

| Setting | Default | |
|---|---|---|
| `Enabled` | `true` | |
| `RemoveWeaponRestrictions` | `true` | Drop every weapon-limiting hazard. |
| `AlsoRemove` | empty | More hazards to drop, comma separated: `Death`, `NoPotions`, `Potions50Pct`, `NoRevives`, `CreatureSwarms`, `Damage150Pct`, `Damage200Pct`, `DragonBattle`, `MiniBossBattle`, `BossBattle`. The log lists every name at startup. |
| `BonusMultiplier` | `1.5` | Multiplies every hazard's bonus. `1` is vanilla, `2` doubles it. |
| `RoundUpTo` | `5` | Rounds every scaled bonus UP to the next multiple of this many percent, so no decimals: `5` turns 37.5% into 40%, `1` into 38%, `0` leaves it. Ignored when `BonusMultiplier` is `1`. |
| `VerboseLogging` | `true` | Log each hazard list the mod changes, and why it left one alone. |

A removed hazard also takes its bonus with it: a dungeon whose only hazard was Bows Only
becomes a plain dungeon with no hazard bonus.

## How it works

`GameManager.GenerateDeterministicHazards` (where a mission gets its hazard list) and
`GameManager.InitHazardsForLevel` (where a run switches them on) both get the list filtered.
The bonus percentages are scaled in place in the `HazardData` asset's per-tier table, with the
originals kept so the multiplier can change or be undone without a restart. Apart from the
three player properties above, nothing is sent over the network.

**Your account.** The XP and gold are paid by the game's own end-of-mission code, so a higher
multiplier means more XP and gold saved to your profile than vanilla would give. This mod
never writes to the profile itself.

**LootOverhaul tokens.** LootOverhaul pays its end-of-run tokens from the run's gold, so a
higher hazard gold bonus also raises the tokens, which LootOverhaul 0.12.0's enchanting prices
were balanced against. `BonusMultiplier = 1` keeps token income vanilla.
