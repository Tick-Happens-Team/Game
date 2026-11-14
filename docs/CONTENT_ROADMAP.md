# Content roadmap: characters and items

This plan orders the owner's character, item and enemy lists by how easy they are to add. Each tier builds on the engine systems introduced by the tiers before it, and every finished tier is playable content. Russian names are the canonical content names. Content is data in `content/core/*.json` (see the `add-content` skill); new behaviour is added as engine systems (S0–S14) shared by many entries.

Status markers follow the main [roadmap](../ROADMAP.md): `[x]` verified, `[ ]` pending, `[~]` partial.

## 1. What the engine already has

- **Run loop:** run clock, waves, enemies, XP and level-up cards, gold that belongs to the killer, chests at 10 gold with ×1.5 per purchase, statues, dash.
- **Stats:** `maxHp`, `armor` (flat), `moveSpeed`, `regen`, `magnet`, `might`, `area`, `cooldown`, `amount`, with additive and percentage modifiers and level-up passives.
- **Weapon kinds:** `bolt` (projectiles), `pulse` (aura burst), `chain` (lightning hops), `blast` (explosion in the densest spot). `CombatArea` also provides circle and cone geometry with falloff.
- **Damage types:** `physical`, `magical` and `pure` exist in data, but resistances do not use them yet.
- **VFX:** explosion, impact, slash, lightning, sparkle, pickup, and GPU particles.

Milestone M1 is done: stat model v2 (S0), hero basic attack (S1), the damage pipeline with types and percentage defences (S2), chest loot by rarity with stacking items (S13), six C1 heroes with sprites, six I1 items and the character select screen, all covered by tests. Everything else below is pending unless marked otherwise.

## 2. Engine systems

| ID | System | Depends on | Acceptance |
|---|---|---|---|
| S0 | **Character stat model v2.** See §3. | — | All 16 characters load from JSON with their table values. Unit tests cover per-level growth and percentage defences. |
| S1 | **Hero basic attack** driven by the character's «Урон» and «С/А». Shape pending owner answer Q1. | S0 | Each character attacks with its own damage and rate. Damage and attack speed scale with level. |
| S2 | **Damage pipeline.** Hit = base × source multipliers (hero, items, damage type) → crit → target defence (armor % for physical, М/З % for magical, none for pure) → on-hit and on-damage-taken hooks. Every hit carries source (hero / item id / summon owner), type and flags (crit, proc, reflected). | S0 | Order-independent multiplier tests. Pure damage ignores defences. Summon damage credits the summon owner. |
| S3 | **Crit:** separate chance per damage type and a shared crit damage multiplier. | S2 | Deterministic crit rolls from the seeded RNG. |
| S4 | **Healing layer:** lifesteal (by damage type), shields (absorb, regenerating), heal-received events, a conditional regen hook. | S2 | Lifesteal and shields are tested. Shields show on the HUD. |
| S5 | **Event hooks** usable from data: `onHit`, `onAttack` (proc chance), `onKill`, `onDamageTaken` (threshold accumulator), `onDash`, `onGoldSpent`, `onLevelUp`, `onDeath`, `everySeconds`, `whileStill`. | S2 | Each hook has a test. Hooks are tick-based and deterministic. |
| S6 | **New weapon kinds:** `melee` (arc in the facing direction, slash VFX), `cone` (sustained, CombatArea.Cone), `boomerang` (out-and-back projectile), `bounce` (re-targets on kill), `dropped` (delayed bomb behind the hero), `trail` (ground hazard), `snipe` (charge, then hit the highest-HP enemy), `execute` (instant kill with a filter). | S1, S2 | Every kind is used by at least one item and verified in a capture. |
| S7 | **Status effects on enemies:** slow (proximity-scaled), freeze, stun, poison or delayed damage over time, plus a boss flag that excludes executes and instant kills. | S2 | Effects stack by documented rules and have tests and VFX. |
| S8 | **Ground hazards:** persistent damaging zones such as fire trails and soda bombs, pooled and with a budget. | S6 | 200 active hazards stay within the simulation budget. |
| S9 | **Defensive procs:** block chance (any or physical only), block on cooldown, reflect (percentage and threshold), vision radius modifier. | S2, S5 | Tests cover each proc. Vision radius changes the camera or fog. |
| S10 | **Dash extensions:** extra charges, long dash, dash cooldown stat, on-dash buffs. | existing dash | HUD shows charges. |
| S11 | **Companions and summons:** an entity kind with HP, AI (follow, orbit, attack nearest, kamikaze, decoy, fetch XP), lifetime and respawn. Damage credits the owner. | S2, S5 | Gnome, drone and decoy work in solo play. Summon kills credit their owner's gold. |
| S12 | **Allies:** an "ally" query covering co-op players and summons, radius auras, ally buffs, and heal sharing. Solo rule pending owner answer Q6. | S11, P3 for co-op | Auras affect summons in solo and players in co-op. |
| S13 | **Economy and loot:** gold multiplier, refund on spend, chest loot tables by rarity (Обычный, Редкий, Эпический, Мифический, Легендарный), item slots and stacking. | S0 | Chest rewards follow rarity weights from JSON. |
| S14 | **Cursor targeting (Прицел):** a manual aim point for abilities that support it. This is the only manual targeting allowed by AGENTS.md. | S6 | With Прицел, `bolt`, `cone`, `melee` and `snipe` aim at the cursor. Without it, they auto-target. |

