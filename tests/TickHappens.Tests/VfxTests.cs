using TickHappens.Engine.Vfx;
using TickHappens.Engine.Vfx.Effects;

namespace TickHappens.Tests;

public class VfxTests
{
    // Reference values from the mulberry32 / imul-hash definitions in the Sprite Fusion TypeScript originals.
    [Fact]
    public void Rng_matches_typescript_mulberry32()
    {
        var r = new VfxRng(1);
        double[] expected = [0.627073941, 0.002735721, 0.527447040, 0.981050967, 0.968377898];
        foreach (var e in expected) Assert.Equal(e, r.Next(), 6);
        var r2 = new VfxRng(12345);
        foreach (var e in new[] { 0.979728268, 0.306752264, 0.484205422 }) Assert.Equal(e, r2.Next(), 6);
    }

    [Fact]
    public void Hash_matches_typescript_imul_hash()
    {
        Assert.Equal(0.35761f, VfxMath.Hash(0), 5);
        Assert.Equal(0.12678f, VfxMath.Hash(5), 5);
        Assert.Equal(0.85244f, VfxMath.Hash(-3, 2), 5);
        Assert.Equal(0.52568f, VfxMath.Hash(1000, 1), 5);
        Assert.Equal(0.12585f, VfxMath.Hash(77 * 31 + 5), 5);
    }

    public static TheoryData<string> Effects => ["explosion", "impact", "slash", "lightning-arc", "lightning-strike", "sparkle", "pickup"];

    private static IPixelEffect Make(string name, uint seed) => name switch
    {
        "explosion" => new ExplosionEffect(new ExplosionOptions { X = 400, Y = 300, Seed = seed }),
        "impact" => new ImpactEffect(new ImpactOptions { X = 400, Y = 300, Seed = seed }),
        "slash" => new SlashEffect(new SlashOptions { X = 400, Y = 300, Seed = seed }),
        "lightning-arc" => new LightningEffect(new LightningOptions { Kind = LightningKind.Arc, From = (300, 300), To = [(400, 320), (480, 280)], Seed = seed }),
        "lightning-strike" => new LightningEffect(new LightningOptions { Kind = LightningKind.Strike, X = 400, Y = 300, Seed = seed }),
        "sparkle" => new SparkleEffect(new SparkleOptions { X = 400, Y = 300, Seed = seed }),
        "pickup" => new PickupEffect(new PickupOptions { X = 400, Y = 300, Wait = 0.3f, Seed = seed, Target = () => (300, 280) }),
        _ => throw new ArgumentException(name),
    };

    private static (uint[] Color, uint[] Light, int Frames) Run(string name, uint seed)
    {
        var fx = Make(name, seed);
        var frame = new PixelFrame(400, 300) { Ox = 0, Oy = -50 };
        int painted = 0, frames = 0;
        uint[]? snapshotC = null, snapshotL = null;
        for (; frames < 60 * 8 && !fx.Done; frames++)
        {
            fx.Update(1f / 60f);
            frame.Clear();
            fx.Draw(frame, VfxLayer.All);
            painted += frame.Color.Count(c => c != 0);
            if (frames == 10) { snapshotC = (uint[])frame.Color.Clone(); snapshotL = (uint[])frame.Light.Clone(); }
        }
        Assert.True(painted > 0, $"{name} never drew a pixel");
        return (snapshotC!, snapshotL!, frames);
    }

    [Theory]
    [MemberData(nameof(Effects))]
    public void Effect_is_deterministic_and_finishes(string name)
    {
        var a = Run(name, 7);
        var b = Run(name, 7);
        Assert.Equal(a.Color, b.Color);
        Assert.Equal(a.Light, b.Light);
        Assert.Equal(a.Frames, b.Frames);
        Assert.True(a.Frames < 60 * 8, $"{name} did not finish within 8 s");
    }

    [Fact]
    public void Different_seeds_give_different_explosions()
    {
        Assert.NotEqual(Run("explosion", 1).Color, Run("explosion", 2).Color);
    }
}
