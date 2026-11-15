---
name: add-content
description: Add or change characters, items, enemies, waves or run rules in the built-in core mod (data-driven JSON). Use when the owner provides or changes content tables.
---

Content lives in `Game/content/core/*.json`; the format is the mod format (docs/MODDING.md → Content). Model: `src/TickHappens.Game/Content/ContentDefs.cs`; validation: `ContentLoader.cs`. The ordered plan, owner decisions and open questions are in `docs/CONTENT_ROADMAP.md` — read it first and tick the status column when an entry is verified.

Owner table → JSON:
- Characters: HP `base/+lvl` → `maxHp`/`maxHpPerLevel`; Move speed → `moveSpeed`; Attack speed → `attackSpeed`; Armor/Magic resist % → `armor`/`magicResist` as fractions (5% → 0.05); Damage `base/+lvl` → `damage`/`damagePerLevel`; passive text → `passive`, «Growth per level» → `growth`; behaviour → `passives` [{stat, add, pct, perLevel{add,pct}, per:"runMinute"}].
- Items: rarity Common/Rare/Epic/Mythic/Legendary → common/rare/epic/mythic/legendary; stat effects → `mods`; attacking items → `weapon` (bolt/pulse/chain/blast) or a new engine kind (S6).
- Enemies: Move speed → `speed` (same units), Lifesteal → `lifesteal`, Magic resist/Armor → fractions, БОСС → `boss: true`.

Rules: items come only from chests and stack; attacks, items, pickups and chests are passive (manual: movement, dash, cursor targeting via Scope later). Gold belongs to the killer. Difficulty follows the run clock, never the player level. Units: px/s = Move speed × 3, attacks/s = Attack speed / 25, defences capped at 90%.

Steps: edit JSON → `build-test` (errors name file, id and field) → `play-capture` with `--demo --character ID` and `--menu` → check the HUD and the character screen. A new stat, trigger or behaviour needs C# (StatBlock/RunWorld) plus tests. A new sprite needs an Assets recipe (`art-recipe` skill), merged in Assets before the Game PR.
