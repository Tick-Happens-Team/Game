using Silk.NET.OpenGL;
using TickHappens.Engine.Graphics;
using TickHappens.Engine.Vfx;

namespace TickHappens.Launcher;

/// <summary>
/// The launcher's 640×360 CPU pixel layer, scaled to the largest size that fits the window with its aspect
/// ratio kept and letterboxed with the backdrop color. Fractional scales use sharp bilinear filtering: every
/// pixel stays a solid block and only the one-output-pixel seams between blocks blend, so a maximized
/// window fills the screen without uneven pixel widths. The launcher ships without the game's shader
/// directory, so the tiny blit program is embedded here.
/// </summary>
internal sealed unsafe class Screen : IDisposable
{
    public const int W = 640, H = 360;
    public const uint Backdrop = 0xFFF0F5F7;

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
        uniform vec2 uSize;
        uniform float uScale;
        void main()
        {
            vec2 texel = vUv * uSize;
            vec2 offset = fract(texel) - 0.5;
            vec2 region = vec2(max(0.0, 0.5 - 0.5 / uScale));
            vec2 sharp = floor(texel) + 0.5 + (offset - clamp(offset, -region, region)) * uScale;
            color = vec4(texture(uUi, sharp / uSize).rgb, 1.0);
        }
        """;

    private readonly GL _gl;
    private readonly ShaderProgram _program;
    private readonly Texture2D _texture;
    private readonly uint _vao;

    public PixelFrame Frame { get; } = new(W, H);
    public PixelCanvas Canvas => PixelCanvas.ColorOf(Frame);
    public float Scale { get; private set; } = 1;
    public int OffsetX { get; private set; }
    public int OffsetY { get; private set; }
    private int ScaledW => (int)MathF.Round(W * Scale);
    private int ScaledH => (int)MathF.Round(H * Scale);

    public Screen(GL gl)
    {
        _gl = gl;
        _program = ShaderProgram.FromSource(gl, "launcher-blit", Vertex, Fragment);
        _texture = new Texture2D(gl, W, H, linear: true);
        _vao = gl.GenVertexArray();
    }

    /// <summary>Converts a window position normalized to 0..1 into screen pixels.</summary>
    public (int X, int Y) ToScreen((float X, float Y) point, int windowW, int windowH) =>
        ((int)MathF.Floor((point.X * windowW - OffsetX) / Scale), (int)MathF.Floor((point.Y * windowH - OffsetY) / Scale));

    public void Layout(int windowW, int windowH)
    {
        Scale = Math.Max(0.25f, Math.Min(windowW / (float)W, windowH / (float)H));
        OffsetX = (windowW - ScaledW) / 2;
        OffsetY = (windowH - ScaledH) / 2;
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
        _gl.Viewport(OffsetX, windowH - OffsetY - ScaledH, (uint)ScaledW, (uint)ScaledH);
        _program.Use();
        _program.Set("uUi", 0);
        _program.Set("uSize", W, H);
        _program.Set("uScale", Scale);
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
