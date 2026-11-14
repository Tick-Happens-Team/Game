# TickHappens Game

TickHappens is a free, open-source, top-down bullet heaven with 16-bit pixel art, dynamic lighting, and planned online co-op for 2–4 players. Difficulty follows the run clock. Time statues advance it.

## Play the prototype

Install the .NET 10 SDK, keep Game and Assets side by side, and use a GPU with OpenGL 4.3. From Game, run:

```powershell
dotnet run --project src/TickHappens.Game
```

Pick one of six heroes from the owner roster (Октарин, Мечник, Кузнец, Гофлин, Небесный маг, Часовой), each with table stats and a passive. The hero shoots automatically, crystals level you up (HP and damage grow per level), and items come only from chests, which open on their own when you can pay (10 gold, then ×1.5 per purchase) and roll by rarity. Time statues add minutes to the run clock and the horde grows with it. The run ends when your HP reaches zero.

| Control | Action |
|---|---|
| WASD / arrows | Move |
| Space | Dash (short invulnerable burst, cooldown bar under the hero) |
| A / D or arrows, Space / Enter | Choose a hero / start the run on the character screen |
| P / R / Esc | Pause / back to hero select (after death or while paused) / quit |

Content (6 heroes, 6 items, enemies, waves, rules) is data in `content/core/*.json`, the built-in `core` mod; the full plan is the [content roadmap](docs/CONTENT_ROADMAP.md). Autopilot and captures: `--demo --character ID --menu --frames N --screenshot PATH --seed S --no-vsync`.

## Online co-op

Windows developer builds can enable online co-op with the official EOS SDK and local credentials. See the [online setup and test instructions](docs/NETWORKING.md#local-windows-eos-build).

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

Options include `--demo --frames N --screenshot PATH --enemies N --no-vsync --manual --art recipe|pxs`.

## Documentation

- [Architecture](docs/ARCHITECTURE.md)
- [Art standard](docs/ART_STANDARD.md)
- [Asset pipeline](docs/ASSET_PIPELINE.md)
- [Releases and launcher](docs/RELEASING.md)
- [Networking](docs/NETWORKING.md)
- [Modding and Workshop](docs/MODDING.md)
- [Content roadmap: characters and items](docs/CONTENT_ROADMAP.md)
- [License and third-party notices](LICENSE.md)
