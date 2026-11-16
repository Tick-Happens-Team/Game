using TickHappens.Engine.Assets;
using TickHappens.Engine.Vfx;
using TickHappens.Game.Content;

namespace TickHappens.Game;

/// <summary>
/// Hero sprites for the character screen, read from the atlas image on the CPU and drawn enlarged into the UI layer
/// with their idle animation. Menu-only and unlit, outside the simulation.
/// </summary>
internal sealed class HeroPreview
{
    private readonly RgbaImage _atlas;
    private readonly AtlasSprite[] _sprites;

    public HeroPreview(RgbaImage atlasImage, ContentSet content, Func<string, AtlasSprite> sprite)
    {
        _atlas = atlasImage;
        _sprites = [.. content.Characters.Select(c => sprite(c.Sprite))];
    }

    /// <summary>
    /// Draws the hero's idle loop scaled up with its feet (the sprite origin) at (<paramref name="x"/>, <paramref name="y"/>).
    /// Rows above <paramref name="clipTop"/> are skipped so tall heroes stay inside a roster row.
    /// </summary>
    public void DrawHero(PixelCanvas ui, int hero, float time, int x, int y, int scale, int clipTop = int.MinValue)
    {
        var s = _sprites[hero];
        int frame = 0;
        if (s.Anims.TryGetValue("idle", out var idle) && idle.Frames.Count > 0)
            frame = idle.Frames[(int)MathF.Floor(time * idle.Fps) % idle.Frames.Count];
        int fx = s.Frames[frame][0], fy = s.Frames[frame][1];
        int dx = x - s.Origin[0] * scale, dy = y - s.Origin[1] * scale;
        for (int py = 0; py < s.H; py++)
            for (int px = 0; px < s.W; px++)
            {
                uint c = _atlas[fx + px, fy + py];
                if (RgbaImage.A(c) == 0) continue;
                for (int sy = 0; sy < scale; sy++)
                {
                    if (dy + py * scale + sy < clipTop) continue;
                    for (int sx = 0; sx < scale; sx++)
                        ui.Set(dx + px * scale + sx, dy + py * scale + sy, c);
                }
            }
    }
}
