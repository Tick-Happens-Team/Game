using TickHappens.Game;

namespace TickHappens.Tests;

public sealed class RunWorldTests
{
    [Fact]
    public void EnemyGoldPaysForNearbyChestOncePerRun()
    {
        var world = new RunWorld();
        world.Step(default);
        Assert.False(world.IsAlive(0));
        Assert.True(world.HasGoldDrop(0));
        Assert.Equal(0, world.Wallet.Gold);
        Assert.Equal(0, world.OpenedCount);

        for (int i = 0; i < 14; i++) world.Step(new(0, -1));
        Assert.False(world.HasGoldDrop(0));
        Assert.True(world.IsOpen(0));
        Assert.Equal(0, world.LastOpenedChest);
        Assert.Equal(0, world.Wallet.Gold);
        Assert.Equal(15, world.Wallet.NextPrice);
        world.Step(default);
        Assert.Equal(1, world.OpenedCount);
        Assert.False(new RunWorld().IsOpen(0));
    }

    [Fact]
    public void FixedStepProducesSameStateAtDifferentFrameRates()
    {
        var fast = PlayAt(120);
        var slow = PlayAt(24);
        Assert.Equal(10_000, fast.Tick);
        Assert.Equal(fast.Tick, slow.Tick);
        Assert.Equal(fast.Hero, slow.Hero);
        Assert.Equal(fast.PreviousHero, slow.PreviousHero);
        Assert.Equal(1, fast.OpenedCount);
        Assert.Equal(fast.OpenedCount, slow.OpenedCount);
        Assert.Equal(fast.LastOpenedTick, slow.LastOpenedTick);
    }

    [Fact]
    public void PauseFreezesTheRun()
    {
        var world = new RunWorld();
        var loop = new RunLoop(world);
        for (int i = 0; i < 14; i++) world.Step(new(0, -1));
        Assert.Equal(1, world.OpenedCount);

        loop.TogglePause();
        loop.Advance(0.25, new(1, 0));
        Assert.Equal(14, world.Tick);
        loop.TogglePause();
        loop.Advance(1d / 30, default);
        Assert.Equal(15, world.Tick);
    }

    [Fact]
    public void GoldBelongsToKillerOrSummonerAndChestPricesAreIndividual()
    {
        var first = new ChestWallet();
        var second = new ChestWallet();
        var direct = GoldDrop.FromKill(25, killerPlayerId: 0);
        var summonKill = GoldDrop.FromKill(23, killerPlayerId: 0, summonOwnerId: 1);
        first.Collect(summonKill, playerId: 0);
        second.Collect(direct, playerId: 1);
        Assert.False(first.TryPay());
        Assert.Equal(0, second.Gold);
        first.Collect(direct, playerId: 0);
        second.Collect(summonKill, playerId: 1);
        Assert.Equal(23, second.Gold);
        Assert.True(first.TryPay());
        Assert.Equal(15, first.NextPrice);
        Assert.Equal(10, second.NextPrice);
        Assert.True(first.TryPay());
        Assert.Equal(23, first.NextPrice);
        Assert.False(first.TryPay());
        Assert.Equal(23, first.NextPrice);
    }

    private static RunWorld PlayAt(int fps)
    {
        var world = new RunWorld();
        var loop = new RunLoop(world);
        for (int frame = 0; frame < 10_000 * fps / 30; frame++)
        {
            bool approaching = frame < fps / 2;
            loop.Advance(1d / fps, new(0, approaching ? -1 : 0));
        }
        return world;
    }
}
