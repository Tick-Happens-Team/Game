using Silk.NET.OpenGL;
using TickHappens.Engine.Graphics;
using TickHappens.Engine.Vfx;

namespace TickHappens.Launcher;

/// <summary>
/// The launcher's 640×360 CPU pixel layer, shown at the largest integer scale that fits the window and
/// letterboxed with the backdrop color. The launcher ships without the game's shader directory, so the
/// tiny blit program is embedded here.
/// </summary>
internal sealed unsafe class Screen : IDisposable
{
    public const int W = 640, H = 360;
    public const uint Backdrop = 0xFF1A0F14;

    private const string Vertex = """
        #version 430 core
        out vec2 vUv;
        void main()
        {
            vec2 p = vec2((gl_VertexID << 1) & 2, gl_VertexID & 2);
            // Canvas row 0 is the top of the window.
            vUv = vec2(p.x, 1.0 - p.y);
            gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
        }
        """;

    private const string Fragment = """
        #version 430 core
        in vec2 vUv;
        out vec4 color;
        uniform sampler2D uUi;
        void main() { color = vec4(texture(uUi, vUv).rgb, 1.0); }
        """;

    private readonly GL _gl;
    private readonly ShaderProgram _program;
    private readonly Texture2D _texture;
    private readonly uint _vao;

    public PixelFrame Frame { get; } = new(W, H);
    public PixelCanvas Canvas => PixelCanvas.ColorOf(Frame);
    public int Scale { get; private set; } = 1;
    public int OffsetX { get; private set; }
    public int OffsetY { get; private set; }

    public Screen(GL gl)
    {
        _gl = gl;
        _program = ShaderProgram.FromSource(gl, "launcher-blit", Vertex, Fragment);
        _texture = new Texture2D(gl, W, H);
        _vao = gl.GenVertexArray();
    }

    /// <summary>Converts a window position normalized to 0..1 into screen pixels.</summary>
    public (int X, int Y) ToScreen((float X, float Y) point, int windowW, int windowH) =>
        ((int)MathF.Floor((point.X * windowW - OffsetX) / Scale), (int)MathF.Floor((point.Y * windowH - OffsetY) / Scale));

    public void Layout(int windowW, int windowH)
    {
        Scale = Math.Max(1, Math.Min(windowW / W, windowH / H));
        OffsetX = (windowW - W * Scale) / 2;
        OffsetY = (windowH - H * Scale) / 2;
    }

    public void Present(int windowW, int windowH)
    {
        Layout(windowW, windowH);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        _gl.Viewport(0, 0, (uint)windowW, (uint)windowH);
        _gl.ClearColor((Backdrop & 255) / 255f, ((Backdrop >> 8) & 255) / 255f, ((Backdrop >> 16) & 255) / 255f, 1);
        _gl.Clear(ClearBufferMask.ColorBufferBit);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _texture.Upload(Frame.Color);
        // GL viewports start at the bottom-left corner.
        _gl.Viewport(OffsetX, windowH - OffsetY - H * Scale, (uint)(W * Scale), (uint)(H * Scale));
        _program.Use();
        _program.Set("uUi", 0);
        _texture.Bind(0);
        _gl.Disable(EnableCap.Blend);
        _gl.BindVertexArray(_vao);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
    }

    public void Dispose()
    {
        _gl.DeleteVertexArray(_vao);
        _texture.Dispose();
        _program.Dispose();
    }
}
