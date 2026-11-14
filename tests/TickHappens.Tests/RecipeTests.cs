using TickHappens.AssetTool;
using TickHappens.AssetTool.ProcGen;
using TickHappens.Engine.Assets;

namespace TickHappens.Tests;

public class RecipeTests
{
    private static readonly Palette Pal = Palette.Load(Path.Combine(AssetPaths.SourceRoot(AssetPipelineTests.RepoRoot()), "art", "palette", "tickhappens-v1.hex"));

    private static IEnumerable<string> RecipeFiles() =>
        Directory.EnumerateFiles(Path.Combine(AssetPaths.SourceRoot(AssetPipelineTests.RepoRoot()), "art", "recipes"), "*.json").Order();

    public static TheoryData<string> Recipes => [.. RecipeFiles().Select(f => Path.GetFileName(f))];

    private static string PathOf(string name) => Path.Combine(AssetPaths.SourceRoot(AssetPipelineTests.RepoRoot()), "art", "recipes", name);

    [Theory]
    [MemberData(nameof(Recipes))]
    public void Recipe_render_is_deterministic(string name)
    {
        var a = new RecipeRenderer(Recipe.Load(PathOf(name)), Pal).Render(name);
        var b = new RecipeRenderer(Recipe.Load(PathOf(name)), Pal).Render(name);
        Assert.Equal(a.Frames.Count, b.Frames.Count);
        for (int i = 0; i < a.Frames.Count; i++)
        {
            Assert.Equal(a.Frames[i].Color.Px, b.Frames[i].Color.Px);
            Assert.Equal(a.Frames[i].Emission.Px, b.Frames[i].Emission.Px);
            Assert.Equal(a.Normals[i]!.Px, b.Normals[i]!.Px);
        }
    }

    [Theory]
    [MemberData(nameof(Recipes))]
    public void Recipe_output_passes_style_lint_without_errors(string name)
    {
        var sheet = new RecipeRenderer(Recipe.Load(PathOf(name)), Pal).Render(name);
        var errors = StyleLint.Check(sheet, Pal).Where(i => i.Severity == Severity.Error).ToList();
        Assert.True(errors.Count == 0, string.Join("\n", errors));
        // Every pixel is opaque-or-clear and on palette; every anim frame exists.
        foreach (var f in sheet.Frames)
            foreach (uint c in f.Color.Px)
                Assert.True(RgbaImage.A(c) == 0 || (RgbaImage.A(c) == 255 && Pal.Contains(c)));
        foreach (var anim in sheet.Anims.Values) Assert.All(anim.Frames, i => Assert.InRange(i, 0, sheet.Frames.Count - 1));
    }

    [Fact]
    public void Seed_selects_palette_variant_and_keeps_shape()
    {
        var recipe = Recipe.Load(PathOf("enemy_amberslime.json"));
        var s1 = new RecipeRenderer(recipe, Pal, 1).Render("t");
        var s2 = new RecipeRenderer(recipe, Pal, 2).Render("t");
        Assert.NotEqual(s1.Frames[0].Color.Px, s2.Frames[0].Color.Px);
        // Same silhouette: palette swap only changes colors.
        Assert.Equal(s1.Frames[0].Color.Px.Select(c => RgbaImage.A(c) != 0), s2.Frames[0].Color.Px.Select(c => RgbaImage.A(c) != 0));
    }

    [Fact]
    public void Off_palette_recipe_color_is_rejected()
    {
        var recipe = Recipe.Load(PathOf("enemy_amberslime.json"));
        recipe.Materials["gel"].Ramp[0] = "123456";
        recipe.Materials["gel"].Variants = null;
        Assert.Throws<InvalidDataException>(() => new RecipeRenderer(recipe, Pal));
    }

    [Fact]
    public void Cleanup_removes_specks_and_fills_holes()
    {
        uint c = Palette.ParseHex("4250b0") | 0xFF000000;
        var img = new RgbaImage(8, 8);
        for (int y = 2; y < 6; y++)
            for (int x = 2; x < 6; x++) img[x, y] = c;
        img[3, 3] = 0;      // pin hole
        img[0, 0] = c;      // speck
        Assert.Equal(1, PixelCleanup.FillHoles(img));
        Assert.Equal(1, PixelCleanup.Despeckle(img));
        Assert.Equal(c, img[3, 3]);
        Assert.Equal(0u, img[0, 0]);
    }
}
