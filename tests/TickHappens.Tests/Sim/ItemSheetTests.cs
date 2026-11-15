using System.Numerics;
using TickHappens.Game;
using TickHappens.Game.Content;
using TickHappens.Game.Sim;

namespace TickHappens.Tests;

public sealed class ItemSheetTests
{
    [Fact]
    public void ContactDamageGrantsExactlyThreeTicksOfInvulnerability()
    {
        var content = QuietContent();
        content.Enemies[0].Speed = 0;
        content.Enemies[0].Hp = 100_000;
        var world = new RunWorld(content);
        world.SpawnEnemy(0, world.Player.Pos);

        world.Step(default);
        Assert.Equal(3, world.Player.InvulnTicks);
        int firstHit = world.Player.HurtTick;

        world.Step(default);
        world.Step(default);
        Assert.Equal(firstHit, world.Player.HurtTick);
        world.SpawnEnemy(0, world.Player.Pos);
        world.Step(default);
        Assert.Equal(firstHit + 3, world.Player.HurtTick);
    }

    [Fact]
    public void DogPullsXpFromOutsideTheHerosMagnetAndReplicates()
    {
        var content = QuietContent();
        var world = new RunWorld(content);
        int dog = content.ItemIndex("sausages");
        world.GiveItem(dog);
        world.GX[0] = world.Player.Pos.X + 100;
        world.GY[0] = world.Player.Pos.Y - 8;
        world.GKind[0] = PickupKind.Xp;
        world.GOwner[0] = -1;
        world.GValue[0] = 1;
        world.GAlive[0] = true;
        world.Step(default);

        byte[] snapshot = new byte[RunWorld.SnapshotCapacity];
        var replica = new RunWorld(content);
        Assert.True(replica.TryApplySnapshot(snapshot.AsSpan(0, world.WriteSnapshot(snapshot))));
        Assert.Contains(replica.Companions.ToArray(), companion => companion.Active && companion.Owner == 0 && companion.Item == dog);

        for (int i = 0; i < 3 * RunWorld.TicksPerSecond; i++) world.Step(default);
        Assert.True(world.Player.Xp > 0);
    }

    [Theory]
    [InlineData(PickupKind.Xp)]
    [InlineData(PickupKind.Gold)]
    public void PickupsMoveAtFifteenMoveSpeed(PickupKind kind)
    {
        var world = new RunWorld(QuietContent());
        float start = world.Player.Pos.X - 30;
        world.GX[0] = start;
        world.GY[0] = world.Player.Pos.Y - 8;
        world.GKind[0] = kind;
        world.GOwner[0] = kind == PickupKind.Xp ? -1 : world.Player.Id;
        world.GAlive[0] = true;

        float step = 15 * world.Rules.Units.MoveSpeedScale * RunWorld.Dt;
        world.Step(default);
        TestWorld.Close(start + step, world.GX[0]);
        world.Step(default);
        TestWorld.Close(start + 2 * step, world.GX[0]);
    }

    [Fact]
    public void ArmorAuraWorksForTheOwnerAndNearAllies()
    {
        var content = QuietContent();
        var world = new RunWorld(content, playerCount: 2);
        world.SetPlayerActive(1, true);
        var remote = world.Players[1];
        remote.Pos = world.Player.Pos + new Vector2(135, 0);
        world.GiveItem(content.ItemIndex("dominator_helm"));
        world.StepCoop([default, default]);

        TestWorld.Close(0.18f, world.Player.Stats[Stat.Armor]);
        TestWorld.Close(0.18f, remote.Stats[Stat.Armor]);

        remote.Pos = world.Player.Pos + new Vector2(800, 0);
        world.StepCoop([default, default]);
        TestWorld.Close(0.05f, remote.Stats[Stat.Armor]);
    }

