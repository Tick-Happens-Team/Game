# TickHappens Game

TickHappens is a free, open-source, top-down bullet heaven with 16-bit pixel art, dynamic lighting, and planned online co-op for 2–4 players. Difficulty follows the run clock. Time statues advance it.

## Play the prototype

Install the .NET 10 SDK, keep Game and Assets side by side, and use a GPU with OpenGL 4.3. From Game, run:

```powershell
dotnet run --project src/TickHappens.Game
```

Pick one of six heroes from the owner roster (Florist, Swordsman, Smith, Goflin, Celestial Mage, Watchman), each with table stats and a passive. The hero shoots automatically, crystals level you up (HP and damage grow per level), and items come only from chests, which open on their own when you can pay (10 gold, then ×1.5 per purchase) and roll by rarity. Time statues add minutes to the run clock and the horde grows with it. The run ends when your HP reaches zero.

| Control | Action |
|---|---|
| WASD / arrows | Move |
| Space | Dash (short invulnerable burst, cooldown bar under the hero) |
| W / S or arrows, Space / Enter | Choose a hero / start the run on the character screen |
| F3 | Open volume and control settings on the character screen |
| Esc / P | Open or close the pause menu during a solo run |
| R | Back to hero select after death or from pause |

Content (6 heroes, 6 items, enemies, waves, rules) is data in `content/core/*.json`, the built-in `core` mod; the full plan is the [content roadmap](docs/CONTENT_ROADMAP.md). Autopilot and captures: `--demo --character ID --menu --frames N --screenshot PATH --seed S --no-vsync`.

Measure a dense single-player simulation in Release without an SDL window or the asset atlas:

```powershell
dotnet run -c Release --project src/TickHappens.Game -- --sim-bench ../.tmp/bench/single-player.csv
```

The benchmark keeps 2,000 moving enemies alive around a stationary hero using auto-attacks. It warms up for 300 ticks, writes the next 600 tick timings to CSV, and prints min, average, p99, max, and managed allocations.

## Co-op for 2–4 players

Choose English or Russian from the launcher language selector or press **F4** on the game's character selection screen. Both applications use the same saved language. On first launch they use the system UI language when supported, with English as the fallback.

Press **F1** to host, discover, or join a LAN game by IPv4 address. Windows developer builds also support internet play with the official EOS SDK and local credentials. See the [online setup and test instructions](docs/NETWORKING.md#local-windows-eos-build).

## Windows launcher

The Windows launcher and signed-release tooling are described in the [release guide](docs/RELEASING.md). A signed release supplies the one-time launcher download. The source launch command above also works for development.

## Contributing

Submit changes through a pull request into `dev`. See the [repository workflow](docs/RELEASING.md) for CI, coordinated Assets changes, and promotion to `stable`.

## Run the visual benchmark

Install the .NET 10 SDK and use a GPU with OpenGL 4.3. Keep the Game and Assets directories side by side. Run these commands from Game.

```powershell
dotnet build TickHappens.sln
dotnet test
dotnet run --project tools/TickHappens.AssetTool -- build
dotnet run --project src/TickHappens.VisualBench
```

Set `TICKHAPPENS_ASSETS_ROOT` to the absolute path of an alternate Assets checkout if needed.

| Control | Action |
|---|---|
| WASD | Move |
| Space / E + Space | Explosion / large explosion |
| 1 / 2 / 3 / 4 | Impact / slash / chain lightning / lightning strike |
| 5 / 6 / Q | Time statue / crystal pickup / large explosion |
| F1 / F2 / F3 / F4 | Lighting / bloom / debug views / light bands |
| N / Tab / F12 | Day-night / effects opacity / screenshot |

Options include `--demo --frames N --screenshot PATH --enemies N --no-vsync --manual --art recipe|pxs --report PATH`. The bench writes per-frame interval, CPU, and VFX timings with min/avg/p99 to `.tmp/bench/frames.csv`.

## Documentation

- [Architecture](docs/ARCHITECTURE.md)
- [Art standard](docs/ART_STANDARD.md)
- [Asset pipeline](docs/ASSET_PIPELINE.md)
- [Releases and launcher](docs/RELEASING.md)
- [Networking](docs/NETWORKING.md)
- [Modding and Workshop](docs/MODDING.md)
- [Content roadmap: characters and items](docs/CONTENT_ROADMAP.md)
- [License and third-party notices](LICENSE.md)
