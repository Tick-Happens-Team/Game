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
| 0.12 | [x] Port remaining effects | P+ | 0.11 | Energy (charge, beam, orb, projectile), Fire, Power-up, and Embers adapted to the engine. Crackle uses the animated actor silhouette. Seeded tests and VisualBench captures cover the new effects. |
| 0.13 | [x] Visual benchmark | P! | 0.6, 0.11 | G-buffer, quantized lighting, emission, CPU VFX, GPU particles, bloom, integer scale, and screenshot capture. Developer PC measured ~550 FPS at 1,500 enemies and ~400 FPS at 3,000 in Release without vsync. |
| 0.14 | [x] Owner selects licenses | P! | — | MIT for Game and the Mods catalog, CC BY-NC 4.0 for original Assets (changed from CC BY 4.0; earlier copies keep CC BY 4.0), with separate third-party terms. License files are in each repository. |
| 0.15 | [ ] Effect intensity profiles | P+ | 0.12 | [x] Port remaining effects | P+ | 0.11 | Energy (charge, beam, orb, projectile), Fire, Power-up, and Embers adapted to the engine. Crackle uses the animated actor silhouette. Seeded tests and VisualBench captures cover the new effects. |
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
| 1.7 | [x] SDL3 audio streams, mixer, voice pool, SFX caps, buses | P+ | 1.1 | 100 hits per frame without clipping or stalls. `AudioMixer` (48 voices, per-clip caps, music and effect buses, looping crossfaded music, peak limiter) is fed from the main thread into an SDL3 stream by `AudioDevice`. `Hundred_hits_per_frame_never_clip_and_respect_the_instance_cap` covers the acceptance; `--record-audio` measured a 30 s demo at about -18 dBFS RMS and 0.73 peak. |
| 1.8 | [~] GPU queries, frame overlay, min/avg/p99 reports | P+ | 1.4 | Bench writes per-frame interval, CPU, and VFX timings with min/avg/p99 to `.tmp/bench/frames.csv` and shows CPU p99 in the title. GPU timer queries remain. |
| 1.9 | [x] Bitmap font and UI layer above upscale | P! | 1.4 | Built-in 5×7 PixelFont and screen-space UiFrame composited after tonemapping. HUD, damage numbers and cards verified in captures at ×2. Cyrillic glyphs pending 6.2. |
| 1.10 | [ ] Development hot reload of shaders and atlas | P- | 1.4 | Edits to frag or indexed art appear without restart. |
| 1.11 | [~] Self-contained win-x64 game ZIP and separate Assets pack | P+ | 1.1, 2.1 | Signed local packages install and run from a separate directory. Clean Windows acceptance remains. |

## P2. Single-player prototype

| ID | Task | Priority | Depends on | Acceptance |
|---|---|---|---|---|
| 2.1 | [x] Game project, struct-of-arrays world, handles, systems, and seeded RNG | P! | 1.3 | RunWorld with SoA enemies (free-list slots, uids), projectiles, pickups, xorshift SimRng, StateHash. Same seed gives identical hashes after 3,000 ticks; different render rates give identical state. |
| 2.2 | [x] Data loader for built-in core mod | P! | 2.1 | content/core JSON for characters, weapons, items, enemies, waves, rules. ContentLoader reports file, id and field for unknown references, stats, wave gaps, duplicates and the fixed 1.5 chest growth. |
| 2.3 | [x] Movement, enemy collisions, contact damage, HP, and death | P! | 2.1 | Seek plus SpatialGrid separation, contact damage with armor and i-frames, regen, death screen with results and R for a new run. |
| 2.3a | [x] Dash/dodge | P+ | 2.3 | Space dashes along facing with invulnerability and cooldown bar. Movement and dash are the only manual gameplay actions. |
| 2.4 | [x] Projectile, area, and chain-lightning auto-attacks with VFX | P! | 2.2, 0.12 | Data-driven bolt (pierce, multi-target), pulse, chain (hops), and blast (densest enemy cluster in range). Item copies upgrade attacks. Impact, lightning, explosion, and particle VFX consume combat events. |
| 2.5 | [x] RunClock and time-based waves, stats, and elites | P! | 2.2 | Run clock = ticks + statue bonus; wave table without gaps, HP/damage scaling per minute, timed elites. Tests cover scaling and elite spawns after a statue advances the clock. Boss encounters are tracked in 5.3. |
| 2.6 | [x] Time statues advance RunClock | P! | 2.5 | Standing near a statue for 1 s adds 2 minutes, HUD shows the bonus and a notice; tests verify BonusSeconds and HpScale. |
| 2.7 | [x] XP drops, magnet, level-up, and upgrade choice | P! | 2.3 | Crystals with magnet pull and XP curve. Owner decision: level-ups apply table growth automatically and level-up cards were removed. The prototype awards items from chests; cursed boss drops are planned in P5. |
| 2.8 | [x] Ordered additive and multiplicative StatBlock modifiers and event-based passives | P! | 2.2 | (base + Σadd) × (1 + Σpct), cooldown floor, order-independent; levelUp passive (+1 armor per level) tested. |
| 2.9 | [x] Chests and loot tables | P+ | 2.8 | Chests open passively when affordable and grant one item rolled by rarity weights (rules.json); copies stack. Killer-owned gold with the gold multiplier, 1.5× price per player. |
| 2.10 | [x] HUD clock, level, HP, XP, and results screen | P+ | 1.9 | XP bar, level, run clock with statue bonus, gold and chest price, kills, loadout, HP and dash bars, notices, pause and death results. |
| 2.11 | [x] Simulation performance | P! | 2.3 | Headless Release benchmark reports per-tick timings for 2,000 active enemies. Local run: 1.45 ms average, 2.24 ms p99, zero allocations over 600 measured ticks. GPU/frame profiling is tracked in 1.8. |

