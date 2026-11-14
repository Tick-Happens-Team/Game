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

- `TickHappens.Engine` owns SDL3, OpenGL resources, SpriteBatch, PixelRenderer, GPU particles, VFX, images, palettes, atlases, and SpatialGrid.
- `TickHappens.Game` runs the single-player prototype. RunWorld owns movement and chest state, RunLoop advances the world at 30 Hz, and RunView renders interpolated movement.
- `TickHappens.VisualBench` exercises rendering and crowds. See the [Game README](../README.md) for arguments.
- `TickHappens.AssetTool`, `TickHappens.Distribution`, `TickHappens.ReleaseTool`, and `TickHappens.Tests` cover asset generation, package contracts, release tooling, and tests. ReleaseTool is still a scaffold.
- Planned projects cover network transports and snapshots and mod validation and installation.

Game depends on Engine. Net depends on the game protocol and Engine. The engine is genre-independent.

## Render pipeline

Rendering uses 640×360 art pixels and two pixels of padding on each side for fractional camera movement.

1. The RGBA8 MRT G-buffer stores albedo, normals, and emission. SpriteBatch draws up to 65,536 instanced quads per flush with one atlas and an alpha test using `texelFetch`. Draw terrain first and actors in Y order. Instance flags handle X mirroring and emission. `tint.a < 1` supplies a white hit flash.
2. CPU effects draw camera-relative color and light to PixelFrame and upload two textures.
3. Lighting uses RGBA16F. Albedo is multiplied by ambient and accumulated point lights using N·L. Six light levels use 4×4 Bayer dithering. The 64 lights nearest the camera are used. The VFX light layer illuminates the world, the color layer is self-lit, and HDR emission is multiplied by 1.8.
4. GPU particles use an SSBO ring of 131,072 entries and compute integration. They draw one-pixel additive points and strokes using GL_LINES in HDR.
5. Bloom uses five levels, soft-threshold downsampling, and bilinear tent upsampling.
6. The final pass uses integer upscaling with fractional camera offset and texelFetch, followed by bloom and a vignette. Tone mapping rolls off only values above 0.82 to preserve palette colors in neutral lighting.

Debug controls include lighting (F1), bloom (F2), G-buffer and VFX light views (F3), light bands (F4), effect opacity (Tab), and screenshots (F12). CPU effects currently draw above all actors, including scorch marks. P1 introduces separate back and front passes.

## Simulation

The prototype advances movement and chest interactions at a deterministic 30 Hz with render interpolation. The platform loop supplies elapsed time and input. RunWorld owns the result of each tick and records the last chest opened for presentation. P2 plans struct-of-arrays entity storage by type and handles with an index and generation. SpatialGrid uses allocation-free O(n) counting sort for crowd separation, contact damage, and area queries.

RunClock tracks elapsed simulation time plus time statue bonuses. Wave composition, stats, and bosses use this clock. A `StatueActivated{id, minutes}` event advances it immediately. StatBlock applies ordered additive and multiplicative modifiers. Passives subscribe to events such as OnLevelUp. Seed each system's RNG. The host owns authoritative state while clients predict their own movement.

See [networking](NETWORKING.md) and [modding](MODDING.md). The base game uses the same data loader as mods through built-in `core`.

## Profiling

The bench title shows FPS, CPU VFX time, sprites, enemies, lights, and scale. P1 adds GPU timer queries per pass, a frame-time graph, and min/avg/p99 reports under `.tmp/bench/`.
