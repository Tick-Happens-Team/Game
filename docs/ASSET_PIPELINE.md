# Local asset pipeline

AssetTool generates art from local files using deterministic recipes and seeds. It requires no external services, paid APIs, credits, or generative models. Art sources and the built atlas live in Assets. AssetTool, tile generation code, and shaders live in Game.

Run commands from Game with Assets next to it or set `TICKHAPPENS_ASSETS_ROOT` to the absolute Assets path. Pass explicit paths for inputs to commands such as `gen`.

```powershell
dotnet run --project tools/TickHappens.AssetTool -- build
dotnet run --project tools/TickHappens.AssetTool -- gen ../Assets/art/recipes/hero_clockwarden.json --seeds 1-6
dotnet run --project tools/TickHappens.AssetTool -- import art.png NAME FW FH --tool aseprite
dotnet run --project tools/TickHappens.AssetTool -- lint sprite.png FW FH
dotnet run --project tools/TickHappens.AssetTool -- snap in.png out.png
dotnet run --project tools/TickHappens.AssetTool -- crop shot.png X Y W H ZOOM .tmp/out.png
```

## Sources

Use procedural recipes for animated characters, crowds, and seeded palette swaps. Hand-authored `.pxs` grids suit small unique pickups, icons, and props. Imported PNG artwork requires visual review and provenance. TileGenerator produces seamless ground variants.

## Procedural recipes

1. Bones form a hierarchy such as root → hips → body → head. Keyframes provide offsets `[dx, dy]` and scales `[dx, dy, sx, sy]`.
2. Parts bind to bones. Primitives include ellipse, rounded rect, poly, and line, with z-order and mirroring. Volume profiles are dome, cylinder, bevel, and flat. Rasterization samples pixel centers without antialiasing.
3. Materials use palette ramps from shadow to light. `bands` sets thresholds, `base` the darkest band index, `spec` the highlight, and `emissive` the emission mask. Seed 1 uses the base ramp and later seeds pick variants. `dither` sets the Bayer 4×4 transition half-width, default zero. Values around 0.04–0.08 suit sprites at least 48 px wide.
4. Light defaults to upper left. `unify` (default 0.45) blends part and whole-silhouette normals. Quantized N·L selects a ramp band.
5. Higher-z parts above or left darken neighboring pixels of another material by one tone. Set `"shadow": false` to disable contact shadow.
6. Bone-bound decals stamp protected pixels such as eyes and highlights. They may emit light. `hideDecals` hides them for frames such as a blink.
7. PixelCleanup removes one-pixel protrusions, orphaned tones, disconnected fragments up to 3 px, and holes.
8. Four-neighbor outlines extend one pixel outside the silhouette. The lit side uses the darkest material tone. Other edges use ink `#1a1422`.
9. Normals use the same geometry and duplicate frames share atlas entries. Recipe sprite IDs end in `_r`.

Examples are `hero_clockwarden.json` (idle 3, walk 4, cast 3), `enemy_gloombat.json` (fly 4), and `enemy_amberslime.json` (idle 4, hop 5). Start from a similar recipe, follow the [art standard](ART_STANDARD.md), generate seeds 1–6, inspect `.tmp/gen/`, and save the chosen seed. Run build, resolve lint issues, and compare the previews and bench captures with `--art recipe` and `--art pxs`.

The November 2026 comparison favored recipes for the hero and found the slime and revised bat comparable to their hand-authored references. Revisions covered sprite-wide lighting, shadows between materials, eliminating spikes, lighter bat ramps, a cast windup and flash, and removal of the magenta hero variant. Roadmap 0.16 covers angled bones, secondary motion, rim light and nighttime hue shifts, `.pxs` frame patches, and four directions.

## Indexed source format

```text
sprite enemy_gloombat
size 16 12
origin 8 11
mirror
key o 1a1422
emissive r
frame up
...
end
frame down from up shift 0 1
row 3 .oNo..oNo
end
anim fly 10 up down
```

Use palette colors. A dot represents transparency. `mirror` supplies the left half. Derived frames can shift and override rows. Add `!` to the animation FPS for one-shot playback.

## Import and lint

Import removes a solid background from the corners, reverses integer upscaling, binarizes alpha, snaps colors in OKLab, and cleans holes and fragments. It writes a PNG and `.sprite.json` to Assets under `art/source/imported/`, records the source in `provenance.tool`, and runs lint.

| Code | Severity | Check |
|---|---|---|
| TH000 | Error | Empty frame |
| TH001 | Error | Off-palette color |
| TH002 | Error | Partial alpha outside `vfx_*` |
| TH003 | Error | Integer-upscaled pixels |
| TH004 | Warning | More than 30% isolated pixel noise |
| TH005 | Warning | Non-standard frame size |
| TH006 | Warning | Less than 60% dark silhouette edge |
| TH007 | Warning | More than 24 colors |
| TH008 | Error | Silhouette holes |
| TH009 | Error | Detached fragments of 3 px or less |
| TH010 | Warning | Light body pixels touch top or side edges |
| TH011 | Warning | Silhouette area changes by more than 25% between adjacent frames |

`build` writes Assets `assets/atlas/main.json` plus aligned albedo (`main.png`), emission (`main_e.png`), and OpenGL Y-up normals (`main_n.png`). The JSON stores frame dimensions, origins, frame coordinates and names, and animations with fps, frames, and loop. NormalMapGenerator derives normals for `.pxs` and imports from the silhouette and brightness. Enlarged previews go under Game's `.tmp/preview/`.

Sprite Fusion VFX ports in Engine cover explosion, impact, slash, lightning, sparkle, and pickup. Preserve their MIT notice and source headers. New C# effects use PixelFrame, PixelCanvas, and VfxMath.

## Generator evaluation

Local ComfyUI and SD/SDXL/FLUX pixel-art LoRAs were evaluated on an RTX 3060 12 GB. Downscaling lost outlines and consistent lighting. Frame consistency required additional tools and manual correction. Unclear license terms, multi-gigabyte models, CUDA/PyTorch dependencies, and cross-driver reproducibility also weighed against this path.