## 3. Character stat model (S0)

The table uses these columns. «Офф/Мод» is «О» (official) for every character.

| Column | Meaning | Engine stat |
|---|---|---|
| ХП `base/+lvl` | max HP at level 1 and per level | `maxHp`, `maxHpPerLevel` |
| С/П | movement speed | `moveSpeed`, mapped to px/s (Q2) |
| С/А | attack speed | new `attackSpeed`, mapped to attacks/s (Q2) |
| Броня | physical damage reduction in % | `armor` changes from flat to percent (Q3) |
| М/З | magical damage reduction in % | new `magicResist` |
| Урон `base/+lvl` | basic attack damage and per-level growth | new `damage`, `damagePerLevel` |
| Прирост с уровнем | how the passive grows per level | passive parameter `perLevel` |

**Proposed JSON:**

```json
"stats": { "maxHp": 150, "maxHpPerLevel": 5, "moveSpeed": 25, "attackSpeed": 25, "armor": 0.05, "magicResist": 0.10, "damage": 15, "damagePerLevel": 2.75 },
"passives": [ { "id": "octarine_cooldown", "params": { "cooldown": -0.10 }, "perLevel": { "cooldown": -0.0015 } } ]
```

**Migration:** the current demo character `clockwarden` uses flat armor and no basic attack. It is replaced by the real roster in Tier C1.

## 4. Characters ordered by ease

Stats are copied from the owner's table. Growth means the per-level value of the passive.

### Tier C1: stat-only passives (S0, S1, S13 for gold)

| # | Character | HP | С/П | С/А | Броня | М/З | Урон | Passive → implementation | Growth | Status |
|---|---|---|---|---|---|---|---|---|---|---|
| 1 | Октарин | 150/+5 | 25 | 25 | 5% | 10% | 15/+2.75 | −10% cooldown on all abilities and items → `cooldown` pct | 0.15% | [x] |
| 2 | Мечник | 300/+5 | 18 | 20 | 10% | 3% | 20/+1 | armor rises every level → levelUp passive (exists, switches to %) | 1% | [x] |
| 3 | Кузнец | 250/+2 | 30 | 25 | 5% | 4% | 15/+1.5 | +15% item damage → `itemDamage` multiplier (needs S2 source tags) | 1% | [x] |
| 4 | Гофлин | 200/+4 | 35 | 30 | 4% | 6% | 5/+0.5 | +10% gold → `goldGain` (S13) | 0.75% | [x] |
| 5 | Небесный маг | 200/+1 | 28 | 28 | 25% | 25% | 10/+0.25 | +25% magical damage, max HP always −65% → `magicDamage` pct and `maxHp` final multiplier 0.35 | 2.25% | [x] |
| 6 | Часовой | 225/+1.5 | 40 | 50 | 5% | 5% | 3/+0.25 | +0.75 damage per minute of play → stat bound to run time (Q8) | +0.25 damage per level | [x] |

