using System.Numerics;
using TickHappens.Engine.Spatial;
using TickHappens.Game.Content;

namespace TickHappens.Game.Sim;

/// <summary>
/// Deterministic single-run simulation at 30 Hz. Owns run time, waves, enemies, weapons, drops, chests and statues.
/// Rendering reads state and drains <see cref="Events"/>; nothing here touches the clock, GL or unseeded randomness.
/// </summary>
public sealed partial class RunWorld
{
    public const int TicksPerSecond = 30;
    public const float Dt = 1f / TicksPerSecond;
    public const float PlayerRadius = 6;

    public ContentSet Content { get; }
    public RunRules Rules => Content.Rules;
    public int Tick { get; private set; }
    public RunPhase Phase { get; private set; } = RunPhase.Playing;
    /// <summary>Bonus seconds added by time statues.</summary>
    public float BonusSeconds { get; private set; }
    /// <summary>Run clock in seconds: real elapsed time plus statue bonuses. Drives difficulty.</summary>
    public float RunSeconds => Tick * Dt + BonusSeconds;
    public float Minutes => RunSeconds / 60f;
    public uint Seed { get; }
    public PlayerState Player { get; }

    private SimRng _rng;
    private readonly List<GameEvent> _events = [];
    public readonly List<Vector2> ChainPoints = [];

    // ---- enemies (struct of arrays, free-list slots) ----
    public readonly int EnemyCapacity;
    public readonly float[] EX, EY, EHp, EMaxHp, EKx, EKy, EDamage;
    public readonly short[] EDef;
    public readonly bool[] EAlive;
    public readonly byte[] EFlash;
    public readonly int[] EUid;
    private readonly float[] _prevX, _prevY;
    private readonly Stack<int> _freeEnemies = new();
    private int _enemyHigh, _nextUid = 1;
    public int AliveEnemies { get; private set; }
    public int EnemyHighWater => _enemyHigh;
    private readonly SpatialGrid _grid;
    private readonly DamageKind[] _enemyKind;
    private float _spawnAcc;
    private int _nextElite;

    public RunWorld(ContentSet content, uint seed = 1, string? character = null)
    {
        Content = content;
        Seed = seed;
        _rng = new SimRng(seed);
        var ch = character != null ? content.Characters.Find(c => c.Id == character) ?? throw new ArgumentException($"unknown character '{character}'")
                                   : content.Characters[0];
        int arena = Rules.ArenaSize;
        Player = new PlayerState
        {
            Character = ch,
            Stats = new StatBlock(),
            Attack = ch.Attack ?? Rules.BasicAttack,
            Wallet = new ChestWallet(Rules.Chests.StartPrice, Rules.Chests.PriceGrowth),
            ItemStacks = new int[content.Items.Count],
            ItemTimers = new float[content.Items.Count],
            Pos = new Vector2(arena / 2f, arena / 2f),
        };
        Player.Prev = Player.Pos;
        var st = Player.Stats;
        var cs = ch.Stats;
        st.SetBase(Stat.MoveSpeed, cs.MoveSpeed);
        st.SetBase(Stat.AttackSpeed, cs.AttackSpeed);
        st.SetBase(Stat.Armor, cs.Armor);
        st.SetBase(Stat.MagicResist, cs.MagicResist);
        st.SetBase(Stat.Regen, cs.Regen);
        st.SetBase(Stat.Magnet, cs.Magnet);
        ApplyLevelStats();
        UpdateDynamicStats();
        Player.Hp = MaxHealth;
        Player.XpNext = XpForLevel(1);

        EnemyCapacity = content.Waves.MaxAlive + 64;
        EX = new float[EnemyCapacity]; EY = new float[EnemyCapacity];
        _prevX = new float[EnemyCapacity]; _prevY = new float[EnemyCapacity];
        EHp = new float[EnemyCapacity]; EMaxHp = new float[EnemyCapacity];
        EKx = new float[EnemyCapacity]; EKy = new float[EnemyCapacity];
        EDamage = new float[EnemyCapacity];
        EDef = new short[EnemyCapacity];
        EAlive = new bool[EnemyCapacity];
        EFlash = new byte[EnemyCapacity];
        EUid = new int[EnemyCapacity];
        Array.Fill(EX, -1e6f);
        Array.Fill(EY, -1e6f);
        _grid = new SpatialGrid(0, 0, arena, arena, 24);
        _enemyKind = [.. content.Enemies.Select(en => ContentLoader.TryParseDamage(en.DamageType, out var k) ? k : DamageKind.Physical)];
        InitPickups();
        PlaceStatuesAndChests();
    }

