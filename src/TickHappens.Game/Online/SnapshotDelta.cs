using System.Buffers.Binary;

namespace TickHappens.Game.Online;

/// <summary>Encodes consecutive unchanged bytes as copies from a reliable keyframe.</summary>
public static class SnapshotDelta
{
    public const int MaxHeaderBytes = 8;
    private const int NoBaseline = -1;
    private const int MaxRun = 128;

    public static int Encode(int baselineTick, ReadOnlySpan<byte> baseline, ReadOnlySpan<byte> snapshot, Span<byte> output)
    {
        int rawLength = sizeof(int) + snapshot.Length;
        if (rawLength > output.Length) throw new ArgumentException("Snapshot buffer is too small.", nameof(output));
        if (baselineTick >= 0 && rawLength > MaxHeaderBytes)
        {
            int written = MaxHeaderBytes;
            int cursor = 0;
            while (cursor < snapshot.Length && written < rawLength)
            {
                int start = cursor;
                bool copy = Matches(baseline, snapshot, cursor);
                int limit = Math.Min(start + MaxRun, snapshot.Length);
                int end = start + 1;
                while (end < limit && Matches(baseline, snapshot, end) == copy) end++;
                int length = end - start;
                if (!copy && written + 1 + length >= rawLength) break;
                cursor = end;
                output[written++] = (byte)((copy ? 0x80 : 0) | (length - 1));
                if (copy) continue;
                snapshot.Slice(start, length).CopyTo(output[written..]);
                written += length;
            }
            if (cursor == snapshot.Length && written < rawLength)
            {
                BinaryPrimitives.WriteInt32LittleEndian(output, baselineTick);
                BinaryPrimitives.WriteInt32LittleEndian(output[sizeof(int)..], snapshot.Length);
                return written;
            }
        }

        BinaryPrimitives.WriteInt32LittleEndian(output, NoBaseline);
        snapshot.CopyTo(output[sizeof(int)..]);
        return rawLength;
    }

    public static int Decode(int baselineTick, ReadOnlySpan<byte> baseline, ReadOnlySpan<byte> packet, Span<byte> output)
    {
        if (packet.Length <= sizeof(int)) return 0;
        int reference = BinaryPrimitives.ReadInt32LittleEndian(packet);
        if (reference == NoBaseline)
        {
            var raw = packet[sizeof(int)..];
            if (raw.Length > output.Length) return 0;
            raw.CopyTo(output);
            return raw.Length;
        }
        if (reference < 0 || reference != baselineTick || packet.Length <= MaxHeaderBytes) return 0;
        int total = BinaryPrimitives.ReadInt32LittleEndian(packet[sizeof(int)..]);
        if (total <= 0 || total > output.Length) return 0;
        return Expand(packet[MaxHeaderBytes..], baseline, total, output) ? total : 0;
    }

    private static bool Matches(ReadOnlySpan<byte> baseline, ReadOnlySpan<byte> snapshot, int index) =>
        index < baseline.Length && baseline[index] == snapshot[index];

    private static bool Expand(ReadOnlySpan<byte> encoded, ReadOnlySpan<byte> baseline, int total, Span<byte> output)
    {
        int cursor = 0;
        while (!encoded.IsEmpty)
        {
            int control = encoded[0];
            int length = (control & 0x7f) + 1;
            encoded = encoded[1..];
            if (length > total - cursor) return false;
            bool copy = (control & 0x80) != 0;
            if (copy)
            {
                if (length > baseline.Length - cursor) return false;
                baseline.Slice(cursor, length).CopyTo(output[cursor..]);
            }
            else
            {
                if (length > encoded.Length) return false;
                encoded[..length].CopyTo(output[cursor..]);
                encoded = encoded[length..];
            }
            cursor += length;
        }
        return cursor == total;
    }
}
