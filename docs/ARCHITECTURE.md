# Architecture

TickHappens uses a specialized engine. Low-level libraries handle platform, graphics, and codecs. Add abstractions when a second implementation is planned.

| Area | Decision |
|---|---|
| Language and runtime | C# / .NET 10, unsafe blocks for GPU buffers, warnings as errors |
| Window, input and audio | SDL3 through SDL3-CS 3.4.20 and SDL3-CS.Native (zlib) |
| GPU | OpenGL 4.3 core through Silk.NET.OpenGL 2.23 (MIT). Compute shaders and SSBOs support GPU particles. SDL_GPU is a P6 option if macOS support requires it. |
| Images | StbImageSharp and StbImageWriteSharp for PNG (public domain) |
| Audio | SDL3 streams and a custom mixer with pooled voices and per-effect limits (P1) |
| Network | Loopback, LiteNetLib (MIT), optional proprietary EOS SDK in official release builds |
| Tests | xUnit |

Veldrid was dropped over maintenance concerns. raylib-cs offered insufficient control and lacked the compute pipeline needed here. FNA and MonoGame exceed the intended scope of the engine stack. Steamworks does not fit the distribution model. SIPSorcery was ruled out over licensing restrictions.

## Projects

`TickHappens.Localization` owns the UTF-8 `en.json` and `ru.json` resource catalogs shared by the game and launcher. Use stable keys and numbered format arguments for player-facing text. Add both translations together. Core character and item text uses `content.<type>.<id>.<field>` keys. Mod-provided names and descriptions remain valid fallbacks. Keep localization resources and preferences outside the gameplay content hash.

Both applications persist the selected language in `%LOCALAPPDATA%/TickHappens/language.txt`. For a capture or development run, set `TICKHAPPENS_LANGUAGE=en` or `TICKHAPPENS_LANGUAGE=ru`. Each application can change the selection on its own interface.

The launcher uses the engine's SDL3 window and OpenGL 4.3 context. It draws its UI on the CPU into a 640×360 pixel layer with the built-in pixel font, so it needs no Assets pack or shader directory before the first install. A small embedded blit program shows that layer at an integer scale. The banner scene is procedural. The launcher is published self-contained and adds no dependencies beyond the engine's.

- `TickHappens.Engine` owns SDL3, OpenGL resources, SpriteBatch, PixelRenderer, GPU particles, VFX, images, palettes, atlases, and SpatialGrid.
- `TickHappens.Game` runs the single-player game.
  - `Content/` loads and validates the data-driven `core` mod (`content/core/*.json`): characters (owner table stats and level-scaled passives), items (rarity, stat mods, optional weapon), enemies, waves, rules (unit scales, basic attack, chest rarity weights).
  - `Sim/` is the deterministic 30 Hz simulation. `RunWorld` owns the run clock (ticks + statue bonus), waves and elites, struct-of-arrays enemies with free-list slots, projectiles, pickups with owner-only gold, the hero basic attack (auto-aimed bolts at the Attack speed rate), item weapons (`bolt`, `pulse`, `chain`, `blast`), the damage pipeline (might × type multiplier × item bonus × (1 − target armor or magic resist), pure ignores defences, cap 90%), enemy lifesteal, `StatBlock` ((base + Σadd + Σdynamic) × (1 + Σpct + Σdynamic pct)) with passives recomputed per tick from level and run clock, automatic level growth, chests that grant rarity-weighted stacking items, and statues. It emits `GameEvent`s for cosmetics and never reads clocks or GL. `DemoPilot` drives `--demo` and long-run tests.
  - `RunLoop` advances the world at 30 Hz; `RunView` renders interpolated state and turns events into local VFX (portable Sprite Fusion ports, GPU particles) under a CPU-effect budget; `Hud` draws the HUD and the character select screen with the built-in Latin/Cyrillic pixel font.
- `TickHappens.VisualBench` exercises rendering and crowds. See the [Game README](../README.md) for arguments.
- `TickHappens.AssetTool`, `TickHappens.Distribution`, `TickHappens.ReleaseTool`, and `TickHappens.Tests` cover asset generation, package contracts, release tooling, and tests. ReleaseTool is still a scaffold.
- Planned projects cover network transports and snapshots and mod validation and installation.

Game depends on Engine. Net depends on the game protocol and Engine. The engine is genre-independent.

## Render pipeline

