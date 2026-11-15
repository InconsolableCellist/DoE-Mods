# HazardTweaks changelog

## 0.1.0 — 2026-11-15 (first build)

- Removes every weapon-limiting hazard (Bows Only, Axes Only, Swords & Shields, Daggers Only,
  Fireball Staffs Only, Random Weapons) from the mission board and from a run.
- `AlsoRemove` drops any other hazard by name, such as `Death`.
- `BonusMultiplier` (default 2) scales the hazard XP/gold bonus table in memory.
- **Lobby gate** (review on PR #22): on only offline, or in a private lobby where every player
  advertises the same version, DLL hash and hazard-settings hash (`ht.ver` / `ht.sha` /
  `ht.cfg` Photon player properties). Anything else stays vanilla and the log says why.
  Re-checked twice a second and whenever a board is rolled. Replaces the earlier
  `PrivateLobbyOnly` setting.
- F9 reloads the settings (and republishes the settings hash).
- README notes that a doubled gold bonus also raises LootOverhaul's end-of-run tokens.

Untested in a headset as of writing.