### Tier C2: simple triggers (S2, S4, S5)

| # | Character | HP | С/П | С/А | Броня | М/З | Урон | Passive → implementation | Growth | Status |
|---|---|---|---|---|---|---|---|---|---|---|
| 7 | Вампир | 175/+5 | 35 | 30 | 5% | 0% | 15/+1.5 | 10% lifesteal from damage dealt → S4 | 1.05% | [ ] |
| 8 | Шахтёр | 300/+3 | 25 | 20 | 6% | 4% | 10/+1 | after taking 50 damage, explodes for 5 in radius 50 → `onDamageTaken` accumulator plus blast VFX | +2.5 damage and +2.5 radius | [ ] |
| 9 | Кибернетический куб | 150/+5 | 25 | 30 | 10% | 10% | 0/+0.1 | each level: a random stat +1..5, and damage the same way → seeded roll on `onLevelUp` | roll min/max +0.25..2.5 per level | [ ] |
| 10 | Мазохист | 190/+4 | 20 | 35 | 1% | 1% | 13.5/+2.15 | lower HP gives more regen, move speed, attack speed and damage → stats computed from missing HP | conversion +0.15% per level | [ ] |

### Tier C3: new world mechanics (S6, S8, S9, death override)

| # | Character | HP | С/П | С/А | Броня | М/З | Урон | Passive → implementation | Growth | Status |
|---|---|---|---|---|---|---|---|---|---|---|
| 11 | Скейтер | 200/+3 | 25 | 25 | 5% | 5% | 20/+1 | +25% move speed; leaves a fire trail (5 dps, lasts 3 s) → S8 trail hazard | +1% move speed, +3 trail dps | [ ] |
| 12 | Водолаз | 200/+5.5 | 10 | 15 | 7% | 7% | 25/+1 | low base move speed that cannot be increased; armor grows with distance travelled; reflection grows per level → move-speed lock, odometer stat, S9 reflect | 0.25% | [ ] |
| 13 | Свинья | 200/+4 | 30 | 30 | 7.5% | 8% | 5/+8.25 | becomes a ghost for 5 s after death → death-override state (Q7) | +0.25 s | [ ] |

### Tier C4: allies and summons (S11, S12; co-op for full value)

| # | Character | HP | С/П | С/А | Броня | М/З | Урон | Passive → implementation | Growth | Status |
|---|---|---|---|---|---|---|---|---|---|---|
| 14 | Некотян | 175/+3.5 | 45 | 35 | 2.5% | 2.5% | 15/+1.5 | allies take −12% damage and heal 2.5 HP within radius 200; the bonuses apply to herself ×1.75 → ally aura (S12), works in solo through the self ×1.75 rule | −0.25% damage taken, +1.25 radius, +0.25 regen per level | [ ] |
| 15 | Кукловод | 200/+3 | 30 | 20 | 5% | 5% | 20/+1 | +10% damage and attack speed for allies and allied creatures → ally buff (S12); needs summons from items to matter in solo | 1% | [ ] |
| 16 | Некромант | 200/+5 | 30 | 25 | 0% | 15% | 15/+1 | killed enemies leave souls; every 30 s spends them: 1–20 souls → healing ghost, 21–50 → warrior ghost (HP and damage = souls), 51+ → big area ghost (HP and damage = souls); during the cooldown each soul adds +0.5% damage → soul counter (`onKill`), three summon AIs (S11), ally heal (S12) | +0.1% damage per soul per level | [ ] |

## 5. Items ordered by ease

There are 56 items. Rarity drives chest loot weights (S13); see Q5 for item levels and slots. A damage type of «—» means the item deals no damage.

### Tier I1: stat items (S0, S10 for dash cooldown)

