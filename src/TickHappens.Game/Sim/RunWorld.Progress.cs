using System.Numerics;
using TickHappens.Game.Content;

namespace TickHappens.Game.Sim;

public enum PickupKind : byte { Xp, Gold, Heal, Cursed }

public sealed partial class RunWorld
{
    public const int PickupCapacity = 4096;

    // ---- pickups ----
    public readonly float[] GX = new float[PickupCapacity], GY = new float[PickupCapacity];
    public readonly PickupKind[] GKind = new PickupKind[PickupCapacity];
    public readonly int[] GValue = new int[PickupCapacity], GOwner = new int[PickupCapacity];
    public readonly bool[] GAlive = new bool[PickupCapacity];
    private readonly bool[] _gPulled = new bool[PickupCapacity];
    private readonly byte[] _gPullTarget = new byte[PickupCapacity];
    private int _pickupCursor;

    // ---- statues and chests ----
    public Vector2[] Statues { get; private set; } = [];
    public bool[] StatueActive { get; private set; } = [];
    public float[] StatueHold { get; private set; } = [];
    public Vector2[] Chests { get; private set; } = [];
    public bool[] ChestOpen { get; private set; } = [];
    private const int ChestCooldownTicks = 60 * TicksPerSecond;
    private int[] _chestReadyTick = [];
    /// <summary>Unopened chest the player stands at but cannot afford yet (-1 if none).</summary>
    public int NearbyChest => Player.NearbyChest;

    private void InitPickups() { }

    private void PlaceStatuesAndChests()
    {
        // Deterministic layout from the run seed: rings around the start, so early ones are close.
        var rng = new SimRng(Seed * 2654435761u + 17);
        var center = new Vector2(Rules.ArenaSize / 2f);
        float arena = Rules.ArenaSize;
        Vector2 Ring(int i, int n, float r0, float r1)
        {
            float a = (i + rng.Range(0.15f, 0.85f)) / n * MathF.Tau;
            float r = rng.Range(r0, r1);
            return Vector2.Clamp(center + new Vector2(MathF.Cos(a) * r, MathF.Sin(a) * r), new Vector2(40), new Vector2(arena - 40));
        }
        int sc = Rules.Statue.Count, cc = Rules.Chests.Count;
        Statues = new Vector2[sc];
        for (int i = 0; i < sc; i++) Statues[i] = Ring(i, sc, 220, 700);
        StatueActive = new bool[sc];
        StatueHold = new float[sc];
        Chests = new Vector2[cc];
        for (int i = 0; i < cc; i++) Chests[i] = Ring(i, cc, 110, 600);
        ChestOpen = new bool[cc];
        _chestReadyTick = new int[cc];
    }

    private void DropPickup(PickupKind kind, float x, float y, int value, int owner)
    {
        int slot = -1;
        for (int k = 0; k < PickupCapacity; k++)
        {
            int i = (_pickupCursor + k) % PickupCapacity;
            if (!GAlive[i]) { slot = i; break; }
        }
        if (slot < 0)
        {
            // Full: merge XP into an existing crystal instead of losing it.
            if (kind == PickupKind.Xp)
                for (int i = 0; i < PickupCapacity; i++)
                    if (GKind[i] == PickupKind.Xp) { GValue[i] += value; return; }
            if (kind == PickupKind.Cursed) AwardItem(_players[owner], value, fromChest: false);
            return;
        }
        _pickupCursor = (slot + 1) % PickupCapacity;
        GX[slot] = x; GY[slot] = y; GKind[slot] = kind; GValue[slot] = value; GOwner[slot] = owner;
        GAlive[slot] = true;
        _gPulled[slot] = false;
    }

    private void StepPickups()
    {
        for (int i = 0; i < PickupCapacity; i++)
        {
            if (!GAlive[i]) continue;
            int collector = _gPullTarget[i];
            if (!_gPulled[i] || !_players[collector].Active || _players[collector].Hp <= 0)
            {
                _gPulled[i] = false;
                float closest = float.PositiveInfinity;
                for (int j = 0; j < _players.Length; j++)
                {
                    var candidate = _players[j];
                    if (!candidate.Active || candidate.Hp <= 0 ||
                        (GKind[i] != PickupKind.Xp && GOwner[i] != j)) continue;
                    float mx = candidate.Pos.X - GX[i], my = candidate.Pos.Y - 8 - GY[i];
                    float dSquared = mx * mx + my * my;
                    float radius = candidate.Stats[Stat.Magnet];
                    if (dSquared >= closest || dSquared >= radius * radius) continue;
                    closest = dSquared;
                    collector = j;
                    _gPulled[i] = true;
                }
                if (!_gPulled[i]) continue;
                _gPullTarget[i] = (byte)collector;
            }
            var p = _players[collector];
            float magnet = p.Stats[Stat.Magnet];
            float dx = p.Pos.X - GX[i], dy = p.Pos.Y - 8 - GY[i], d2 = dx * dx + dy * dy;
            float d = MathF.Sqrt(d2);
            if (d < 7)
            {
                GAlive[i] = false;
                if (GKind[i] == PickupKind.Xp) GainXp(p, GValue[i]);
                else if (GKind[i] == PickupKind.Gold) CollectGold(p, GValue[i], GOwner[i]);
                else if (GKind[i] == PickupKind.Heal) RestorePlayer(p, p.Hp * GValue[i] / 10_000);
                else AwardItem(p, GValue[i], fromChest: false);
                Emit(EventKind.Pickup, GX[i], GY[i], GValue[i], a: (int)GKind[i]);
                continue;
            }
            float speed = GKind[i] is PickupKind.Xp or PickupKind.Gold
                ? 15 * Rules.Units.MoveSpeedScale
                : 140 + 260 * MathF.Max(0, 1 - d / MathF.Max(magnet, 1));
            float step = MathF.Min(d, speed * Dt);
            GX[i] += dx / d * step;
            GY[i] += dy / d * step;
        }
    }

