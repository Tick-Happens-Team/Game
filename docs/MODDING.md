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

| Path under `content/` | Data |
|---|---|
| `characters/*.json` | Sprite, stats, starter weapon, passive rule |
| `weapons/*.json` | Attack pattern, upgrades, VFX recipe |
| `items/*.json` | StatBlock modifiers |
| `enemies/*.json` | Sprite, stats, behavior, drops |
| `waves/*.json` | Wave additions indexed by RunClock |
| `sprites/*.png` and `*.sprite.json` | Frames, origin, animations, emission, normals |
| `sfx/*.ogg` | Sound effects |

Mods compose parameterized engine behaviors. For example, `{"rule":"on_level_up","effect":"add_stat","stat":"armor","value":1}`. Formula expressions use a deterministic AST with no loops or I/O. StyleLint applies to mod sprites, with TH001–TH003 blocking errors. The loader builds the atlas at runtime.

## Catalog and client

1. PR validation checks schemas, SPDX allowlist (including CC0-1.0, CC-BY-4.0, CC-BY-SA-4.0, MIT, MPL-2.0), path and size limits, PNG dimensions up to 4096×4096, unique IDs, dependency resolution, sprite lint, and provenance. Require DCO sign-off and human review.
2. Merging to main publishes deterministic `<id>-<version>.zip` files to Releases. Pages serves previews and an Ed25519-signed `index.json` with IDs, versions, URLs, sizes, SHA-256 hashes, and descriptions. Game embeds the public key.
3. The client caches the index on disk, revalidates it with If-None-Match, and verifies the signature. It shows preview, description, author, license, compatibility, and install state.
4. The client checks package SHA-256 before extracting to `mods/<id>/<version>/`. Validate paths, reject symlinks, cap size, file count, and compression ratio, and allow only TOML, JSON, PNG, OGG, and Markdown.
5. Use `mods.lock.json` for the selected ID, version, SHA-256, and enabled state. For multiplayer, check the required mod hash and install missing versions to a temporary session profile.

Each mod retains its license and third-party notices. See the Mods contribution guide for rights claims and the roadmap for repository licensing.
