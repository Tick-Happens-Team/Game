using System.Buffers.Binary;
using System.Numerics;
using TickHappens.Game.Content;

namespace TickHappens.Game.Sim;

public sealed partial class RunWorld
{
    public const int SnapshotCapacity = 64 * 1024;
    private const byte SnapshotVersion = 7;
    private int _replicaTick = -1;
    private readonly ushort[] _enemySnapshotSlots = new ushort[ushort.MaxValue / 8];

    public int WriteSnapshot(Span<byte> destination)
    {
        try
        {
            var w = new SnapshotWriter(destination);
            w.Byte(SnapshotVersion);
            w.Int(Tick);
            w.Int(unchecked((int)Seed));
            w.Float(BonusSeconds);
            w.Int(_timekeeperTicks);
            w.Byte((byte)Phase);
            w.Byte((byte)_players.Length);
            foreach (var p in _players)
            {
                w.Byte(p.Active ? (byte)1 : (byte)0);
                if (!p.Active) continue;
                w.Short(Content.CharacterIndex(p.Character.Id));
                WritePoint(ref w, p.Pos.X, p.Pos.Y);
                w.Float(p.Hp);
                w.Byte((byte)p.RevivalTicks);
                w.Int(p.Level);
                w.Float(p.Xp);
                w.Float(p.XpNext);
                w.Int(p.Kills);
                w.Int(p.Wallet.Gold);
                w.Int(p.Wallet.NextPrice);
                w.Int(p.InvulnTicks);
                w.Int(p.DashTicks);
                w.Int(p.DashTicksTotal);
                w.Int(p.DashChargesUsed);
                w.Int(p.DashCooldownTicks);
                w.Int(p.DashSpeedTicks);
                w.Int(p.AcceleratorTicks);
                w.Float(p.AcceleratorSpeedBonus);
                w.Float(p.Shield);
                w.Float(p.BonusShield);
                w.Int(p.GhostUntilTick);
                w.Int(p.HeroAttackTick);
                w.Int(p.EnemySlowUntil);
                w.Int(p.EnemyRootUntil);
                w.Int(p.StolenItem);
                w.Byte(p.TimekeeperSlowed ? (byte)1 : (byte)0);
                switch (p.Character.Id)
                {
                    case "diver": w.Float(p.HeroDistance); break;
                    case "necromancer": w.Int(p.Souls); break;
                    case "illusionist":
                        w.Float(MathF.Max(0, p.IllusionHealth));
                        w.Float(p.IllusionMaxHealth);
                        break;
                    case "cybernetic_cube":
                        foreach (float bonus in p.CubeBonuses) w.Float(bonus);
                        break;
                }
                w.Int(p.IllusionUntilTick);
                WritePoint(ref w, p.IllusionPosition.X, p.IllusionPosition.Y);
                w.Int(p.NearbyChest);
                w.Int(p.LastChestItem);
                w.Int(p.ChestOfferFirst);
                w.Int(p.ChestOfferSecond);
                w.Int(p.ChestOfferThird);
                w.Int(p.ChestOfferUntilTick);
                w.Byte((byte)((p.Walking ? 1 : 0) | (p.FacingLeft ? 2 : 0) | (p.PrevDash ? 4 : 0)));
                w.Int(p.LastAppliedInput);
                w.Short(EncodeAngle(p.Facing));
                w.Short(EncodeAngle(p.DashDir));
                int equipped = 0;
                foreach (int count in p.ItemStacks) if (count > 0) equipped++;
                w.Short(equipped);
                for (int i = 0; i < p.ItemStacks.Length; i++)
                {
                    if (p.ItemStacks[i] == 0) continue;
                    w.Short(i);
                    w.Short(p.ItemStacks[i]);
                }
            }
            w.Short(Statues.Length);
            for (int i = 0; i < Statues.Length; i++)
            {
                WritePoint(ref w, Statues[i].X, Statues[i].Y);
                w.Byte(StatueActive[i] ? (byte)1 : (byte)0);
                w.Short((int)MathF.Round(Math.Clamp(StatueHold[i] / Math.Max(Rules.Statue.HoldSeconds, Dt), 0, 1) * ushort.MaxValue));
            }
            w.Short(Chests.Length);
            for (int i = 0; i < Chests.Length; i++)
            {
                WritePoint(ref w, Chests[i].X, Chests[i].Y);
                w.Byte(ChestOpen[i] ? (byte)1 : (byte)0);
            }
            w.Short(AliveEnemies);
            for (int i = 0; i < _enemyHigh; i++)
            {
                if (!EAlive[i]) continue;
                w.Short(i);
                w.Short(EUid[i] & ushort.MaxValue);
                WritePoint(ref w, EX[i], EY[i]);
                w.Short(EDef[i]);
                int hp = Math.Clamp((int)MathF.Ceiling(EHp[i] / EMaxHp[i] * 63), 1, 63);
                w.Byte((byte)(hp | (EFlash[i] > 0 ? 64 : 0) | (_enemyHidden[i] ? 128 : 0)));
                w.Byte(_enemyAttackTicks[i]);
            }
            int companions = 0;
            foreach (ref readonly var ally in Companions) if (ally.Active) companions++;
            w.Byte((byte)companions);
            for (int i = 0; i < CompanionCapacity; i++)
            {
                ref readonly var ally = ref _companions[i];
                if (!ally.Active) continue;
                w.Byte((byte)i);
                w.Int(ally.Id);
                w.Short(ally.Item & ushort.MaxValue);
                w.Byte((byte)(ally.Owner | (ally.FacingLeft ? 4 : 0) | (ally.Healing ? 8 : 0)));
                WritePoint(ref w, ally.Pos.X, ally.Pos.Y);
                w.Float(ally.Health);
                w.Float(ally.AuraCapacity);
            }
            w.Short(CountActive(GAlive));
            for (int i = 0; i < PickupCapacity; i++)
            {
                if (!GAlive[i]) continue;
                WritePoint(ref w, GX[i], GY[i]);
                w.Byte(GKind[i] == PickupKind.Xp ? (byte)0 : (byte)(1 + ((int)GKind[i] - 1) * 4 + GOwner[i]));
                w.Short(Math.Min(ushort.MaxValue, GValue[i]));
            }
            w.Short(CountActive(PAlive));
            for (int i = 0; i < ProjectileCapacity; i++)
            {
                if (!PAlive[i]) continue;
                WritePoint(ref w, PX[i], PY[i]);
                w.Byte((byte)PStyle[i]);
                w.Short((int)Math.Clamp(MathF.Round(PVx[i] * 4), short.MinValue, short.MaxValue) & ushort.MaxValue);
                w.Short((int)Math.Clamp(MathF.Round(PVy[i] * 4), short.MinValue, short.MaxValue) & ushort.MaxValue);
            }
            w.Short(_enemyShotCount);
            foreach (ref readonly var shot in EnemyShots)
            {
                WritePoint(ref w, shot.Pos.X, shot.Pos.Y);
                w.Float(shot.Velocity.X);
                w.Float(shot.Velocity.Y);
            }
            w.Byte((byte)_enemyCloudCount);
            foreach (ref readonly var cloud in EnemyClouds)
            {
                WritePoint(ref w, cloud.Pos.X, cloud.Pos.Y);
                w.Float(cloud.Radius);
                w.Int(cloud.Life);
            }
            return w.Length;
        }
        catch (InvalidDataException) { return 0; }
    }

