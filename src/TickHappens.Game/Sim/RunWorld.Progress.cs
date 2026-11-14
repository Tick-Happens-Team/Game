using System.Numerics;
using TickHappens.Game.Content;

namespace TickHappens.Game.Sim;

public enum PickupKind : byte { Xp, Gold }

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
                        (GKind[i] == PickupKind.Gold && GOwner[i] != j)) continue;
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
                else CollectGold(p, GValue[i], GOwner[i]);
                Emit(EventKind.Pickup, GX[i], GY[i], GValue[i], a: (int)GKind[i]);
                continue;
            }
            float speed = 140 + 260 * MathF.Max(0, 1 - d / MathF.Max(magnet, 1));
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
            p.XpNext = XpForLevel(p.Level);
            ApplyLevelStats(p);
            UpdateDynamicStats(p);
            p.Hp += MathF.Max(0, MaxHealthFor(p) - before);
            Emit(EventKind.LevelUp, p.Pos.X, p.Pos.Y, p.Level);
        }
    }

    // ---------------------------------------------------------------- items
    /// <summary>Gives the player one more copy of an item: its stat mods apply once per copy; weapon copies add +1 count.</summary>
    public void GiveItem(int def) => GiveItem(Player, def);

    private void GiveItem(PlayerState p, int def)
    {
        float before = MaxHealthFor(p);
        p.ItemStacks[def]++;
        foreach (var m in Content.Items[def].Mods)
            if (ContentLoader.TryParseStat(m.Stat, out var s)) p.Stats.Apply(s, m.Add, m.Pct);
        // Max HP increases also heal by the same amount; decreases clamp.
        float after = MaxHealthFor(p);
        p.Hp = after >= before ? p.Hp + (after - before) : MathF.Min(p.Hp, after);
    }

    /// <summary>Rolls a chest item: pick a rarity by weight among rarities that have items, then a uniform item of it.</summary>
    public int RollChestItem()
    {
        var weights = Rules.Chests.RarityWeights;
        float total = 0;
        foreach (var r in ContentSet.Rarities)
            if (Content.Items.Exists(i => i.Rarity == r)) total += weights.GetValueOrDefault(r);
        if (total <= 0) return -1;
        float roll = _rng.Next() * total;
        string rarity = ContentSet.Rarities[^1];
        foreach (var r in ContentSet.Rarities)
        {
            if (!Content.Items.Exists(i => i.Rarity == r)) continue;
            roll -= weights.GetValueOrDefault(r);
            if (roll <= 0) { rarity = r; break; }
        }
        int n = Content.Items.Count(i => i.Rarity == rarity);
        int pick = _rng.Int(Math.Max(1, n));
        for (int i = 0; i < Content.Items.Count; i++)
            if (Content.Items[i].Rarity == rarity && pick-- == 0) return i;
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

    private void StepChests()
    {
        foreach (var p in _players)
        {
            p.NearbyChest = -1;
            if (!p.Active || p.Hp <= 0) continue;
            var area = CombatArea.Circle(p.Pos, Rules.Chests.Radius);
            for (int i = 0; i < Chests.Length; i++)
            {
                if (ChestOpen[i] || !area.Contains(Chests[i], out _)) continue;
                int price = p.Wallet.NextPrice;
                if (!p.Wallet.TryPay()) { p.NearbyChest = i; continue; }
                ChestOpen[i] = true;
                int item = RollChestItem();
                if (item >= 0) GiveItem(p, item);
                p.LastChestItem = item;
                Emit(EventKind.Chest, Chests[i].X, Chests[i].Y, price, a: item);
            }
        }
        foreach (var p in _players)
            if (p.NearbyChest >= 0 && ChestOpen[p.NearbyChest]) p.NearbyChest = -1;
    }

    /// <summary>Item granted by the most recent chest (-1 if none yet).</summary>
    public int LastChestItem => Player.LastChestItem;
}
