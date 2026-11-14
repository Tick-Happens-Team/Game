using TickHappens.AssetTool;
using TickHappens.Engine.Assets;

namespace TickHappens.Tests;

public class AssetPipelineTests
{
    private static readonly Palette Pal = Palette.Load(Path.Combine(AssetPaths.SourceRoot(RepoRoot()), "art", "palette", "tickhappens-v1.hex"));

    internal static string RepoRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "TickHappens.sln"))) return d.FullName;
        throw new DirectoryNotFoundException("repo root");
    }

    /// <summary>Test scratch space: the repository's .tmp/tests (never the system temp dir).</summary>
    internal static string TmpDir()
    {
        string d = Path.Combine(RepoRoot(), ".tmp", "tests");
        Directory.CreateDirectory(d);
        return d;
    }

    private static readonly uint Outline = Palette.ParseHex("1a1422") | 0xFF000000;
    private static readonly uint Body = Palette.ParseHex("4250b0") | 0xFF000000;
    private static readonly uint Light = Palette.ParseHex("6a7fe0") | 0xFF000000;

    /// <summary>A clean 16x16 outlined blob with a highlight: passes the lint.</summary>
    private static RgbaImage CleanSprite()
    {
        var img = new RgbaImage(16, 16);
        for (int y = 2; y < 14; y++)
            for (int x = 3; x < 13; x++)
                img[x, y] = x == 3 || x == 12 || y == 2 || y == 13 ? Outline : (x < 7 && y < 7 ? Light : Body);
        return img;
    }

    private static SpriteSheet Sheet(RgbaImage img, string name = "enemy_test") =>
        new() { Name = name, W = img.W, H = img.H, Source = "test", Frames = { new SpriteFrame("f0", img, new RgbaImage(img.W, img.H)) } };

    [Fact]
    public void Clean_sprite_passes_lint()
    {
        Assert.DoesNotContain(StyleLint.Check(Sheet(CleanSprite()), Pal), i => i.Severity == Severity.Error);
    }

    [Fact]
    public void Off_palette_and_soft_alpha_are_errors()
    {
        var img = CleanSprite();
        img[6, 6] = RgbaImage.Pack(1, 2, 3);
        img[8, 8] = (Body & 0x00FFFFFF) | 0x80000000;
        var codes = StyleLint.Check(Sheet(img), Pal).Select(i => i.Code).ToHashSet();
        Assert.Contains("TH001", codes);
        Assert.Contains("TH002", codes);
    }

    [Fact]
    public void Upscaled_art_is_detected_and_restored()
    {
        var src = CleanSprite();
        var big = new RgbaImage(64, 64);
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++) big[x, y] = src[x / 4, y / 4];
        Assert.Equal(4, PixelScale.Detect(big));
        Assert.Contains(StyleLint.Check(new SpriteSheet { Name = "enemy_big", W = 64, H = 64, Source = "t", Frames = { new SpriteFrame("f", big, new RgbaImage(64, 64)) } }, Pal),
            i => i.Code == "TH003");
        Assert.Equal(src.Px, PixelScale.Downsample(big, 4).Px);
        Assert.Equal(1, PixelScale.Detect(src));
    }

    [Fact]
    public void Palette_nearest_snaps_to_palette()
    {
        uint c = Pal.Nearest(RgbaImage.Pack(0x45, 0x52, 0xb2));
        Assert.True(Pal.Contains(c));
        Assert.Equal(Body & 0x00FFFFFF, c);
    }

    [Fact]
    public void Pxs_mirror_and_derived_frames()
    {
        string path = Path.Combine(TmpDir(), $"th_{Guid.NewGuid():N}.pxs");
        File.WriteAllText(path, """
            sprite t
            size 4 2
            mirror
            key o 1a1422
            key b 4250b0
            emissive b
            frame a
            ob
            .o
            end
            frame c from a
            row 1 ob
            end
            anim idle 4 a c
            """);
        try
        {
            var s = PxsCompiler.Compile(path, Pal);
            var a = s.Frames[0].Color;
            Assert.Equal(Outline, a[0, 0]);
            Assert.Equal(Outline, a[3, 0]);     // mirrored
            Assert.Equal(Body, a[1, 0]);
            Assert.Equal(Body, a[2, 0]);
            Assert.Equal(0u, a[0, 1]);
            Assert.Equal(Body, s.Frames[0].Emission[1, 0]);
            Assert.Equal(Body, s.Frames[1].Color[2, 1]);
            Assert.Equal([0, 1], s.Anims["idle"].Frames);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Pxs_rejects_colors_outside_palette()
    {
        string path = Path.Combine(TmpDir(), $"th_{Guid.NewGuid():N}.pxs");
        File.WriteAllText(path, "sprite t\nsize 2 1\nkey x 123456\nframe a\nxx\nend\n");
        try { Assert.Throws<InvalidDataException>(() => PxsCompiler.Compile(path, Pal)); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Project_art_sources_compile_and_pass_lint()
    {
        string root = RepoRoot();
        var sheets = Directory.EnumerateFiles(Path.Combine(AssetPaths.SourceRoot(root), "art", "source"), "*.pxs", SearchOption.AllDirectories)
            .Select(f => PxsCompiler.Compile(f, Pal)).Concat(TileGenerator.Generate(Pal)).ToList();
        Assert.NotEmpty(sheets);
        var errors = sheets.SelectMany(s => StyleLint.Check(s, Pal)).Where(i => i.Severity == Severity.Error).ToList();
        Assert.True(errors.Count == 0, string.Join("\n", errors));
    }

    [Fact]
    public void Atlas_layout_has_no_overlaps()
    {
        var sheets = TileGenerator.Generate(Pal).ToList();
        sheets.Add(Sheet(CleanSprite(), "enemy_a"));
        string outBase = Path.Combine(TmpDir(), $"th_atlas_{Guid.NewGuid():N}");
        var atlas = AtlasBuilder.Build(sheets, outBase);
        var rects = atlas.Sprites.Values.SelectMany(s => s.Frames.Select(f => (X: f[0], Y: f[1], s.W, s.H))).ToList();
        for (int i = 0; i < rects.Count; i++)
            for (int j = i + 1; j < rects.Count; j++)
            {
                var a = rects[i]; var b = rects[j];
                bool overlap = a.X < b.X + b.W && b.X < a.X + a.W && a.Y < b.Y + b.H && b.Y < a.Y + a.H;
                Assert.False(overlap, $"{a} overlaps {b}");
            }
        foreach (var ext in new[] { ".png", "_e.png", "_n.png", ".json" }) File.Delete(outBase + ext);
    }
}
