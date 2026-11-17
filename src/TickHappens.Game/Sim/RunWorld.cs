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
    /// <summary>Run clock in seconds: real elapsed time plus time effects. Drives difficulty.</summary>
    public float RunSeconds => (Tick + _timekeeperTicks) * Dt + BonusSeconds;
    public float Minutes => RunSeconds / 60f;
    public bool TimeAccelerated => _timekeepersAlive > 0;
    public uint Seed { get; private set; }
    private readonly PlayerState[] _players;
    private readonly int _localPlayerId;
    public PlayerState Player => _players[_localPlayerId];
    public IReadOnlyList<PlayerState> Players => _players;

    private SimRng _rng;
    private readonly List<GameEvent> _events = [];
    public readonly List<Vector2> ChainPoints = [];

    // ---- enemies (struct of arrays, free-list slots) ----
    public readonly int EnemyCapacity;
    public readonly float[] EX, EY, EHp, EMaxHp, EKx, EKy, EDamage;
    public readonly short[] EDef;
    public readonly bool[] EAlive, EElite;
    public readonly byte[] EFlash;
    public readonly int[] EUid;
    private readonly float[] _prevX, _prevY;
    private readonly Stack<int> _freeEnemies = new();
    private int _enemyHigh, _nextUid = 1;
    public int AliveEnemies { get; private set; }
    public int EnemyHighWater => _enemyHigh;
    internal static int EnemySlots(ContentSet content) => content.Waves.MaxAlive + 64;
    private readonly SpatialGrid _grid;
    private readonly DamageKind[] _enemyKind;
    private readonly float[] _enemySlow;
    private readonly int[] _enemySlowUntil, _enemyStunUntil;
    private readonly int[,] _acceleratorContact, _boarContact;
    private readonly int[] _acceleratorItem = [-1, -1, -1, -1];
    private float _spawnAcc;
    private int _nextElite, _bossesSpawned;
    private readonly int[] _bossRotation;
    private SimRng _bossRng;

    public RunWorld(ContentSet content, uint seed = 1, string? character = null, int playerCount = 1, int localPlayerId = 0)
    {
        Content = content;
        Seed = seed;
        _bossRotation = Enumerable.Range(0, content.Enemies.Count).Where(i => content.Enemies[i].Boss).ToArray();
        _bossRng = new SimRng(seed);
        _rng = new SimRng(seed);
        var ch = character != null ? content.Characters.Find(c => c.Id == character) ?? throw new ArgumentException($"unknown character '{character}'")
                                   : content.Characters[0];
        int arena = Rules.ArenaSize;
        if (playerCount is < 1 or > 4 || (uint)localPlayerId >= (uint)playerCount)
            throw new ArgumentOutOfRangeException(nameof(playerCount));
        _localPlayerId = localPlayerId;
        _players = new PlayerState[playerCount];
        for (int i = 0; i < playerCount; i++) _players[i] = CreatePlayer(ch, i, i == localPlayerId);

        EnemyCapacity = EnemySlots(content);
        _enemyAbilities = new EnemyAbility[EnemyCapacity];
        _enemyHidden = new bool[EnemyCapacity];
        _phantomIllusion = content.EnemyIndex("phantom_illusion");
        _goldVein = content.EnemyIndex("gold_vein");
        _juniorCultist = content.EnemyIndex("junior_cultist");
        _seniorCultist = content.EnemyIndex("senior_cultist");
        _cultistShaman = content.EnemyIndex("cultist_shaman");
        _enemyAttackTicks = new byte[EnemyCapacity];
        _enemyAttackReady = new int[EnemyCapacity];
        _enemyBlasts = new Queue<EnemyBlast>(EnemyCapacity);
        _acceleratorContact = new int[playerCount, EnemyCapacity];
        _boarContact = new int[playerCount, EnemyCapacity];
        _enemySlow = new float[EnemyCapacity];
        _enemySlowUntil = new int[EnemyCapacity];
        _enemyStunUntil = new int[EnemyCapacity];
        _enemyPush = new Vector2[EnemyCapacity];
        EX = new float[EnemyCapacity]; EY = new float[EnemyCapacity];
        _prevX = new float[EnemyCapacity]; _prevY = new float[EnemyCapacity];
        EHp = new float[EnemyCapacity]; EMaxHp = new float[EnemyCapacity];
        EKx = new float[EnemyCapacity]; EKy = new float[EnemyCapacity];
        EDamage = new float[EnemyCapacity];
        EDef = new short[EnemyCapacity];
        EAlive = new bool[EnemyCapacity];
        EElite = new bool[EnemyCapacity];
        _poisonDamage = new float[playerCount, EnemyCapacity];
        _poisonTick = new int[playerCount, EnemyCapacity];
        _poisonSource = new int[playerCount, EnemyCapacity];
        _armorShred = new float[playerCount, EnemyCapacity];
        _armorShredUntil = new int[playerCount, EnemyCapacity];
        _charmOwner = new int[EnemyCapacity];
        _charmUntil = new int[EnemyCapacity];
        _charmAttackReady = new int[EnemyCapacity];
        for (int item = 0; item < content.Items.Count; item++)
            if (content.Items[item].BossDrop is { } boss) _bossRewards.TryAdd(boss, item);
        EFlash = new byte[EnemyCapacity];
        EUid = new int[EnemyCapacity];
        Array.Fill(EX, -1e6f);
        Array.Fill(EY, -1e6f);
        _grid = new SpatialGrid(0, 0, arena, arena, 24);
        _enemyKind = [.. content.Enemies.Select(en => ContentLoader.TryParseDamage(en.DamageType, out var k) ? k : DamageKind.Physical)];
        InitPickups();
        PlaceStatuesAndChests();
    }

    private PlayerState CreatePlayer(CharacterDef character, int id, bool active)
    {
        float center = Rules.ArenaSize / 2f;
        var p = new PlayerState
        {
            Id = id,
            Active = active,
            Character = character,
            Stats = new StatBlock(),
            Attack = character.Attack ?? Rules.BasicAttack,
            Wallet = new ChestWallet(Rules.Chests.StartPrice, Rules.Chests.PriceGrowth),
            ItemStacks = new int[Content.Items.Count],
            ItemTimers = new float[Content.Items.Count],
            ItemPulseTimers = new float[Content.Items.Count],
            Pos = new Vector2(center + (id % 2) * 16, center + (id / 2) * 16),
            XpNext = XpForLevel(1),
            NextHeroPassiveTick = Tick + (int)((character.Id == "necromancer" ? 30 : character.Id == "succubus" ? 12 : IllusionCooldown) * TicksPerSecond),
        };
        p.Prev = p.Pos;
        var cs = character.Stats;
        p.Stats.SetBase(Stat.MoveSpeed, cs.MoveSpeed);
        p.Stats.SetBase(Stat.AttackSpeed, cs.AttackSpeed);
        p.Stats.SetBase(Stat.Armor, cs.Armor);
        p.Stats.SetBase(Stat.MagicResist, cs.MagicResist);
        p.Stats.SetBase(Stat.Regen, cs.Regen);
        p.Stats.SetBase(Stat.Magnet, cs.Magnet);
        ApplyLevelStats(p);
        UpdateDynamicStats(p);
        p.Hp = MaxHealthFor(p);
        if (character.Id == "masochist") UpdateDynamicStats(p);
        return p;
    }

    public void SetPlayerActive(int id, bool active)
    {
        if (_players[id].Active == active) return;
        if (active)
        {
            var next = CreatePlayer(_players[id].Character, id, true);
            int anchor = NearestLivingPlayer(_players[0].Pos);
            if (anchor >= 0)
            {
                next.Pos = Vector2.Clamp(_players[anchor].Pos + new Vector2((id % 2) * 16, (id / 2) * 16),
                    new Vector2(12), new Vector2(Rules.ArenaSize - 12));
                next.Prev = next.Pos;
            }
            for (int i = 0; i < PickupCapacity; i++)
                if (GAlive[i] && GKind[i] == PickupKind.Xp && GOwner[i] == id) GAlive[i] = false;
            _players[id] = next;
            return;
        }
        _players[id].Active = false;
        for (int i = 0; i < TrailCapacity; i++) _heroTrail[id, i] = default;
        for (int i = 0; i < PickupCapacity; i++)
            if (GAlive[i] && GKind[i] != PickupKind.Xp && GOwner[i] == id) GAlive[i] = false;
        for (int i = 0; i < ProjectileCapacity; i++)
            if (PAlive[i] && _pOwner[i] == id) PAlive[i] = false;
    }

    internal void RestoreCoopPlayer(int id)
    {
        var player = _players[id];
        player.Active = true;
        player.Prev = player.Pos;
        player.Walking = false;
        player.PrevDash = false;
    }

    internal void SetStartingHero(int slot, int hero)
    {
        if (Tick != 0) throw new InvalidOperationException("Hero selection is closed after the run starts.");
        var player = _players[slot];
        _players[slot] = CreatePlayer(Content.Characters[hero], slot, player.Active);
    }

    private int NearestLivingPlayer(Vector2 point)
    {
        int best = -1;
        float distance = float.PositiveInfinity;
        for (int i = 0; i < _players.Length; i++)
        {
            var p = _players[i];
            if (!p.Active || p.Hp <= 0) continue;
            float d = Vector2.DistanceSquared(point, p.Pos);
            if (d >= distance) continue;
            (best, distance) = (i, d);
        }
        return best;
    }

    /// <summary>Returns and clears the events produced since the last call.</summary>
    public List<GameEvent> Events => _events;

    public void ClearEvents()
    {
        _events.Clear();
        ChainPoints.Clear();
    }

    private void Emit(EventKind kind, float x, float y, float value = 0, int a = 0, int b = 0, float x2 = 0, float y2 = 0, uint? seed = null)
    {
        uint cosmeticSeed = _rng.NextU();
        _events.Add(new GameEvent { Kind = kind, X = x, Y = y, X2 = x2, Y2 = y2, Value = value, A = a, B = b, Seed = seed ?? cosmeticSeed });
    }

    public float XpForLevel(int level) => MathF.Round(Rules.XpBase * MathF.Pow(Rules.XpGrowth, level - 1));

    /// <summary>Final max HP: (maxHp from table and items) × hpMultiplier.</summary>
    public float MaxHealth => MaxHealthFor(Player);
    internal static float MaxHealthFor(PlayerState p) => p.Stats[Stat.MaxHp] * p.Stats[Stat.HpMultiplier];
    public float MoveSpeedPx => MovementSpeed(Player) * Rules.Units.MoveSpeedScale;
    public float AttacksPerSecond => Player.Stats[Stat.AttackSpeed] * Rules.Units.AttacksPerUnit;

    /// <summary>Table stats that grow per level: max HP and basic attack damage ("base/+per level").</summary>
    private static void ApplyLevelStats(PlayerState p)
    {
        var cs = p.Character.Stats;
        int l = p.Level - 1;
        p.Stats.SetBase(Stat.MaxHp, cs.MaxHp + cs.MaxHpPerLevel * l);
        p.Stats.SetBase(Stat.Damage, cs.Damage + cs.DamagePerLevel * l);
    }

    /// <summary>Recomputes passives: value = base + perLevel × (level − 1), × run minutes when "per": "runMinute".</summary>
    private void UpdateDynamicStats(PlayerState p)
    {
        p.Stats.ClearDynamic();
        int l = p.Level - 1;
        foreach (var pas in p.Character.Passives)
        {
            if (!ContentLoader.TryParseStat(pas.Stat, out var s)) continue;
            float add = pas.Add + (pas.PerLevel?.Add ?? 0) * l, pct = pas.Pct + (pas.PerLevel?.Pct ?? 0) * l;
            if (pas.Per == "runMinute") { add *= Minutes; pct *= Minutes; }
            p.Stats.ApplyDynamic(s, add, pct);
        }
        ApplyHeroStats(p);
    }

    public void Step(RunInput input)
    {
        Span<RunInput> inputs = stackalloc RunInput[4];
        inputs.Clear();
        inputs[_localPlayerId] = input;
        StepCoop(inputs);
    }

    public void StepCoop(ReadOnlySpan<RunInput> inputs)
    {
        if (_replicaTick >= 0) throw new InvalidOperationException("Only the host can advance the simulation.");
        if (Phase == RunPhase.Dead) return;
        if (inputs.Length < _players.Length) throw new ArgumentException("Each player slot needs an input.", nameof(inputs));
        Tick++;
        if (_timekeepersAlive > 0) _timekeeperTicks++;
        StepStolenItems();
        foreach (var player in _players)
        {
            if (player.GhostUntilTick == 0 || player.GhostUntilTick > Tick) continue;
            player.GhostUntilTick = 0;
            player.Hp = 0;
        }
        UpdateAllPlayerStats();
        BuildEnemyGrid();
        foreach (var p in _players)
        {
            if (!p.Active || p.Hp <= 0) continue;
            p.TimekeeperSlowed = InTimekeeperAura(p);
            StepPlayer(p, inputs[p.Id]);
            StepItemEffects(p);
        }
        if (NearestLivingPlayer(Player.Pos) < 0)
        {
            Phase = RunPhase.Dead;
            return;
        }
        SpawnEnemies();
        StepEnemies();
        SeparateEnemies();
        StepBloodRain();
        // Combat queries use enemy positions after movement.
        BuildEnemyGrid();
        foreach (var p in _players)
        {
            if (!p.Active) continue;
            if (p.Hp > 0 || p.Character.Id == "miner") StepHeroPassive(p);
            if (p.Hp > 0) StepWeapons(p);
        }
        foreach (var p in _players)
            if (p.Active && p.Hp > 0) StepCompanionItems(p);
        StepCompanions();
        StepProjectiles();
        StepEnemyEffects();
        StepPickups();
        StepStatues();
        StepChests(inputs);
        StepRevival();
        if (NearestLivingPlayer(Player.Pos) < 0) Phase = RunPhase.Dead;
    }

    // ---------------------------------------------------------------- player
    /// <summary>
    /// Moves the local hero of a replica ahead of the host. With the input's <paramref name="sequence"/> the step runs
    /// at the host tick that will apply it (the snapshot tick plus the inputs after the acknowledged one), so slows
    /// and roots end on the same tick on both sides.
    /// </summary>
    internal void PredictLocalMovement(RunInput input, int sequence = 0)
    {
        if (_replicaTick < 0 || Phase != RunPhase.Playing || !Player.Active || Player.Hp <= 0) return;
        int snapshotTick = Tick;
        if (sequence > Player.LastAppliedInput) Tick = snapshotTick + (sequence - Player.LastAppliedInput);
        try { StepPlayer(Player, input, predict: true); }
        finally { Tick = snapshotTick; }
    }

    private static float MovementSpeed(PlayerState player) => player.Character.Id == "diver"
        ? player.Character.Stats.MoveSpeed
        : player.Stats[Stat.MoveSpeed];

    private void StepPlayer(PlayerState p, RunInput input, bool predict = false)
    {
        p.Prev = p.Pos;
        if (p.DashSpeedTicks > 0) p.DashSpeedTicks--;
        if (p.AcceleratorTicks > 0) p.AcceleratorTicks--;
        var dir = new Vector2(Math.Sign(input.X), Math.Sign(input.Y));
        if (dir != Vector2.Zero)
        {
            dir = Vector2.Normalize(dir);
            p.Facing = dir;
            if (dir.X != 0) p.FacingLeft = dir.X < 0;
        }
        if (!predict && p.InvulnTicks > 0) p.InvulnTicks--;
        int dashCooldown = Math.Max(1, (int)MathF.Round(Rules.Dash.Cooldown * p.Stats[Stat.DashCooldown] *
            p.Stats[Stat.Cooldown] * TicksPerSecond));
        if (p.DashCooldownTicks > 0 && --p.DashCooldownTicks == 0 && p.DashChargesUsed > 0)
        {
            p.DashChargesUsed--;
            if (p.DashChargesUsed > 0) p.DashCooldownTicks = dashCooldown;
        }
        bool dashPressed = input.Dash && !p.PrevDash && p.EnemyRootUntil <= Tick;
        p.PrevDash = input.Dash;
        var dash = Rules.Dash;
        if (dashPressed && p.DashChargesUsed < DashChargeCapacity(p) && p.DashTicks == 0)
        {
            p.DashTicksTotal = p.DashTicks = Math.Max(1, (int)MathF.Round(dash.Seconds * p.Stats[Stat.DashLength] * TicksPerSecond));
            p.DashChargesUsed++;
            if (p.DashCooldownTicks == 0) p.DashCooldownTicks = dashCooldown;
            p.DashDir = p.Facing;
            if (p.Stats[Stat.DashSpeedBonus] > 0) p.DashSpeedTicks = 3 * TicksPerSecond;
            if (!predict)
            {
                if (dash.Invulnerable) p.InvulnTicks = Math.Max(p.InvulnTicks, p.DashTicks + 2);
                Emit(EventKind.Dash, p.Pos.X, p.Pos.Y, x2: p.DashDir.X, y2: p.DashDir.Y);
            }
        }
        Vector2 move;
        if (p.Character.Id == "pig")
            p.BoarWalkTicks = dir != Vector2.Zero && p.DashTicks == 0 && p.EnemyRootUntil <= Tick
                ? Math.Min(BoarChargeTicks, p.BoarWalkTicks + 1) : 0;
        if (p.DashTicks > 0)
        {
            p.DashTicks--;
            // Exact distance: split it over the whole number of dash ticks.
            move = p.DashDir * (dash.Distance * p.Stats[Stat.DashLength] / Math.Max(1, p.DashTicksTotal));
        }
        else
        {
            float speedBonus = p.DashSpeedTicks > 0 ? p.Stats[Stat.DashSpeedBonus] : 0;
            if (p.AcceleratorTicks > 0) speedBonus += p.AcceleratorSpeedBonus;
            float speed = MovementSpeed(p) * (p.Character.Id == "diver" ? 1 : 1 + speedBonus);
            if (p.BoarWalkTicks == BoarChargeTicks) speed *= 1.25f;
            if (p.TimekeeperSlowed) speed *= 0.5f;
            if (p.EnemySlowUntil > Tick) speed *= 0.75f;
            if (p.EnemyRootUntil > Tick) speed = 0;
            move = dir * speed * Rules.Units.MoveSpeedScale * Dt;
        }
        float a = Rules.ArenaSize;
        var min = new Vector2(12);
        var max = new Vector2(a - 12);
        p.Pos = Vector2.Clamp(MoveWithTeammateCollision(p, Vector2.Clamp(p.Pos + move, min, max)), min, max);
        p.Walking = p.Pos != p.Prev;
        if (!p.Walking) p.BoarWalkTicks = 0;
        if (!predict)
        {
            if (p.Character.Id == "diver") p.HeroDistance += Vector2.Distance(p.Prev, p.Pos);
            float regen = p.Stats[Stat.Regen];
            if (!p.Walking) regen += p.Stats[Stat.StationaryRegen] * MaxHealthFor(p);
            p.Hp = Math.Min(p.Hp, MaxHealthFor(p));
            RestorePlayer(p, regen * Dt);
            p.LifestealSpent = Math.Max(0, p.LifestealSpent - p.Stats[Stat.LifestealHealCap] * MaxHealthFor(p) * Dt);
            if (Tick >= p.ShieldRechargeTick) p.Shield = ShieldCapacity(p);
        }
    }

    /// <summary>Fraction of damage blocked: armor for physical, magic resist for magical, nothing for pure (capped).</summary>
    public float Resist(DamageKind kind, float armor, float magicResist) => MathF.Min(Rules.Units.ResistCap, kind switch
    {
        DamageKind.Physical => armor,
        DamageKind.Magical => magicResist,
        _ => 0,
    });

    internal static int DashChargeCapacity(PlayerState p) => 1 + Math.Min(8, (int)p.Stats[Stat.ExtraDashCharges]);

    private static float ShieldCapacity(PlayerState p) => MaxHealthFor(p) * p.Stats[Stat.RechargingShield];

    /// <summary>Damages the player after defences; returns the damage actually dealt (0 while invulnerable).</summary>
    private float HurtPlayer(PlayerState p, float amount, DamageKind kind, float fromX, float fromY, int enemy, bool continuous = false)
    {
        if (p.InvulnTicks > 0 || p.Hp <= 0 || p.GhostUntilTick > Tick) return 0;
        float dmg = amount * (1 - Resist(kind, p.Stats[Stat.Armor], p.Stats[Stat.MagicResist])) * p.Stats[Stat.DamageTaken];
        if (kind == DamageKind.Physical && p.Stats[Stat.PhysicalBlock] > 0 && Tick >= p.PhysicalBlockTick)
        {
            p.PhysicalBlockTick = Tick + (int)MathF.Round(12 * p.Stats[Stat.Cooldown] * TicksPerSecond);
            return 0;
        }
        if (!continuous && kind == DamageKind.Physical && p.Character.Id == "swordsman" && enemy >= 0 &&
            EAlive[enemy] && Content.Enemies[EDef[enemy]].ShotRange == 0)
        {
            float contact = PlayerRadius + Content.Enemies[EDef[enemy]].Radius + ContactTolerance;
            if (Vector2.DistanceSquared(p.Pos, new(EX[enemy], EY[enemy])) <= contact * contact && RollChance(0.185f + 0.0065f * (p.Level - 1)))
                return 0;
        }
        if (TryBlockOrRedirect(p, enemy, kind, dmg)) return 0;
        float bonusAbsorbed = Math.Min(dmg, p.BonusShield);
        p.BonusShield -= bonusAbsorbed;
        dmg -= bonusAbsorbed;
        float absorbed = Math.Min(dmg, p.Shield);
        p.Shield -= absorbed;
        p.ShieldRechargeTick = Tick + 4 * TicksPerSecond;
        dmg = Math.Min(p.Hp, dmg - absorbed);
        p.Hp -= dmg;
        if (!continuous) p.InvulnTicks = (int)MathF.Round(Rules.InvulnerableSeconds * TicksPerSecond);
        if (p.Hp <= 0) TryRevive(p);
        OnHeroDamaged(p, dmg);
        p.HurtTick = Tick;
        Emit(EventKind.PlayerHurt, p.Pos.X, p.Pos.Y, dmg, a: p.Id, x2: fromX, y2: fromY);
        return dmg;
    }

    // ---------------------------------------------------------------- enemies
    private WaveDef CurrentWave()
    {
        float m = Minutes;
        foreach (var w in Content.Waves.Waves) if (m >= w.From && m < w.To) return w;
        return Content.Waves.Waves[^1];
    }

    public float HpScale
    {
        get
        {
            float minutes = Minutes;
            return 1 + minutes * (Content.Waves.HpPerMinute + Content.Waves.HpPerMinuteSquared * minutes);
        }
    }
    public float DamageScale => 1 + Content.Waves.DamagePerMinute * Minutes;

    private int FirstBossWeight(int enemyDef) => Content.Enemies[enemyDef].Id is "timekeeper" or "ancient_cultist" ? 6 : 1;

    private void ShuffleBossRotation(bool firstCycle)
    {
        int start = 0;
        if (firstCycle)
        {
            int totalWeight = 0;
            foreach (int def in _bossRotation) totalWeight += FirstBossWeight(def);
            int roll = _bossRng.Int(totalWeight);
            int first = 0;
            for (; first < _bossRotation.Length - 1; first++)
            {
                roll -= FirstBossWeight(_bossRotation[first]);
                if (roll < 0) break;
            }
            (_bossRotation[0], _bossRotation[first]) = (_bossRotation[first], _bossRotation[0]);
            start = 1;
        }

        for (int i = _bossRotation.Length - 1; i > start; i--)
        {
            int j = start + _bossRng.Int(i - start + 1);
            (_bossRotation[i], _bossRotation[j]) = (_bossRotation[j], _bossRotation[i]);
        }
    }

    private void SpawnEnemies()
    {
        int interval = Content.Waves.BossIntervalMinutes;
        while (interval > 0 && _bossRotation.Length > 0 && Minutes >= (_bossesSpawned + 1L) * interval)
        {
            int count = _bossesSpawned < _bossRotation.Length ? 1 : 2;
            if (EnemyCapacity - AliveEnemies < count) break;
            int rotationIndex = _bossesSpawned % _bossRotation.Length;
            if (rotationIndex == 0) ShuffleBossRotation(firstCycle: _bossesSpawned == 0);
            int def = _bossRotation[rotationIndex];
            for (int i = 0; i < count; i++)
            {
                int boss = SpawnEnemy(def);
                if (i == 0) Emit(EventKind.Elite, EX[boss], EY[boss], a: boss, b: def + 1);
            }
            _bossesSpawned++;
        }
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
                if (slot >= 0)
                {
                    EElite[slot] = true;
                    Emit(EventKind.Elite, EX[slot], EY[slot], a: slot);
                }
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
            int player = NearestLivingPlayer(Player.Pos);
            if (_players.Length > 1)
            {
                // Rotate spawns across living players so split groups both see enemies.
                for (int offset = 0; offset < _players.Length; offset++)
                {
                    int id = (_nextUid + offset) % _players.Length;
                    if (_players[id].Active && _players[id].Hp > 0) { player = id; break; }
                }
            }
            pos = (player >= 0 ? _players[player].Pos : Player.Pos) +
                new Vector2(MathF.Cos(ang) * dist, MathF.Sin(ang) * dist * 0.7f);
        }
        float arena = Rules.ArenaSize;
        pos = Vector2.Clamp(pos, new Vector2(8), new Vector2(arena - 8));
        EX[slot] = _prevX[slot] = pos.X;
        EY[slot] = _prevY[slot] = pos.Y;
        EMaxHp[slot] = EHp[slot] = d.Hp * (d.Boss ? 1 : HpScale);
        EDamage[slot] = d.Damage * (d.Boss ? 1 : DamageScale);
        EDef[slot] = (short)def;
        EAlive[slot] = true;
        EElite[slot] = d.Elite;
        for (int player = 0; player < _players.Length; player++)
        {
            _poisonTick[player, slot] = 0;
            _poisonDamage[player, slot] = 0;
            _armorShred[player, slot] = 0;
            _armorShredUntil[player, slot] = 0;
        }
        EFlash[slot] = 0;
        _enemyAttackTicks[slot] = 0;
        _enemyAttackReady[slot] = 0;
        InitEnemyAbility(slot, d);
        _enemySlowUntil[slot] = _enemyStunUntil[slot] = 0;
        _charmUntil[slot] = _charmAttackReady[slot] = 0;
        EKx[slot] = EKy[slot] = 0;
        EUid[slot] = _nextUid++;
        AliveEnemies++;
        return slot;
    }

    private void BuildEnemyGrid() => _grid.Build(EX.AsSpan(0, _enemyHigh), EY.AsSpan(0, _enemyHigh));

    private void StepEnemies()
    {
        float arena = Rules.ArenaSize;
        float moveSpeedScale = Rules.Units.MoveSpeedScale * (1 + 0.01f * MathF.Floor(Minutes / 5f));
        bool hasCompanions = false;
        foreach (ref readonly var companion in Companions)
            if (companion.Active && companion.Health > 0) { hasCompanions = true; break; }
        float enemyLifestealCap = EnemyLifestealCap();
        for (int i = 0; i < _enemyHigh; i++)
        {
            if (!EAlive[i]) continue;
            StepEnemyPoison(i);
            if (!EAlive[i]) continue;
            _prevX[i] = EX[i];
            _prevY[i] = EY[i];
            if (EFlash[i] > 0) EFlash[i]--;
            if (StepCharmedEnemy(i)) continue;
            var d = Content.Enemies[EDef[i]];
            if (d.Behavior == "illusion" && _enemyAbilities[i].Expires > 0 && Tick >= _enemyAbilities[i].Expires)
            {
                Kill(i, credit: -1, deathEffects: false);
                continue;
            }
            int target = NearestLivingPlayer(new Vector2(EX[i], EY[i]));
            if (target < 0) break;
            var player = _players[target];
            var p = EnemyTarget(new Vector2(EX[i], EY[i]), player, out var decoy);
            float dx = p.X - EX[i], dy = p.Y - EY[i], dist = MathF.Sqrt(dx * dx + dy * dy);
            float vx = 0, vy = 0;
            bool stunned = _enemyStunUntil[i] > Tick;
            var pos = new Vector2(EX[i], EY[i]);
            var goal = EnemySteering(i, d, player, pos, p, decoy >= 0);
            float speed = stunned ? 0 : (d.Speed + 5 * _enemyAbilities[i].SpeedBuffs) * moveSpeedScale;
            if (!stunned) StepEnemyAbility(i, d, player, p, ref goal, ref speed);
            if (!EAlive[i]) continue;
            dist = Vector2.Distance(p, new Vector2(EX[i], EY[i]));
            if (_enemySlowUntil[i] > Tick) speed *= 1 - _enemySlow[i];
            if (_enemyAttackTicks[i] > 0 && d.Behavior != "screamer") speed *= 0.2f;
            else if (d.ShotRange > 0)
                speed *= dist < d.ShotRange * 0.5f ? -1 : dist <= d.ShotRange * 0.85f ? 0 : 1;
            var direction = goal - new Vector2(EX[i], EY[i]);
            float distanceToGoal = direction.Length();
            if (distanceToGoal > 0.01f) { vx = direction.X / distanceToGoal * speed; vy = direction.Y / distanceToGoal * speed; }
            if (d.Behavior == "vein") continue;
            var start = new Vector2(EX[i], EY[i]);
            var move = new Vector2(vx + EKx[i], vy + EKy[i]) * Dt;
            var end = Vector2.Clamp(start + move, new Vector2(4), new Vector2(arena - 4));
            (EX[i], EY[i]) = (end.X, end.Y);
            dist = Vector2.Distance(p, end);
            if (!stunned && d.Behavior == "jumper" && Tick == _enemyAbilities[i].Expires)
                EnemyAreaAttack(end, 50, EDamage[i], DamageKind.Physical, effect: EventKind.EnemyQuake);
            EKx[i] *= 0.82f;
            EKy[i] *= 0.82f;
            for (int j = 0; j < _players.Length; j++)
            {
                var runner = _players[j];
                bool boar = runner.Character.Id == "pig";
                bool canAccelerate = runner.AcceleratorTicks > 0 && _acceleratorContact[j, i] != EUid[i];
                if (!runner.Active || runner.Hp <= 0 || (!boar && !canAccelerate)) continue;

                float contactRadius = d.Radius + PlayerRadius + ContactTolerance;
                bool touching = Vector2.DistanceSquared(runner.Pos, end) <= contactRadius * contactRadius;
                if (boar) StepBoarContact(runner, i, touching);
                if (!EAlive[i]) break;
                if (!touching || !canAccelerate) continue;
                _acceleratorContact[j, i] = EUid[i];
                int item = _acceleratorItem[j];
                DamageEnemy(i, Content.Items[item].Weapon!.Damage * runner.ItemStacks[item], DamageKind.Physical,
                    new DamageSourceRef(j, item), runner.Pos.X, runner.Pos.Y);
                if (!EAlive[i]) break;
            }
            if (!EAlive[i]) continue;
            if (d.AttackSpeed > 0 || d.DetonateRange > 0)
            {
                bool mining = d.Behavior == "prospector" && EnemyLinkAlive(_enemyAbilities[i]);
                bool retreating = d.Behavior == "ninja" && _enemyAbilities[i].SecondaryReady > Tick;
                if (!mining && !retreating) StepEnemyAttack(i, d, player, decoy, p, stunned, enemyLifestealCap);
                if (EAlive[i] && !d.Boss && dist > 700) Kill(i, credit: -1, deathEffects: false);
                continue;
            }
            if (d.Damage <= 0 || d.Behavior == "jumper")
            {
                if (dist > 700) Kill(i, credit: -1, deathEffects: false);
                continue;
            }
            if (!stunned && Tick >= _enemyAttackReady[i])
            {
                if (hasCompanions && TryHitCompanion(end, d.Radius + CompanionRadius, EDamage[i], _enemyKind[EDef[i]]))
                    _enemyAttackReady[i] = Tick + ContactCooldownTicks;
                else if (dist <= d.Radius + PlayerRadius + ContactTolerance)
                {
                    _enemyAttackReady[i] = Tick + ContactCooldownTicks;
                    HitPlayerWithEnemy(i, d, player, decoy, enemyLifestealCap);
                    if (!EAlive[i]) continue;
                }
            }
            // Enemies left far behind are recycled closer to the action (keeps pressure constant).
            if (!d.Boss && dist > 700) { Kill(i, credit: -1, deathEffects: false); }
        }
    }

    private void ApplyEnemyStatus(int enemy, float slow, float slowSeconds, float stunSeconds)
    {
        if (slow > 0 && (_enemySlowUntil[enemy] <= Tick || slow >= _enemySlow[enemy]))
        {
            _enemySlow[enemy] = slow;
            _enemySlowUntil[enemy] = Tick + (int)MathF.Ceiling(slowSeconds * TicksPerSecond);
        }
        if (stunSeconds > 0)
            _enemyStunUntil[enemy] = Math.Max(_enemyStunUntil[enemy], Tick + (int)MathF.Ceiling(stunSeconds * TicksPerSecond));
    }

    public Vector2 EnemyPos(int i, float alpha) => new(_prevX[i] + (EX[i] - _prevX[i]) * alpha, _prevY[i] + (EY[i] - _prevY[i]) * alpha);

    /// <summary>Outgoing multiplier for a hit: might × damage-type bonus × item bonus (for item sources).</summary>
    public float OutgoingMultiplier(DamageKind kind, bool fromItem) => OutgoingMultiplier(Player, kind, fromItem) * (1 + Player.PuppeteerDamageBonus);

    private static float OutgoingMultiplier(PlayerState p, DamageKind kind, bool fromItem)
    {
        var s = p.Stats;
        float m = s[Stat.Might] * (1 + p.Souls * (0.005f + 0.001f * (p.Level - 1))) * kind switch
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
        if (!EAlive[i] || IsCharmed(i) || raw <= 0) return;
        var def = Content.Enemies[EDef[i]];
        int playerId = source.PlayerId;
        var p = (uint)playerId < (uint)_players.Length ? _players[playerId] : Player;
        if (RollChance(p.Stats[Stat.DamageReroll])) kind = (DamageKind)(((int)kind + 1 + _rng.Int(2)) % 3);
        float critChance = kind switch
        {
            DamageKind.Physical => p.Stats[Stat.PhysicalCrit],
            DamageKind.Magical => p.Stats[Stat.MagicalCrit],
            _ => 0,
        };
        float critical = RollChance(critChance) ? p.Stats[Stat.CritDamage] : 1;
        if (EElite[i] || def.Boss) raw *= p.Stats[Stat.EliteDamage];
        float largestEnemyBonus = p.Stats[Stat.HighestHealthDamage];
        if (largestEnemyBonus > 1 && i == HighestHealthEnemy()) raw *= largestEnemyBonus;
        float outgoing = OutgoingMultiplier(p, kind, source.FromItem);
        outgoing *= 1 + (source.CompanionSlot >= 0 ? _companions[source.CompanionSlot].DamageBonus : p.PuppeteerDamageBonus);
        if (kind == DamageKind.Physical && source.CompanionSlot >= 0)
            outgoing *= (1 + _companions[source.CompanionSlot].PhysicalDamage) / (1 + p.PhysicalAuraBonus);
        if (!source.FromItem && source.CompanionSlot < 0 && HasNegativeEffect(i)) raw *= DebuffedDamageMultiplier(p);
        float amount = raw * critical * outgoing * (1 - Resist(kind, Math.Max(0, def.Armor - ArmorShredAt(i)), def.MagicResist));
        if (amount <= 0) return;
        if (_guardiansAlive > 0 && def.Behavior is not ("guardian" or "vein" or "illusion"))
        {
            int guardian = FindEnemy(new(EX[i], EY[i]), i, 95, "guardian");
            if (guardian >= 0)
            {
                float redirected = amount * 0.35f;
                ApplyEnemyHit(guardian, redirected, kind, playerId, fromX, fromY, 0, triggerItems: false);
                amount -= redirected;
            }
        }
        float hpBefore = EHp[i];
        ApplyEnemyHit(i, amount, kind, playerId, fromX, fromY, knock, triggerItems: !source.SuppressProcs);
        float lifesteal = p.Stats[Stat.Lifesteal] + (kind == DamageKind.Magical ? p.Stats[Stat.MagicalLifesteal] : 0);
        if (source.CompanionSlot >= 0)
        {
            ref var ally = ref _companions[source.CompanionSlot];
            RestoreCompanion(ref ally, Math.Min(amount, hpBefore) * ally.Lifesteal);
        }
        else if (lifesteal > 0) RestoreFromLifesteal(p, Math.Min(amount, hpBefore) * lifesteal);
        float threshold = EMaxHp[i] * 0.12f;
        if (source.SuppressProcs) return;
        if (EAlive[i])
        {
            OnEnemyHitWithItemEffects(p, i, amount);
            if (!source.FromItem && source.CompanionSlot < 0) ApplyAttackModifiers(p, i);
        }
        if (EAlive[i] && !def.Boss && def.Behavior is not ("illusion" or "vein") && p.ConversionItem >= 0 && ItemEnabled(p, p.ConversionItem))
        {
            int item = p.ConversionItem;
            float chance = Content.Items[item].Companion!.Chance * p.ItemStacks[item];
            if (RollChance(chance) && SpawnCompanion(p.Id, item, new Vector2(EX[i], EY[i]), EHp[i]))
            {
                Emit(EventKind.Pulse, EX[i], EY[i], 15);
                Kill(i, credit: -1, deathEffects: false);
                return;
            }
        }
        if (EAlive[i] && !def.Boss && hpBefore >= threshold && EHp[i] < threshold && RollChance(p.Stats[Stat.FinisherChance]))
            ApplyEnemyHit(i, EHp[i], DamageKind.Pure, playerId, fromX, fromY, 0);
    }

    private bool RollChance(float chance) => chance > 0 && (chance >= 1 || _rng.Next() < chance);

    private void ApplyEnemyHit(int i, float amount, DamageKind kind, int playerId, float fromX, float fromY, float knock, bool triggerItems = true)
    {
        EHp[i] -= amount;
        var def = Content.Enemies[EDef[i]];
        EFlash[i] = 3;
        float dx = EX[i] - fromX, dy = EY[i] - fromY, d = MathF.Sqrt(dx * dx + dy * dy);
        if (d > 0.01f && !def.Boss && def.Hp < 100)
        {
            EKx[i] += dx / d * knock;
            EKy[i] += dy / d * knock;
        }
        Emit(EventKind.Hit, EX[i], EY[i], amount, a: (int)kind);
        if (EHp[i] <= 0) Kill(i, playerId, triggerItems);
    }

    private void Kill(int i, int credit, bool triggerItems = false, bool deathEffects = true)
    {
        if (!EAlive[i]) return;
        EAlive[i] = false;
        AliveEnemies--;
        var d = Content.Enemies[EDef[i]];
        OnEnemyAbilityDeath(i, d);
        if (deathEffects) QueueEnemyDeathEffect(i, d, credit);
        bool hasKiller = (uint)credit < (uint)_players.Length;
        if (d.Xp > 0 && (deathEffects || hasKiller))
        {
            double scale = 1 + (int)Minutes * (double)Content.Waves.XpPerMinute;
            int xp = (int)Math.Min(ushort.MaxValue, Math.Round(d.Xp * scale, MidpointRounding.AwayFromZero));
            DropPickup(PickupKind.Xp, EX[i], EY[i], xp, hasKiller ? credit : -1);
        }
        if (hasKiller)
        {
            var killer = _players[credit];
            bool countsAsKill = d.Behavior is not ("illusion" or "vein");
            if (countsAsKill)
            {
                killer.Kills++;
                if (killer.Character.Id == "necromancer") killer.Souls++;
            }
            uint dropSeed = _rng.State;
            if (countsAsKill && d.Gold > 0 && _rng.Next() < d.GoldChance)
            {
                int gold = d.Gold + (int)Minutes * Content.Waves.GoldPerMinute;
                CollectGold(killer, gold, credit);
                Emit(EventKind.Pickup, EX[i] + _rng.Range(-3, 3), EY[i] + _rng.Range(-3, 3),
                    gold, a: (int)PickupKind.Gold);
            }
            Emit(EventKind.Kill, EX[i], EY[i], a: EDef[i], b: killer.Id, seed: dropSeed);
            if (countsAsKill && triggerItems) OnEnemyKilledWithItemEffects(_players[credit], new Vector2(EX[i], EY[i]));
        }
        if (d.Boss && deathEffects)
        {
            var position = new Vector2(EX[i], EY[i]);
            DropBossChest(position);
            int owner = (uint)credit < (uint)_players.Length ? credit : NearestLivingPlayer(position);
            if (owner >= 0 && _bossRewards.TryGetValue(d.Id, out int reward))
                DropPickup(PickupKind.Cursed, position.X, position.Y, reward, owner);
        }
        EX[i] = EY[i] = -1e6f;
        _freeEnemies.Push(i);
    }

    /// <summary>Deterministic digest of the run state (for tests and future network desync checks).</summary>
    public ulong StateHash()
    {
        ulong h = 1469598103934665603;
        void Mix(float v) { h = (h ^ BitConverter.SingleToUInt32Bits(v)) * 1099511628211; }
        Mix(Tick); Mix(BonusSeconds); Mix(_timekeeperTicks);
        foreach (var p in _players)
        {
            Mix(p.Active ? 1 : 0);
            if (!p.Active) continue;
            Mix(p.Pos.X); Mix(p.Pos.Y); Mix(p.Hp); Mix(p.Xp); Mix(p.Level);
            Mix(p.Kills); Mix(p.Wallet.Gold); Mix(p.Wallet.NextPrice);
            if (p.Character.Id != "illusionist") continue;
            foreach (ref readonly var illusion in p.Illusions.AsSpan())
            {
                if (illusion.UntilTick <= Tick) continue;
                Mix(illusion.Position.X); Mix(illusion.Position.Y); Mix(illusion.Health);
                Mix(illusion.UntilTick); Mix(illusion.HurtTick);
            }
        }
        for (int i = 0; i < _enemyHigh; i++)
        {
            if (!EAlive[i]) continue;
            Mix(EX[i]); Mix(EY[i]); Mix(EHp[i]);
            Mix(_charmOwner[i]); Mix(_charmUntil[i]); Mix(_charmAttackReady[i]);
            Mix(_enemySlow[i]); Mix(_enemySlowUntil[i]); Mix(_enemyStunUntil[i]);
            for (int owner = 0; owner < _players.Length; owner++)
            {
                Mix(_armorShred[owner, i]); Mix(_armorShredUntil[owner, i]);
                Mix(_poisonTick[owner, i]); Mix(_poisonDamage[owner, i]);
            }
        }
        foreach (ref readonly var shot in EnemyShots) { Mix(shot.Pos.X); Mix(shot.Pos.Y); Mix(shot.Life); }
        foreach (ref readonly var cloud in EnemyClouds) { Mix(cloud.Pos.X); Mix(cloud.Pos.Y); Mix(cloud.Life); }
        Mix(AliveEnemies);
        return h;
    }
}