| Item | Rarity | Effect | Implementation | Status |
|---|---|---|---|---|
| Чешки | Обычный | +10% move speed | `moveSpeed` pct (exists) | [x] |
| Банка шпрот | Обычный | +25 max HP | `maxHp` add (exists) | [x] |
| Шоколад | Обычный | +3% magical damage | `magicDamage` pct | [x] |
| Батончик | Обычный | −10% dash cooldown | `dashCooldown` pct | [x] |
| Расширяло | Эпический | +15% radius of all items and weapons | `area` pct (exists) | [x] |
| Тяжкие кандалы | Легендарный | −50% move speed, +30% all outgoing damage | `moveSpeed` and `might` pct | [x] |

### Tier I2: crit, sustain, economy, dash (S3, S4, S5, S10, S13)

| Item | Rarity | Effect | Implementation | Status |
|---|---|---|---|---|
| Вилка | Мифический | +33% physical crit chance | S3 | [ ] |
| Игла | Редкий | +30% crit damage | S3 | [ ] |
| Кровавый камень | Мифический | +25% magical crit chance, +15% lifesteal from magical damage | S3 and S4 | [ ] |
| Разумная трава | Редкий | while standing still, regenerate 2.5% of max HP per second | `whileStill` plus regen (S5) | [ ] |
| Бабушкин компот | Редкий | regenerating shield equal to 1/4 of max HP | shield (S4) | [ ] |
| Звезда Давида | Эпический | refunds 10% of every gold purchase | `onGoldSpent` (S5, S13) | [ ] |
| Банка с ветром | Редкий | +1 dash charge | S10 | [ ] |
| Подушка пердушка | Обычный | every dash gives +5% move speed for 3 s | `onDash` buff (S5, S10) | [ ] |
| Джетпак | Эпический | longer dash | S10 | [ ] |
| Ускоритель | Мифический | every 27.18 s: +speed for 3.14 s; touching enemies deals 16.1 physical damage | `everySeconds` buff plus contact damage (S5) | [ ] |

### Tier I3: weapons and attack procs (S1, S2, S6)

| Item | Rarity | Effect | Implementation | Damage type | Status |
|---|---|---|---|---|---|
| Меч | Обычный | strikes in the facing direction every 2 s for 15 | `melee` plus slash VFX | Физический | [ ] |
| Газировка | Обычный | every 3 s drops a soda bomb behind; it explodes after 2 s for 15 in radius 50 | `dropped` plus explosion VFX | Магический | [ ] |
| Огнемет | Эпический | sets enemies on fire in a 45° cone forward, 35 dps | `cone` (CombatArea.Cone); fire VFX port needed (0.12) | Магический | [ ] |
| Трезубец | Эпический | every 3 s throws a trident to range 200 at speed 75; it returns; 30 damage to everything it passes | `boomerang` | Физический | [ ] |
| Магический снаряд | Редкий | shot at a random enemy, 25 damage; on a kill it bounces to an enemy within 500 | `bounce` | Магический | [ ] |
| Электро перчатки | Эпический | 20% chance on the hero's attack: lightning bouncing up to 5 times, 10 damage | `onAttack` proc plus `chain` (exists) | Магический | [ ] |
| Ракета | Мифический | 25% chance on attack: a rocket with 50% of the attack's damage | `onAttack` proc plus projectile | Физический | [ ] |
| Лазер | Легендарный | instantly kills one enemy every 5 s | `execute` (bosses excluded, Q11) | Чистый | [ ] |
| Галактическая винтовка | Эпический | every 25 s targets the enemy with the most HP, aims 5 s, deals 20% of its current HP | `snipe` | Физический | [ ] |
| Трость | Обычный | 0.1% chance on a 30 s cooldown to kill the enemy with the most HP | `execute` with a proc chance | Чистый | [ ] |
| Добивалка | Легендарный | 50% chance to finish an enemy below 10% HP (not bosses) | `onHit` execute | Чистый | [ ] |

### Tier I4: status effects and defensive procs (S7, S8, S9)

