# TickHappens Game

Follow the workspace AGENTS.md when working in the shared local workspace. This repository owns the .NET engine, game, launcher, AssetTool, shaders, and Actions. Run build and test from this directory. Keep Assets beside Game or set `TICKHAPPENS_ASSETS_ROOT`.

See the [roadmap](ROADMAP.md) and [release design](docs/RELEASING.md). Keep the rendering and gameplay hot paths allocation-free and keep the fixed-step simulation independent of rendering.

Patch notes are mandatory before every promotion to `stable`. Before opening or merging the `dev` → `stable` PR, update `patchnotes/ru.json` and `patchnotes/en.json` in `dev` so they cover every player-visible change since the latest release: balance, new content, fixes, and launcher changes. Validate them with `dotnet run --project tools/TickHappens.ReleaseTool -- notes patchnotes .tmp/notes.md` and review them on the launcher's Patch notes page. Do not promote when the notes are missing, unchanged since the previous release, or incomplete: a release ships only the note files that changed. See [patchnotes/README.md](patchnotes/README.md).

Project skills live in `.claude/skills/`: `build-test`, `play-capture`, `add-content`, `art-recipe`, `vibe-delegate`. Use them instead of re-deriving the steps, and add a skill when a workflow repeats.
