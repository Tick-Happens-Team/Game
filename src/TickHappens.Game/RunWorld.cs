using System.Numerics;

namespace TickHappens.Game;

public readonly record struct RunInput(int X, int Y, bool Interact = false);

public sealed class RunWorld
{
    public const int TicksPerSecond = 30;
    public const int ArenaSize = 1024;
    private const float StepDistance = 90f / TicksPerSecond;
    private const float Reach = 22;

    private static readonly Vector2[] ChestPositions =
    [
        new(512, 462),
        new(430, 550),
        new(614, 553),
    ];
    private readonly bool[] _opened = new bool[ChestPositions.Length];

    public int Tick { get; private set; }
    public Vector2 Hero { get; private set; } = new(512, 512);
    public Vector2 PreviousHero { get; private set; } = new(512, 512);
    public bool Walking { get; private set; }
    public bool FacingLeft { get; private set; }
    public int OpenedCount { get; private set; }
    public int NearbyChest { get; private set; } = -1;
    public int LastOpenedChest { get; private set; } = -1;
    public int LastOpenedTick { get; private set; }
    public ReadOnlySpan<Vector2> Chests => ChestPositions;
    public bool IsOpen(int chest) => _opened[chest];

    public void Step(RunInput input)
    {
        Tick++;
        PreviousHero = Hero;
        var direction = new Vector2(Math.Sign(input.X), Math.Sign(input.Y));
        if (direction != Vector2.Zero)
            Hero = Vector2.Clamp(Hero + Vector2.Normalize(direction) * StepDistance, new(12), new(ArenaSize - 12));
        Walking = Hero != PreviousHero;
        if (direction.X != 0) FacingLeft = direction.X < 0;

        NearbyChest = -1;
        float bestDistance = Reach * Reach;
        for (int i = 0; i < ChestPositions.Length; i++)
        {
            if (_opened[i]) continue;
            float distance = Vector2.DistanceSquared(Hero, ChestPositions[i]);
            if (distance > bestDistance || (distance == bestDistance && NearbyChest >= 0)) continue;
            bestDistance = distance;
            NearbyChest = i;
        }

        if (input.Interact && NearbyChest >= 0)
        {
            _opened[NearbyChest] = true;
            LastOpenedChest = NearbyChest;
            LastOpenedTick = Tick;
            NearbyChest = -1;
            OpenedCount++;
        }
    }
}
