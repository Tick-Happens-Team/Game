# Roadmap

[x] Verified · [ ] Pending · [~] Partial. P! is a prerequisite, P+ important, P- optional. Follow P0 → P1 → P2 → P3. P4 follows P2 and shares its content format. P5 and P6 proceed alongside P3/P4 as dependencies allow. P0 is the current priority.

## P0. Visual research and asset pipeline

| ID | Task | Priority | Depends on | Acceptance |
|---|---|---|---|---|
| 0.1 | [x] Evaluate Sprite Fusion | P+ | — | MIT VFX ported. Paid external art generation ruled out. |
| 0.4 | [x] Art standard | P! | — | Resolution, palette, lighting, readability, animation, and VFX documented. |
| 0.5 | [x] 50-color palette v1 | P! | 0.4 | Palette in Assets. |
| 0.6 | [x] AssetTool for indexed art, tiles, normals, atlas, previews, import, lint, snap, and crop | P! | 0.4 | Build produces albedo, emission, and normals without lint errors. |
| 0.7 | [x] TH001–TH007 style lint | P! | 0.6 | Errors block builds, upscale and alpha tests pass. |
| 0.8 | [x] Hand-authored reference set | P+ | 0.5 | Hero idle/walk, two enemies, statue, chest, XP crystal, grass and stone checked in bench. |
| 0.9 | [x] Procedural recipes and gen | P! | 0.6 | Bones, primitives, ramps, generated lighting, decals, selective outlines, animation, and seed variants. Hero idle/walk/cast, bat flight, and slime idle/hop pass tests, lint, and bench review. |
| 0.9a | [x] PixelCleanup and TH008–TH011 | P! | 0.9 | Tests cover detached pixels, holes, clipping, and silhouette jumps, including indexed inputs. |
| 0.9b | [x] Evaluate ComfyUI and pixel-art LoRAs | P+ | — | Rejected over small-sprite quality, animation, licensing, and footprint. |
| 0.10 | [x] Review recipes against reference art | P+ | 0.9 | Reviewed bench recipe and pxs modes. Improved lighting, contact shadows, silhouettes, bat ramps, and casting. |
| 0.11 | [x] Port Sprite Fusion VFX core and explosion | P! | — | PixelFrame, PixelCanvas, VfxMath, and ExplosionEffect. RNG matches original and effect renders. |
| 0.12 | [~] Port remaining effects | P+ | 0.11 | Impact, slash, lightning strike/arc/impact, sparkle, and pickup work. Crackle lacks Actor.silhouette. Energy, fire, power-up, and embers remain. Each needs build, RNG test, bench control, and screenshot. |
| 0.13 | [x] Visual benchmark | P! | 0.6, 0.11 | G-buffer, quantized lighting, emission, CPU VFX, GPU particles, bloom, integer scale, and screenshot capture. Developer PC measured ~550 FPS at 1,500 enemies and ~400 FPS at 3,000 in Release without vsync. |
| 0.14 | [x] Owner selects licenses | P! | — | MIT for Game and the Mods catalog, CC BY 4.0 for original Assets, with separate third-party terms. License files are in each repository. |
| 0.15 | [ ] Effect intensity profiles | P+ | 0.12 | Tune per-effect light and bloom and cap flashes near the player. A chain-lightning capture shows hero and enemies within 24 px in every frame. |
| 0.16 | [ ] Recipe generator extensions | P+ | 0.9 | Angled bones and parts, secondary motion, wing folding, rim light and hue shift, indexed frame patches, and four directions. A new test enemy passes lint and meets the hand-authored reference, including bats in night crowds. |

## P1. Engine

