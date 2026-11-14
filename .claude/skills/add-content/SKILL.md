---
name: add-content
description: Add or change characters, items, enemies, waves or run rules in the built-in core mod (data-driven JSON). Use when the owner provides or changes content tables.
---

Content lives in `Game/content/core/*.json`; the format is the mod format (docs/MODDING.md → Content). Model: `src/TickHappens.Game/Content/ContentDefs.cs`; validation: `ContentLoader.cs`. The ordered plan, owner decisions and open questions are in `docs/CONTENT_ROADMAP.md` — read it first and tick the status column when an entry is verified.

Owner table → JSON:
- Characters: ХП `base/+lvl` → `maxHp`/`maxHpPerLevel`; С/П → `moveSpeed`; С/А → `attackSpeed`; Броня/М/З % → `armor`/`magicResist` as fractions (5% → 0.05); Урон `base/+lvl` → `damage`/`damagePerLevel`; passive text → `passive`, «Прирост с уровнем» → `growth`; behaviour → `passives` [{stat, add, pct, perLevel{add,pct}, per:"runMinute"}].
- Items: rarity Обычный/Редкий/Эпический/Мифический/Легендарный → common/rare/epic/mythic/legendary; stat effects → `mods`; attacking items → `weapon` (bolt/pulse/chain/blast) or a new engine kind (S6).
- Enemies: С/П → `speed` (same units), Вампиризм → `lifesteal`, М/З/Броня → fractions, БОСС → `boss: true`.

Rules: items come only from chests and stack; attacks, items, pickups and chests are passive (manual: movement, dash, cursor targeting via Прицел later). Gold belongs to the killer. Difficulty follows the run clock, never the player level. Units: px/s = С/П × 3, attacks/s = С/А / 25, defences capped at 90%.

Steps: edit JSON → `build-test` (errors name file, id and field) → `play-capture` with `--demo --character ID` and `--menu` → check the HUD and the character screen. A new stat, trigger or behaviour needs C# (StatBlock/RunWorld) plus tests. A new sprite needs an Assets recipe (`art-recipe` skill), merged in Assets before the Game PR.
