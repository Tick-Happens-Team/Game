using TickHappens.Game.Online;

namespace TickHappens.Tests;

public sealed class SnapshotDeltaTests
{
    [Theory]
    [InlineData(512)]
    [InlineData(768)]
    [InlineData(1024)]
    public void DeltaRoundTripsAcrossChangesInSnapshotLength(int snapshotLength)
    {
        byte[] baseline = new byte[768];
        new Random(42).NextBytes(baseline);
        byte[] snapshot = new byte[snapshotLength];
        baseline.AsSpan(0, Math.Min(baseline.Length, snapshot.Length)).CopyTo(snapshot);
        snapshot[11] ^= 0x40;
        snapshot[220] ^= 0x04;
        byte[] packet = new byte[snapshot.Length + SnapshotDelta.MaxHeaderBytes];
        byte[] decoded = new byte[snapshot.Length];

        int size = SnapshotDelta.Encode(20, baseline, snapshot, packet);
        Assert.InRange(size, 1, snapshot.Length - 1);
        Assert.Equal(0, SnapshotDelta.Decode(21, baseline, packet.AsSpan(0, size), decoded));
        Assert.Equal(0, SnapshotDelta.Decode(20, baseline, packet.AsSpan(0, size - 1), decoded));
        Assert.Equal(snapshot.Length, SnapshotDelta.Decode(20, baseline, packet.AsSpan(0, size), decoded));
        Assert.Equal(snapshot, decoded);
    }

    [Fact]
    public void RawFallbackWorksWithoutTheKeyframe()
    {
        byte[] snapshot = new byte[512];
        Array.Fill(snapshot, (byte)42);
        byte[] baseline = new byte[snapshot.Length];
        byte[] packet = new byte[snapshot.Length + SnapshotDelta.MaxHeaderBytes];
        byte[] decoded = new byte[snapshot.Length];
        int size = SnapshotDelta.Encode(20, baseline, snapshot, packet);
        Assert.Equal(snapshot.Length + sizeof(int), size);
        Assert.Equal(snapshot.Length, SnapshotDelta.Decode(-1, [], packet.AsSpan(0, size), decoded));
        Assert.Equal(snapshot, decoded);
    }
}