    public bool TryApplySnapshot(ReadOnlySpan<byte> source, float previousAlpha = 1)
    {
        if (source.Length > SnapshotCapacity) return false;
        try
        {
            ReadSnapshot(source, apply: false, previousAlpha);
            ReadSnapshot(source, apply: true, previousAlpha);
            BuildEnemyGrid();
            return true;
        }
        catch (InvalidDataException) { return false; }
    }

    // A validation pass ensures a malformed packet leaves the visible world unchanged.
    private void ReadSnapshot(ReadOnlySpan<byte> source, bool apply, float previousAlpha)
    {
        var r = new SnapshotReader(source);
        Require(r.Byte() == SnapshotVersion);
        int tick = r.Int();
        uint seed = unchecked((uint)r.Int());
        float bonusSeconds = r.Float();
        int timekeeperTicks = r.Int();
        int phase = r.Byte();
        int playerCount = r.Byte();
        Require(tick >= 0 && tick > _replicaTick && phase <= (byte)RunPhase.Dead &&
            timekeeperTicks >= 0 && timekeeperTicks <= tick && bonusSeconds >= 0 && playerCount == _players.Length);
        if (apply)
        {
            Seed = seed;
            Tick = tick;
            BonusSeconds = bonusSeconds;
            _timekeeperTicks = timekeeperTicks;
            Phase = (RunPhase)phase;
        }
        int highestChestHint = -1;
        for (int i = 0; i < playerCount; i++) highestChestHint = Math.Max(highestChestHint, ReadPlayer(ref r, i, apply, previousAlpha));
        if (apply) UpdateAllPlayerStats();
        Require(r.Short() == Statues.Length);
        for (int i = 0; i < Statues.Length; i++)
        {
            var position = ReadPoint(ref r);
            bool active = r.Flag();
            int hold = r.Short();
            if (!apply) continue;
            Statues[i] = position;
            StatueActive[i] = active;
            StatueHold[i] = hold * (Rules.Statue.HoldSeconds / ushort.MaxValue);
        }
        int chests = r.Short();
        Require(chests >= Rules.Chests.Count && chests >= Chests.Length && chests <= MaxChestSlots && highestChestHint < chests);
        if (apply && chests > Chests.Length) ResizeChests(chests);
        for (int i = 0; i < chests; i++)
        {
            var position = ReadPoint(ref r);
            bool open = r.Flag();
            if (!apply) continue;
            Chests[i] = position;
            ChestOpen[i] = open;
        }
        int count = r.Short();
        Require(count <= EnemyCapacity && count <= _enemySnapshotSlots.Length);
        if (apply) _timekeepersAlive = 0;
        int prev = -1;
        for (int j = 0; j < count; j++)
        {
            int slot = r.Short(), uid = r.Short();
            Vector2 position = ReadPoint(ref r);
            int def = r.Short(), status = r.Byte(), windup = r.Byte();
            Require(slot < EnemyCapacity && slot > prev && def < Content.Enemies.Count &&
                (status & 63) > 0 && windup <= EnemyWindupDuration(Content.Enemies[def]));
            prev = slot;
            if (!apply) continue;
            _enemySnapshotSlots[j] = (ushort)slot;
            bool existing = EAlive[slot] && EUid[slot] == uid;
            Vector2 previous = existing ? EnemyPos(slot, previousAlpha) : position;
            _prevX[slot] = previous.X;
            _prevY[slot] = previous.Y;
            EX[slot] = position.X;
            EY[slot] = position.Y;
            EDef[slot] = (short)def;
            if (Content.Enemies[def].Behavior == "timekeeper") _timekeepersAlive++;
            EUid[slot] = uid;
            EFlash[slot] = (status & 64) == 0 ? (byte)0 : (byte)3;
            _enemyAttackTicks[slot] = (byte)windup;
            _enemyHidden[slot] = (status & 128) != 0;
            EMaxHp[slot] = Content.Enemies[def].Hp * (Content.Enemies[def].Boss ? 1 : HpScale);
            EHp[slot] = EMaxHp[slot] * (status & 63) / 63;
        }
        if (apply)
        {
            int read = 0;
            int high = prev + 1;
            for (int i = 0; i < Math.Max(_enemyHigh, high); i++)
            {
                bool active = read < count && _enemySnapshotSlots[read] == i;
                if (active) read++;
                EAlive[i] = active;
            }
            _enemyHigh = high;
            AliveEnemies = count;
        }
        int companions = r.Byte();
        Require(companions <= CompanionCapacity);
        int lastCompanion = -1;
        ulong aliveCompanions = 0;
        for (int i = 0; i < companions; i++)
        {
            int slot = r.Byte(), id = r.Int(), item = r.Short(), flags = r.Byte();
            var pos = ReadPoint(ref r);
            float health = r.Float(), capacity = r.Float();
            if (item > ushort.MaxValue - SoulCompanions.Length) item -= ushort.MaxValue + 1;
            Require(slot > lastCompanion && slot < CompanionCapacity && id > 0 && flags <= 15 && health >= 0 && capacity >= health &&
                (flags & 3) < playerCount && (item < 0 || item < Content.Items.Count && Content.Items[item].Companion != null));
            lastCompanion = slot;
            if (!apply) continue;
            aliveCompanions |= 1UL << slot;
            ref var ally = ref _companions[slot];
            var prevPos = ally.Active && ally.Id == id ? Vector2.Lerp(ally.Prev, ally.Pos, previousAlpha) : pos;
            ally = new Companion
            {
                Active = true, Id = id, Item = item, Owner = flags & 3, FacingLeft = (flags & 4) != 0, Healing = (flags & 8) != 0, Pos = pos, Prev = prevPos,
                Health = health, AuraCapacity = capacity,
            };
        }
        if (apply)
            for (int i = 0; i < CompanionCapacity; i++)
                if ((aliveCompanions & (1UL << i)) == 0) _companions[i].Active = false;

        int pickups = r.Short();
        Require(pickups <= PickupCapacity);
        if (apply) Array.Clear(GAlive);
        for (int i = 0; i < pickups; i++)
        {
            var position = ReadPoint(ref r);
            int packed = r.Byte(), value = r.Short();
            int owner = packed == 0 ? -1 : (packed - 1) & 3;
            var kind = packed == 0 ? PickupKind.Xp : (PickupKind)(1 + (packed - 1) / 4);
            Require(kind <= PickupKind.Cursed && owner < playerCount &&
                (kind != PickupKind.Cursed || value < Content.Items.Count && Content.Items[value].Rarity == ContentSet.CursedRarity));
            if (!apply) continue;
            GAlive[i] = true;
            GX[i] = position.X;
            GY[i] = position.Y;
            GKind[i] = kind;
            GOwner[i] = owner;
            GValue[i] = value;
        }
        int projectiles = r.Short();
        Require(projectiles <= ProjectileCapacity);
        if (apply) Array.Clear(PAlive);
        for (int i = 0; i < projectiles; i++)
        {
            var position = ReadPoint(ref r);
            var style = (ProjectileStyle)r.Byte();
            Require(style <= ProjectileStyle.Phantom);
            float vx = (short)r.Short() * 0.25f, vy = (short)r.Short() * 0.25f;
            if (!apply) continue;
            PAlive[i] = true;
            PStyle[i] = style;
            PX[i] = position.X;
            PY[i] = position.Y;
            PVx[i] = vx;
            PVy[i] = vy;
        }
        int shots = r.Short();
        Require(shots <= _enemyShots.Length);
        for (int i = 0; i < shots; i++)
        {
            var pos = ReadPoint(ref r);
            var velocity = new Vector2(r.Float(), r.Float());
            if (apply) _enemyShots[i] = new EnemyShot(pos, velocity, 0, 0, DamageKind.Magical);
        }
        int clouds = r.Byte();
        Require(clouds <= _enemyClouds.Length);
        for (int i = 0; i < clouds; i++)
        {
            var pos = ReadPoint(ref r);
            float radius = r.Float();
            int life = r.Int();
            Require(radius > 0 && life > 0);
            if (apply) _enemyClouds[i] = new EnemyCloud(pos, radius, life, 0);
        }
        if (apply) (_enemyShotCount, _enemyCloudCount) = (shots, clouds);
        Require(r.Remaining == 0);
        if (apply) _replicaTick = tick;
    }

