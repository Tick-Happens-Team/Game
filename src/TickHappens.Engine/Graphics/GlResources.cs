using Silk.NET.OpenGL;
using TickHappens.Engine.Assets;

namespace TickHappens.Engine.Graphics;

public sealed class ShaderProgram : IDisposable
{
    private readonly GL _gl;
    private readonly Dictionary<string, int> _locations = [];
    public uint Id { get; }

    private ShaderProgram(GL gl, uint id)
    {
        _gl = gl;
        Id = id;
    }

    public static ShaderProgram FromFiles(GL gl, string vertPath, string fragPath) =>
        Link(gl, Path.GetFileName(fragPath), Compile(gl, ShaderType.VertexShader, vertPath), Compile(gl, ShaderType.FragmentShader, fragPath));

    public static ShaderProgram Compute(GL gl, string path) => Link(gl, Path.GetFileName(path), Compile(gl, ShaderType.ComputeShader, path));

    private static uint Compile(GL gl, ShaderType type, string path)
    {
        uint s = gl.CreateShader(type);
        gl.ShaderSource(s, File.ReadAllText(path));
        gl.CompileShader(s);
        gl.GetShader(s, ShaderParameterName.CompileStatus, out int ok);
        if (ok == 0) throw new InvalidOperationException($"{path}: {gl.GetShaderInfoLog(s)}");
        return s;
    }

    private static ShaderProgram Link(GL gl, string name, params uint[] shaders)
    {
        uint p = gl.CreateProgram();
        foreach (var s in shaders) gl.AttachShader(p, s);
        gl.LinkProgram(p);
        gl.GetProgram(p, ProgramPropertyARB.LinkStatus, out int ok);
        if (ok == 0) throw new InvalidOperationException($"{name}: link failed: {gl.GetProgramInfoLog(p)}");
        foreach (var s in shaders) { gl.DetachShader(p, s); gl.DeleteShader(s); }
        return new ShaderProgram(gl, p);
    }

    public void Use() => _gl.UseProgram(Id);

    private int Loc(string name)
    {
        if (!_locations.TryGetValue(name, out int l)) _locations[name] = l = _gl.GetUniformLocation(Id, name);
        return l;
    }

    public void Set(string n, int v) => _gl.Uniform1(Loc(n), v);
    public void Set(string n, float v) => _gl.Uniform1(Loc(n), v);
    public void Set(string n, float x, float y) => _gl.Uniform2(Loc(n), x, y);
    public void Set(string n, float x, float y, float z) => _gl.Uniform3(Loc(n), x, y, z);
    public void Set(string n, float x, float y, float z, float w) => _gl.Uniform4(Loc(n), x, y, z, w);

    public unsafe void SetVec4Array(string n, ReadOnlySpan<float> data)
    {
        fixed (float* p = data) _gl.Uniform4(Loc(n), (uint)(data.Length / 4), p);
    }

    public void Dispose() => _gl.DeleteProgram(Id);
}

public sealed class Texture2D : IDisposable
{
    private readonly GL _gl;
    public uint Id { get; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public InternalFormat Format { get; }

    public unsafe Texture2D(GL gl, int w, int h, InternalFormat format = InternalFormat.Rgba8, bool linear = false, void* data = null)
    {
        _gl = gl;
        Format = format;
        Id = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, Id);
        int filter = (int)(linear ? TextureMinFilter.Linear : TextureMinFilter.Nearest);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, filter);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, filter);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        Allocate(w, h, data);
    }

    public static unsafe Texture2D FromImage(GL gl, RgbaImage img)
    {
        fixed (uint* p = img.Px) return new Texture2D(gl, img.W, img.H, InternalFormat.Rgba8, false, p);
    }

    private unsafe void Allocate(int w, int h, void* data)
    {
        Width = w;
        Height = h;
        bool half = Format == InternalFormat.Rgba16f;
        _gl.TexImage2D(TextureTarget.Texture2D, 0, Format, (uint)w, (uint)h, 0, PixelFormat.Rgba, half ? PixelType.HalfFloat : PixelType.UnsignedByte, data);
    }

    public unsafe void Resize(int w, int h)
    {
        if (w == Width && h == Height) return;
        _gl.BindTexture(TextureTarget.Texture2D, Id);
        Allocate(w, h, null);
    }

    public unsafe void Upload(uint[] rgba)
    {
        _gl.BindTexture(TextureTarget.Texture2D, Id);
        fixed (uint* p = rgba) _gl.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, (uint)Width, (uint)Height, PixelFormat.Rgba, PixelType.UnsignedByte, p);
    }

    public void Bind(int unit)
    {
        _gl.ActiveTexture(TextureUnit.Texture0 + unit);
        _gl.BindTexture(TextureTarget.Texture2D, Id);
    }

    public void Dispose() => _gl.DeleteTexture(Id);
}

/// <summary>Framebuffer with one or more color attachments of equal size.</summary>
public sealed unsafe class RenderTarget : IDisposable
{
    private readonly GL _gl;
    public uint Fbo { get; }
    public Texture2D[] Colors { get; }
    public int Width => Colors[0].Width;
    public int Height => Colors[0].Height;

    public RenderTarget(GL gl, int w, int h, params (InternalFormat Format, bool Linear)[] attachments)
    {
        _gl = gl;
        Colors = [.. attachments.Select(a => new Texture2D(gl, w, h, a.Format, a.Linear))];
        Fbo = gl.GenFramebuffer();
        Attach();
    }

    private unsafe void Attach()
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, Fbo);
        var bufs = stackalloc GLEnum[Colors.Length];
        for (int i = 0; i < Colors.Length; i++)
        {
            _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0 + i, TextureTarget.Texture2D, Colors[i].Id, 0);
            bufs[i] = GLEnum.ColorAttachment0 + i;
        }
        _gl.DrawBuffers((uint)Colors.Length, bufs);
        var status = _gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != GLEnum.FramebufferComplete) throw new InvalidOperationException("framebuffer incomplete: " + status);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    public void Resize(int w, int h)
    {
        if (w == Width && h == Height) return;
        foreach (var c in Colors) c.Resize(w, h);
        Attach();
    }

    public void Bind()
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, Fbo);
        _gl.Viewport(0, 0, (uint)Width, (uint)Height);
    }

    public void Dispose()
    {
        _gl.DeleteFramebuffer(Fbo);
        foreach (var c in Colors) c.Dispose();
    }
}
