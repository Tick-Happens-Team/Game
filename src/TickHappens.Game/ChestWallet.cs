namespace TickHappens.Game;

public sealed class ChestWallet
{
    private decimal _nextPrice = 10;

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
        _nextPrice *= 1.5m;
        return true;
    }
}

public readonly record struct GoldDrop(int Amount, int OwnerId)
{
    public static GoldDrop FromKill(int amount, int killerPlayerId, int? summonOwnerId = null) =>
        new(amount, summonOwnerId ?? killerPlayerId);
}