    /// <summary>Returns and clears the events produced since the last call.</summary>
    public List<GameEvent> Events => _events;

    public void ClearEvents()
    {
        _events.Clear();
        ChainPoints.Clear();
    }

    private void Emit(EventKind kind, float x, float y, float value = 0, int a = 0, int b = 0, float x2 = 0, float y2 = 0) =>
        _events.Add(new GameEvent { Kind = kind, X = x, Y = y, X2 = x2, Y2 = y2, Value = value, A = a, B = b, Seed = _rng.NextU() });

    public float XpForLevel(int level) => MathF.Round(Rules.XpBase * MathF.Pow(Rules.XpGrowth, level - 1));

    /// <summary>Final max HP: (maxHp from table and items) × hpMultiplier.</summary>
    public float MaxHealth => Player.Stats[Stat.MaxHp] * Player.Stats[Stat.HpMultiplier];
    public float MoveSpeedPx => Player.Stats[Stat.MoveSpeed] * Rules.Units.MoveSpeedScale;
    public float AttacksPerSecond => Player.Stats[Stat.AttackSpeed] * Rules.Units.AttacksPerUnit;

    /// <summary>Table stats that grow per level: max HP and basic attack damage ("base/+per level").</summary>
    private void ApplyLevelStats()
    {
        var cs = Player.Character.Stats;
        int l = Player.Level - 1;
        Player.Stats.SetBase(Stat.MaxHp, cs.MaxHp + cs.MaxHpPerLevel * l);
        Player.Stats.SetBase(Stat.Damage, cs.Damage + cs.DamagePerLevel * l);
    }

    /// <summary>Recomputes passives: value = base + perLevel × (level − 1), × run minutes when "per": "runMinute".</summary>
    private void UpdateDynamicStats()
    {
        var p = Player;
        p.Stats.ClearDynamic();
        int l = p.Level - 1;
        foreach (var pas in p.Character.Passives)
        {
            if (!ContentLoader.TryParseStat(pas.Stat, out var s)) continue;
            float add = pas.Add + (pas.PerLevel?.Add ?? 0) * l, pct = pas.Pct + (pas.PerLevel?.Pct ?? 0) * l;
            if (pas.Per == "runMinute") { add *= Minutes; pct *= Minutes; }
            p.Stats.ApplyDynamic(s, add, pct);
        }
    }

    public void Step(RunInput input)
    {
        if (Phase == RunPhase.Dead) return;
        Tick++;
        UpdateDynamicStats();
        StepPlayer(input);
        SpawnEnemies();
        StepEnemies();
        StepWeapons();
        StepProjectiles();
        StepPickups();
        StepStatues();
        StepChests();
        if (Player.Hp <= 0)
        {
            Player.Hp = 0;
            Phase = RunPhase.Dead;
        }
    }

