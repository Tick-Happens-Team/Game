using System.Runtime.InteropServices;
using Silk.NET.OpenGL;

namespace TickHappens.Engine.Graphics;

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct GpuParticle
{
    public float X, Y, Vx, Vy;
    public float Life, MaxLife, Drag, Gravity;
    /// <summary>Start / end colors, RGBA8 (0xAABBGGRR). Intensity is applied in the shader (HDR).</summary>
    public uint ColorA, ColorB;
    public float Intensity;
    /// <summary>0 = spark (velocity streak), 1 = mote (1px with flicker).</summary>
    public uint Kind;
}

/// <summary>
/// Cosmetic GPU particles: a ring buffer in an SSBO, simulated by a compute shader and drawn as
/// pixel-sized points/streaks additively into the HDR scene. Never networked; spawn calls are
/// deterministic per seed so clients can reproduce them from gameplay events.
/// </summary>
public sealed unsafe class GpuParticles : IDisposable
{
    private readonly GL _gl;
    private readonly ShaderProgram _sim, _draw;
    private readonly uint _ssbo, _vao;
    private readonly GpuParticle[] _pending = new GpuParticle[8192];
    private int _pendingCount, _cursor;

    public int Capacity { get; }

    public GpuParticles(GL gl, ShaderProgram sim, ShaderProgram draw, int capacity = 1 << 17)
    {
        _gl = gl;
        _sim = sim;
        _draw = draw;
        Capacity = capacity;
        _ssbo = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, _ssbo);
        var zero = new GpuParticle[capacity];
        fixed (GpuParticle* p = zero)
            gl.BufferData(BufferTargetARB.ShaderStorageBuffer, (nuint)(capacity * sizeof(GpuParticle)), p, BufferUsageARB.DynamicDraw);
        _vao = gl.GenVertexArray();
    }

    public void Spawn(in GpuParticle p)
    {
        if (_pendingCount == _pending.Length) return; // budget exceeded this frame: drop (cosmetic only)
        _pending[_pendingCount++] = p;
    }

    public void Update(float dt)
    {
        _gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, _ssbo);
        // Upload this frame's spawns into the ring (split when wrapping).
        int done = 0;
        while (done < _pendingCount)
        {
            int n = Math.Min(_pendingCount - done, Capacity - _cursor);
            fixed (GpuParticle* p = &_pending[done])
                _gl.BufferSubData(BufferTargetARB.ShaderStorageBuffer, _cursor * sizeof(GpuParticle), (nuint)(n * sizeof(GpuParticle)), p);
            _cursor = (_cursor + n) % Capacity;
            done += n;
        }
        _pendingCount = 0;

        _sim.Use();
        _sim.Set("uDt", dt);
        _sim.Set("uCount", Capacity);
        _gl.BindBufferBase(BufferTargetARB.ShaderStorageBuffer, 0, _ssbo);
        _gl.DispatchCompute((uint)((Capacity + 255) / 256), 1, 1);
        _gl.MemoryBarrier(MemoryBarrierMask.ShaderStorageBarrierBit);
    }

    /// <summary>Draws into the bound HDR target. Camera is the target's top-left in world pixels.</summary>
    public void Draw(float camX, float camY, int targetW, int targetH, float time)
    {
        _draw.Use();
        _draw.Set("uCam", camX, camY);
        _draw.Set("uTarget", targetW, targetH);
        _draw.Set("uTime", time);
        _gl.BindBufferBase(BufferTargetARB.ShaderStorageBuffer, 0, _ssbo);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.One, BlendingFactor.One);
        _gl.BindVertexArray(_vao);
        // 2 vertices per particle: GL_LINES give 1px streaks for fast sparks and dots for motes.
        _gl.DrawArrays(PrimitiveType.Lines, 0, (uint)(Capacity * 2));
        _gl.BindVertexArray(0);
        _gl.Disable(EnableCap.Blend);
    }

    public void Dispose()
    {
        _gl.DeleteBuffer(_ssbo);
        _gl.DeleteVertexArray(_vao);
    }
}