| Item | Rarity | Effect | Implementation | Damage type | Status |
|---|---|---|---|---|---|
| Вич доктор | Мифический | each hit adds delayed poison damage | poison (S7) | Магический | [ ] |
| АУРА | Мифический | a wild aura around the hero slows enemies and deals 20 dps | slow plus damage zone (S7, S8) | Магический | [ ] |
| Деревяшка | Легендарный | slows and damages enemies within 700 for 30–100; closer means stronger | proximity-scaled slow and damage (S7) | Чистый | [ ] |
| Сосулька | Мифический | a killed enemy freezes and explodes for 30 nearby; creates an icicle that heals 30% of current HP | `onKill` freeze, explosion, heal pickup (S5, S7) | Магический | [ ] |
| Кости | Мифический | 33% chance to change an attack's or item's damage type at random | damage-type remap in S2 | — | [ ] |
| Щит | Редкий | blocks one physical attack, 10 s cooldown | S9 | — | [ ] |
| Маска сварщика | Легендарный | 15% chance to block any damage; reduces vision radius | S9 block plus vision | — | [ ] |
| Крапива | Эпический | reflects 30% of damage dealt by enemies | S9 reflect | Чистый | [ ] |
| Чеснок | Эпический | reduces any enemy lifesteal to 25% | enemy lifesteal cap (Q10) | — | [ ] |

### Tier I5: companions and summons (S11)

| Item | Rarity | Effect | Implementation | Damage type | Status |
|---|---|---|---|---|---|
| Садовый гном | Редкий | every 5 s summons a gnome: 150 HP, 10 damage | summon, attack nearest | Физический | [ ] |
| FPV ДРОН | Редкий | a drone flies into an enemy and explodes for 50, respawns after 10 s | summon, kamikaze | Физический | [ ] |
| Сосиски | Эпический | a friendly dog fetches XP orbs | summon, fetch XP | — | [ ] |
| Вентилятор | Эпический | a fan on the hero's back pushes away enemies approaching from behind | attached summon, knockback cone | — | [ ] |
| Приманка | Эпический | every 10 s a boar decoy: spits poison, slows by 25%, 25 damage, 400 HP | summon, decoy aggro, poison (S7) | Магический | [ ] |
| Орёл смерти | Мифический | an eagle circles the hero; every 10 s dives at a random enemy for 50 and stuns it for 0.3 s | summon, orbit, stun (S7) | Физический | [ ] |
| Подсос | Обычный | every 60 s summons a creature with 200 HP that transfers its HP to the player at 20 per second | summon, HP drain to owner (S4) | — | [ ] |
| Сатанинская библия | Легендарный | 21% chance to turn hit enemies into allied demons (20 damage, 2 hits/s, live 15 s) | enemy conversion into summons (S11) | Чистый | [ ] |

### Tier I6: ally auras (S12; most value in co-op)

| Item | Rarity | Effect | Implementation | Status |
|---|---|---|---|---|
| Кольцо имени Ф.Б. | Обычный | +15 HP to allies within 300 | aura | [ ] |
| Сердце лидера | Обычный | +20 attack speed to nearby allies | aura | [ ] |
| Тотем | Обычный | heals all allied creatures within 500 at 1 HP/s | heal aura | [ ] |
| Капюшон вампира | Редкий | 15% lifesteal to all allies within 350 | lifesteal aura (S4) | [ ] |
| Щит бедняков | Редкий | 100 shield to the ally with the lowest HP | targeted shield (S4) | [ ] |
| Маска мотивации | Эпический | +20% physical damage to allies within 300 | aura | [ ] |
| Общее сердце | Эпический | the hero receives 20% (summons: 5%) of healing done to allies | heal share | [ ] |
| Зуб гиены | Эпический | +1 skill level to allies, owner included | level bonus aura | [ ] |
| Гантеля | Мифический | +150 HP to allies, +50 to summons | aura | [ ] |
| Нью-роки (шипастые ботинки) | Мифический | +55 attack speed to allies within 500; 7% chance to reflect a physical hit above 35 at its source | aura plus reflect (S9) | [ ] |
| Шлем доминатора | Легендарный | +15% physical resistance to allies within 700/800/900 (wider with more enemies near the hero); 5% chance to block a physical attack on the owner | aura plus block (S9) | [ ] |

