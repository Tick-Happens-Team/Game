namespace TickHappens.Game;

/// <summary>Per-player gold and chest price progression: every purchase multiplies that player's next price by the growth factor (1.5).</summary>
public sealed class ChestWallet(int startPrice = 10, decimal growth = 1.5m)
{
    private decimal _nextPrice = startPrice;

    public int Gold { get; private set; }
    public int NextPrice => (int)decimal.Ceiling(_nextPrice);

    public void Collect(GoldDrop drop, int playerId)
    {
        if (drop.OwnerId == playerId) Gold += drop.Amount;
    }

    public bool TryPay()
    {
        int price = NextPrice;
        if (Gold < price) return false;
        Gold -= price;
        _nextPrice *= growth;
        return true;
    }

    internal void ApplyReplica(int gold, int nextPrice)
    {
        Gold = gold;
        _nextPrice = nextPrice;
    }
}

public readonly record struct GoldDrop(int Amount, int OwnerId)
{
    public static GoldDrop FromKill(int amount, int killerPlayerId, int? summonOwnerId = null) =>
        new(amount, summonOwnerId ?? killerPlayerId);
}