    // ---------------------------------------------------------------- player
    private void StepPlayer(RunInput input)
    {
        var p = Player;
        p.Prev = p.Pos;
        var dir = new Vector2(Math.Sign(input.X), Math.Sign(input.Y));
        if (dir != Vector2.Zero)
        {
            dir = Vector2.Normalize(dir);
            p.Facing = dir;
            if (dir.X != 0) p.FacingLeft = dir.X < 0;
        }
        if (p.InvulnTicks > 0) p.InvulnTicks--;
        if (p.DashCooldownTicks > 0) p.DashCooldownTicks--;
        bool dashPressed = input.Dash && !p.PrevDash;
        p.PrevDash = input.Dash;
        var dash = Rules.Dash;
        if (dashPressed && p.DashCooldownTicks == 0 && p.DashTicks == 0)
        {
            p.DashTicks = Math.Max(1, (int)MathF.Round(dash.Seconds * TicksPerSecond));
            // The dash is an ability: both the dash and the general cooldown multipliers apply.
            p.DashCooldownTicks = (int)MathF.Round(dash.Cooldown * p.Stats[Stat.DashCooldown] * p.Stats[Stat.Cooldown] * TicksPerSecond);
            p.DashDir = p.Facing;
            if (dash.Invulnerable) p.InvulnTicks = Math.Max(p.InvulnTicks, p.DashTicks + 2);
            Emit(EventKind.Dash, p.Pos.X, p.Pos.Y, x2: p.DashDir.X, y2: p.DashDir.Y);
        }
        Vector2 move;
        if (p.DashTicks > 0)
        {
            p.DashTicks--;
            // Exact distance: split it over the whole number of dash ticks.
            move = p.DashDir * (dash.Distance / Math.Max(1, (int)MathF.Round(dash.Seconds * TicksPerSecond)));
        }
        else move = dir * MoveSpeedPx * Dt;
        float a = Rules.ArenaSize;
        p.Pos = Vector2.Clamp(p.Pos + move, new Vector2(12), new Vector2(a - 12));
        p.Walking = p.Pos != p.Prev;
        p.Hp = MathF.Min(MaxHealth, p.Hp + p.Stats[Stat.Regen] * Dt);
    }

    /// <summary>Fraction of damage blocked: armor for physical, magic resist for magical, nothing for pure (capped).</summary>
    public float Resist(DamageKind kind, float armor, float magicResist) => MathF.Min(Rules.Units.ResistCap, kind switch
    {
        DamageKind.Physical => armor,
        DamageKind.Magical => magicResist,
        _ => 0,
    });

    /// <summary>Damages the player after defences; returns the damage actually dealt (0 while invulnerable).</summary>
    private float HurtPlayer(float amount, DamageKind kind, float fromX, float fromY)
    {
        var p = Player;
        if (p.InvulnTicks > 0 || Phase != RunPhase.Playing) return 0;
        float dmg = amount * (1 - Resist(kind, p.Stats[Stat.Armor], p.Stats[Stat.MagicResist]));
        p.Hp -= dmg;
        p.InvulnTicks = (int)MathF.Round(Rules.InvulnerableSeconds * TicksPerSecond);
        p.HurtTick = Tick;
        Emit(EventKind.PlayerHurt, p.Pos.X, p.Pos.Y, dmg, x2: fromX, y2: fromY);
        return dmg;
    }

    // ---------------------------------------------------------------- enemies
    private WaveDef CurrentWave()
    {
        float m = Minutes;
        foreach (var w in Content.Waves.Waves) if (m >= w.From && m < w.To) return w;
        return Content.Waves.Waves[^1];
    }

    public float HpScale => 1 + Content.Waves.HpPerMinute * Minutes;
    public float DamageScale => 1 + Content.Waves.DamagePerMinute * Minutes;

    private void SpawnEnemies()
    {
        var wave = CurrentWave();
        _spawnAcc += wave.PerSecond * Dt;
        while (_spawnAcc >= 1)
        {
            _spawnAcc -= 1;
            if (AliveEnemies >= Content.Waves.MaxAlive) { _spawnAcc = 0; break; }
            SpawnEnemy(PickFromMix(wave));
        }
        var elites = Content.Waves.Elites;
        while (_nextElite < elites.Count && Minutes >= elites[_nextElite].At)
        {
            var e = elites[_nextElite++];
            int def = Content.EnemyIndex(e.Enemy);
            for (int i = 0; i < e.Count; i++)
            {
                int slot = SpawnEnemy(def);
                if (slot >= 0) Emit(EventKind.Elite, EX[slot], EY[slot], a: slot);
            }
        }
    }