### Special: manual targeting (S14)

| Item | Rarity | Effect | Status |
|---|---|---|---|
| Прицел | Редкий | aims abilities at the cursor point | [ ] |

**Coverage:** 6 + 10 + 11 + 9 + 8 + 11 + 1 = 56 items.

## 6. Enemies ordered by ease

The owner's roster has 18 enemies: 8 normal, 7 elite, 3 bosses. They replace the demo enemies (gloombat, amberslime, duskslime).

**Rules applied to every enemy:**
- **Speed.** Enemy С/П uses the same scale as heroes: px/s = С/П × 3.
- **Attacks.** An enemy with С/А attacks in melee contact at that rate (attacks/s = С/А / 25). Enemies without С/А keep contact damage gated by the player's invulnerability window.
- **Lifesteal («Вампиризм»).** It heals the enemy by that share of the damage it deals to a player. The Чеснок item caps it at 25%, which answers the earlier Чеснок question.

### Enemy systems

| ID | System | Depends on | Acceptance |
|---|---|---|---|
| E0 | **Enemy stat model v2:** HP, damage and type, С/П, optional С/А, armor and М/З in %, XP, lifesteal, role (normal / elite / boss) | S2 | All 18 enemies load from JSON with the table values. Enemy lifesteal heals in tests. |
| E1 | **Enemy projectiles and ranged AI:** keep distance, shoot, retreat from melee range | E0 | Искра and Воришка kite and shoot in a capture. Enemy projectiles respect the player's defences. |
| E2 | **Death effects:** a hazard on death (S8) and an explosion that hurts both the player and enemies | E0, S8 | Гниль leaves a 200-radius cloud. Камикадзе's blast damages nearby enemies. |
| E3 | **Special movement:** jumps with a landing area, teleport, stealth (hidden until the attack) | E0 | Прыгун, Тёмный маг, Ниндзя and Фантом move as described. |
| E4 | **Enemy auras and buffs:** damage share to a guardian, a timed global buff | E0 | Страж absorbs a share of nearby damage. Крикун's 10 s timer buffs living enemies once. |
| E5 | **Debuffs on the player:** slow, root, HP drain into max HP, item disable, cooldown slowdown | E0, S7 | Each debuff shows on the HUD and expires. Disabled items resume, and all of them resume on the thief's death. |
| E6 | **Bosses:** spawn schedule, boss HP bar, scripted phases (enrage, devour, time acceleration, illusions with a real target and stun) | E0–E5 | Each boss can be beaten in a scripted test and reads clearly in a capture. |
| E7 | **XP rebalance** for table XP values (7–500 per kill) | E0 | Level curve tuned so a 15-minute run reaches the intended level range (owner target pending). |

### Tier E1: stat-only (E0)

| Enemy | Role | HP | Урон | Тип | С/П | С/А | М/З | Броня | Опыт | Вампиризм | Behaviour | Status |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Слайм | normal | 50 | 5 | физ | 15 | — | 10% | 0% | 10 | 0% | plain chaser | [ ] |
| Скелет с мечом | normal | 20 | 10 | физ | 15 | 30 | 0% | 85% | 8 | 0% | plain chaser, very high armor | [ ] |
| Мясник | elite | 300 | 40 | физ | 15 | — | 15% | 15% | 25 | 25% | slow melee chaser with a cleaver | [ ] |

### Tier E2: death effects and simple specials (E2, E4, E5)

