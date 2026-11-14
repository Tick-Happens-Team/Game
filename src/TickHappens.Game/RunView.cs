using System.Numerics;
using Silk.NET.OpenGL;
using TickHappens.Engine.Assets;
using TickHappens.Engine.Graphics;
using TickHappens.Engine.Vfx;

namespace TickHappens.Game;

internal sealed class RunView : IDisposable
{
    private const int Tile = 16;
    private readonly PixelRenderer _renderer;
    private readonly ShaderProgram _shader;
    private readonly SpriteBatch _batch;
    private readonly Texture2D _albedo, _normals, _emission;
    private readonly AtlasSprite _hero, _chest, _grass, _stone;
    private readonly int _openFrame;

    public RunView(GL gl, AtlasData atlas)
    {
        _hero = atlas.Sprites["hero_clockwarden_r"];
        _chest = atlas.Sprites["prop_chest"];
        _grass = atlas.Sprites["tile_grass"];
        _stone = atlas.Sprites["tile_stone"];
        _openFrame = _chest.FrameNames.IndexOf("open");
        if (_openFrame < 0)
            throw new InvalidDataException("The atlas is missing the open chest frame. Run TickHappens.AssetTool -- build.");

        var shaders = AssetPaths.Get("shaders");
        _albedo = Texture2D.FromImage(gl, RgbaImage.Load(AssetPaths.Get("atlas", "main.png")));
        _normals = Texture2D.FromImage(gl, RgbaImage.Load(AssetPaths.Get("atlas", "main_n.png")));
        _emission = Texture2D.FromImage(gl, RgbaImage.Load(AssetPaths.Get("atlas", "main_e.png")));
        _shader = ShaderProgram.FromFiles(gl, Path.Combine(shaders, "sprite.vert"), Path.Combine(shaders, "sprite.frag"));
        _batch = new SpriteBatch(gl, _shader);
        _renderer = new PixelRenderer(gl, shaders);
        _renderer.Settings.AmbientR = 0.72f;
        _renderer.Settings.AmbientG = 0.73f;
        _renderer.Settings.AmbientB = 0.68f;
    }

    public void Draw(int width, int height, RunWorld world, float interpolation)
    {
        var hero = Vector2.Lerp(world.PreviousHero, world.Hero, interpolation);
        float time = Math.Max(0, world.Tick - 1 + interpolation) / RunWorld.TicksPerSecond;
        _renderer.BeginFrame(width, height, hero.X, hero.Y - 10);
        int ox = _renderer.OriginX, oy = _renderer.OriginY;
        int tw = _renderer.TargetW, th = _renderer.TargetH;
        _batch.Begin();
        DrawTerrain(ox, oy, tw, th);

        // A small fixed set of props. Sort with the hero by their ground pivots.
        Span<int> order = stackalloc int[world.Chests.Length + 1];
        Span<float> depth = stackalloc float[order.Length];
        for (int i = 0; i < world.Chests.Length; i++)
        {
            order[i] = i;
            depth[i] = world.Chests[i].Y;
        }
        order[^1] = -1;
        depth[^1] = hero.Y;
        depth.Sort(order);
        foreach (int id in order)
        {
            if (id == -1)
            {
                var animation = _hero.Anims[world.Walking ? "walk" : "idle"];
                int frame = animation.Frames[(int)(time * animation.Fps) % animation.Frames.Count];
                _batch.Draw(_hero, frame, hero.X - ox, hero.Y - oy, world.FacingLeft);
                continue;
            }
            var chest = world.Chests[id];
            _batch.Draw(_chest, world.IsOpen(id) ? _openFrame : 0, chest.X - ox, chest.Y - oy);
        }
        _batch.Flush(_albedo, _normals, _emission, tw, th);

        var vfx = _renderer.VfxFrame;
        vfx.Clear();
        var canvas = PixelCanvas.ColorOf(vfx);
        if (world.NearbyChest >= 0)
        {
            var chest = world.Chests[world.NearbyChest];
            // Downward marker points to the chest the interaction will select.
            canvas.Line(chest.X - 3, chest.Y - 19, chest.X, chest.Y - 16, 0xFF6BE2FF);
            canvas.Line(chest.X + 3, chest.Y - 19, chest.X, chest.Y - 16, 0xFF6BE2FF);
        }
        int age = world.Tick - world.LastOpenedTick;
        if (world.LastOpenedChest >= 0 && age < 15)
        {
            var chest = world.Chests[world.LastOpenedChest];
            float progress = (age + interpolation) / 15;
            canvas.Ring(chest.X, chest.Y - 7, 4 + 12 * progress, 1, 0xFF6BE2FF, 1 - progress);
        }
        _renderer.UploadVfx();
        Span<PointLight> lights = stackalloc PointLight[1];
        lights[0] = new PointLight { X = hero.X - 5, Y = hero.Y - 20, Radius = 88, R = 0.55f, G = 0.38f, B = 0.22f, Height = 24 };
        _renderer.Light(lights, time);
        _renderer.Finish();
    }

    private void DrawTerrain(int ox, int oy, int width, int height)
    {
        int left = Math.Max(0, ox / Tile);
        int top = Math.Max(0, oy / Tile);
        int right = Math.Min(RunWorld.ArenaSize / Tile - 1, (ox + width - 1) / Tile);
        int bottom = Math.Min(RunWorld.ArenaSize / Tile - 1, (oy + height - 1) / Tile);
        for (int y = top; y <= bottom; y++)
        for (int x = left; x <= right; x++)
        {
            int dx = x - 32, dy = y - 32;
            bool stone = dx * dx + dy * dy < 5 * 5 || Math.Abs(dx) <= 1 || Math.Abs(dy) <= 1;
            uint variant = unchecked((uint)x * 0x9E3779B9u ^ (uint)y * 0x85EBCA6Bu);
            var tile = stone ? _stone : _grass;
            _batch.Draw(tile, (int)((variant >> 16) % (uint)tile.Frames.Count), x * Tile - ox, y * Tile - oy);
        }
    }

    public void SaveScreenshot(string path)
    {
        var pixels = _renderer.ReadBackBuffer(out int width, out int height);
        new RgbaImage(width, height, pixels).Save(path);
    }

    public void Dispose()
    {
        _renderer.Dispose();
        _batch.Dispose();
        _shader.Dispose();
        _emission.Dispose();
        _normals.Dispose();
        _albedo.Dispose();
    }
}