    private int PickFromMix(WaveDef wave)
    {
        float total = 0;
        foreach (var w in wave.Mix.Values) total += w;
        float r = _rng.Next() * total;
        foreach (var (id, w) in wave.Mix)
        {
            r -= w;
            if (r <= 0) return Content.EnemyIndex(id);
        }
        return Content.EnemyIndex(wave.Mix.Keys.Last());
    }

    /// <summary>Spawns just outside the view around the player (arena-clamped). Returns the slot or -1 when full.</summary>
    public int SpawnEnemy(int def, Vector2? at = null)
    {
        int slot;
        if (_freeEnemies.Count > 0) slot = _freeEnemies.Pop();
        else if (_enemyHigh < EnemyCapacity) slot = _enemyHigh++;
        else return -1;
        var d = Content.Enemies[def];
        Vector2 pos;
        if (at is { } a) pos = a;
        else
        {
            float ang = _rng.Range(0, MathF.Tau), dist = _rng.Range(350, 410);
            pos = Player.Pos + new Vector2(MathF.Cos(ang) * dist, MathF.Sin(ang) * dist * 0.7f);
        }
        float arena = Rules.ArenaSize;
        pos = Vector2.Clamp(pos, new Vector2(8), new Vector2(arena - 8));
        EX[slot] = _prevX[slot] = pos.X;
        EY[slot] = _prevY[slot] = pos.Y;
        EMaxHp[slot] = EHp[slot] = d.Hp * HpScale;
        EDamage[slot] = d.Damage * DamageScale;
        EDef[slot] = (short)def;
        EAlive[slot] = true;
        EFlash[slot] = 0;
        EKx[slot] = EKy[slot] = 0;
        EUid[slot] = _nextUid++;
        AliveEnemies++;
        return slot;
    }

    private struct Separation(RunWorld w, int self, float radius) : IGridVisitor
    {
        public float Px, Py;
        public void Visit(int j)
        {
            if (j == self || !w.EAlive[j]) return;
            float dx = w.EX[self] - w.EX[j], dy = w.EY[self] - w.EY[j], d2 = dx * dx + dy * dy;
            float min = radius + w.Content.Enemies[w.EDef[j]].Radius;
            if (d2 >= min * min) return;
            if (d2 < 0.0001f) { dx = (self - j) * 0.01f; dy = 0.01f; d2 = dx * dx + dy * dy; }
            float d = MathF.Sqrt(d2), push = (min - d) / d;
            Px += dx * push;
            Py += dy * push;
        }
    }

    private void StepEnemies()
    {
        _grid.Build(EX.AsSpan(0, _enemyHigh), EY.AsSpan(0, _enemyHigh));
        var p = Player.Pos;
        float arena = Rules.ArenaSize;
        for (int i = 0; i < _enemyHigh; i++)
        {
            if (!EAlive[i]) continue;
            _prevX[i] = EX[i];
            _prevY[i] = EY[i];
            if (EFlash[i] > 0) EFlash[i]--;
            var d = Content.Enemies[EDef[i]];
            float dx = p.X - EX[i], dy = p.Y - EY[i], dist = MathF.Sqrt(dx * dx + dy * dy);
            float vx = 0, vy = 0;
            float speed = d.Speed * Rules.Units.MoveSpeedScale;
            if (dist > 0.5f) { vx = dx / dist * speed; vy = dy / dist * speed; }
            var sep = new Separation(this, i, d.Radius);
            _grid.Query(EX[i], EY[i], d.Radius * 2 + 2, ref sep);
            EX[i] = Math.Clamp(EX[i] + (vx + EKx[i]) * Dt + sep.Px * 0.5f, 4, arena - 4);
            EY[i] = Math.Clamp(EY[i] + (vy + EKy[i]) * Dt + sep.Py * 0.5f, 4, arena - 4);
            EKx[i] *= 0.82f;
            EKy[i] *= 0.82f;
            if (dist < d.Radius + PlayerRadius)
            {
                float dealt = HurtPlayer(EDamage[i], _enemyKind[EDef[i]], EX[i], EY[i]);
                // Enemy lifesteal (Вампиризм) heals the attacker by its share of the damage dealt.
                if (dealt > 0 && d.Lifesteal > 0) EHp[i] = MathF.Min(EMaxHp[i], EHp[i] + dealt * d.Lifesteal);
            }
            // Enemies left far behind are recycled closer to the action (keeps pressure constant).
            if (dist > 700) { Kill(i, credit: -1); }
        }
    }