| Enemy | Role | HP | Урон | Тип | С/П | С/А | М/З | Броня | Опыт | Вампиризм | Behaviour | Status |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Гниль | normal | 80 | 8 | физ | 10 | — | 0% | 0% | 10 | 5% | slow melee chaser; on death leaves a rot cloud (radius 200, 15 magical dps) | [ ] |
| Камикадзе | normal | 50 | 50 | маг | 25 | — | 0% | 0% | 7 | 0% | runs at the player and blows itself up nearby; its death explosion damages the player and enemies | [ ] |
| Крикун | normal | 75 | — | — | 25 | — | 0% | 0% | 10 | 0% | screams from behind the horde; if alive after 10 s, buffs all existing enemies by +50 HP and +5 С/П | [ ] |
| Сосунок | normal | 100 | 5 | маг | 20 | drains 15 HP/s | 5% | 5% | 20 | 0% | drains the player's HP from range and turns it into its own max HP | [ ] |

### Tier E3: ranged and special movement (E1, E3, E5)

| Enemy | Role | HP | Урон | Тип | С/П | С/А | М/З | Броня | Опыт | Вампиризм | Behaviour | Status |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Искра | normal | 50 | 10 | маг | 25 | 25 | 10% | 5% | 10 | 0% | a spark that shoots small sparks at the hero from range | [ ] |
| Ведьма | elite | 125 | 12 | маг | 20 | — | 15% | 10% | 30 | 10% | lightning that can hit several targets (reuses the chain arc) | [ ] |
| Прыгун | normal | 100 | 15 | физ | 0 | — | 20% | 20% | 20 | 0% | moves only by jumps every 2 s up to 250; each landing makes an earthquake (radius 150, 15 damage) | [ ] |
| Тёмный маг | elite | 100 | 25 | маг | 0 | — | 40% | 15% | 40 | 15% | dark-magic caster that moves only by teleport | [ ] |
| Воришка | elite | 215 | 25 | физ | 25 | 25 | 0% | 15% | 50 | 15% | throws daggers and avoids melee; every 15 s disables a random player item for 10 s; on its death all items work again | [ ] |

### Tier E4: complex elites (E3, E4, E5, enemy crit)

| Enemy | Role | HP | Урон | Тип | С/П | С/А | М/З | Броня | Опыт | Вампиризм | Behaviour | Status |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Страж | elite | 300 | — | — | 25 | — | 35% | 35% | 15 | 0% | protects the nearest enemies by taking part of their damage | [ ] |
| Ниндзя | elite | 135 | 25 | физ | 30 | — | 0% | 15% | 15 | 25% | turns invisible, comes from behind, makes one attack (25 damage, slows the player by 25%), runs away and repeats; at least 10 s between approaches | [ ] |
| Волчица | elite | 115 | 35 | физ | 55 | 65 | 15% | 20% | 45 | 35% | fast hunter; 33% crit chance; every third hit roots the player for 2 s | [ ] |

### Tier E5: bosses (E6)

| Boss | HP | Урон | Тип | С/П | С/А | М/З | Броня | Опыт | Вампиризм | Behaviour | Status |
|---|---|---|---|---|---|---|---|---|---|---|---|
| Пожиратель | 5000 | 125 | физ | 20 | 15 | 20% | 40% | 350 | 15% | huge, slow chaser; every 20 s devours the nearest mob and heals 300; below 25% HP speeds up to 30 С/П | [ ] |
| Хранитель времени | 3500 | 70 | маг | 25 | 30 | 45% | 15% | 500 | 0% | on spawn doubles the flow of time; an aura of radius 250 slows the player by 50% and makes items and passives recharge 25% slower | [ ] |
| Фантом | 1800 | 45 | маг | 25 | 35 | 35% | 35% | 325 | 25% | every 4 s teleports to a random point within 700 of the player and leaves a 5 s illusion that repeats its attacks at 30% damage; every 30 s makes 4 illusions around the player, one of them real; dealing 500 damage to the real one removes the others and stuns the boss for 5 s | [ ] |

**Coverage:** 3 + 4 + 5 + 3 + 3 = 18 enemies.
- Normal (8): Гниль, Слайм, Прыгун, Искра, Сосунок, Скелет с мечом, Крикун, Камикадзе.
- Elite (7): Ведьма, Страж, Мясник, Тёмный маг, Ниндзя, Волчица, Воришка.
- Bosses (3): Пожиратель, Хранитель времени, Фантом.

