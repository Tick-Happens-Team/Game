using System.Numerics;
using TickHappens.Game.Content;

namespace TickHappens.Game.Sim;

public sealed partial class RunWorld
{
    private const int EventBytes = 1 + sizeof(int) * 8;
    private const int MaxChainHops = 16;

    public int WriteEvents(Span<byte> destination, ref int nextEvent)
    {
        var writer = new SnapshotWriter(destination);
        while (nextEvent < _events.Count)
        {
            var e = _events[nextEvent];
            int points = e.Kind == EventKind.Chain ? e.B : 0;
            if (destination.Length - writer.Length < EventBytes + points * 2 * sizeof(float)) break;

            writer.Byte((byte)e.Kind);
            writer.Float(e.X);
            writer.Float(e.Y);
            writer.Float(e.X2);
            writer.Float(e.Y2);
            writer.Float(e.Value);
            writer.Int(points == 0 ? e.A : 0);
            writer.Int(e.B);
            writer.Int(unchecked((int)e.Seed));
            for (int i = 0; i < points; i++)
            {
                Vector2 point = ChainPoints[e.A + i];
                writer.Float(point.X);
                writer.Float(point.Y);
            }
            nextEvent++;
        }
        return writer.Length;
    }

    public bool TryApplyEvents(ReadOnlySpan<byte> source)
    {
        int eventsBefore = _events.Count, pointsBefore = ChainPoints.Count;
        try
        {
            var reader = new SnapshotReader(source);
            while (reader.Remaining != 0)
            {
                var e = new GameEvent
                {
                    Kind = (EventKind)reader.Byte(),
                    X = reader.Float(),
                    Y = reader.Float(),
                    X2 = reader.Float(),
                    Y2 = reader.Float(),
                    Value = reader.Float(),
                    A = reader.Int(),
                    B = reader.Int(),
                    Seed = unchecked((uint)reader.Int()),
                };
                Require(e.Kind <= EventKind.BossReward);
                if (e.Kind == EventKind.Hit) Require((uint)e.A <= (uint)DamageKind.Pure);
                if (e.Kind is EventKind.Chest or EventKind.BossReward) Require((uint)e.A < (uint)Content.Items.Count && (uint)e.B < (uint)_players.Length);
                if (e.Kind == EventKind.Chain)
                {
                    Require(e.B is >= 2 and <= MaxChainHops + 1);
                    e.A = ChainPoints.Count;
                    for (int i = 0; i < e.B; i++)
                        ChainPoints.Add(new Vector2(reader.Float(), reader.Float()));
                }
                _events.Add(e);
            }
            return true;
        }
        catch (InvalidDataException)
        {
            _events.RemoveRange(eventsBefore, _events.Count - eventsBefore);
            ChainPoints.RemoveRange(pointsBefore, ChainPoints.Count - pointsBefore);
            return false;
        }
    }
}
