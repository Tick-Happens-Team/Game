# Modding and the in-game Workshop

The base game is the built-in `core` mod and uses the same loader as community content. IDs use the `modid:item` namespace, for example `core:clockwarden`. Mods use TOML, JSON, PNG, and OGG. Executable code is excluded. A later sandbox would require an explicit capabilities field and resource budgets.

Game owns the loader, validator, schemas, balance data, and client. Assets owns the base game's media. Mods owns the community catalog. See [release design](RELEASING.md).

## Manifest

```toml
schema = 1
id = "frostpack"
name = "Frost Pack"
version = "1.2.0"
game = ">=0.4 <0.5"
authors = ["Name"]
license = "CC-BY-4.0"
description = "Ice weapons and enemies"
homepage = "https://github.com/..."
multiplayer = "required"
tags = ["weapons", "enemies"]
preview = "preview.png"

[dependencies]
core = ">=0.4"
[optional]
[conflicts]

[provenance]
ai_generated = false
tools = ["LibreSprite"]
third_party = []
```

Use stable lowercase IDs, SemVer versions, a game compatibility range, and an allowed SPDX identifier. The `client-only` multiplayer setting is for cosmetics and excludes the mod from the compatibility hash. Disclose AI asset generation where applicable and record tool, model, and licensed third-party sources.

## Content

The built-in `core` mod (`Game/content/core/`) uses exactly this format. Validation errors name the file, id and field.

| File under `content/` | Data |
|---|---|
| `characters.json` | `id`, `name`, `sprite`, designer `passive` and `growth` text, table `stats` (`maxHp`/`maxHpPerLevel`, `moveSpeed` Move speed, `attackSpeed` Attack speed, `armor` and `magicResist` as fractions, `damage`/`damagePerLevel`), optional basic `attack` override (damage type, speed, range, pierce, count), `passives` |
| `items.json` | `id`, `name`, `rarity` (common, rare, epic, mythic, legendary), `description`, stat `mods` applied once per copy, optional `weapon` (kind `bolt`, `pulse`, `chain` or `blast`, damage type and parameters) |
| `enemies.json` | `id`, sprite and animation, optional tint, HP, `speed` in Move speed, damage and type, `armor`/`magicResist`, `lifesteal`, radius, XP, gold chance, `boss` flag |
| `waves.json` | Wave mix by run-clock minute, HP and damage growth per minute, max alive, timed elites |
| `rules.json` | Arena, XP curve, unit scales (`units`), basic attack defaults, dash, statues, chests with `rarityWeights` (price growth fixed at 1.5) |
| `sprites/*.png` and `*.sprite.json` | Frames, origin, animations, emission, normals |
| `sfx/*.ogg` | Sound effects |

A passive is a level-scaled stat formula: `{"stat": "armor", "add": 0, "pct": 0, "perLevel": {"add": 0.01}, "per": "runMinute"}`. Its value is `base + perLevel × (level − 1)`; with `"per": "runMinute"` it is multiplied by the run clock in minutes. Stats are `(base + Σadd) × (1 + Σpct)`. Damage passes through might, the type multiplier and, for items, item damage, then the target's armor (physical) or magic resist (magical), capped at 90%. Pure damage ignores defences. Units: px/s = Move speed × 3; attacks per second = Attack speed / 25. Items come only from chests and stack. See [the content roadmap](CONTENT_ROADMAP.md) for the systems that later tiers add.

Mods compose parameterized engine behaviors; new behaviors arrive as engine systems listed in the content roadmap. Formula expressions use a deterministic AST with no loops or I/O. StyleLint applies to mod sprites, with TH001–TH003 blocking errors. The loader builds the atlas at runtime.

### Combat effect parameters

The prototype measures distances in world pixels between ground pivots. Circle and cone boundaries are inclusive. Cone angle is the full opening angle and facing follows the last movement direction. Linear radial falloff goes from full strength at the center to zero at the edge. Use the resolved radius multiplier for item and weapon areas. Chest reach and gold pickup use separate distances. The balance sheet values remain design inputs until the playable scale is confirmed.

| Design examples | Runtime parameters and behavior |
|---|---|
| Wooden Stick, Aura, Totem, Motivation Mask | Radius, origin, ally/enemy filter, tick interval, and optional distance falloff. Aura bonuses need to expire on exit. |
| Flamethrower, Fan, Sword | Cone radius and full angle. Use facing or its reverse as the direction. |
| Trident, Magic Projectile, Electric Gloves | Projectile travel distance and speed are separate from hit and bounce radius. A bounce searches from its last impact, with an explicit hop limit and repeat-target rule. |
| Soda, Icicle, Galactic Rifle | Delayed effects carry a location or target and a due tick. Specify whether to reselect a missing target. |
| Rocket, Finisher, Shield, Nettle, Shared Heart, Star of David | Typed hit, kill, received damage, heal, and gold-spend events. Give secondary effects explicit proc and reflection rules to prevent loops. |
| Garden Gnome, Death Eagle, Bait | Summon owner, lifetime, and attack cadence. Keep owner credit on the final hit for enemy gold. |
| Jetpack, Jar of Wind, Snack Bar, Whoopee Cushion | Dash duration, charges, cooldown, and dash-triggered passive effects. |
| Scope | A key enables cursor targeting for supported effects. Effect timing remains automatic. Key binding and activation lifetime are pending. |

The current code supplies area geometry, directional facing, resolved radius and typed damage multipliers, and hit attribution. The remaining behaviors in the table are requirements for the content loader and combat systems. Quantize authored durations to simulation ticks and use seeded rolls for random targets and proc chances.

## Catalog and client

1. PR validation checks schemas, SPDX allowlist (including CC0-1.0, CC-BY-4.0, CC-BY-SA-4.0, MIT, MPL-2.0), path and size limits, PNG dimensions up to 4096×4096, unique IDs, dependency resolution, sprite lint, and provenance. Require DCO sign-off and human review.
2. Merging to main publishes deterministic `<id>-<version>.zip` files to Releases. Pages serves previews and an Ed25519-signed `index.json` with IDs, versions, URLs, sizes, SHA-256 hashes, and descriptions. Game embeds the public key.
3. The client caches the index on disk, revalidates it with If-None-Match, and verifies the signature. It shows preview, description, author, license, compatibility, and install state.
4. The client checks package SHA-256 before extracting to `mods/<id>/<version>/`. Validate paths, reject symlinks, cap size, file count, and compression ratio, and allow only TOML, JSON, PNG, OGG, and Markdown.
5. Use `mods.lock.json` for the selected ID, version, SHA-256, and enabled state. For multiplayer, check the required mod hash and install missing versions to a temporary session profile.

Each mod retains its license and third-party notices. See the Mods contribution guide for rights claims and the roadmap for repository licensing.
