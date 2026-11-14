---
name: play-capture
description: Run the TickHappens game or visual bench headlessly-scripted and capture screenshots for visual review. Use to verify gameplay, HUD, VFX or rendering changes.
---

From `D:\tickhappens\Game`:

- Game autopilot (deterministic, fixed 60 FPS frame time, seeded):
  `dotnet run -c Release --project src/TickHappens.Game -- --demo --frames N --screenshot ../.tmp/screenshots/NAME.png [--seed S]`
  - 60 frames = 1 s of play. 900 ≈ 15 s, 7200 ≈ 2 min, 14400 ≈ 4 min. The last line prints tick, run clock, level, HP, kills, enemies, gold, loadout.
  - `--hold-levelup` stops on the first level-up screen (cards review).
- Visual bench: `dotnet run --project src/TickHappens.VisualBench -- --demo --frames N --screenshot ../.tmp/screenshots/NAME.png [--art recipe|pxs]`.
- Magnify a region: `dotnet run --project tools/TickHappens.AssetTool -- crop IN.png X Y W H ZOOM OUT.png`.
- Open PNGs with the Read tool and actually look: readability of hero vs crowd, HUD overlap, UI artifacts, effect blowout, arena edges.

Interactive controls: WASD/arrows move, SPACE dash, 1-3 pick upgrade, P pause, R new run (after death or while paused), ESC quit.
All screenshots stay under `D:\tickhappens\.tmp\`.
