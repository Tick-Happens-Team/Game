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
    private int _pickupCursor;

    // ---- statues and chests ----
    public Vector2[] Statues { get; private set; } = [];
    public bool[] StatueActive { get; private set; } = [];
    public float[] StatueHold { get; private set; } = [];
    public Vector2[] Chests { get; private set; } = [];
    public bool[] ChestOpen { get; private set; } = [];
    /// <summary>Unopened chest the player stands at but cannot afford yet (-1 if none).</summary>
    public int NearbyChest { get; private set; } = -1;

    private void InitPickups() { }

    private void PlaceStatuesAndChests()
    {
        // Deterministic layout from the run seed: rings around the start, so early ones are close.
        var rng = new SimRng(Seed * 2654435761u + 17);
        var center = Player.Pos;
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
        var p = Player;
        float magnet = p.Stats[Stat.Magnet];
        for (int i = 0; i < PickupCapacity; i++)
        {
            if (!GAlive[i]) continue;
            // Gold belongs to the killer: other players cannot see-pull or collect it.
            if (GKind[i] == PickupKind.Gold && GOwner[i] != p.Id) continue;
            float dx = p.Pos.X - GX[i], dy = p.Pos.Y - 8 - GY[i], d2 = dx * dx + dy * dy;
            if (!_gPulled[i] && d2 < magnet * magnet) _gPulled[i] = true;
            if (!_gPulled[i]) continue;
            float d = MathF.Sqrt(d2);
            if (d < 7)
            {
                GAlive[i] = false;
                if (GKind[i] == PickupKind.Xp) GainXp(GValue[i]);
                else p.Wallet.Collect(new GoldDrop(GValue[i], GOwner[i]), p.Id);
                Emit(EventKind.Pickup, GX[i], GY[i], GValue[i], a: (int)GKind[i]);
                continue;
            }
            float speed = 140 + 260 * MathF.Max(0, 1 - d / MathF.Max(magnet, 1));
            float step = MathF.Min(d, speed * Dt);
            GX[i] += dx / d * step;
            GY[i] += dy / d * step;
        }
    }

    public void GainXp(float amount)
    {
        var p = Player;
        p.Xp += amount;
        while (p.Xp >= p.XpNext)
        {
            p.Xp -= p.XpNext;
            p.Level++;
            p.XpNext = XpForLevel(p.Level);
            PendingLevels++;
            ApplyPassives("levelUp");
            Emit(EventKind.LevelUp, p.Pos.X, p.Pos.Y, p.Level);
        }
    }

    private void ApplyPassives(string trigger)
    {
        foreach (var pas in Player.Character.Passives)
            if (pas.On == trigger && ContentLoader.TryParseStat(pas.Stat, out var s))
                Player.Stats.Apply(s, pas.Add, pas.Pct);
    }

    // ---------------------------------------------------------------- upgrades
    /// <summary>All upgrades currently available to the player (owned weapon/item levels, new ones while slots remain).</summary>
    public List<UpgradeOption> AvailableUpgrades()
    {
        var p = Player;
        var list = new List<UpgradeOption>();
        foreach (var w in p.Weapons)
            if (w.Level < Content.Weapons[w.Def].Levels.Count) list.Add(new(OfferKind.WeaponLevel, w.Def, w.Level + 1));
        foreach (var it in p.Items)
            if (it.Level < Content.Items[it.Def].MaxLevel) list.Add(new(OfferKind.ItemLevel, it.Def, it.Level + 1));
        if (p.Weapons.Count < Rules.MaxWeapons)
            for (int i = 0; i < Content.Weapons.Count; i++)
                if (!p.Weapons.Exists(w => w.Def == i)) list.Add(new(OfferKind.NewWeapon, i, 1));
        if (p.Items.Count < Rules.MaxItems)
            for (int i = 0; i < Content.Items.Count; i++)
                if (!p.Items.Exists(x => x.Def == i)) list.Add(new(OfferKind.NewItem, i, 1));
        return list;
    }

    private void OpenOffer()
    {
        PendingLevels--;
        _offer.Clear();
        var pool = AvailableUpgrades();
        for (int k = 0; k < 3 && pool.Count > 0; k++)
        {
            int i = _rng.Int(pool.Count);
            _offer.Add(pool[i]);
            pool.RemoveAt(i);
        }
        if (_offer.Count == 0) _offer.Add(new UpgradeOption(OfferKind.Heal, 0, 0));
        Phase = RunPhase.LevelUp;
    }

    public void ChooseUpgrade(int index)
    {
        if (Phase != RunPhase.LevelUp || index < 0 || index >= _offer.Count) return;
        Apply(_offer[index]);
        _offer.Clear();
        Phase = RunPhase.Playing;
        if (PendingLevels > 0) OpenOffer();
    }

    public void Apply(UpgradeOption o)
    {
        var p = Player;
        switch (o.Kind)
        {
            case OfferKind.NewWeapon: p.Weapons.Add(new WeaponState(o.Def, 1)); break;
            case OfferKind.WeaponLevel: p.Weapons.Find(w => w.Def == o.Def)!.Level = o.NewLevel; break;
            case OfferKind.NewItem:
                p.Items.Add(new ItemState(o.Def, 1));
                ApplyItemLevel(o.Def);
                break;
            case OfferKind.ItemLevel:
                p.Items.Find(x => x.Def == o.Def)!.Level = o.NewLevel;
                ApplyItemLevel(o.Def);
                break;
            case OfferKind.Heal: p.Hp = MathF.Min(p.Stats[Stat.MaxHp], p.Hp + 30); break;
        }
    }

    private void ApplyItemLevel(int def)
    {
        var p = Player;
        float maxBefore = p.Stats[Stat.MaxHp];
        foreach (var m in Content.Items[def].Mods)
            if (ContentLoader.TryParseStat(m.Stat, out var s)) p.Stats.Apply(s, m.Add, m.Pct);
        // Max HP increases also heal by the same amount.
        p.Hp += MathF.Max(0, p.Stats[Stat.MaxHp] - maxBefore);
    }

    public string Describe(UpgradeOption o) => o.Kind switch
    {
        OfferKind.NewWeapon => $"{Content.Weapons[o.Def].Name}|NEW WEAPON|{Content.Weapons[o.Def].Description}",
        OfferKind.WeaponLevel => $"{Content.Weapons[o.Def].Name}|LEVEL {o.NewLevel}|{WeaponDelta(o.Def, o.NewLevel)}",
        OfferKind.NewItem => $"{Content.Items[o.Def].Name}|NEW ITEM|{Content.Items[o.Def].Description}",
        OfferKind.ItemLevel => $"{Content.Items[o.Def].Name}|LEVEL {o.NewLevel}|{Content.Items[o.Def].Description}",
        _ => "Rest|HEAL|Restore 30 HP.",
    };

    private string WeaponDelta(int def, int level)
    {
        var a = Content.Weapons[def].Levels[level - 2];
        var b = Content.Weapons[def].Levels[level - 1];
        var parts = new List<string>();
        if (b.Damage != a.Damage) parts.Add($"DMG {a.Damage}>{b.Damage}");
        if (b.Count != a.Count) parts.Add($"COUNT {a.Count}>{b.Count}");
        if (b.Area != a.Area) parts.Add($"AREA {a.Area}>{b.Area}");
        if (b.Cooldown != a.Cooldown) parts.Add($"CD {a.Cooldown}>{b.Cooldown}");
        if (b.Pierce != a.Pierce) parts.Add($"PIERCE {a.Pierce}>{b.Pierce}");
        return string.Join(" ", parts);
    }

    // ---------------------------------------------------------------- statues and chests
    private void StepStatues()
    {
        var st = Rules.Statue;
        for (int i = 0; i < Statues.Length; i++)
        {
            if (StatueActive[i]) continue;
            bool near = Vector2.DistanceSquared(Player.Pos, Statues[i]) < st.Radius * st.Radius;
            StatueHold[i] = near ? StatueHold[i] + Dt : MathF.Max(0, StatueHold[i] - Dt * 2);
            if (StatueHold[i] < st.HoldSeconds) continue;
            StatueActive[i] = true;
            BonusSeconds += st.Minutes * 60;
            Emit(EventKind.Statue, Statues[i].X, Statues[i].Y, st.Minutes, a: i);
        }
    }

    private void StepChests()
    {
        NearbyChest = -1;
        var area = CombatArea.Circle(Player.Pos, Rules.Chests.Radius);
        for (int i = 0; i < Chests.Length; i++)
        {
            if (ChestOpen[i] || !area.Contains(Chests[i], out _)) continue;
            int price = Player.Wallet.NextPrice;
            if (!Player.Wallet.TryPay()) { NearbyChest = i; continue; }
            ChestOpen[i] = true;
            // Chests open passively and grant a random upgrade (or a heal when everything is maxed).
            var pool = AvailableUpgrades();
            var reward = pool.Count > 0 ? pool[_rng.Int(pool.Count)] : new UpgradeOption(OfferKind.Heal, 0, 0);
            Apply(reward);
            LastChestReward = reward;
            Emit(EventKind.Chest, Chests[i].X, Chests[i].Y, price, a: i);
        }
    }

    public UpgradeOption? LastChestReward { get; private set; }
}