    /// <summary>Adds owned gold through the gold multiplier, carrying fractions so small bonuses are never lost.</summary>
    private static void CollectGold(PlayerState p, int amount, int owner)
    {
        if (owner != p.Id) return;
        float total = amount * p.Stats[Stat.GoldGain] + p.GoldCarry;
        int whole = (int)MathF.Floor(total);
        p.GoldCarry = total - whole;
        if (whole > 0) p.Wallet.Collect(new GoldDrop(whole, owner), p.Id);
    }

    /// <summary>Levels apply automatically: table growth (HP, damage) and level-scaled passives. Max HP growth also heals.</summary>
    public void GainXp(float amount) => GainXp(Player, amount);

    private void GainXp(PlayerState p, float amount)
    {
        p.Xp += amount;
        while (p.Xp >= p.XpNext)
        {
            p.Xp -= p.XpNext;
            float before = MaxHealthFor(p);
            p.Level++;
            RollCubeLevel(p);
            p.XpNext = XpForLevel(p.Level);
            ApplyLevelStats(p);
            UpdateAllPlayerStats();
            if (p.GhostUntilTick == 0) p.Hp += MathF.Max(0, MaxHealthFor(p) - before);
            Emit(EventKind.LevelUp, p.Pos.X, p.Pos.Y, p.Level, a: p.Id);
        }
    }

    // ---------------------------------------------------------------- items
    /// <summary>Gives the player one more copy of an item: its stat mods apply once per copy; weapon copies add +1 count.</summary>
    public void GiveItem(int def) => GiveItem(Player, def);

    private void GiveItem(PlayerState p, int def)
    {
        float before = MaxHealthFor(p);
        float shieldBefore = ShieldCapacity(p);
        p.ItemStacks[def]++;
        var item = Content.Items[def];
        if (item.Effect is { } effect && p.ItemStacks[def] == 1)
        {
            p.EffectItems.Add(def);
            if (effect.Kind is "resetCooldown" or "lowestHealthShield")
                p.ItemTimers[def] = effect.Duration * p.Stats[Stat.Cooldown];
        }
        if (p.ItemStacks[def] == 1 && item.Weapon is { Kind: "accelerator" or "execute" or "snipe" or "dropped" or "boomerang" or "cane" } weapon)
            p.ItemTimers[def] = weapon.Cooldown * p.Stats[Stat.Cooldown];
        if (item.Companion is { } companion && p.ItemStacks[def] == 1)
        {
            if (companion.Kind == "demon") p.ConversionItem = def;
            else if (companion.Kind is "gnome" or "boar" or "leech")
                p.ItemTimers[def] = companion.Interval * p.Stats[Stat.Cooldown];
        }
        foreach (var m in item.Mods)
            if (ContentLoader.TryParseStat(m.Stat, out var s)) p.Stats.Apply(s, m.Add, m.Pct);
        // Max HP increases also heal by the same amount; decreases clamp.
        float after = MaxHealthFor(p);
        p.Hp = after >= before ? p.Hp + (after - before) : MathF.Min(p.Hp, after);
        p.Shield += Math.Max(0, ShieldCapacity(p) - shieldBefore);
        if (item.XpOnPickup > 0) GainXp(p, item.XpOnPickup * Minutes);
    }

    public const int ChestChoiceTicks = 10 * TicksPerSecond;

    /// <summary>Rolls a weighted rarity, then a uniform item within that rarity.</summary>
    public int RollChestItem() => RollChestItem(-1, -1);