    [Fact]
    public void ASecondDashChargeCanBeSpentBeforeTheFirstRecovers()
    {
        var content = QuietContent();
        var world = new RunWorld(content);
        world.GiveItem(content.ItemIndex("jar_of_wind"));

        world.Step(new RunInput(1, 0, Dash: true));
        while (world.Player.DashTicks > 0) world.Step(new RunInput(1, 0));
        Assert.True(world.Player.DashCooldownTicks > 0);
        world.Step(new RunInput(1, 0, Dash: true));

        Assert.Equal(2, world.Player.DashChargesUsed);
        Assert.True(world.Player.DashTicks > 0);
    }

    [Fact]
    public void NamedBossDropsCursedItemToItsKiller()
    {
        var content = QuietContent();
        content.Enemies[0].Id = "ancient_cultist";
        content.Enemies[0].Boss = true;
        var world = new RunWorld(content, playerCount: 2);
        world.SetPlayerActive(1, true);
        var remote = world.Players[1];
        int boss = world.SpawnEnemy(0, remote.Pos - new Vector2(0, 8));
        int cross = content.ItemIndex("inverted_cross");
        world.DamageEnemy(boss, 1_000_000, DamageKind.Pure, new DamageSourceRef(1), 0, 0);

        Assert.Contains(world.GKind, kind => kind == PickupKind.Cursed);
        world.StepCoop([default, default]);

        Assert.Equal(0, world.Player.ItemStacks[cross]);
        Assert.Equal(1, remote.ItemStacks[cross]);
    }

    [Fact]
    public void GoldVeinHeartAddsDistinctThirdChoiceToEverySecondChest()
    {
        var content = QuietContent();
        var world = new RunWorld(content);
        var player = world.Player;
        world.GiveItem(content.ItemIndex("gold_vein_heart"));
        player.Wallet.Collect(new GoldDrop(100, player.Id), player.Id);

        player.Pos = world.Chests[0];
        world.Step(new RunInput(0, 0, OpenChest: true));
        Assert.Equal(-1, player.ChestOfferThird);
        world.Step(new RunInput(0, 0, ChestChoice: 1));

        player.Pos = world.Chests[1];
        world.Step(new RunInput(0, 0, OpenChest: true));
        int third = player.ChestOfferThird;
        Assert.InRange(third, 0, content.Items.Count - 1);
        Assert.NotEqual(player.ChestOfferFirst, third);
        Assert.NotEqual(player.ChestOfferSecond, third);
        world.Step(new RunInput(0, 0, ChestChoice: 3));
        Assert.Equal(third, world.LastChestItem);
    }

    [Fact]
    public void EveryItemCanRunTogetherAndProduceAValidCoopSnapshot()
    {
        var content = QuietContent();
        content.Enemies[0].Hp = 150;
        content.Enemies[0].Speed = 1;
        var world = new RunWorld(content, seed: 19);
        for (int item = 0; item < content.Items.Count; item++) world.GiveItem(item);
        for (int i = 0; i < 2150; i++)
        {
            if (i % 30 == 0)
                world.SpawnEnemy(0, world.Player.Pos + new Vector2(i % 170 - 85, i % 130 - 65));
            world.Step(new RunInput(1, 0, Dash: i % 60 == 0));
            world.ClearEvents();
        }

        var replica = new RunWorld(content);
        var snapshot = new byte[RunWorld.SnapshotCapacity];
        Assert.True(replica.TryApplySnapshot(snapshot.AsSpan(0, world.WriteSnapshot(snapshot))));
        Assert.Equal(world.Player.ItemStacks, replica.Player.ItemStacks);
        Assert.True(world.Player.Kills > 0);
        Assert.Contains(world.Companions.ToArray(), companion => companion.Active);
    }

    private static ContentSet QuietContent()
    {
        var content = TestContent.LoadCore();
        foreach (var wave in content.Waves.Waves) wave.PerSecond = 0;
        content.Waves.Elites.Clear();
        return content;
    }
}
