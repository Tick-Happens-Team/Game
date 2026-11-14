---
name: add-content
description: Add or change characters, weapons, items, enemies, waves or run rules in the built-in core mod (data-driven JSON). Use when the owner provides content lists.
---

Content lives in `Game/content/core/*.json` (format = mod format, see docs/MODDING.md). Model: `src/TickHappens.Game/Content/ContentDefs.cs`; validation: `ContentLoader.cs`.

- `characters.json`: id, name, sprite (atlas id), stats {maxHp, moveSpeed, armor, regen, magnet, might, area, cooldown, amount}, weapon (starting weapon id), passives [{on: "levelUp", stat, add|pct}].
- `weapons.json`: id, name, kind (`bolt` projectile | `pulse` aura | `chain` lightning | `blast` explosion), damageType (physical/magical/pure), description, levels [{damage, cooldown, count, speed, pierce, range, area}].
- `items.json`: id, name, description, maxLevel, mods [{stat, add|pct}] applied once per level.
- `enemies.json`: id, sprite, anim, tint (RRGGBB, optional), hp, speed, damage, radius, xp, gold, goldChance.
- `waves.json`: hpPerMinute, damagePerMinute, maxAlive, waves [{from, to (minutes of run clock), perSecond, mix {enemyId: weight}}] without gaps, elites [{at, enemy, count}].
- `rules.json`: arena, slots, XP curve, dash, statues (count, minutes, radius, holdSeconds), chests (priceGrowth is fixed at 1.5 by design).

Rules: attacks/items/pickups/chests are passive; only movement, dash and cursor targeting (item "Прицел", not implemented yet) are manual. Gold belongs to the killer. Difficulty is driven by the run clock, never the player level.

Steps: edit JSON → `build-test` (ContentLoader errors name the file, id and field) → `play-capture` with `--demo` → check the HUD/cards. A new weapon *kind* or stat needs C# in `Sim/RunWorld.Weapons.cs` / `StatBlock` plus a test. A new sprite needs an Assets recipe (`art-recipe` skill).
