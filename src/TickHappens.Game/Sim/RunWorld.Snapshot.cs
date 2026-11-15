using System.Buffers.Binary;
using System.Numerics;
using TickHappens.Game.Content;

namespace TickHappens.Game.Sim;

public sealed partial class RunWorld
{
    public const int SnapshotCapacity = 64 * 1024;
    private const byte SnapshotVersion = 2;
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
            w.Byte((byte)Phase);
            w.Byte((byte)_players.Length);
            foreach (var p in _players)
            {
                w.Byte(p.Active ? (byte)1 : (byte)0);
                if (!p.Active) continue;
                w.Short(Content.CharacterIndex(p.Character.Id));
                WritePoint(ref w, p.Pos.X, p.Pos.Y);
                w.Float(p.Hp);
                w.Int(p.Level);
                w.Float(p.Xp);
                w.Float(p.XpNext);
                w.Int(p.Kills);
                w.Int(p.Wallet.Gold);
                w.Int(p.Wallet.NextPrice);
                w.Int(p.InvulnTicks);
                w.Int(p.DashTicks);
                w.Int(p.DashCooldownTicks);
                w.Int(p.NearbyChest);
                w.Int(p.LastChestItem);
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
                w.Byte((byte)(hp | (EFlash[i] > 0 ? 64 : 0)));
            }
            w.Short(CountActive(GAlive));
            for (int i = 0; i < PickupCapacity; i++)
            {
                if (!GAlive[i]) continue;
                WritePoint(ref w, GX[i], GY[i]);
                w.Byte(GKind[i] == PickupKind.Xp ? (byte)0 : (byte)(1 + GOwner[i]));
                w.Short(Math.Min(ushort.MaxValue, GValue[i]));
            }
            w.Short(CountActive(PAlive));
            for (int i = 0; i < ProjectileCapacity; i++)
            {
                if (!PAlive[i]) continue;
                WritePoint(ref w, PX[i], PY[i]);
                w.Short((int)Math.Clamp(MathF.Round(PVx[i] * 4), short.MinValue, short.MaxValue) & ushort.MaxValue);
                w.Short((int)Math.Clamp(MathF.Round(PVy[i] * 4), short.MinValue, short.MaxValue) & ushort.MaxValue);
            }
            return w.Length;
        }
        catch (InvalidDataException) { return 0; }
    }

    public bool TryApplySnapshot(ReadOnlySpan<byte> source)
    {
        if (source.Length > SnapshotCapacity) return false;
        try
        {
            ReadSnapshot(source, apply: false);
            ReadSnapshot(source, apply: true);
            return true;
        }
        catch (InvalidDataException) { return false; }
    }

    // A validation pass ensures a malformed packet leaves the visible world unchanged.
    private void ReadSnapshot(ReadOnlySpan<byte> source, bool apply)
    {
        var r = new SnapshotReader(source);
        Require(r.Byte() == SnapshotVersion);
        int tick = r.Int();
        uint seed = unchecked((uint)r.Int());
        float bonusSeconds = r.Float();
        int phase = r.Byte();
        int playerCount = r.Byte();
        Require(tick >= 0 && tick > _replicaTick && phase <= (byte)RunPhase.Dead &&
            bonusSeconds >= 0 && playerCount == _players.Length);
        if (apply)
        {
            Seed = seed;
            Tick = tick;
            BonusSeconds = bonusSeconds;
            Phase = (RunPhase)phase;
        }
        for (int i = 0; i < playerCount; i++) ReadPlayer(ref r, i, apply);
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
        Require(r.Short() == Chests.Length);
        for (int i = 0; i < Chests.Length; i++)
        {
            var position = ReadPoint(ref r);
            bool open = r.Flag();
            if (!apply) continue;
            Chests[i] = position;
            ChestOpen[i] = open;
        }
        int count = r.Short();
        Require(count <= EnemyCapacity && count <= _enemySnapshotSlots.Length);
        int prev = -1;
        for (int j = 0; j < count; j++)
        {
            int slot = r.Short(), uid = r.Short();
            Vector2 position = ReadPoint(ref r);
            int def = r.Short(), status = r.Byte();
            Require(slot < EnemyCapacity && slot > prev && def < Content.Enemies.Count &&
                status is >= 1 and <= 127 && (status & 63) > 0);
            prev = slot;
            if (!apply) continue;
            _enemySnapshotSlots[j] = (ushort)slot;
            bool existing = EAlive[slot] && EUid[slot] == uid;
            _prevX[slot] = existing ? EX[slot] : position.X;
            _prevY[slot] = existing ? EY[slot] : position.Y;
            EX[slot] = position.X;
            EY[slot] = position.Y;
            EDef[slot] = (short)def;
            EUid[slot] = uid;
            EFlash[slot] = (status & 64) == 0 ? (byte)0 : (byte)3;
            EMaxHp[slot] = Content.Enemies[def].Hp * HpScale;
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
        int pickups = r.Short();
        Require(pickups <= PickupCapacity);
        if (apply) Array.Clear(GAlive);
        for (int i = 0; i < pickups; i++)
        {
            var position = ReadPoint(ref r);
            int owner = r.Byte(), value = r.Short();
            Require(owner <= playerCount);
            if (!apply) continue;
            GAlive[i] = true;
            GX[i] = position.X;
            GY[i] = position.Y;
            GKind[i] = owner == 0 ? PickupKind.Xp : PickupKind.Gold;
            GOwner[i] = owner - 1;
            GValue[i] = value;
        }
        int projectiles = r.Short();
        Require(projectiles <= ProjectileCapacity);
        if (apply) Array.Clear(PAlive);
        for (int i = 0; i < projectiles; i++)
        {
            var position = ReadPoint(ref r);
            float vx = (short)r.Short() * 0.25f, vy = (short)r.Short() * 0.25f;
            if (!apply) continue;
            PAlive[i] = vx != 0 || vy != 0;
            PX[i] = position.X;
            PY[i] = position.Y;
            PVx[i] = vx;
            PVy[i] = vy;
        }
        Require(r.Remaining == 0);
        if (apply) _replicaTick = tick;
    }

    private void ReadPlayer(ref SnapshotReader r, int slot, bool apply)
    {
        bool active = r.Flag();
        var p = _players[slot];
        if (!active)
        {
            if (apply) p.Active = false;
            return;
        }
        int character = r.Short();
        var position = ReadPoint(ref r);
        float hp = r.Float();
        int level = r.Int();
        float xp = r.Float(), xpNext = r.Float();
        int kills = r.Int(), gold = r.Int(), price = r.Int(), invulnerable = r.Int(), dash = r.Int(), cooldown = r.Int();
        int chest = r.Int(), item = r.Int(), flags = r.Byte();
        int lastAppliedInput = r.Int();
        var facing = DecodeAngle(r.Short());
        var dashDirection = DecodeAngle(r.Short());
        int equipped = r.Short();
        Require(character < Content.Characters.Count && hp >= 0 && level > 0 && xp >= 0 && xpNext > 0 &&
            kills >= 0 && gold >= 0 && price > 0 && invulnerable >= 0 && dash >= 0 && cooldown >= 0 &&
            chest >= -1 && chest < Chests.Length && item >= -1 && item < Content.Items.Count && flags <= 7 && lastAppliedInput >= 0 &&
            equipped <= Content.Items.Count);
        if (apply)
        {
            if (!p.Active || p.Character != Content.Characters[character])
            {
                p = CreatePlayer(Content.Characters[character], slot, false);
                _players[slot] = p;
            }
            p.Prev = p.Active && _replicaTick >= 0 ? p.Pos : position;
            p.Pos = position;
            p.Active = true;
            p.Facing = facing;
            p.DashDir = dashDirection;
            p.Walking = (flags & 1) != 0;
            p.FacingLeft = (flags & 2) != 0;
            p.PrevDash = (flags & 4) != 0;
            p.LastAppliedInput = lastAppliedInput;
            p.Hp = hp;
            p.Level = level;
            p.Xp = xp;
            p.XpNext = xpNext;
            p.Kills = kills;
            p.Wallet.ApplyReplica(gold, price);
            p.InvulnTicks = invulnerable;
            p.DashTicks = dash;
            p.DashCooldownTicks = cooldown;
            p.NearbyChest = chest;
            p.LastChestItem = item;
            Array.Clear(p.ItemStacks);
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
            foreach (var m in Content.Items[id].Mods)
                if (ContentLoader.TryParseStat(m.Stat, out var stat)) p.Stats.Apply(stat, m.Add * copies, m.Pct * copies);
        }
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
