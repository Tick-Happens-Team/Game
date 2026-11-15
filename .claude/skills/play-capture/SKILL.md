---
name: play-capture
description: Run the TickHappens game or visual bench headlessly-scripted and capture screenshots for visual review. Use to verify gameplay, HUD, VFX or rendering changes.
---

From `D:\tickhappens\Game`:

- Game autopilot (deterministic, fixed 60 FPS frame time, seeded):
  `dotnet run -c Release --project src/TickHappens.Game -- --demo --frames N --screenshot ../.tmp/screenshots/NAME.png [--seed S]`
  - 60 frames = 1 s of play. 900 ≈ 15 s, 7200 ≈ 2 min, 14400 ≈ 4 min. The last line prints tick, run clock, level, HP, kills, enemies, gold, loadout.
  - `--dash-every T` makes the autopilot also dash every T ticks (30 ticks/s): a dash at tick 360 is on screen around frames 722-760 for VFX captures.
  - `--record-audio PATH.wav` mixes the run's sound into a WAV (one frame of audio per frame); measure it with Python `wave`. Captures with `--frames` are otherwise silent.
  - `--character ID` picks the hero (octarine, swordsman, smith, goflin, celestial_mage, watchman, nekochan); `--menu --character ID --frames 30` captures the character select screen.
- Visual bench: `dotnet run --project src/TickHappens.VisualBench -- --demo --frames N --screenshot ../.tmp/screenshots/NAME.png [--art recipe|pxs]`.
- Magnify a region: `dotnet run --project tools/TickHappens.AssetTool -- crop IN.png X Y W H ZOOM OUT.png`.
- Open PNGs with the Read tool and actually look: readability of hero vs crowd, HUD overlap, UI artifacts, effect blowout, arena edges.

Interactive controls: hero screen W/S or arrows + Space/Enter, F3 settings; in a solo run WASD/arrows move, Space dash, Esc/P pause, R back to hero select (after death or while paused).
All screenshots stay under `D:\tickhappens\.tmp\`.
