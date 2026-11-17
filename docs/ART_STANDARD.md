# TickHappens art standard

Aim for SNES-era 16-bit pixel art with modern lighting and effects. Keep the action readable with hundreds of enemies on screen.

## Resolution and scaling

Use 640×360 art pixels (16:9). Scale by an integer, typically ×2 at 720p, ×3 at 1080p, ×4 at 1440p, or ×6 at 2160p. For irregular windows, expand the art buffer to `ceil(window / scale)` without letterboxing. Render the world at integer coordinates and apply fractional camera movement at upscale time. Sample sprites with `texelFetch`. Smooth filtering is reserved for lighting and bloom.

## Sprite sizes

| Class | Frame size and notes |
|---|---|
| Ground tiles | 16×16, seamless, 2–6 variants per biome |
| Players | 16×24 with roughly 12×22 silhouette, origin between feet |
| Small enemies | 16×12 or 16×16 |
| Medium enemies | 24×24 or 32×32 |
| Elites | 48×48 |
| Bosses | 64–128 px |
| Items and pickups | 8×8 or 16×16 |
| Props | 16×12 to 16×32 |

TH005 allows sides of 8, 12, 16, 24, 32, 48, 64, 96, or 128 pixels.

Succubus uses a 24×24 frame to leave room for bat wings and a heart-tipped tail. Her body keeps the normal player height and feet origin; the extra width is cosmetic and does not enlarge the collision radius. The open outfit uses a black two-piece, exposed red shoulders and midriff, and tall boots.

## Palette

The 50-color `Assets/art/palette/tickhappens-v1.hex` uses hue-shifted ramps. Neutrals include ink `#1a1422`, shadows and bone. Warm ramps cover skin and wood. The fire ramp matches Sprite Fusion VFX. Green, turquoise, blue, violet-magenta, and stone round out the palette. Use at most 24 colors per sprite (TH007) and alpha 0 or 255 (TH002), with translucent VFX and particles allowed. Palette expansion requires a versioned palette and migration with `snap`.

## Light, volume, and outlines

Paint light from the upper left. Use a dark outline around at least 60% of actors and props (TH006), with material shades for internal lines. Avoid per-pixel gradients, pillow shading, isolated pixel noise (TH004), and exterior antialiasing. Use ordered Bayer 4×4 dithering for large surfaces and VFX.

The renderer combines sprite normals with point lights and quantizes light into six Bayer-dithered bands. Night ambient is approximately (0.22, 0.22, 0.38), while daylight is around 0.9–0.95. Mark emissive details such as eyes, runes, crystals, and hourglass sand through `emissive` keys in `.pxs` or colors in `.sprite.json`.

## Gameplay readability

- Enemies should contrast with terrain in hue and brightness. The grass slime became amber to improve contrast.
- Use turquoise and warm gold for players and their effects, purple/magenta/amber for enemies, bright red and magenta with dark outlines for hazards, turquoise glow for XP, and gold for chests and time.
- Keep hostile bullets above player effects. `VfxOpacity` defaults to 1.0 with a calmer 0.55 preset. Limit bloom through threshold and strength.
- Give the player a light and an emissive detail so they remain visible in a crowd.
- Use a white hit flash for 2–4 frames through `tint.a`. Expose strength controls for screen shake, flashes, and chromatic effects. Avoid full-screen flashes.

## Animation

| Type | FPS | Frames |
|---|---|---|
| Player walk | 8 | 4, alternating contact and passing |
| Idle | 2–4 | 2 |
| Enemy flight and pulse | 4–10 | 2–4 |
| Attacks | 12–15 | 3–6, including at least one anticipation frame |
| VFX | 30–60 simulation | Varies |

Walk cycles shift the body by one pixel. Mirror horizontally where appropriate and draw separate frames for asymmetric designs when needed.

## Effects

CPU pixel VFX cover explosions, impacts, lightning, and slashes in art-resolution color and light buffers. GPU compute particles draw tens of thousands of additive one-pixel sparks, fireflies, and embers. Bake flipbooks when repeated CPU effects exceed the 2 ms budget.

Use fire, turquoise, and violet ramps near the palette. HDR values above 1.0 can bloom. Bloom uses threshold 1.0, strength 0.85, and five levels. Keep enemy silhouettes clear.

## Review checklist

TH001 detects off-palette colors, TH002 translucent edges, TH003 oversized pixels from upscaling, TH004 isolated noise, and TH006 missing outlines. Review lighting consistency, top-down camera perspective, unnecessary details under two pixels, and blurred faces or eyes by hand. After lint, inspect `.tmp/preview/` on checkerboard and grass backgrounds.
