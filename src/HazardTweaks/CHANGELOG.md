# HazardTweaks changelog

## 0.1.1 — 2026-11-17

- **The host's settings apply to the whole lobby.** In 0.1.0 every player had to have the
  exact same `RemoveWeaponRestrictions`, `AlsoRemove`, `BonusMultiplier` and `RoundUpTo`, and
  one difference switched the mod off for everyone. At a playtest on 2026-11-17 the guest had
  `BonusMultiplier = 1.0` and the host the default 1.25, so the log said
  `INERT: settings differ from PlasmaFox's` all evening and an Axes Only dungeon came through.
  Now a private lobby only needs every player on the same version and DLL. The host (Photon
  master client) publishes its settings as `ht.rm` (removed hazards by name), `ht.x` and
  `ht.r`, and every player filters hazards and scales the bonus with those. A guest's own
  settings only count offline or when they host. `ht.cfg` is gone.
- The log names whose settings are in force and what they are, for example
  `ACTIVE: private lobby, all 2 players run this build, using PlasmaFox's (host) settings: removing [BowsOnly, AxesOnly, ...], hazard bonus x1.25, rounded up to 1%.`
  It logs again when the host or the host's settings change.
- **The hazard bonus is vanilla by default.** `BonusMultiplier` now defaults to 1. A saved
  1.25 (the 0.1.0 default) is reset to 1 once, the first time 0.1.1 starts (hidden
  `ConfigVersion` = 2). Any other value you set is kept.

## 0.1.0 — 2026-11-15 (first build)

- Removes every weapon-limiting hazard (Bows Only, Axes Only, Swords & Shields, Daggers Only,
  Fireball Staffs Only, Random Weapons) from the mission board and from a run.
- `AlsoRemove` drops any other hazard by name, such as `Death`.
- `BonusMultiplier` (default 1.25, so +25% becomes +32% after rounding) scales the hazard XP/gold bonus table in memory.
- **Lobby gate** (review on PR #22): on only offline, or in a private lobby where every player
  advertises the same version, DLL hash and hazard-settings hash (`ht.ver` / `ht.sha` /
  `ht.cfg` Photon player properties). Anything else stays vanilla and the log says why.
  Re-checked twice a second and whenever a board is rolled. Replaces the earlier
  `PrivateLobbyOnly` setting.
- `RoundUpTo` (default 1) rounds every scaled bonus up to the next whole percent, so the
  board never shows decimals (31.25% -> 32%).
- F9 reloads the settings (and republishes the settings hash).
- README notes that a higher gold bonus also raises LootOverhaul's end-of-run tokens.

Untested in a headset as of writing.
