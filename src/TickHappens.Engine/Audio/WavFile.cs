namespace TickHappens.Engine.Audio;

/// <summary>A decoded sound: interleaved float samples in -1..1.</summary>
public sealed class AudioClip(string name, float[] samples, int channels, int rate)
{
    public string Name { get; } = name;
    public float[] Samples { get; } = samples;
    public int Channels { get; } = channels;
    public int Rate { get; } = rate;
    public int Frames => Samples.Length / Channels;
}

/// <summary>Reads and writes 16-bit PCM RIFF/WAVE files (the format AssetTool writes into Assets).</summary>
public static class WavFile
{
    public static void Write(string path, ReadOnlySpan<float> interleaved, int channels, int rate)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var w = new BinaryWriter(File.Create(path));
        int data = interleaved.Length * 2;
        w.Write("RIFF"u8);
        w.Write(36 + data);
        w.Write("WAVE"u8);
        w.Write("fmt "u8);
        w.Write(16);
        w.Write((short)1);
        w.Write((short)channels);
        w.Write(rate);
        w.Write(rate * channels * 2);
        w.Write((short)(channels * 2));
        w.Write((short)16);
        w.Write("data"u8);
        w.Write(data);
        foreach (float x in interleaved)
            w.Write((short)Math.Clamp(MathF.Round(x * 32767f), -32768f, 32767f));
    }

    public static AudioClip Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 12 || bytes[0] != 'R' || bytes[1] != 'I' || bytes[2] != 'F' || bytes[3] != 'F' || bytes[8] != 'W')
            throw new InvalidDataException($"{path}: not a RIFF/WAVE file");
        int channels = 0, rate = 0, bits = 0, format = 0, pos = 12;
        while (pos + 8 <= bytes.Length)
        {
            string id = System.Text.Encoding.ASCII.GetString(bytes, pos, 4);
            int size = BitConverter.ToInt32(bytes, pos + 4);
            int body = pos + 8;
            if (size < 0 || body + size > bytes.Length) throw new InvalidDataException($"{path}: truncated chunk '{id}'");
            if (id == "fmt ")
            {
                format = BitConverter.ToInt16(bytes, body);
                channels = BitConverter.ToInt16(bytes, body + 2);
                rate = BitConverter.ToInt32(bytes, body + 4);
                bits = BitConverter.ToInt16(bytes, body + 14);
            }
            else if (id == "data")
            {
                if (format != 1 || bits != 16 || channels is < 1 or > 2)
                    throw new InvalidDataException($"{path}: expected 16-bit PCM mono or stereo");
                var samples = new float[size / 2];
                for (int i = 0; i < samples.Length; i++) samples[i] = BitConverter.ToInt16(bytes, body + i * 2) / 32768f;
                return new AudioClip(Path.GetFileNameWithoutExtension(path), samples, channels, rate);
            }
            pos = body + size + (size & 1);
        }
        throw new InvalidDataException($"{path}: no data chunk");
    }
}