    public Vector2 EnemyPos(int i, float alpha) => new(_prevX[i] + (EX[i] - _prevX[i]) * alpha, _prevY[i] + (EY[i] - _prevY[i]) * alpha);

    /// <summary>Outgoing multiplier for a hit: might × damage-type bonus × item bonus (for item sources).</summary>
    public float OutgoingMultiplier(DamageKind kind, bool fromItem)
    {
        var s = Player.Stats;
        float m = s[Stat.Might] * kind switch
        {
            DamageKind.Physical => s[Stat.PhysicalDamage],
            DamageKind.Magical => s[Stat.MagicalDamage],
            _ => 1,
        };
        return fromItem ? m * s[Stat.ItemDamage] : m;
    }

    /// <summary>
    /// Damage pipeline: raw × outgoing multipliers × (1 − target resistance of that type), then knockback away from (fromX, fromY).
    /// Kills credit gold to <paramref name="source"/>'s player.
    /// </summary>
    public void DamageEnemy(int i, float raw, DamageKind kind, DamageSourceRef source, float fromX, float fromY, float knock = 40)
    {
        if (!EAlive[i] || raw <= 0) return;
        var def = Content.Enemies[EDef[i]];
        float amount = raw * OutgoingMultiplier(kind, source.FromItem) * (1 - Resist(kind, def.Armor, def.MagicResist));
        int playerId = source.PlayerId;
        if (amount <= 0) return;
        EHp[i] -= amount;
        EFlash[i] = 3;
        float dx = EX[i] - fromX, dy = EY[i] - fromY, d = MathF.Sqrt(dx * dx + dy * dy);
        if (d > 0.01f && !def.Boss && def.Hp < 100)
        {
            EKx[i] += dx / d * knock;
            EKy[i] += dy / d * knock;
        }
        Emit(EventKind.Hit, EX[i], EY[i], amount, a: (int)kind);
        if (EHp[i] <= 0) Kill(i, playerId);
    }

    private void Kill(int i, int credit)
    {
        if (!EAlive[i]) return;
        EAlive[i] = false;
        AliveEnemies--;
        var d = Content.Enemies[EDef[i]];
        if (credit >= 0)
        {
            Player.Kills++;
            DropPickup(PickupKind.Xp, EX[i], EY[i], d.Xp, -1);
            if (d.Gold > 0 && _rng.Next() < d.GoldChance)
                DropPickup(PickupKind.Gold, EX[i] + _rng.Range(-3, 3), EY[i] + _rng.Range(-3, 3), d.Gold, GoldDrop.FromKill(d.Gold, credit).OwnerId);
            Emit(EventKind.Kill, EX[i], EY[i], a: EDef[i]);
        }
        EX[i] = EY[i] = -1e6f;
        _freeEnemies.Push(i);
    }

    /// <summary>Deterministic digest of the run state (for tests and future network desync checks).</summary>
    public ulong StateHash()
    {
        ulong h = 1469598103934665603;
        void Mix(float v) { h = (h ^ BitConverter.SingleToUInt32Bits(v)) * 1099511628211; }
        Mix(Tick); Mix(Player.Pos.X); Mix(Player.Pos.Y); Mix(Player.Hp); Mix(Player.Xp); Mix(Player.Level); Mix(BonusSeconds);
        for (int i = 0; i < _enemyHigh; i++) if (EAlive[i]) { Mix(EX[i]); Mix(EY[i]); Mix(EHp[i]); }
        Mix(AliveEnemies); Mix(Player.Kills); Mix(Player.Wallet.Gold);
        return h;
    }
}