| ID | Task | Priority | Depends on | Acceptance |
|---|---|---|---|---|
| 1.1 | [x] SDL3 window, GL 4.3 context, keyboard | P! | — | Bench opens and responds. |
| 1.2 | [ ] SDL3 gamepad and rebinding | P+ | 1.1 | Stick movement and menu navigation. |
| 1.3 | [x] 30 Hz fixed step, interpolation, and pause | P! | 1.1 | Simulation produces identical positions at different render rates. |
| 1.4 | [x] Instanced SpriteBatch, atlas, animation | P! | — | About 2,850 sprites per draw in the bench. |
| 1.5 | [ ] Back and front CPU VFX passes | P+ | 0.11 | Scorch below enemies and sparks above in a screenshot. |
| 1.6 | [ ] Bake VFX flipbooks into a VFX atlas | P+ | 0.11 | 200 concurrent explosions use ≤2 ms CPU per frame. |
| 1.7 | [ ] SDL3 audio streams, mixer, voice pool, SFX caps, buses | P+ | 1.1 | 100 hits per frame without clipping or stalls. |
| 1.8 | [ ] GPU queries, frame overlay, min/avg/p99 reports | P+ | 1.4 | Bench writes timings to `.tmp/bench/`. |
| 1.9 | [ ] Bitmap font and UI layer above upscale | P! | 1.4 | Damage numbers and HUD readable at ×2–×6. |
| 1.10 | [ ] Development hot reload of shaders and atlas | P- | 1.4 | Edits to frag or indexed art appear without restart. |
| 1.11 | [ ] Self-contained win-x64 game ZIP and separate Assets pack | P+ | 1.1, 2.1 | Locally packaged pair runs on clean Windows without .NET SDK. |

## P2. Single-player prototype

| ID | Task | Priority | Depends on | Acceptance |
|---|---|---|---|---|
| 2.1 | [~] Game project, struct-of-arrays world, handles, systems, and seeded RNG | P! | 1.3 | Standalone prototype and 10,000-tick movement/chest test. General entity storage, handles, seeded RNG, and state hashes remain. |
| 2.2 | [ ] Data loader for built-in core mod | P! | 2.1 | Characters, weapons, enemies, and waves load from JSON with clear validation errors. |
| 2.3 | [~] Movement, enemy collisions, contact damage, HP, and death | P! | 2.1 | Player movement and arena bounds work. Enemy collisions, damage, HP, and death remain. |
| 2.3a | [ ] Dash/dodge | P+ | 2.3 | Movement and dash/dodge are the only manual gameplay actions. Attacks, items, and interactions fire passively. |
| 2.4 | [ ] Projectile, area, and chain-lightning auto-attacks with VFX | P! | 2.2, 0.12 | JSON-defined weapons and upgrades work. |
| 2.5 | [ ] RunClock and time-based waves, stats, and bosses | P! | 2.2 | Wave composition matches tables at minutes 0, 5, and 10. |
| 2.6 | [ ] Time statues advance RunClock | P! | 2.5 | Difficulty jumps and HUD reflects it. |
| 2.7 | [ ] XP drops, magnet, level-up, and upgrade choice | P! | 2.3 | Three upgrade choices update StatBlock. |
| 2.8 | [ ] Ordered additive and multiplicative StatBlock modifiers and event-based passives | P! | 2.2 | Combination tests cover examples such as +1 armor on level-up. |
| 2.9 | [~] Chests and loot tables | P+ | 2.8 | Three shared arena chests open automatically when the nearby player can pay. Gold drops belong to the killer or the killing summon’s owner, and prices scale 1.5× per player. Item pools and rewards follow the item system. |
| 2.10 | [ ] HUD clock, level, HP, XP, and results screen | P+ | 1.9 | Complete run flow. |
| 2.11 | [ ] Simulation performance | P! | 2.3 | Bench confirms ≤4 ms per tick with 2,000 enemies. |

## P3. Co-op for 2–4 players

