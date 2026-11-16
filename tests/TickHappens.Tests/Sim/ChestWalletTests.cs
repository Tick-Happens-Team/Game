using TickHappens.Game;

namespace TickHappens.Tests;

public sealed class ChestWalletTests
{
    [Fact]
    public void SummonKillsCreditTheSummonOwner()
    {
        var summonKill = GoldDrop.FromKill(10, killerPlayerId: 0, summonOwnerId: 1);
        Assert.Equal(1, summonKill.OwnerId);
        Assert.Equal(10, summonKill.Amount);

        Assert.Equal(0, GoldDrop.FromKill(10, killerPlayerId: 0).OwnerId);
    }

    [Fact]
    public void WalletIgnoresGoldOwnedByAnotherPlayer()
    {
        var wallet = new ChestWallet();

        wallet.Collect(new GoldDrop(10, OwnerId: 1), playerId: 0);
        Assert.Equal(0, wallet.Gold);

        wallet.Collect(new GoldDrop(10, OwnerId: 0), playerId: 0);
        Assert.Equal(10, wallet.Gold);
    }

    [Fact]
    public void EveryPurchaseMultipliesTheNextPrice()
    {
        var wallet = new ChestWallet();
        wallet.Collect(new GoldDrop(100, OwnerId: 0), playerId: 0);
        Assert.Equal(10, wallet.NextPrice);

        Assert.True(wallet.TryPay());
        Assert.Equal(90, wallet.Gold);
        Assert.Equal(15, wallet.NextPrice);

        Assert.True(wallet.TryPay());
        Assert.Equal(75, wallet.Gold);
        Assert.Equal(23, wallet.NextPrice);

        Assert.True(wallet.TryPay());
        Assert.Equal(52, wallet.Gold);
        Assert.Equal(34, wallet.NextPrice);

        Assert.True(wallet.TryPay());
        Assert.Equal(18, wallet.Gold);
        Assert.Equal(51, wallet.NextPrice);

        Assert.False(wallet.TryPay());
        Assert.Equal(18, wallet.Gold);
        Assert.Equal(51, wallet.NextPrice);
    }
}
