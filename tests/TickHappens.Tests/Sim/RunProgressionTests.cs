using TickHappens.Game;
using TickHappens.Game.Content;
using TickHappens.Game.Sim;

namespace TickHappens.Tests;

public sealed class RunProgressionTests
{
    [Fact]
    public void ClockwardenPassiveAddsArmorOnEveryLevelUp()
    {
        var world = new RunWorld(TestContent.LoadCore(), seed: 7, character: "clockwarden");
        float armorBefore = world.Player.Stats[Stat.Armor];

        world.GainXp(world.XpForLevel(1) + world.XpForLevel(2) + world.XpForLevel(3));

        Assert.Equal(4, world.Player.Level);
        Assert.Equal(3, world.PendingLevels);
        Assert.Equal(armorBefore + 3f, world.Player.Stats[Stat.Armor]);
    }

    [Fact]
    public void LevelUpOfferAppliesTheChosenUpgrade()
    {
        var world = new RunWorld(TestContent.LoadCore(), seed: 11);
        world.GainXp(world.XpForLevel(1));
        Assert.Equal(1, world.PendingLevels);
        Assert.Equal(RunPhase.Playing, world.Phase);

        world.Step(default);

        Assert.Equal(RunPhase.LevelUp, world.Phase);
        Assert.Equal(0, world.PendingLevels);
        var offer = world.Offer.ToArray();
        Assert.InRange(offer.Length, 1, 3);
        Assert.Equal(offer.Length, offer.Distinct().ToArray().Length);

        var chosen = offer[0];
        int weaponsBefore = world.Player.Weapons.Count;
        int itemsBefore = world.Player.Items.Count;

        world.Step(new RunInput(0, 0, Choice: 1));

        Assert.Equal(RunPhase.Playing, world.Phase);
        Assert.Equal(0, world.PendingLevels);
        switch (chosen.Kind)
        {
            case OfferKind.NewWeapon:
                Assert.Equal(weaponsBefore + 1, world.Player.Weapons.Count);
                Assert.Contains(world.Player.Weapons, w => w.Def == chosen.Def && w.Level == 1);
                break;
            case OfferKind.WeaponLevel:
                Assert.Equal(weaponsBefore, world.Player.Weapons.Count);
                Assert.Equal(chosen.NewLevel, world.Player.Weapons.Single(w => w.Def == chosen.Def).Level);
                break;
            case OfferKind.NewItem:
                Assert.Equal(itemsBefore + 1, world.Player.Items.Count);
                Assert.Contains(world.Player.Items, i => i.Def == chosen.Def && i.Level == 1);
                break;
            case OfferKind.ItemLevel:
                Assert.Equal(itemsBefore, world.Player.Items.Count);
                Assert.Equal(chosen.NewLevel, world.Player.Items.Single(i => i.Def == chosen.Def).Level);
                break;
            case OfferKind.Heal:
                Assert.True(world.Player.Hp > 0, "a heal offer must leave the player alive");
                break;
        }
    }

    [Fact]
    public void LevelUpOfferReopensWhileLevelsArePending()
    {
        var world = new RunWorld(TestContent.LoadCore(), seed: 11);
        world.GainXp(world.XpForLevel(1) + world.XpForLevel(2) + world.XpForLevel(3));
        Assert.Equal(3, world.PendingLevels);

        world.Step(default);
        Assert.Equal(RunPhase.LevelUp, world.Phase);

        int offers = 0;
        for (int guard = 0; guard < 10 && world.Phase == RunPhase.LevelUp; guard++)
        {
            offers++;
            Assert.InRange(world.Offer.Count, 1, 3);
            world.Step(new RunInput(0, 0, Choice: 1));
        }

        Assert.Equal(3, offers);
        Assert.Equal(RunPhase.Playing, world.Phase);
        Assert.Equal(0, world.PendingLevels);
    }

    [Fact]
    public void StatueHoldAddsBonusMinutes()
    {
        var content = TestContent.LoadCore();
        var world = new RunWorld(content, seed: 5);
        var rules = content.Rules.Statue;
        float minutesBefore = world.Minutes;

        world.Player.Pos = world.Statues[0];
        int ticks = (int)(rules.HoldSeconds * RunWorld.TicksPerSecond) + 2;
        for (int i = 0; i < ticks; i++) world.Step(default);

        Assert.True(world.StatueActive[0], "standing on the statue for holdSeconds must activate it");
        Assert.Equal(rules.Minutes * 60f, world.BonusSeconds);
        Assert.True(world.Minutes >= minutesBefore + rules.Minutes,
            $"minutes {world.Minutes} should have jumped by {rules.Minutes}");
        Assert.True(world.Minutes <= minutesBefore + rules.Minutes + ticks * RunWorld.Dt / 60f + 0.001f,
            $"minutes {world.Minutes} jumped by more than one statue bonus");
    }

    [Fact]
    public void ChestPurchaseSpendsGoldAndRaisesPrice()
    {
        var content = TestContent.LoadCore();
        var world = new RunWorld(content, seed: 1);
        var wallet = world.Player.Wallet;
        wallet.Collect(new GoldDrop(100, world.Player.Id), world.Player.Id);
        Assert.Equal(100, wallet.Gold);

        world.Player.Pos = world.Chests[0];
        world.Step(default);

        Assert.True(world.ChestOpen[0], "standing on an affordable chest must open it");
        Assert.Equal(100 - content.Rules.Chests.StartPrice, wallet.Gold);
        Assert.Equal(15, wallet.NextPrice);
        Assert.True(world.LastChestReward.HasValue, "an opened chest must grant a reward");
    }

    [Fact]
    public void DifficultyScalesWithRunTimeNotPlayerLevel()
    {
        var content = TestContent.LoadCore();
        var world = new RunWorld(content, seed: 5);
        float before = world.HpScale;
        Assert.Equal(1f, before);

        world.Player.Pos = world.Statues[0];
        int ticks = (int)(content.Rules.Statue.HoldSeconds * RunWorld.TicksPerSecond) + 2;
        for (int i = 0; i < ticks; i++) world.Step(default);

        Assert.True(world.StatueActive[0], "the statue must be active before difficulty is compared");
        Assert.True(world.HpScale > before, $"hp scale {world.HpScale} should exceed {before} after the statue bonus");
        Assert.Equal(1f + content.Waves.HpPerMinute * world.Minutes, world.HpScale);

        float afterStatue = world.HpScale;
        world.GainXp(world.XpForLevel(1) * 3);
        Assert.True(world.Player.Level > 1, "the player should have levelled up");
        Assert.Equal(afterStatue, world.HpScale);
    }
}
