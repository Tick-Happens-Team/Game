using TickHappens.Engine.Spatial;

namespace TickHappens.Tests;

public class SpatialGridTests
{
    private struct Collect(List<int> into) : IGridVisitor
    {
        public void Visit(int index) => into.Add(index);
    }

    [Fact]
    public void Query_finds_every_item_within_radius()
    {
        var rng = new Random(3);
        int n = 5000;
        var xs = new float[n];
        var ys = new float[n];
        for (int i = 0; i < n; i++) { xs[i] = rng.NextSingle() * 1000; ys[i] = rng.NextSingle() * 1000; }
        var grid = new SpatialGrid(0, 0, 1000, 1000, 16);
        grid.Build(xs, ys);

        for (int q = 0; q < 50; q++)
        {
            float qx = rng.NextSingle() * 1000, qy = rng.NextSingle() * 1000, r = 5 + rng.NextSingle() * 40;
            var found = new List<int>();
            var v = new Collect(found);
            grid.Query(qx, qy, r, ref v);
            var set = found.ToHashSet();
            Assert.Equal(found.Count, set.Count); // no duplicates
            for (int i = 0; i < n; i++)
            {
                float dx = xs[i] - qx, dy = ys[i] - qy;
                if (dx * dx + dy * dy <= r * r) Assert.Contains(i, set);
            }
        }
    }
}