Rendering uses an integer scale chosen so the world view is at least `MinViewW × MinViewH` art pixels (640×360 in the bench, 426×240 in the game), plus two pixels of padding on each side for fractional camera movement. A screen-space UI layer (`PixelRenderer.UiFrame`, built-in 5×7 `PixelFont`) keeps the 640×360 layout at its own integer scale (`UiScale`, `UiW × UiH`) and is composited after grading without the camera offset, so HUD text stays crisp and the HUD layout does not change with the world zoom.

1. The RGBA8 MRT G-buffer stores albedo, normals, and emission. SpriteBatch draws up to 65,536 instanced quads per flush with one atlas and an alpha test using `texelFetch`. Draw terrain first and actors in Y order. Instance flags handle X mirroring and emission. `tint.a < 1` supplies a white hit flash.
2. CPU effects draw camera-relative color and light to PixelFrame and upload two textures.
3. After terrain, contact shadows (`shadow.frag`, dithered ellipses) multiply into the albedo attachment only (`BeginAlbedoMultiply`).
4. Lighting uses RGBA16F. Albedo is multiplied by ambient, a directional key light (`Sun*`) on the normal maps, drifting value-noise cloud shadows (`Clouds`) and accumulated point lights using N·L. Six light levels use 4×4 Bayer dithering. The 64 lights nearest the camera are used. The VFX light layer illuminates the world, the color layer is self-lit, and HDR emission is multiplied by 1.8.
5. GPU particles use an SSBO ring of 131,072 entries and compute integration. They draw one-pixel additive points and strokes using GL_LINES in HDR.
6. Bloom uses five levels, soft-threshold downsampling, and bilinear tent upsampling.
7. The final pass uses integer upscaling with fractional camera offset and texelFetch, an optional diorama tilt-shift blur toward the top and bottom, per-frame chromatic aberration and flash, bloom, a highlight shoulder above 0.82, then a grade (split toning, contrast, saturation, lift) and a vignette. Defaults leave the grade neutral so palette colors stay exact in the bench.

Debug controls include lighting (F1), bloom (F2), G-buffer and VFX light views (F3), light bands (F4), effect opacity (Tab), and screenshots (F12). CPU effects currently draw above all actors, including scorch marks. P1 introduces separate back and front passes.

## Audio

`TickHappens.Engine.Audio` has no new dependencies. `WavFile` reads and writes 16-bit PCM. `AudioMixer` mixes stereo at 32 kHz without a device: 48 pooled voices with linear-interpolated pitch, a per-clip instance cap that replaces the oldest copy, music and effect buses, two music slots for crossfades, and a limiter (instant attack, 150 ms release, soft knee) that keeps the output below full scale. `AudioDevice` opens the default SDL3 playback stream and tops its queue up from the main thread to max(70 ms, 1.5 frames) of audio, so the mixer never runs on the audio thread. The game's `GameAudio` maps simulation events to effects with distance attenuation and equal-power panning around the local hero, caps hits, kills and pickups per frame, raises the pitch of crystal streaks, ducks music while paused and after death, and switches between the hero-select and run tracks. Audio is cosmetic and never networked. Runs with `--frames` stay silent unless `--record-audio PATH` mixes exactly one frame of audio per rendered frame into a WAV for measurement.

## Simulation

The prototype advances movement and chest interactions at a deterministic 30 Hz with render interpolation. The platform loop supplies elapsed time and input. RunWorld owns the result of each tick and records the last chest opened for presentation. P2 plans struct-of-arrays entity storage by type and handles with an index and generation. SpatialGrid uses allocation-free O(n) counting sort for crowd separation, contact damage, and area queries.

RunClock tracks elapsed simulation time plus time statue bonuses. Wave composition, stats, and bosses use this clock. A `StatueActivated{id, minutes}` event advances it immediately. StatBlock applies ordered additive and multiplicative modifiers. Passives subscribe to events such as OnLevelUp. Seed each system's RNG. The host owns authoritative state while clients predict their own movement.

See [networking](NETWORKING.md) and [modding](MODDING.md). The base game uses the same data loader as mods through built-in `core`.

## Profiling

The bench title shows FPS, CPU frame time with p99, CPU VFX time, sprites, enemies, lights, and scale. It writes per-frame interval, CPU, and VFX timings with min/avg/p99 to `.tmp/bench/frames.csv`, and `--report PATH` overrides the path. P1 still adds GPU timer queries per pass and a frame-time graph.