    private int RollChestItem(int excluded, int alsoExcluded)
    {
        var weights = Rules.Chests.RarityWeights;
        float total = 0;
        var rarities = ContentSet.Rarities;
        Span<int> counts = stackalloc int[rarities.Length];
        counts.Clear();
        for (int i = 0; i < Content.Items.Count; i++)
        {
            if (i == excluded || i == alsoExcluded) continue;
            int rarityIndex = Array.IndexOf(rarities, Content.Items[i].Rarity);
            if (rarityIndex >= 0) counts[rarityIndex]++;
        }
        for (int i = 0; i < rarities.Length; i++)
            if (counts[i] > 0) total += weights.GetValueOrDefault(rarities[i]);
        if (total <= 0) return -1;
        float roll = _rng.Next() * total;
        for (int rarity = 0; rarity < rarities.Length; rarity++)
        {
            float weight = weights.GetValueOrDefault(rarities[rarity]);
            if (counts[rarity] == 0 || weight <= 0) continue;
            if (roll >= weight) { roll -= weight; continue; }
            int pick = _rng.Int(counts[rarity]);
            for (int i = 0; i < Content.Items.Count; i++)
                if (i != excluded && i != alsoExcluded && Content.Items[i].Rarity == rarities[rarity] && pick-- == 0) return i;
        }
        return -1;
    }

    // ---------------------------------------------------------------- statues and chests
    private void StepStatues()
    {
        var st = Rules.Statue;
        for (int i = 0; i < Statues.Length; i++)
        {
            if (StatueActive[i]) continue;
            int player = NearestLivingPlayer(Statues[i]);
            bool near = player >= 0 && Vector2.DistanceSquared(_players[player].Pos, Statues[i]) < st.Radius * st.Radius;
            StatueHold[i] = near ? StatueHold[i] + Dt : MathF.Max(0, StatueHold[i] - Dt * 2);
            if (StatueHold[i] < st.HoldSeconds) continue;
            StatueActive[i] = true;
            BonusSeconds += st.Minutes * 60;
            Emit(EventKind.Statue, Statues[i].X, Statues[i].Y, st.Minutes, a: i);
        }
    }

    private void StepChests(ReadOnlySpan<RunInput> inputs)
    {
        for (int i = 0; i < Chests.Length; i++)
            ChestOpen[i] = Tick < _chestReadyTick[i];

        foreach (var p in _players)
        {
            p.NearbyChest = -1;
            if (!p.Active || p.Hp <= 0) continue;
            if (p.ChestOfferFirst >= 0)
            {
                int choice = inputs[p.Id].ChestChoice;
                if (Tick >= p.ChestOfferUntilTick) choice = 1;
                int selected = choice switch
                {
                    1 => p.ChestOfferFirst,
                    2 => p.ChestOfferSecond,
                    3 => p.ChestOfferThird,
                    _ => -1,
                };
                if (selected >= 0)
                {
                    AwardItem(p, selected, fromChest: true);
                    p.ChestOfferFirst = p.ChestOfferSecond = p.ChestOfferThird = -1;
                    p.ChestOfferUntilTick = 0;
                }
                continue;
            }
            var area = CombatArea.Circle(p.Pos, Rules.Chests.Radius);
            for (int i = 0; i < Chests.Length; i++)
            {
                if (ChestOpen[i] || !area.Contains(Chests[i], out _)) continue;
                int price = p.Wallet.NextPrice;
                if (!p.Wallet.TryPay()) { p.NearbyChest = i; continue; }
                float refund = price * Math.Min(1, p.Stats[Stat.GoldRefund]) + p.RefundCarry;
                int coins = (int)MathF.Floor(refund);
                p.RefundCarry = refund - coins;
                if (coins > 0) p.Wallet.Collect(new GoldDrop(coins, p.Id), p.Id);
                _chestReadyTick[i] = Tick + ChestCooldownTicks;
                ChestOpen[i] = true;
                int first = RollChestItem();
                int second = first >= 0 ? RollChestItem(first, -1) : -1;
                bool extraChoice = false;
                foreach (int item in p.EffectItems)
                {
                    if (Content.Items[item].Effect!.Kind != "extraChestChoice") continue;
                    extraChoice = ++p.BoughtChests % 2 == 0;
                    break;
                }
                if (second < 0) AwardItem(p, first, fromChest: true);
                else
                {
                    p.ChestOfferFirst = first;
                    p.ChestOfferSecond = second;
                    p.ChestOfferThird = extraChoice ? RollChestItem(first, second) : -1;
                    p.ChestOfferUntilTick = Tick + ChestChoiceTicks;
                }
                break;
            }
        }
        foreach (var p in _players)
            if (p.NearbyChest >= 0 && ChestOpen[p.NearbyChest]) p.NearbyChest = -1;
    }

    private void AwardItem(PlayerState player, int item, bool fromChest)
    {
        if (item < 0) return;
        GiveItem(player, item);
        if (fromChest) player.LastChestItem = item;
        Emit(fromChest ? EventKind.Chest : EventKind.BossReward, player.Pos.X, player.Pos.Y, a: item, b: player.Id);
    }

    /// <summary>Item granted by the most recent chest (-1 if none yet).</summary>
    public int LastChestItem => Player.LastChestItem;
}
