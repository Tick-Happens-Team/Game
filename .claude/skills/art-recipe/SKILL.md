---
name: art-recipe
description: Create or change pixel-art sprites with the local procedural recipe pipeline (no external services). Use for new characters, enemies, props or animation frames.
---

Sources live in the Assets repo (`D:\tickhappens\Assets\art`), the tool in Game. See Game/docs/ASSET_PIPELINE.md and ART_STANDARD.md.

1. Copy the closest recipe in `Assets/art/recipes/*.json` (bones, parts, palette-ramp materials, decals, anims). Sizes per ART_STANDARD §2, color role per §5 (enemies purple/amber, player teal/gold).
2. From Game: `dotnet run --project tools/TickHappens.AssetTool -- gen ../Assets/art/recipes/X.json --seeds 1-6` → review `.tmp/gen/X_seeds_1-6.png`, put the chosen seed into the recipe.
3. `dotnet run --project tools/TickHappens.AssetTool -- build` — 0 lint errors; investigate TH010 (clipping) and TH011 (silhouette jumps).
4. Review `.tmp/preview/X.png`, then a game/bench capture (`play-capture`).
5. Commit recipe + regenerated `Assets/assets/atlas/*` in an Assets branch (PR to `dev`), merged before the Game PR that needs the sprite.
Never use paid generators or neural networks; never downscale AI images.
