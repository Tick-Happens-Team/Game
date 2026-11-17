# Content roadmap: characters, items, and enemies

This plan orders the owner's character, item and enemy lists by how easy they are to add. Each tier builds on the engine systems introduced by the tiers before it, and every finished tier is playable content. Russian names are the canonical content names. Content is data in `content/core/*.json` (see the `add-content` skill); new behaviour is added as engine systems (S0–S13) shared by many entries.

The [owner's Google Sheet](https://docs.google.com/spreadsheets/d/1q4DUxTFMWcSg_zevYYDIWf2DWFH_v2YpFHKe_LTDcOM/edit) is the source of truth for content names, stats, and effects. This roadmap tracks implementation order and status. For balance values, use the linked sheet and the core mod.

Status markers follow the main [roadmap](../ROADMAP.md): `[x]` verified, `[ ]` pending, `[~]` partial.

## 1. What the engine already has

- **Run loop:** run clock, waves, enemies, XP and automatic stat growth, gold that belongs to the killer, interactable chests, with paid chests starting at 10 gold and scaling by ×1.5 per purchase, statues, dash.
- **Stats:** `maxHp`, `armor` (percentage physical reduction), `moveSpeed`, `regen`, `magnet`, `might`, `area`, `cooldown`, `amount`, with additive and percentage modifiers and level-up passives.
- **Weapon kinds:** `bolt` (projectiles), `pulse` (aura burst), `chain` (lightning hops), `blast` (explosion in the densest spot). `CombatArea` also provides circle and cone geometry with falloff.
- **Damage types:** `physical`, `magical` and `pure` are supported. Physical and magical damage use the corresponding defences, while pure damage bypasses them.
- **VFX:** explosion, impact, slash, lightning, sparkle, pickup, and GPU particles.

Milestone M1 is playable: stat model v2 (S0), hero basic attack (S1), the damage pipeline with types and percentage defences (S2), chest loot by rarity with stacking items (S13), six C1 heroes with sprites and the character select screen. The item catalog implements all 65 sheet entries. The five cursed rewards are wired to bosses and drop once per defeated boss. Each boss also leaves a free, one-use chest. The playable roster uses the hero sheet’s base stats and growth.

## 2. Engine systems

| ID | System | Depends on | Acceptance |
|---|---|---|---|
| S0 | **Character stat model v2.** See §3. | — | All 18 characters load from JSON with their table values. Unit tests cover per-level growth and percentage defences. |
| S1 | **Hero basic attack** driven by the character's «Damage» and «Attack speed». Auto-aimed projectiles use the shape described in §8. | S0 | Each character attacks with its own damage and rate. Damage and attack speed scale with level. |
| S2 | **Damage pipeline.** Hit = base × source multipliers (hero, items, damage type) → crit → target defence (armor % for physical, Magic resist % for magical, none for pure) → on-hit and on-damage-taken hooks. Every hit carries source (hero / item id / summon owner), type and flags (crit, proc, reflected). | S0 | Order-independent multiplier tests. Pure damage ignores defences. Summon damage credits the summon owner. |
| S3 | **Crit:** Players start with 5% physical and 5% magical crit chance, with a shared base crit multiplier of ×2. | S2 | Deterministic crit rolls from the seeded RNG. |
| S4 | **Healing layer:** lifesteal (by damage type) soft-capped by `lifestealHealCap`: it heals in full up to 6% of max HP per second (a budget that holds 2 seconds) and at 20% beyond it, so area damage cannot out-heal every threat, shields (absorb, regenerating), heal-received events, a conditional regen hook. | S2 | Lifesteal and shields are tested. Shields show on the HUD. |
| S5 | **Event hooks** usable from data: `onHit`, `onAttack` (proc chance), `onKill`, `onDamageTaken` (threshold accumulator), `onDash`, `onGoldSpent`, `onItemAcquired`, `onLevelUp`, `onDeath`, `everySeconds`, `whileStill`. | S2 | Each hook has a test. Hooks are tick-based and deterministic. |
| S6 | **New weapon kinds:** `melee` (arc in the facing direction, slash VFX), `cone` (sustained, CombatArea.Cone), `boomerang` (out-and-back projectile), `bounce` (re-targets on kill), `dropped` (delayed bomb behind the hero), `trail` (ground hazard), `snipe` (hit the highest-HP enemy), `execute` (instant kill with a filter). | S1, S2 | Every kind is used by at least one item and verified in a capture. |
| S7 | **Status effects on enemies:** slow (proximity-scaled), freeze, stun, poison or delayed damage over time, plus a boss flag that excludes executes and instant kills. | S2 | Effects stack by documented rules and have tests and VFX. |
| S8 | **Ground hazards:** persistent damaging zones such as fire trails and soda bombs, pooled and with a budget. | S6 | 200 active hazards stay within the simulation budget. |
| S9 | **Defensive procs:** block chance (any or physical only), block on cooldown, reflect (percentage and threshold), vision radius modifier. | S2, S5 | Tests cover each proc. Vision radius changes the camera or fog. |
| S10 | **Dash extensions:** extra charges, long dash, dash cooldown stat, on-dash buffs. | existing dash | HUD shows charges. |
| S11 | **Companions and summons:** an entity kind with HP, AI (follow, orbit, attack nearest, kamikaze, decoy, fetch XP), lifetime and respawn. Damage credits the owner. | S2, S5 | Gnome, drone and decoy work in solo play. Summon kills credit their owner's gold. |
| S12 | **Allies:** an "ally" query covering co-op players and summons, radius auras, ally buffs, and heal sharing. Auras include their owner in solo. | S11, P3 for co-op | Auras affect summons in solo and players in co-op. |
| S13 | **Economy and loot:** gold multiplier, refund on spend, chest loot tables by rarity (Common, Rare, Epic, Mythic, Legendary), item stacking, and cursed boss drops. | S0; E6 for boss drops | Chest rewards match the sheet's rarity chances, and bosses drop their assigned cursed items. |

## 3. Character stat model (S0)

The table uses these columns. The source marks every character as official.

| Column | Meaning | Engine stat |
|---|---|---|
| HP `base/+lvl` | max HP at level 1 and per level | `maxHp`, `maxHpPerLevel` |
| Move speed | movement speed | `moveSpeed`, mapped to px/s (Q2) |
| Attack speed | attack speed | new `attackSpeed`, mapped to attacks/s (Q2) |
| Armor | physical damage reduction in % | `armor` changes from flat to percent (Q3) |
| Magic resist | magical damage reduction in % | new `magicResist` |
| Regen | base HP restored per second | `regen` |
| Damage `base/+lvl` | basic attack damage and per-level growth | new `damage`, `damagePerLevel` |
| Growth per level | how the passive grows per level | passive parameter `perLevel` |

The engine already reads `stats.regen` and applies it per second. All six playable heroes currently omit it, so their base regeneration is zero.

**Proposed JSON:**

```json
"stats": { "maxHp": 150, "maxHpPerLevel": 5, "regen": 2, "moveSpeed": 25, "attackSpeed": 25, "armor": 0.05, "magicResist": 0.10, "damage": 15, "damagePerLevel": 2.75 },
"passives": [ { "id": "octarine_cooldown", "params": { "cooldown": -0.10 }, "perLevel": { "cooldown": -0.0015 } } ]
```

Tier C1 replaced the demo character `clockwarden` with six heroes using this model.

## 4. Characters ordered by ease

Stats are copied from the owner's table. Growth means the per-level value of the passive. C1 heroes are playable with their sheet values.

### Tier C1: playable heroes (S0, S1, S13 for gold; S3 for Watchman’s crit growth)

| # | Character | HP | Regen (HP/s) | Move speed | Attack speed | Armor | Magic resist | Damage | Passive → implementation | Growth | Status |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | Florist (formerly Octarine) | 135/+5 | 1.75 | 25 | 25 | 5% | 15% | 18/+2.75 | −10% cooldown on all abilities and items → `cooldown` pct | 0.5% | [~] |
| 2 | Swordsman | 275/+4.5 | 3 | 18 | 20 | 8% | 5% | 20/+1.75 | armor rises every level and blocks a physical contact hit with 18.5% chance | +0.5 percentage points to armor and block chance | [x] |
| 3 | Smith | 200/+2 | 2.75 | 30 | 25 | 5% | 4% | 15/+1.8 | +10% item damage → `itemDamage` multiplier (needs S2 source tags) | 0.25% | [~] |
| 4 | Goflin | 125/+3.5 | 1.75 | 35 | 30 | 4% | 6% | 5/+2.5 | +20% gold → `goldGain` (S13) | 0.75% | [~] |
| 5 | Celestial Mage | 185/+1 | 1.25 | 28 | 28 | 15% | 35% | 10/+0.25 | +35% magical damage, max HP always −60% → `magicalDamage` pct and `hpMultiplier` final multiplier 0.4 | 2.25% | [~] |
| 6 | Watchman | 200/+1.5 | 5 | 40 | 55 | 5% | 5% | 4/+0.35 | +0.75 damage per minute of play → stat bound to run time (Q8) | +1.05 percentage points crit chance | [x] |

### Tier C2: simple triggers (S2, S4, S5)

| # | Character | HP | Regen (HP/s) | Move speed | Attack speed | Armor | Magic resist | Damage | Passive → implementation | Growth | Status |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 7 | Vamp-chan | 125/+1.5 | 1.25 | 35 | 35 | 5% | 0% | 18/+1.75 | 3.5% lifesteal from damage dealt; lifesteal heals in full up to 10% max HP/s instead of 6% → S4 | +0.25 pp lifesteal | [x] |
| 8 | Miner | 300/+3 | 3.75 | 20 | 20 | 10% | 5% | 10/+1.25 | after taking 80 damage, explodes for 10 in radius 50 → `onDamageTaken` accumulator plus blast VFX | +2.5 damage and +2.5 radius | [x] |
| 9 | Cybernetic Cube | 100/+3.5 | 1.75 | 25 | 30 | 10% | 10% | 10/+0.75 | each level increases one random stat by 0.75–8.25% → seeded roll on `onLevelUp` | fixed roll range | [x] |
| 10 | Masochist | 150/+3 | 0.25 | 22 | 35 | 1% | 3% | 13/+2.25 | lower HP gives more regen and move speed → stats computed from missing HP | +0.15 HP/s and +0.3 percentage points movement speed at maximum bonus | [x] |

### Tier C3: new world mechanics (S6, S8, S9, death override)

| # | Character | HP | Regen (HP/s) | Move speed | Attack speed | Armor | Magic resist | Damage | Passive → implementation | Growth | Status |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 11 | Skater | 135/+3 | 1.25 | 25 | 25 | 5% | 5% | 12/+1.25 | +25% move speed; leaves a fire trail (5 dps, lasts 3 s) → S8 trail hazard | +1% move speed, +2.75 trail dps | [x] |
| 12 | Diver | 200/+5.25 | 6.75 | 10 | 15 | 7% | 7% | 15/+3.25 | low base move speed that cannot be increased; armor grows with distance travelled; reflection grows per level → move-speed lock, odometer stat, S9 reflect | 0.25% | [x] |
| 13 | Boar | 200/+5 | 3 | 38 | 15 | 5.5% | 8% | 5/+2.5 | rams for 5% of max HP and knocks enemies back, gains 25% speed and 10% ram damage after 5 s of walking, becomes a ghost for 5 s after death | +0.1 s in ghost form | [x] |

### Tier C4: allies and summons (S11, S12; co-op for full value)

| # | Character | HP | Regen (HP/s) | Move speed | Attack speed | Armor | Magic resist | Damage | Passive → implementation | Growth | Status |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 14 | Neko-chan | 135/+3.25 | 0.25 | 35 | 35 | 2.5% | 2.5% | 15/+2.25 | allies take −7% damage and heal 1.5 HP within radius 175; self bonuses ×1.5 → ally aura (S12) | −0.35 percentage points damage taken, +1.5 radius, +0.25 regen per level | [x] |
| 15 | Puppeteer | 200/+1 | 1 | 30 | 20 | 7.5% | 5% | 15/+1.25 | +10% damage and attack speed for allies and allied creatures → ally buff (S12); needs summons from items to matter in solo | 1% | [x] |
| 16 | Necromancer | 200/+5 | 1 | 30 | 25 | 0% | 15% | 15/+1 | killed enemies leave souls; every 30 s spends them: 1–20 souls → healing ghost, 21–50 → warrior ghost (HP and damage = souls), 51+ → big area ghost (HP and damage = souls); during the cooldown each soul adds +0.5% damage → soul counter (`onKill`), three summon AIs (S11), ally heal (S12) | +0.1% damage per soul per level | [x] |
| 17 | Иллюзионист | 125/+3 | 1.75 | 25 | 25 | 5% | 15% | 12/+1.75 | every 15 s creates a copy with 50% of the hero’s max HP; it lasts 8 s, draws enemies, and repeats attacks at 25% damage. Up to eight hero copies can overlap, plus the Phantom Mask copy. | +0.25 percentage points of damage and +0.15 s lifetime per level | [x] |
| 18 | King Arthur | 200/+5 | 2.75 | 25 | 20 | 10% | 5% | 15/+2 | Excalibur replaces projectiles with a 350° physical slash in radius 30 | +5 Excalibur radius per level | [x] |

### Planned characters (owner ideas, not in the sheet yet)

| # | Character | Idea | Likely systems | Status |
|---|---|---|---|---|
| 19 | Succubus (Суккуб) | Planned hero. Theme, passive, stats, and growth are pending in the owner's sheet. | — | [ ] |

The heroes use these defaults where the sheet leaves values open. Diver gains one percentage point of armor per 100 pixels travelled and can still dash. Skater fire patches share a damage tick, so overlapping patches from the same owner do not multiply damage. Necromancer summons use the shared companion cap. Pig keeps moving and attacking during the ghost interval with damage immunity.

## 5. Items ordered by ease

There are 65 items: 60 in the chest rarity pool and five cursed boss drops. The chest rolls use 58% common, 25% rare, 10% epic, 5% mythic, and 2% legendary as rarity weights. Each chest offers two distinct items while the run continues. Press 1 or 2 within 10 seconds. The first option is granted on timeout. Heart of the Gold Vein adds key 3 and a third item to every second chest opened while held. A damage type of «—» means the item deals no damage.

Values the sheet leaves open use tunable defaults in `items.json`. Jetpack increases dash distance and duration by 75%. Wooden Stick pulses every 5 s, Magic Projectile fires every 3 s, and Poor Man's Shield refreshes every 12 s. Sword reaches 75, Fan 150, and Leader's Heart 250. Cane checks its chance on a basic attack once its cooldown is ready. Repeated summons have per-item caps and a shared 64-entity simulation cap.

### Tier I1: stat items (S0, S10 for dash cooldown)

| Item | Rarity | Effect | Implementation | Status |
|---|---|---|---|---|
| Sneakers | Common | +5% move speed | `moveSpeed` pct (exists) | [x] |
| Tin of Sprats | Common | +10 max HP | `maxHp` add (exists) | [x] |
| Chocolate | Common | +5% magical damage | `magicDamage` pct | [x] |
| Snack Bar | Common | −8% dash cooldown | `dashCooldown` pct | [x] |
| Expander | Epic | +20% radius of all items and weapons | `area` pct (exists) | [x] |
| Heavy Shackles | Legendary | −40% move speed, +30% all outgoing hero damage | `moveSpeed` and `might` pct | [x] |

### Tier I2: crit, sustain, progression, and dash (S3, S4, S5, S10, S13)

| Item | Rarity | Effect | Implementation | Status |
|---|---|---|---|---|
| Fork | Mythic | +33% physical crit chance | S3 | [x] |
| Needle | Rare | +12% crit damage | S3 | [x] |
| Bloodstone | Mythic | +33% magical crit chance and +10 percentage points of magical lifesteal | S3 and S4 | [x] |
| Sentient Grass | Rare | while standing still, regenerates 2.25% of max HP per second | `whileStill` plus regen (S5) | [x] |
| Grandma's Compote | Rare | shield equal to 15% of max HP, replenished after 6 s without taking damage | shield (S4) | [x] |
| Star of David | Epic | refunds 25% of every gold purchase | `onGoldSpent` (S5, S13) | [x] |
| Jar of Wind | Rare | +1 dash charge | S10 | [x] |
| Whoopee Cushion | Common | every dash gives +5% move speed for 3 s | `onDash` buff (S5, S10) | [x] |
| Jetpack | Epic | longer dash | S10 | [x] |
| Accelerator | Mythic | every 27.18 s: +25% movement speed for 3.14 s; touching enemies deals 16.1 physical damage | `everySeconds` buff plus contact damage (S5) | [x] |
| Hyena Tooth | Epic | on pickup grants 100 × run minute / 3 XP | item-acquired hook and run clock (S5, S13) | [x] |
| Magnet | Rare | every 50 s attracts XP and owned gold across the arena | periodic pickup pull (S5) | [x] |

### Tier I3: weapons and conditional damage (S1, S2, S6; E0 for target roles)

| Item | Rarity | Effect | Implementation | Damage type | Status |
|---|---|---|---|---|---|
| Sword | Common | strikes in a 120° forward cone every 1.5 s for 15 damage | `melee` plus slash VFX | Physical | [x] |
| Soda | Common | every 3 s drops a soda bomb behind; it explodes after 1 s for 15 in radius 50 | `dropped` plus explosion VFX | Magical | [x] |
| Flamethrower | Epic | burns enemies in a 120° forward cone within 50 for 25 dps; runs for 5 s and cools down for 3 s | timed `cone` (CombatArea.Cone) with the fire VFX from 0.12 | Magical | [x] |
| Trident | Epic | every 7 s throws a returning trident to range 200 at speed 75; deals 30 damage to enemies it pierces | `boomerang` | Physical | [x] |
| Magic Projectile | Rare | shot at a random enemy, 25 damage; on a kill it bounces to an enemy within 350 | `bounce` | Magical | [x] |
| Electric Gloves | Epic | 20% chance on the hero’s attack: lightning dealing 25 damage and bouncing up to 4 times | `onAttack` proc plus `chain` (exists) | Magical | [x] |
| Rocket | Mythic | 20% chance on attack to fire a rocket for 30% of the attack’s damage | `onAttack` proc plus projectile | Physical | [x] |
| Laser | Legendary | instantly kills up to two enemies every 9 s, excluding bosses | `execute` | Pure | [x] |
| Galactic Rifle | Epic | every 16 s hits the highest-HP enemy for 20% of current HP (5% against bosses) | `snipe` targeting without a wind-up | Physical | [x] |
| Cane | Common | 0.5% chance on a 30 s cooldown to kill the highest-HP enemy; deals 300 damage to bosses | `execute` with a proc chance | Pure | [x] |
| Finisher | Legendary | 50% chance to finish an enemy below 12% HP (excluding bosses) | `onHit` execute | Pure | [x] |
| Клинок палача | Epic | +12% damage against elites and bosses | target-role damage multiplier (S2, E0) | — | [x] |
| Прицел | Rare | +9% damage against the highest-HP enemy | target-based damage multiplier (S2) | — | [x] |

### Tier I4: status effects and defensive procs (S7, S8, S9)

| Item | Rarity | Effect | Implementation | Damage type | Status |
|---|---|---|---|---|---|
| Сыр с плесенью | Mythic | 33% chance on damage dealt to inflict delayed poison damage equal to 6.5% of the original damage | poison (S7) | Magical | [x] |
| Aura | Mythic | slows enemies by 15% within 150 and deals 16 dps | slow plus damage zone (S7, S8) | Magical | [x] |
| Wooden Stick | Legendary | slows and deals 25–40 damage within 375; both effects are stronger closer to the hero | proximity-scaled slow and damage (S7) | Pure | [x] |
| Icicle | Mythic | 13% chance on kill: the enemy freezes and explodes for 30 nearby, leaving an icicle that heals 25% of current HP | `onKill` freeze, explosion, heal pickup (S5, S7) | Magical | [x] |
| Dice | Mythic | 22% chance to change an attack's or item's damage type to a different type at random | damage-type remap in S2 | — | [x] |
| Shield | Common | blocks one physical attack, 12 s cooldown | S9 | — | [x] |
| Welding Mask | Legendary | 15% chance to block any damage, 0.5 s cooldown; restricts vision to a 50° cone | S9 block plus vision | — | [x] |
| Nettle | Epic | reflects 16% of damage dealt by enemies | S9 reflect | Pure | [x] |
| Garlic | Epic | caps enemy lifesteal at 12% | enemy lifesteal cap (12%) | — | [x] |
| Fan | Epic | slows enemies behind the hero by 20% and adds 20 move speed | rear-facing slow (S7) and `moveSpeed` bonus | — | [x] |

### Tier I5: companions and summons (S11)

| Item | Rarity | Effect | Implementation | Damage type | Status |
|---|---|---|---|---|---|
| Garden Gnome | Rare | every 7 s summons a gnome with 150 HP and 10 damage | summon, attack nearest | Physical | [x] |
| FPV DRONE | Rare | a drone flies into an enemy and explodes for 35 in radius 115, then respawns after 10 s | summon, kamikaze | Physical | [x] |
| Sausages | Epic | a friendly dog fetches XP orbs within 1000 | summon, fetch XP | — | [x] |
| Bait | Epic | every 5 s summons a 125 HP boar that spits poison, slows by 18%, and deals 20 damage in radius 80 | summon, area poison and slow (S7) | Magical | [x] |
| Death Eagle | Mythic | every 8 s the circling eagle dives on a random target, dealing 50 damage and stunning for 0.8 s in radius 150 | summon, orbit, stun (S7) | Physical | [x] |
| Leech | Common | every 50 s summons a creature with 100 HP that transfers its HP to the player at 5 per second | summon, HP drain to owner (S4) | — | [x] |
| Satanic Bible | Legendary | 16% chance to turn hit enemies into allied demons (22 damage, 2 hits/s, live 6 s) | enemy conversion into summons (S11) | Pure | [x] |

### Tier I6: ally auras (S12; most value in co-op)

| Item | Rarity | Effect | Implementation | Status |
|---|---|---|---|---|
| F.B. Ring | Common | +15 HP to allies within 225 | aura | [x] |
| Leader's Heart | Common | +8 attack speed to nearby allies | aura | [x] |
| Totem | Common | heals all allied creatures within 425 at 1 HP/s | heal aura | [x] |
| Vampire Hood | Rare | 8% lifesteal to all allies within 180; copies widen the radius by 15% and hoods do not add up | lifesteal aura (S4) | [x] |
| Poor Man's Shield | Rare | 50 shield to the ally with the lowest HP | targeted shield (S4) | [x] |
| Motivation Mask | Epic | +12% physical damage to allies within 300 | aura | [x] |
| Shared Heart | Epic | the hero receives 7% of healing done to allies and 6% of healing done to summons | heal share | [x] |
| Dumbbell | Epic | +50 HP to allies and +25 HP to summons within 150 | aura | [x] |
| Шипастые ботинки | Mythic | +30 attack speed to allies within 300; 9% chance to redirect a physical hit above 35 to its source | aura plus reflect (S9) | [x] |
| Dominator Helm | Legendary | +10% armor to allies within 400 and 15% chance to block a physical hit on an ally; applies to all allies | aura plus block (S9) | [x] |
| Hookah | Epic | +10 percentage points of magic resistance for the owner, allies and summons within 200 | aura with locally rendered mist | [x] |
| Archmage's Cloak | Rare | +8 percentage points of magic resistance | stat modifier | [x] |

### Tier I7: cursed boss drops (E6, S13)

| Item | Boss | Effect | Implementation | Status |
|---|---|---|---|---|
| Перевёрнутый крест | Древний культист | once prevents death, restores full HP, grants 3.5 s of invulnerability, and is consumed | death override and item consumption (S4, S5) | [~] |
| Сердце золотой жилы | Старатель | every second chest offers a third reward option | per-player chest counter and reward choices (S13) | [~] |
| Маска Фантома | Фантом | 10% chance on kill to create a 4 s illusion that repeats attacks at 25% damage; at most one illusion | on-kill mirrored summon (S5, S11) | [~] |
| Разбитые часы | Хранитель времени | every 11 s resets one item that is on cooldown | periodic cooldown reset (S5) | [~] |
| Сердце Пожирателя | Пожиратель | below 15% HP, devours a random non-boss enemy and restores 20% of max HP; 35 s cooldown | HP threshold trigger, filtered execute, and heal (S4–S6) | [~] |

**Coverage:** 60 chest items and five cursed rewards.

## 6. Enemies ordered by ease

The owner's roster has 24 enemies: 10 normal, 9 elite, 5 bosses. They replace the demo enemies (gloombat, amberslime, duskslime).

**Rules applied to every enemy:**
- **Speed.** Enemy Move speed uses the same scale as heroes: px/s = Move speed × 3.
- **Attacks.** An enemy with Attack speed attacks in melee contact at that rate (attacks/s = Attack speed / 25). Enemies without Attack speed keep contact damage gated by the player's invulnerability window.
- **Lifesteal («Lifesteal»).** It heals the enemy by that share of the damage it deals to a player. The Garlic item caps it at 20%, which answers the earlier Garlic question.

### Enemy systems

| ID | System | Depends on | Acceptance |
|---|---|---|---|
| E0 | **Enemy stat model v2:** HP, damage and type, Move speed, optional Attack speed, armor and Magic resist in %, XP, lifesteal, role (normal / elite / boss) | S2 | All 24 enemies load from JSON with the table values. Enemy lifesteal heals in tests. |
| E1 | **Enemy projectiles and ranged AI:** keep distance, shoot, retreat from melee range | E0 | Spark and Thief kite and shoot in a capture. Enemy projectiles respect the player's defences. |
| E2 | **Death effects:** a hazard on death (S8) and an explosion that hurts both the player and enemies | E0, S8 | Rot leaves a 200-radius cloud. Kamikaze's blast damages nearby enemies. |
| E3 | **Special movement:** jumps with a landing area, teleport, stealth (hidden until the attack) | E0 | Jumper, Dark Mage, Ninja and Phantom move as described. |
| E4 | **Enemy auras and buffs:** damage share to a guardian, a timed global buff | E0 | Guardian absorbs a share of nearby damage. Screamer's 10 s timer buffs living enemies once. |
| E5 | **Debuffs on the player:** slow, root, HP drain into max HP, item disable, cooldown slowdown | E0, S7 | Each debuff shows on the HUD and expires. Disabled items resume, and all of them resume on the thief's death. |
| E6 | **Bosses:** spawn schedule, boss HP bar, scripted phases (enrage, devour, time acceleration, illusions, destructible veins, and interruptible sacrifices) | E0–E5 | Each boss can be beaten in a scripted test and reads clearly in a capture. |
| E7 | **XP rebalance** for table XP values (7–500 per kill) | E0 | Level curve tuned so a 15-minute run reaches the intended level range (owner target pending). |

### Tier E1: stat-only (E0)

| Enemy | Role | HP | Damage | Type | Move speed | Attack speed | Magic resist | Armor | XP | Lifesteal | Behaviour | Status |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Slime | normal | 50 | 7.5 | physical | 20 | — | 20% | 0% | 15 | 0% | plain chaser | [ ] |
| Skeleton Swordsman | normal | 20 | 10 | physical | 15 | 30 | 0% | 85% | 8 | 0% | plain chaser, very high armor | [~] |
| Butcher | elite | 300 | 40 | physical | 15 | 15 | 15% | 15% | 25 | 25% | slow melee chaser with a cleaver | [~] |

### Tier E2: death effects and simple specials (E2, E4, E5)

| Enemy | Role | HP | Damage | Type | Move speed | Attack speed | Magic resist | Armor | XP | Lifesteal | Behaviour | Status |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Rot | normal | 80 | 8 | physical | 10 | 15 | 0% | 0% | 10 | 5% | slow melee chaser; on death leaves a rot cloud for 4 s (radius 200 sheet units, 15 magical dps) | [x] |
| Kamikaze | normal | 50 | 50 | magical | 25 | — | 0% | 0% | 7 | 0% | winds up within 22 px of the player and self-destructs; its death explosion damages players and enemies within 55 px | [x] |
| Screamer | normal | 75 | — | — | 25 | — | 0% | 0% | 10 | 0% | screams from behind the horde; if alive after 10 s, buffs all existing enemies by +50 HP and +5 Move speed | [~] |
| Siphoner | normal | 25 | 0 | magical | 7 | drains 2 HP/s | 5% | 5% | 20 | 0% | drains the player's HP within 135 px and converts actual damage dealt into its own max HP | [x] |

### Tier E3: ranged and special movement (E1, E3, E5)

| Enemy | Role | HP | Damage | Type | Move speed | Attack speed | Magic resist | Armor | XP | Lifesteal | Behaviour | Status |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Spark | normal | 25 | 10 | magical | 15 | 25 | 10% | 5% | 10 | 0% | keeps its distance and fires small sparks from up to 160 px | [x] |
| Witch | elite | 75 | 10 | magical | 20 | 25 | 15% | 10% | 30 | 10% | lightning that can hit several targets (reuses the chain arc) | [~] |
| Jumper | normal | 100 | 15 | physical | 0 | — | 20% | 20% | 20 | 0% | moves only by jumps every 2 s up to 250; each landing makes an earthquake (radius 150, 15 damage) | [~] |
| Dark Mage | elite | 100 | 25 | magical | 0 | 20 | 40% | 15% | 40 | 15% | dark-magic caster that moves only by teleport | [~] |
| Thief | elite | 215 | 25 | physical | 25 | 25 | 0% | 15% | 50 | 15% | throws daggers and avoids melee; every 15 s disables a random player item for 10 s; on its death all items work again | [~] |

### Tier E4: complex elites (E3, E4, E5, enemy crit)

| Enemy | Role | HP | Damage | Type | Move speed | Attack speed | Magic resist | Armor | XP | Lifesteal | Behaviour | Status |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Guardian | elite | 300 | — | — | 25 | — | 35% | 35% | 15 | 0% | protects the nearest enemies by taking part of their damage | [~] |
| Ninja | elite | 135 | 25 | physical | 30 | — | 0% | 15% | 15 | 25% | turns invisible, comes from behind, makes one attack (25 damage, slows the player by 25%), runs away and repeats; at least 10 s between approaches | [~] |
| She-Wolf | elite | 115 | 25 | physical | 55 | 65 | 15% | 20% | 45 | 35% | fast hunter; 33% crit chance; every third hit roots the player for 2 s | [~] |

### Tier E4b: cultist support and reinforcements (E4, E5, S8)

| Enemy | Role | HP | Урон | Тип | С/П | С/А | М/З | Броня | Опыт | Вампиризм | Behaviour | Status |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Младший культист | normal | 75 | 20 | физ | 15 | 20 | 15% | 0% | 15 | 35% | melee blood dagger; every 15 s can create a blood circle on another cultist of any rank, healing it for 10 HP/s | [~] |
| Старший культист | elite | 200 | 35 | маг | 20 | 25 | 50% | 15% | 35 | 35% | every 13 s casts radius-300 circles under the player; they activate after 2 s, deal 50 damage, and mark the player so other cultists attack them 15% faster. Every 25 s summons three junior cultists and one shaman | [~] |
| Шаман-культист | normal | 75 | 20 | маг | 15 | 15 | 15% | 0% | 15 | 10% | casts blood rain from range, healing cultists within a 600-radius area for 35 HP/s for 5 s. Casting costs 50 of the shaman's HP and can kill it | [~] |

### Tier E5: bosses (E6)

| Boss | HP | Damage | Type | Move speed | Attack speed | Magic resist | Armor | XP | Lifesteal | Behaviour | Status |
|---|---|---|---|---|---|---|---|---|---|---|---|
| Devourer | 5000 | 125 | physical | 20 | 15 | 20% | 40% | 350 | 15% | huge, slow chaser; every 20 s devours the nearest mob and heals 300; below 25% HP speeds up to 30 Move speed | [~] |
| Timekeeper | 3500 | 70 | magical | 25 | 30 | 45% | 15% | 500 | 0% | on spawn doubles the flow of time; an aura of radius 250 slows the player by 50% and makes items and passives recharge 25% slower | [~] |
| Phantom | 1800 | 45 | magical | 25 | 35 | 35% | 35% | 325 | 25% | every 4 s teleports to a random point within 700 of the player and leaves a 5 s illusion that repeats its attacks at 30% damage; every 30 s makes 4 illusions around the player, one of them real; dealing 500 damage to the real one removes the others and stuns the boss for 5 s | [~] |
| Старатель | 4000 | 85 | physical | 20 | 30 | 35% | 35% | 450 | 9% | every 15 s creates a 500 HP gold vein at a random point within 1000 of himself and runs to mine it. Mining takes 3 s and heals him for 750 HP; the player can destroy the vein first. Below 35% HP, gold fever raises Move speed and Attack speed to 40 and cuts mining to 2 s; his eyes and pickaxe turn gold | [~] |
| Древний культист | 2500 | 50 | magical | 15 | 25 | 50% | 15% | 350 | 35% | attacks with blood daggers from different sides; every 15 s summons one Старший культист and three Младший культист. Below 50% HP can sacrifice a senior cultist at an altar; if the sacrifice survives 3 s, the boss heals 500 HP and gains +15% lifesteal and +20 Attack speed | [~] |

Enemy XP drops gain 5% of the base drop per elapsed run minute, rounded to whole XP.

**Coverage:** 24 roster enemies, plus the legacy bat and elite slime. Gold veins and phantom illusions are encounter entities.
- Normal (10): Rot, Slime, Jumper, Spark, Siphoner, Skeleton Swordsman, Screamer, Kamikaze, Младший культист, Шаман-культист.
- Elite (9): Witch, Guardian, Butcher, Dark Mage, Ninja, She-Wolf, Thief, Старший культист, Огр-великан.
- Bosses (5): Devourer, Timekeeper, Phantom, Старатель, Древний культист.

King Arthur is playable. Excalibur replaces his basic projectile with a forward physical slash and gains 0.25 range per level.

## 7. Milestones

Each milestone is playable content.

| Milestone | Systems | Content | Acceptance |
|---|---|---|---|
| [~] M1 "Stat roster" | S0, S1, S2 (types and defences), S13 (gold, loot tables) | Characters 1–6; items I1; chest rarity weights | Character pick screen. Each character's stats verified by tests against the table. Capture of every character. |
| M1e "First horde" | E0, E7 | Enemies E1 (Slime, Skeleton Swordsman, Butcher) with new waves | Waves use only owner enemies. Enemy lifesteal and defences tested. |
| M2 "Crit and triggers" | S3, S4, S5, S10 | Characters 7–10; items I2; Watchman crit growth | Hook tests. HUD shows shield and dash charges. |
| M3 "Arsenal" | S6, S8, E2 | Items I3 (target-role effects need E0); Skater; enemies E2 | Every new weapon kind captured. Simulation budget kept with 2,000 enemies. |
| M4 "Afflictions and guards" | S7, S9, E1, E3, E4, E5 | Items I4; Diver, Pig; enemies E3 and E4 | Status stacking rules documented and tested. |
| M4b "Bosses" | E6, E4b | All five bosses and the cultist roster | Boss schedule in waves.json. Each boss beaten in a scripted test. |
| Boss rewards | E6, S13, relevant I7 effect systems | Five cursed items | Each boss drops its assigned reward. Старатель's reward requires chest reward choices. |
| M5 "Companions" | S11 | Items I5; Иллюзионист | Summon AI and owner credit tests. 50 summons stay within budget. |
| M6 "Allies" | S12 (+P3 co-op) | Items I6; Neko-chan (done: character `aura` block lowers `damageTaken` and adds regen for players and summons, stacks between Neko-chans), Puppeteer, Necromancer | Auras work in solo (summons) and in a 2-player loopback test. |

Art for every character, enemy and summon goes through recipes (`art-recipe` skill) alongside its milestone.

## 8. Owner decisions

| Question | Decision |
|---|---|
| Basic attack | Automatic projectiles that look nearly the same for every hero. Engine default: auto-aimed bolt at the nearest enemy, range 220, speed 230, pierce 1. Celestial Mage shoots magical bolts; everyone else shoots physical ones. |
| Units | Developer's choice: move px/s = Move speed × 3; attacks per second = Attack speed / 25 (data in `rules.json` → `units`). |
| Defences | Armor and Magic resist are percentage reductions: 50% blocks half. Item and level bonuses add up, with a cap of 90% (`units.resistCap`). Pure damage ignores both. |
| Growth | `base/+lvl`: level 1 has the base value and each later level adds the growth. Passive growth follows the same rule. |
| Items | Standard items come from chests on the map; cursed items drop from their listed bosses. Copies of standard items stack, and mods apply once per copy. Level-ups give automatic stat growth; there are no level-up cards. |
| Ally auras | Work on the owner too, including solo play. |
| Boar | After death becomes an invulnerable ghost re-skin for 5 s (+0.25 s per level), like Wraith King's reincarnation, then dies. |
| Watchman | Counts minutes on the game run clock, statues included. Damage bonus is 0.75 × run minutes. Each level grants +1 percentage point of physical and magical crit chance. |
| Garlic | Caps enemy lifesteal at 12%. |
| Florist | Developer's choice: the cooldown reduction also applies to the dash. |

## 9. Open questions for the owner

1. **Executes.** Laser and Finisher exclude bosses, and Galactic Rifle deals 5% current HP to bosses. Cane deals 165 damage to bosses. Do any of these effects treat elites differently?
2. **Accelerator.** The agreed movement speed bonus is +50%.
3. **Timekeeper.** Does "doubles the flow of time" double the run clock (difficulty) while it is alive, or speed up the whole game?
4. **Guardian.** What share of the protected enemies' damage does it take, and within what radius?
5. **Boss schedule.** Every five run-clock minutes, in roster order. The five bosses debut individually. Subsequent rounds repeat the order in pairs.
6. **XP target.** What level should a typical 15-minute run reach? Enemy XP is now 7–500 per kill.
7. **Gold.** Each successful enemy gold drop adds one coin per completed run-clock minute to its base amount.
9. **Сердце золотой жилы.** Standard chests offer two items while movement continues. With the heart, every second chest offers three.
