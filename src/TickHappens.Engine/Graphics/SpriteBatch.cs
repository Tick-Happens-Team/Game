using System.Runtime.InteropServices;
using Silk.NET.OpenGL;
using TickHappens.Engine.Assets;

namespace TickHappens.Engine.Graphics;

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct SpriteInstance
{
    /// <summary>Top-left in target pixels (integer for pixel-perfect output).</summary>
    public float X, Y;
    public float W, H;
    /// <summary>Atlas texel rect.</summary>
    public float U, V, Uw, Vh;
    /// <summary>RGBA8 tint (0xAABBGGRR); alpha &lt; 255 is used as a flash/whiten amount, not transparency.</summary>
    public uint Tint;
    /// <summary>Bit 0 = flip X. Bits 8..15 = emission boost (0..255).</summary>
    public uint Flags;
}

/// <summary>
/// Instanced sprite renderer for one atlas (albedo + normal + emission share the layout).
/// Writes to the G-buffer (3 render targets) with binary alpha test, so no sorting by blend is needed;
/// callers submit in painter's order (ground, then actors sorted by Y).
/// </summary>
public sealed unsafe class SpriteBatch : IDisposable
{
    public const int MaxInstances = 1 << 16;

    private readonly GL _gl;
    private readonly ShaderProgram _shader;
    private readonly uint _vao, _quadVbo, _instVbo;
    private readonly SpriteInstance[] _items = new SpriteInstance[MaxInstances];
    private int _count;

    public int DrawCalls { get; private set; }
    public int Sprites { get; private set; }

    public SpriteBatch(GL gl, ShaderProgram shader)
    {
        _gl = gl;
        _shader = shader;
        _vao = gl.GenVertexArray();
        gl.BindVertexArray(_vao);

        _quadVbo = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _quadVbo);
        ReadOnlySpan<float> quad = [0, 0, 1, 0, 0, 1, 1, 1];
        fixed (float* p = quad) gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(quad.Length * 4), p, BufferUsageARB.StaticDraw);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 8, (void*)0);

        _instVbo = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _instVbo);
        uint stride = (uint)sizeof(SpriteInstance);
        gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(stride * MaxInstances), null, BufferUsageARB.StreamDraw);
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, false, stride, (void*)0);
        gl.VertexAttribDivisor(1, 1);
        gl.EnableVertexAttribArray(2);
        gl.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, stride, (void*)16);
        gl.VertexAttribDivisor(2, 1);
        gl.EnableVertexAttribArray(3);
        gl.VertexAttribPointer(3, 4, VertexAttribPointerType.UnsignedByte, true, stride, (void*)32);
        gl.VertexAttribDivisor(3, 1);
        gl.EnableVertexAttribArray(4);
        gl.VertexAttribIPointer(4, 1, VertexAttribIType.UnsignedInt, stride, (void*)36);
        gl.VertexAttribDivisor(4, 1);
        gl.BindVertexArray(0);
    }

    public void Begin()
    {
        _count = 0;
        DrawCalls = 0;
        Sprites = 0;
    }

    public void Draw(AtlasSprite s, int frame, float x, float y, bool flipX = false, uint tint = 0xFFFFFFFF, byte emissionBoost = 0)
    {
        if (_count == MaxInstances) throw new InvalidOperationException("sprite batch overflow; call Flush");
        var f = s.Frames[frame];
        _items[_count++] = new SpriteInstance
        {
            X = MathF.Floor(x - s.Origin[0]), Y = MathF.Floor(y - s.Origin[1]), W = s.W, H = s.H,
            U = f[0], V = f[1], Uw = s.W, Vh = s.H, Tint = tint, Flags = (flipX ? 1u : 0u) | ((uint)emissionBoost << 8),
        };
    }

    /// <summary>Draws the accumulated sprites into the currently bound G-buffer.</summary>
    public void Flush(Texture2D albedo, Texture2D normal, Texture2D emission, int targetW, int targetH)
    {
        if (_count == 0) return;
        _shader.Use();
        _shader.Set("uTarget", targetW, targetH);
        _shader.Set("uAtlasSize", albedo.Width, albedo.Height);
        albedo.Bind(0);
        normal.Bind(1);
        emission.Bind(2);
        _shader.Set("uAlbedo", 0);
        _shader.Set("uNormal", 1);
        _shader.Set("uEmission", 2);
        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _instVbo);
        fixed (SpriteInstance* p = _items)
            _gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, (nuint)(_count * sizeof(SpriteInstance)), p);
        _gl.DrawArraysInstanced(PrimitiveType.TriangleStrip, 0, 4, (uint)_count);
        _gl.BindVertexArray(0);
        DrawCalls++;
        Sprites += _count;
        _count = 0;
    }

    public void Dispose()
    {
        _gl.DeleteBuffer(_quadVbo);
        _gl.DeleteBuffer(_instVbo);
        _gl.DeleteVertexArray(_vao);
    }
}