| ID | Task | Priority | Depends on | Acceptance |
|---|---|---|---|---|
| 3.1 | [ ] ITransport, loopback, versioned compressed messages | P! | 2.1 | Host and three clients in one process. |
| 3.2 | [ ] Client input, delta/quantized snapshots, local prediction | P! | 3.1 | ≤150 KB/s per client with 1,000 enemies and responsive input at 100 ms. |
| 3.3 | [ ] Statue, damage, death/drop seed, chest, and level events | P! | 3.2 | RunClock and loot match. Effects stay local. |
| 3.4 | [ ] LiteNetLib LAN discovery and direct IP | P! | 3.1 | Two computers on one LAN can play. |
| 3.5 | [ ] EOS Device ID, room code, P2P relay, and release SDK | P! | 3.1 | Clean Windows installs join over separate NAT/CGNAT networks without registering or using a third-party launcher. Test forced relay. |
| 3.6 | [ ] Reconnect within 60 seconds and handle host departure | P+ | 3.3 | Verify by interrupting a connection. |
| 3.7 | [ ] Required mod compatibility hash | P! | 3.1, 4.2 | Explain mismatches or download exact packages. |
| 3.8 | [ ] Relay-only setting | P+ | 3.5 | Option avoids sharing peer IP addresses. |
| 3.9 | [ ] Assess WebRTC, signaling, and TURN if EOS is blocked | P- | 3.5 | Check relay availability, cost, and account-free access. |

## P4. Mods and Workshop

| ID | Task | Priority | Depends on | Acceptance |
|---|---|---|---|---|
| 4.1 | [ ] mod.toml and JSON schemas | P! | 2.2 | Format documented and schemas under schemas/. |
| 4.2 | [ ] Loader with dependencies, conflicts, order, namespaces, runtime atlas, and lint | P! | 4.1 | Data-only example adds character, weapon, and enemy. |
| 4.3 | [ ] Safe install with SHA-256, path guards, limits, and extensions | P! | 4.2 | Archive validation tests pass. |
| 4.4 | [ ] Pages catalog client with ETag, cache, and Ed25519 verification | P! | 4.3 | Mods menu previews, installs, toggles, updates, and removes. |
| 4.5 | [~] Mods scaffold, validator CI, release ZIPs, signed Pages index, DCO | P! | 4.1, 0.14 | Local docs ready. Validate a sample mod before publication after 6.7. |
| 4.6 | [ ] Evaluate Jint or Wasmtime sandbox if needed | P- | 4.2 | Decision and prototype. |

## P5. Content and progression

Depends on P2 and the owner's content list. Content is data-driven, with stat application tests for each item and character.

- [ ] 5.1 Characters with unique passives.
- [ ] 5.2 Weapons, items, evolutions, and combinations.
- [ ] 5.3 Enemies and bosses based on run time.
- [ ] 5.4 Balance using local run logs under .tmp/.
- [ ] 5.5 Metaprogression pending owner decision.

## P6. Polish and release

- [ ] 6.1 Sound and music.
- [ ] 6.2 Menus, scale, effect brightness, VfxOpacity, screen shake, and RU/EN localization.
- [ ] 6.3 Profile and optimize. Consider SDL_GPU for macOS if needed.
- [~] 6.4 Bootstrap CI for Game and Assets, with an atlas release and a Windows benchmark artifact. Complete signed game and launcher publication after 1.11, 6.8, and 6.9. Validate the exact package pair and recover from interrupted publishes.
- [ ] 6.6 End-to-end acceptance after 3.5, 4.4, 6.4, and 6.9. Install successive Releases on clean Windows, play offline and through CGNAT, and install a compatible Workshop mod.
- [~] 6.7 Public Game, Assets, and Mods repositories in Tick-Happens-Team, with dev/stable for Game and Assets and main for Mods. Complete release tag rules, the signing environment, and Mods Pages alongside their publishing workflows.
- [~] 6.8 Local Game/Assets split and checkout selection are in place. Pin the exact Assets revision for CI and publish the migration after 6.7.
- [ ] 6.9 Finish Distribution and ReleaseTool and implement the launcher after 1.11. Test clean install, one-package updates, tampering, interrupted updates, repeat installs, rollback, and offline launch.

See [release design](docs/RELEASING.md) for the publication and update contracts.
