# TickHappens Game

Follow the workspace AGENTS.md when working in the shared local workspace. This repository owns the .NET engine, game, launcher, AssetTool, shaders, and Actions. Run build and test from this directory. Keep Assets beside Game or set `TICKHAPPENS_ASSETS_ROOT`.

See the [roadmap](ROADMAP.md) and [release design](docs/RELEASING.md). Keep the rendering and gameplay hot paths allocation-free and keep the fixed-step simulation independent of rendering.

Project skills live in `.claude/skills/`: `build-test`, `play-capture`, `add-content`, `art-recipe`, `vibe-delegate`. Use them instead of re-deriving the steps, and add a skill when a workflow repeats.
