using StbImageSharp;
using StbImageWriteSharp;

namespace TickHappens.Engine.Assets;

/// <summary>CPU-side RGBA8 image. Pixels are packed 0xAABBGGRR (RGBA byte order on little-endian).</summary>
public sealed class RgbaImage
{
    public int W { get; }
    public int H { get; }
    public uint[] Px { get; }

    public RgbaImage(int w, int h)
    {
        W = w;
        H = h;
        Px = new uint[w * h];
    }

    public RgbaImage(int w, int h, uint[] px)
    {
        if (px.Length != w * h) throw new ArgumentException("pixel count mismatch");
        W = w;
        H = h;
        Px = px;
    }

    public uint this[int x, int y]
    {
        get => Px[y * W + x];
        set => Px[y * W + x] = value;
    }

    public static RgbaImage Load(string path)
    {
        using var s = File.OpenRead(path);
        var img = ImageResult.FromStream(s, StbImageSharp.ColorComponents.RedGreenBlueAlpha);
        var px = new uint[img.Width * img.Height];
        Buffer.BlockCopy(img.Data, 0, px, 0, img.Data.Length);
        return new RgbaImage(img.Width, img.Height, px);
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var bytes = new byte[Px.Length * 4];
        Buffer.BlockCopy(Px, 0, bytes, 0, bytes.Length);
        using var s = File.Create(path);
        new ImageWriter().WritePng(bytes, W, H, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, s);
    }

    public void Blit(RgbaImage src, int dx, int dy)
    {
        for (int y = 0; y < src.H; y++)
            Array.Copy(src.Px, y * src.W, Px, (dy + y) * W + dx, src.W);
    }

    public RgbaImage Crop(int x0, int y0, int w, int h)
    {
        var r = new RgbaImage(w, h);
        for (int y = 0; y < h; y++)
            Array.Copy(Px, (y0 + y) * W + x0, r.Px, y * w, w);
        return r;
    }

    public static byte A(uint c) => (byte)(c >> 24);
    public static uint Rgb(uint c) => c & 0x00FFFFFF;
    public static uint Pack(int r, int g, int b, int a = 255) => ((uint)a << 24) | ((uint)b << 16) | ((uint)g << 8) | (uint)r;
}