## P3. Co-op for 2–4 players

| ID | Task | Priority | Depends on | Acceptance |
|---|---|---|---|---|
| 3.1 | [x] Transport contract, loopback tests, versioned messages | P! | 2.1 | Host and three clients negotiate slots in one process. |
| 3.2 | [x] Client input, delta/quantized snapshots, local prediction | P! | 3.1 | Hosts send 10 Hz keyframe-relative snapshots and acknowledge input. Clients predict their own movement and replay unacknowledged input. Loopback benchmark at 1,000 enemies measured 70.0 kB/s outbound to one client, 73.5 ms average and 78.6 ms p99 input acknowledgement. |
| 3.3 | [~] Statue, damage, death/drop seed, chest, and level events | P! | 3.2 | Host snapshots carry run time, entities, and per-player rewards. Reliable event batches carry combat and progression events, chain points, cosmetic seeds, and enemy drop seeds. UDP and serialization tests cover delivery. Verify the full set of effects in a multiplayer run. |
| 3.4 | [~] LiteNetLib LAN discovery and direct IP | P! | 3.1 | Shipped in standard builds. UDP tests cover discovery, direct IP, input, snapshots, and disconnect on one machine. Verify on two computers on the same LAN. |
| 3.5 | [~] EOS Device ID, room code, P2P relay, and release SDK | P! | 3.1 | Windows SDK build signs in, creates, finds by code, and closes a real EOS lobby. The signing packer can include the pinned EOS SDK and notices. Validate a full join and run from independent networks, and wire the packer to the protected publishing job. |
| 3.6 | [~] Reconnect within 60 seconds and handle host departure | P+ | 3.3 | The host keeps disconnected player slots and progress for 60 seconds. Loopback and UDP tests cover resumed slots, expiration, and host departure. Verify interrupted play on separate machines and through EOS. |
| 3.7 | [~] Required mod compatibility hash | P! | 3.1, 4.2 | Handshake rejects mismatched loaded gameplay data. Exact mod retrieval remains. |
| 3.8 | [~] Relay-only setting | P+ | 3.5 | The EOS menu can select ForceRelays before connecting. Cross-network verification remains. |
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

Depends on P2. The owner's [Google Sheet](https://docs.google.com/spreadsheets/d/1q4DUxTFMWcSg_zevYYDIWf2DWFH_v2YpFHKe_LTDcOM/edit) is the source of truth for content design. The [content roadmap](docs/CONTENT_ROADMAP.md) orders the 17 characters, 62 items and 23 enemies by ease of implementation, lists the engine systems (S0–S13 for heroes and items, E0–E7 for enemies) they need, groups them into milestones, and records owner decisions and open questions. Content is data-driven, with stat application tests for each item and character.