    private int ReadPlayer(ref SnapshotReader r, int slot, bool apply, float previousAlpha)
    {
        bool active = r.Flag();
        var p = _players[slot];
        if (!active)
        {
            if (apply) p.Active = false;
            return -1;
        }
        int character = r.Short();
        var position = ReadPoint(ref r);
        float hp = r.Float();
        int revival = r.Byte();
        int level = r.Int();
        float xp = r.Float(), xpNext = r.Float();
        int kills = r.Int(), gold = r.Int(), price = r.Int(), invulnerable = r.Int(), dash = r.Int();
        int dashTotal = r.Int(), dashChargesUsed = r.Int(), cooldown = r.Int();
        int dashSpeed = r.Int(), accelerator = r.Int();
        float speedBonus = r.Float(), shield = r.Float(), bonusShield = r.Float();
        Require(character < Content.Characters.Count);
        int ghostUntil = r.Int(), attackTick = r.Int();
        int slowUntil = r.Int(), rootUntil = r.Int(), stolen = r.Int();
        bool timekeeperSlowed = r.Flag();
        string hero = Content.Characters[character].Id;
        float distance = hero == "diver" ? r.Float() : 0;
        int souls = hero == "necromancer" ? r.Int() : 0;
        Span<float> cube = stackalloc float[CubeStats.Length];
        if (hero == "cybernetic_cube")
        {
            for (int i = 0; i < cube.Length; i++)
            {
                cube[i] = r.Float();
                Require(cube[i] >= 0);
            }
        }
        float illusionHealth = 0, illusionMaxHealth = 0;
        if (hero == "illusionist")
        {
            illusionHealth = r.Float();
            illusionMaxHealth = r.Float();
            Require(illusionHealth >= 0 && illusionMaxHealth >= illusionHealth);
        }
        int illusionUntil = r.Int();
        var illusionPos = ReadPoint(ref r);
        int chest = r.Int(), item = r.Int();
        int firstOffer = r.Int(), secondOffer = r.Int(), thirdOffer = r.Int(), offerUntil = r.Int();
        int flags = r.Byte();
        int lastAppliedInput = r.Int();
        var facing = DecodeAngle(r.Short());
        var dashDirection = DecodeAngle(r.Short());
        int equipped = r.Short();
        Require(slowUntil >= 0 && rootUntil >= 0 && stolen >= -1 && stolen < Content.Items.Count && ghostUntil >= 0 && attackTick >= -1 && distance >= 0 && souls >= 0 && hp >= 0 && revival < RevivalHoldTicks &&
            (hp == 0 || revival == 0) && level > 0 && xp >= 0 && xpNext > 0 &&
            kills >= 0 && gold >= 0 && price > 0 && invulnerable >= 0 && dash >= 0 && cooldown >= 0 &&
            chest >= -1 && chest < MaxChestSlots && item >= -1 && item < Content.Items.Count && flags <= 7 && lastAppliedInput >= 0 &&
            dashTotal >= dash && dashChargesUsed is >= 0 and <= 9 && dashSpeed >= 0 && accelerator >= 0 &&
            speedBonus >= 0 && shield >= 0 && bonusShield >= 0 && illusionUntil >= 0 && equipped <= Content.Items.Count &&
            (firstOffer == -1 && secondOffer == -1 && thirdOffer == -1 && offerUntil == 0 ||
            firstOffer >= 0 && firstOffer < Content.Items.Count && secondOffer >= 0 &&
            secondOffer < Content.Items.Count && firstOffer != secondOffer && offerUntil > 0 &&
            (thirdOffer == -1 || thirdOffer >= 0 && thirdOffer < Content.Items.Count &&
                thirdOffer != firstOffer && thirdOffer != secondOffer)));
        if (apply)
        {
            if (!p.Active || p.Character != Content.Characters[character])
            {
                p = CreatePlayer(Content.Characters[character], slot, false);
                _players[slot] = p;
            }
            p.Prev = p.Active && _replicaTick >= 0
                ? slot == _localPlayerId ? p.Pos : Vector2.Lerp(p.Prev, p.Pos, previousAlpha)
                : position;
            p.Pos = position;
            p.Active = true;
            p.Facing = facing;
            p.DashDir = dashDirection;
            p.Walking = (flags & 1) != 0;
            p.FacingLeft = (flags & 2) != 0;
            p.PrevDash = (flags & 4) != 0;
            p.LastAppliedInput = lastAppliedInput;
            p.Hp = hp;
            p.RevivalTicks = revival;
            p.Level = level;
            p.Xp = xp;
            p.XpNext = xpNext;
            p.Kills = kills;
            p.Wallet.ApplyReplica(gold, price);
            p.InvulnTicks = invulnerable;
            p.DashTicks = dash;
            p.DashTicksTotal = dashTotal;
            p.DashChargesUsed = dashChargesUsed;
            p.DashCooldownTicks = cooldown;
            p.DashSpeedTicks = dashSpeed;
            p.AcceleratorTicks = accelerator;
            p.AcceleratorSpeedBonus = speedBonus;
            p.Shield = shield;
            p.BonusShield = bonusShield;
            p.GhostUntilTick = ghostUntil;
            p.HeroAttackTick = attackTick;
            p.EnemySlowUntil = slowUntil;
            p.EnemyRootUntil = rootUntil;
            p.StolenItem = stolen;
            p.TimekeeperSlowed = timekeeperSlowed;
            p.HeroDistance = distance;
            p.Souls = souls;
            if (hero == "cybernetic_cube") cube.CopyTo(p.CubeBonuses);
            p.IllusionUntilTick = illusionUntil;
            p.IllusionHealth = illusionHealth;
            p.IllusionMaxHealth = illusionMaxHealth;
            p.IllusionPosition = illusionPos;
            p.NearbyChest = chest;
            p.LastChestItem = item;
            p.ChestOfferFirst = firstOffer;
            p.ChestOfferSecond = secondOffer;
            p.ChestOfferThird = thirdOffer;
            p.ChestOfferUntilTick = offerUntil;
            Array.Clear(p.ItemStacks);
            p.EffectItems.Clear();
            p.Stats.ClearItems();
            ApplyLevelStats(p);
            UpdateDynamicStats(p);
        }
        int previous = -1;
        for (int j = 0; j < equipped; j++)
        {
            int id = r.Short(), copies = r.Short();
            Require(id > previous && id < Content.Items.Count && copies > 0);
            previous = id;
            if (!apply) continue;
            p.ItemStacks[id] = copies;
            if (Content.Items[id].Effect != null) p.EffectItems.Add(id);
            foreach (var m in Content.Items[id].Mods)
                if (ItemEnabled(p, id) && ContentLoader.TryParseStat(m.Stat, out var stat)) p.Stats.Apply(stat, m.Add * copies, m.Pct * copies);
        }
        return chest;
    }

