# TickHappens Game

TickHappens is a free, open-source, top-down bullet heaven with 16-bit pixel art, dynamic lighting, and planned online co-op for 2–4 players. Difficulty follows the run clock. Time statues advance it.

The visual benchmark and asset pipeline are available today. The playable prototype and launcher are on the [roadmap](ROADMAP.md).

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
- [License and third-party notices](LICENSE.md)
