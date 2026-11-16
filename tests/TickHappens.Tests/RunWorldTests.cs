using TickHappens.Game;
using TickHappens.Game.Content;
using TickHappens.Game.Sim;

namespace TickHappens.Tests;

public sealed class RunWorldTests
{
    private static ContentSet Core() => ContentLoader.Load(ContentLoader.FindCore());

    [Fact]
    public void FixedStepProducesSameStateAtDifferentFrameRates()
    {
        var content = Core();
        var fast = PlayAt(content, 120);
        var slow = PlayAt(content, 24);
        Assert.Equal(fast.Tick, slow.Tick);
        Assert.Equal(fast.StateHash(), slow.StateHash());
    }

    [Fact]
    public void PauseFreezesTheRun()
    {
        var world = new RunWorld(Core());
        var loop = new RunLoop(world);
        loop.Advance(1d / 30, default);
        Assert.Equal(1, world.Tick);
        loop.TogglePause();
        loop.Advance(0.25, new(1, 0));
        Assert.Equal(1, world.Tick);
        loop.TogglePause();
        loop.Advance(1d / 30, default);
        Assert.Equal(2, world.Tick);
    }

    [Fact]
    public void DashCoversDistanceWithInvulnerabilityAndCooldown()
    {
        var world = new RunWorld(Core());
        var p = world.Player;
        var dash = world.Rules.Dash;
        world.Step(new(1, 0));
        var start = p.Pos;
        world.Step(new(1, 0, Dash: true));
        Assert.True(p.InvulnTicks > 0);
        int dashTicks = (int)MathF.Round(dash.Seconds * RunWorld.TicksPerSecond);
        for (int i = 1; i < dashTicks; i++) world.Step(new(1, 0, Dash: true));
        Assert.InRange(p.Pos.X - start.X, dash.Distance - 1, dash.Distance + 1);
        var afterDash = p.Pos;
        world.Step(new(0, 0, Dash: false));
        world.Step(new(0, 0, Dash: true)); // still on cooldown
        Assert.Equal(afterDash, p.Pos);
    }

    [Fact]
    public void DashCooldownIsShortenedByCooldownAndDashCooldownStats()
    {
        var content = Core();
        int baseTicks = (int)MathF.Round(content.Rules.Dash.Cooldown * RunWorld.TicksPerSecond);

        Assert.Equal(baseTicks, DashCooldownTicks(content, "swordsman", item: null));
        Assert.Equal((int)MathF.Round(baseTicks * 0.85f), DashCooldownTicks(content, "octarine", item: null));
        Assert.Equal((int)MathF.Round(baseTicks * 0.9f), DashCooldownTicks(content, "swordsman", item: "snack_bar"));
        Assert.Equal((int)MathF.Round(baseTicks * 0.85f * 0.9f), DashCooldownTicks(content, "octarine", item: "snack_bar"));
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

    private static int DashCooldownTicks(ContentSet content, string character, string? item)
    {
        var world = new RunWorld(content, seed: 1, character: character);
        if (item != null) world.GiveItem(content.ItemIndex(item));
        world.Step(new RunInput(1, 0, Dash: true));
        return world.Player.DashCooldownTicks;
    }

    private static RunWorld PlayAt(ContentSet content, int fps)
    {
        var world = new RunWorld(content, seed: 5);
        var loop = new RunLoop(world);
        for (int frame = 0; frame < 3000 * fps / 30; frame++)
            loop.Advance(1d / fps, new(frame / fps % 2 == 0 ? 1 : -1, 0));
        return world;
    }
}