The table's «10%%» М/З for Слайм is read as 10%.

## 7. Milestones

Each milestone is playable content.

| Milestone | Systems | Content | Acceptance |
|---|---|---|---|
| [x] M1 "Stat roster" | S0, S1, S2 (types and defences), S13 (gold, loot tables) | Characters 1–6; items I1; chest rarity weights | Character pick screen. Each character's stats verified by tests against the table. Capture of every character. |
| M1e "First horde" | E0, E7 | Enemies E1 (Слайм, Скелет с мечом, Мясник) with new waves | Waves use only owner enemies. Enemy lifesteal and defences tested. |
| M2 "Crit and triggers" | S3, S4, S5, S10 | Characters 7–10; items I2 | Hook tests. HUD shows shield and dash charges. |
| M3 "Arsenal" | S6, S8, E2 | Items I3; Скейтер; enemies E2 | Every new weapon kind captured. Simulation budget kept with 2,000 enemies. |
| M4 "Afflictions and guards" | S7, S9, E1, E3, E4, E5 | Items I4; Водолаз, Свинья; enemies E3 and E4 | Status stacking rules documented and tested. |
| M4b "Bosses" | E6 | Пожиратель, Хранитель времени, Фантом | Boss schedule in waves.json. Each boss beaten in a scripted test. |
| M5 "Companions" | S11 | Items I5 | Summon AI and owner credit tests. 50 summons stay within budget. |
| M6 "Allies" | S12 (+P3 co-op) | Items I6; Некотян, Кукловод, Некромант | Auras work in solo (summons) and in a 2-player loopback test. |
| M7 "Aim" | S14 | Прицел | Cursor aim without breaking passive play. |

Art for every character, enemy and summon goes through recipes (`art-recipe` skill) alongside its milestone.

## 8. Owner decisions

| Question | Decision |
|---|---|
| Basic attack | Automatic projectiles that look nearly the same for every hero. Engine default: auto-aimed bolt at the nearest enemy, range 220, speed 230, pierce 1. Небесный маг shoots magical bolts; everyone else shoots physical ones. |
| Units | Developer's choice: move px/s = С/П × 3; attacks per second = С/А / 25 (data in `rules.json` → `units`). |
| Defences | Броня and М/З are percentage reductions: 50% blocks half. Item and level bonuses add up, with a cap of 90% (`units.resistCap`). Pure damage ignores both. |
| Growth | `base/+lvl`: level 1 has the base value and each later level adds the growth. Passive growth follows the same rule. |
| Items | Items come only from chests on the map. Copies stack, and mods apply once per copy. Level-ups give automatic stat growth; there are no level-up cards. |
| Ally auras | Work on the owner too, including solo play. |
| Свинья | After death becomes an invulnerable ghost re-skin for 5 s (+0.25 s per level), like Wraith King's reincarnation, then dies. |
| Часовой | Counts minutes on the game run clock, statues included. Formula used: damage bonus = (0.75 + 0.25 × (level − 1)) × run minutes. |
| Чеснок | Several enemies have lifesteal (§6), so the item caps enemy healing at 25%. |
| Октарин | Developer's choice: the cooldown reduction also applies to the dash. |

## 9. Open questions for the owner

1. **Executes.** Do Лазер, Трость, Добивалка and Галактическая винтовка ignore elites and bosses? Only Добивалка says so explicitly.
2. **Ускоритель.** The speed bonus amount is not specified.
3. **Хранитель времени.** Does "doubles the flow of time" double the run clock (difficulty) while it is alive, or speed up the whole game?
4. **Страж.** What share of the protected enemies' damage does it take, and within what radius?
5. **Boss schedule.** At which run-clock minutes do the bosses appear, and in what order? May the developer propose the waves?
6. **XP target.** What level should a typical 15-minute run reach? Enemy XP is now 7–500 per kill.
7. **Gold.** The enemy table has no gold column. Which enemies drop gold, and how much?
8. **Сосунок.** What is the range of its drain?