    private static int CountActive(bool[] slots)
    {
        int count = 0;
        foreach (bool active in slots) if (active) count++;
        return count;
    }

    private void WritePoint(ref SnapshotWriter w, float x, float y)
    {
        float scale = (float)ushort.MaxValue / Rules.ArenaSize;
        w.Short((int)MathF.Round(Math.Clamp(x, 0, Rules.ArenaSize) * scale));
        w.Short((int)MathF.Round(Math.Clamp(y, 0, Rules.ArenaSize) * scale));
    }

    private Vector2 ReadPoint(ref SnapshotReader r)
    {
        float scale = (float)Rules.ArenaSize / ushort.MaxValue;
        return new(r.Short() * scale, r.Short() * scale);
    }

    private static int EncodeAngle(Vector2 direction) =>
        (int)MathF.Round((MathF.Atan2(direction.Y, direction.X) + MathF.PI) * (ushort.MaxValue / MathF.Tau));

    private static Vector2 DecodeAngle(int angle)
    {
        float radians = angle * (MathF.Tau / ushort.MaxValue) - MathF.PI;
        return new(MathF.Cos(radians), MathF.Sin(radians));
    }

    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidDataException("Malformed or outdated run snapshot.");
    }

    private ref struct SnapshotWriter
    {
        private readonly Span<byte> _buffer;

        public SnapshotWriter(Span<byte> buffer) => _buffer = buffer;

        public int Length { get; private set; }

        private Span<byte> Take(int count)
        {
            Require(count <= _buffer.Length - Length);
            var next = _buffer.Slice(Length, count);
            Length += count;
            return next;
        }

        public void Byte(byte value) => Take(1)[0] = value;
        public void Int(int value) => BinaryPrimitives.WriteInt32LittleEndian(Take(4), value);
        public void Float(float value) => Int(BitConverter.SingleToInt32Bits(value));
        public void Short(int value)
        {
            Require((uint)value <= ushort.MaxValue);
            BinaryPrimitives.WriteUInt16LittleEndian(Take(2), (ushort)value);
        }
    }

    private ref struct SnapshotReader
    {
        private readonly ReadOnlySpan<byte> _buffer;
        private int _position;
        public int Remaining => _buffer.Length - _position;

        public SnapshotReader(ReadOnlySpan<byte> buffer) => _buffer = buffer;

        private ReadOnlySpan<byte> Take(int count)
        {
            Require(count <= Remaining);
            var next = _buffer.Slice(_position, count);
            _position += count;
            return next;
        }

        public int Byte() => Take(1)[0];
        public bool Flag()
        {
            int value = Byte();
            Require(value <= 1);
            return value == 1;
        }

        public int Short() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
        public int Int() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));
        public float Float()
        {
            float value = BitConverter.Int32BitsToSingle(Int());
            Require(float.IsFinite(value));
            return value;
        }
    }
}