- [~] 5.1 Characters with unique passives: six C1 heroes (Octarine, Swordsman, Smith, Goflin, Celestial Mage, Watchman) and Nekotyan (C4, ally aura) are playable with sprites and a character select screen. Apply base HP regeneration to C1 and sync Watchman’s attack stats and crit growth. Ten heroes remain (C2–C4).
- [~] 5.2 Weapons, items, evolutions, and combinations: chest loot stacks and six I1 items are playable. Sync four I1 effects and chest rarity chances to the sheet. I2–I6 and five cursed boss drops (I7) remain.
- [ ] 5.3 Enemies and bosses based on run time: 23 owner enemies, including five bosses, in tiers E1–E5 (see the content roadmap).
- [ ] 5.4 Balance using local run logs under .tmp/.
- [ ] 5.5 Metaprogression pending owner decision.
- [x] 5.6 Projectile splash: bolts damage the nearest enemies within a 30 px radius of the impact instead of only the primary target. Acceptance: a bolt landing in a cluster damages every enemy within 30 px, damage falls off with distance, and a test covers the radius and the falloff. Implemented as `RunWorld.SplashRadius` with linear falloff from full damage at the impact point to 50% at the rim, matching the blast falloff floor. The primary target and earlier pierce hits are excluded through the projectile hit memory, so no enemy takes the bolt twice. `BoltSplashDamagesNearbyEnemiesWithFalloff` covers the radius boundary, the falloff ordering, and the no-double-dip rule.

## P6. Polish and release

- [~] 6.1 Sound and music. AssetTool `audio` renders 17 effect recipes and two music recipes (run and hero select) from Assets into WAV; combat, pickup, progression, dash and menu events play panned by position. Owner listening review, settings volume controls and more tracks for later run phases remain.
- [~] 6.10 Presentation pass. The camera shows at least 426×240 art pixels (×3 at 720p, ×4 at 1080p) with an eased look-ahead; the UI keeps its 640×360 layout at its own scale. Dusk look with moonlight on normal maps, drifting cloud shadows, contact shadows, fireflies, event light flashes, a split-toned grade, diorama tilt-shift, hit aberration and screen flashes. Dash plays `DashSplashEffect` (shockwave, droplet crown with landing ripples, speed streaks), afterimages, sparks and a camera kick. Verified in captures; tuning with the owner and the settings toggles from 6.2 remain.
- [~] 6.2 Game and launcher have English and Russian localization with a shared language preference. Menu expansion, scale, effect brightness, VfxOpacity, and screen shake settings remain.
- [ ] 6.3 Profile and optimize. Consider SDL_GPU for macOS if needed.
- [~] 6.4 Bootstrap CI for Game and Assets, with an atlas release and a Windows benchmark artifact. `release.yml` signs and stages a draft, publishes without latest, verifies downloads, and then promotes. It resumes interrupted drafts and rejects stale sequences. Remaining: run the first release from stable and smoke-test the exact package pair in CI.
- [ ] 6.6 End-to-end acceptance after 3.5, 4.4, 6.4, and 6.9. Install successive Releases on clean Windows, play offline and through CGNAT, and install a compatible Workshop mod.
- [~] 6.7 Public Game, Assets, and Mods repositories in Tick-Happens-Team, with dev/stable for Game and Assets and main for Mods. Complete release tag rules, the signing environment, and Mods Pages alongside their publishing workflows.
- [~] 6.8 Local Game/Assets split and checkout selection are in place. Pin the exact Assets revision for CI and publish the migration after 6.7.
- [~] 6.9 Windows launcher, signed release packaging, game and launcher updates, and local item mod selection. The launcher UI uses SDL3 and OpenGL with the engine's pixel UI layer: Play, Mods, and Settings pages, a box-cover play page with the playable heroes, installed versions, download progress, and a play button. A local loopback release signed with a test key installs, verifies, and reports the game as current. The installed game passed the EOS probe using the packaged `eos.json`. The protected `release` environment and `release.yml` exist, but no release has been published. Remaining: the private eos-sdk repository and its deploy key, player-facing rollback, and clean Windows release acceptance.
- [~] 6.11 Polished menus. The hero screen is restyled: a roster with each hero's sprite, the selected hero's in-game sprite enlarged ×5 with its idle animation on a lit pedestal (`HeroPreview` draws atlas sprites on the CPU UI layer), a framed stats, passive and growth panel, mouse hover, click to select and a start button. A styled title menu (play, online, settings, quit), screen transitions and gamepad navigation (after 1.2) remain. Large separate showcase art and status-bar portraits were tried and dropped by the owner in favour of the in-game sprite.

See [release design](docs/RELEASING.md) for the publication and update contracts.
