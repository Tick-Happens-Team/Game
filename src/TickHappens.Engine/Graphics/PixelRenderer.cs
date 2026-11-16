using Silk.NET.OpenGL;
using TickHappens.Engine.Vfx;

namespace TickHappens.Engine.Graphics;

public struct PointLight
{
    /// <summary>World position in art pixels.</summary>
    public float X, Y;
    public float Radius;
    /// <summary>Linear color times intensity (HDR, may exceed 1).</summary>
    public float R, G, B;
    /// <summary>Height above the ground plane in pixels (lower = grazing, more relief).</summary>
    public float Height;
}

public sealed class RenderSettings
{
    public float AmbientR = 0.32f, AmbientG = 0.30f, AmbientB = 0.45f;
    /// <summary>Light quantization steps (0 = smooth). Pixel-art lighting uses 5-8 bands.</summary>
    public int LightBands = 6;
    public bool Lighting = true;
    public bool Bloom = true;
    public float BloomStrength = 0.85f;
    public float BloomThreshold = 1.0f;
    /// <summary>0 = lit scene, 1 = albedo, 2 = normals, 3 = emission, 4 = VFX light.</summary>
    public int DebugView;
    /// <summary>Opacity of cosmetic VFX color over actors (readability control for crowded screens).</summary>
    public float VfxOpacity = 1f;

    /// <summary>Directional key light (moon/sun) toward the light, z up; its color is added on top of ambient.</summary>
    public float SunX = -0.45f, SunY = -0.6f, SunZ = 0.65f, SunR, SunG, SunB;
    /// <summary>Drifting cloud shadows over the ground (0 = off, 1 = full darkening of key and ambient light).</summary>
    public float Clouds;
    /// <summary>Color grade after tonemapping: contrast and saturation around neutral, a lift of the blacks,
    /// split toning toward shadow / highlight tints, and vignette strength.</summary>
    public float Contrast = 1f, Saturation = 1f, Lift, Split, Vignette = 0.14f;
    public float ShadowTintR = 1, ShadowTintG = 1, ShadowTintB = 1, HighlightTintR = 1, HighlightTintG = 1, HighlightTintB = 1;
    /// <summary>Diorama blur toward the top and bottom of the screen, in art pixels (0 = off).</summary>
    public float TiltShift;
    /// <summary>Per-frame chromatic aberration in art pixels at the screen edge (0 = off).</summary>
    public float Aberration;
    /// <summary>Per-frame additive full-screen flash (linear color).</summary>
    public float FlashR, FlashG, FlashB;
}

/// <summary>
/// The 2D pipeline, all at a low "art" resolution with an integer upscale:
/// G-buffer (albedo, normal, emission) → quantized normal-mapped lighting + CPU pixel-VFX layer (HDR)
/// → GPU particles (additive) → bloom chain → final composite with sub-pixel camera offset, tonemap, vignette.
/// </summary>
public sealed unsafe class PixelRenderer : IDisposable
{
    public const int BaseW = 640, BaseH = 360, Margin = 2, MaxLights = 64, BloomLevels = 5;

    private readonly GL _gl;
    private readonly RenderTarget _gbuf, _lit;
    private readonly RenderTarget[] _bloom = new RenderTarget[BloomLevels];
    private readonly Texture2D _vfxColor, _vfxLight, _ui;
    private readonly ShaderProgram _light, _down, _up, _final;
    private readonly uint _emptyVao;
    private readonly float[] _lightData = new float[MaxLights * 12];

    public RenderSettings Settings { get; } = new();
    public int Scale { get; private set; } = 1;
    /// <summary>Render target size in art pixels (screen view + margin on each side).</summary>
    public int TargetW { get; private set; }
    public int TargetH { get; private set; }
    /// <summary>World position of the target's top-left pixel (integer).</summary>
    public int OriginX { get; private set; }
    public int OriginY { get; private set; }
    public PixelFrame VfxFrame { get; }
    /// <summary>Screen-space UI layer at art resolution (view size, no margin, no camera offset); cleared in BeginFrame, drawn last, unlit, no bloom.</summary>
    public PixelFrame UiFrame { get; }
    public int ViewW { get; private set; } = BaseW;
    public int ViewH { get; private set; } = BaseH;
    /// <summary>Smallest world view the integer scale may show (art pixels). Smaller values bring the camera closer.</summary>
    public int MinViewW { get; set; } = BaseW;
    public int MinViewH { get; set; } = BaseH;
    /// <summary>The UI keeps the 640×360 layout at its own integer scale, independent of the world zoom.</summary>
    public int UiScale { get; private set; } = 1;
    public int UiW { get; private set; } = BaseW;
    public int UiH { get; private set; } = BaseH;
    public RenderTarget GBuffer => _gbuf;
    public RenderTarget Lit => _lit;
    public int LightCount { get; private set; }

    private float _fracX, _fracY;
    private int _windowW, _windowH;

    private int _visionX, _visionY;
    private float _visionFacingX, _visionFacingY, _visionCos, _visionInvFade;
    private bool _visionEnabled;

    public PixelRenderer(GL gl, string shaderDir)
    {
        _gl = gl;
        string S(string f) => Path.Combine(shaderDir, f);
        _light = ShaderProgram.FromFiles(gl, S("fullscreen.vert"), S("light.frag"));
        _down = ShaderProgram.FromFiles(gl, S("fullscreen.vert"), S("bloom_down.frag"));
        _up = ShaderProgram.FromFiles(gl, S("fullscreen.vert"), S("bloom_up.frag"));
        _final = ShaderProgram.FromFiles(gl, S("fullscreen.vert"), S("final.frag"));
        _emptyVao = gl.GenVertexArray();

        TargetW = BaseW + Margin * 2;
        TargetH = BaseH + Margin * 2;
        _gbuf = new RenderTarget(gl, TargetW, TargetH, (InternalFormat.Rgba8, false), (InternalFormat.Rgba8, false), (InternalFormat.Rgba8, false));
        _lit = new RenderTarget(gl, TargetW, TargetH, (InternalFormat.Rgba16f, false));
        for (int i = 0; i < BloomLevels; i++)
            _bloom[i] = new RenderTarget(gl, Math.Max(1, TargetW >> (i + 1)), Math.Max(1, TargetH >> (i + 1)), (InternalFormat.Rgba16f, true));
        _vfxColor = new Texture2D(gl, TargetW, TargetH);
        _vfxLight = new Texture2D(gl, TargetW, TargetH);
        VfxFrame = new PixelFrame(TargetW, TargetH);
        _ui = new Texture2D(gl, BaseW, BaseH);
        UiFrame = new PixelFrame(BaseW, BaseH);
    }

    /// <summary>Chooses the integer scale and target size for the window and positions the camera (world px, view center).</summary>
    public void BeginFrame(int windowW, int windowH, float camCenterX, float camCenterY)
    {
        _windowW = windowW;
        _windowH = windowH;
        Scale = Math.Max(1, Math.Min(windowW / MinViewW, windowH / MinViewH));
        UiScale = Math.Max(1, Math.Min(windowW / BaseW, windowH / BaseH));
        int viewW = (windowW + Scale - 1) / Scale, viewH = (windowH + Scale - 1) / Scale;
        int tw = viewW + Margin * 2, th = viewH + Margin * 2;
        if (tw != TargetW || th != TargetH)
        {
            TargetW = tw;
            TargetH = th;
            _gbuf.Resize(tw, th);
            _lit.Resize(tw, th);
            for (int i = 0; i < BloomLevels; i++) _bloom[i].Resize(Math.Max(1, tw >> (i + 1)), Math.Max(1, th >> (i + 1)));
            _vfxColor.Resize(tw, th);
            _vfxLight.Resize(tw, th);
            VfxFrame.Resize(tw, th);
        }
        ViewW = viewW;
        ViewH = viewH;
        int uiW = (windowW + UiScale - 1) / UiScale, uiH = (windowH + UiScale - 1) / UiScale;
        if (uiW != UiW || uiH != UiH)
        {
            UiW = uiW;
            UiH = uiH;
            _ui.Resize(uiW, uiH);
            UiFrame.Resize(uiW, uiH);
        }
        float left = camCenterX - viewW / 2f, top = camCenterY - viewH / 2f;
        OriginX = (int)MathF.Floor(left) - Margin;
        OriginY = (int)MathF.Floor(top) - Margin;
        _fracX = left - MathF.Floor(left);
        _fracY = top - MathF.Floor(top);
        VfxFrame.Ox = OriginX;
        VfxFrame.Oy = OriginY;
        Array.Clear(UiFrame.Color);
        _visionEnabled = false;

        _gbuf.Bind();
        _gl.ClearColor(0, 0, 0, 0);
        _gl.Clear(ClearBufferMask.ColorBufferBit);
    }

    /// <summary>Uploads the CPU VFX layer (call after effects drew into <see cref="VfxFrame"/>).</summary>
    public void UploadVfx()
    {
        _vfxColor.Upload(VfxFrame.Color);
        _vfxLight.Upload(VfxFrame.Light);
    }

    /// <summary>Converts a world position to UI-layer pixels (for labels that follow world objects).</summary>
    public (int X, int Y) WorldToUi(float x, float y) =>
        ((int)MathF.Floor((x - OriginX - Margin - _fracX) * Scale / UiScale), (int)MathF.Floor((y - OriginY - Margin - _fracY) * Scale / UiScale));

    /// <summary>Dims the scene outside a view cone while keeping the immediate area and UI visible.</summary>
    public void SetVisionCone(float x, float y, float facingX, float facingY, float halfAngle)
    {
        (_visionX, _visionY) = WorldToUi(x, y);
        _visionFacingX = facingX;
        _visionFacingY = facingY;
        _visionCos = MathF.Cos(halfAngle);
        _visionInvFade = 1 / Math.Max(0.00001f,
            _visionCos - MathF.Cos(Math.Min(MathF.PI, halfAngle + MathF.PI / 18f)));
        _visionEnabled = true;
    }

    /// <summary>Lighting pass: writes the HDR lit scene. Lights are in world pixels.</summary>
    public void Light(ReadOnlySpan<PointLight> lights, float time)
    {
        LightCount = Math.Min(MaxLights, lights.Length);
        for (int i = 0; i < LightCount; i++)
        {
            var l = lights[i];
            int o = i * 12;
            _lightData[o] = l.X - OriginX; _lightData[o + 1] = l.Y - OriginY; _lightData[o + 2] = l.Radius; _lightData[o + 3] = l.Height;
            _lightData[o + 4] = l.R; _lightData[o + 5] = l.G; _lightData[o + 6] = l.B; _lightData[o + 7] = 0;
        }
        _lit.Bind();
        _light.Use();
        _gbuf.Colors[0].Bind(0);
        _gbuf.Colors[1].Bind(1);
        _gbuf.Colors[2].Bind(2);
        _vfxColor.Bind(3);
        _vfxLight.Bind(4);
        _light.Set("uAlbedo", 0);
        _light.Set("uNormal", 1);
        _light.Set("uEmission", 2);
        _light.Set("uVfxColor", 3);
        _light.Set("uVfxLight", 4);
        _light.Set("uAmbient", Settings.AmbientR, Settings.AmbientG, Settings.AmbientB);
        _light.Set("uBands", Settings.LightBands);
        _light.Set("uLighting", Settings.Lighting ? 1 : 0);
        _light.Set("uDebug", Settings.DebugView);
        _light.Set("uVfxOpacity", Settings.VfxOpacity);
        _light.Set("uTime", time);
        _light.Set("uLightCount", LightCount);
        _light.Set("uOrigin", OriginX, OriginY);
        _light.Set("uSunDir", Settings.SunX, Settings.SunY, Settings.SunZ);
        _light.Set("uSunColor", Settings.SunR, Settings.SunG, Settings.SunB);
        _light.Set("uClouds", Settings.Clouds);
        // Pack as vec4 pairs: [x, y, radius, height], [r, g, b, 0]
        Span<float> packed = stackalloc float[MaxLights * 8];
        for (int i = 0; i < LightCount; i++)
            for (int k = 0; k < 8; k++) packed[i * 8 + k] = _lightData[i * 12 + k];
        _light.SetVec4Array("uLights", packed[..Math.Max(8, LightCount * 8)]);
        DrawFullscreen();
    }

    /// <summary>Starts a multiply pass into the G-buffer albedo only (contact shadows under actors): draws keep
    /// normals and emission and darken the albedo by the fragment color. Call <see cref="EndAlbedoMultiply"/> after.</summary>
    public void BeginAlbedoMultiply()
    {
        _gbuf.Bind();
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(0, BlendingFactor.DstColor, BlendingFactor.Zero);
        _gl.ColorMask(1, false, false, false, false);
        _gl.ColorMask(2, false, false, false, false);
    }

    public void EndAlbedoMultiply()
    {
        _gl.ColorMask(1, true, true, true, true);
        _gl.ColorMask(2, true, true, true, true);
        _gl.BlendFunc(BlendingFactor.One, BlendingFactor.Zero);
        _gl.Disable(EnableCap.Blend);
    }

    /// <summary>Binds the HDR lit target for additive passes (GPU particles).</summary>
    public void BindLit() => _lit.Bind();

    public void Finish()
    {
        // Upload first: Texture2D.Upload binds to whatever unit is active, which must not clobber the final pass bindings.
        _ui.Upload(UiFrame.Color);
        // Bloom: progressive downsample with soft threshold on the first level, then additive upsample.
        if (Settings.Bloom)
        {
            _down.Use();
            _down.Set("uSrc", 0);
            for (int i = 0; i < BloomLevels; i++)
            {
                var src = i == 0 ? _lit.Colors[0] : _bloom[i - 1].Colors[0];
                _bloom[i].Bind();
                src.Bind(0);
                _down.Set("uTexel", 1f / src.Width, 1f / src.Height);
                _down.Set("uThreshold", i == 0 ? Settings.BloomThreshold : -1f);
                DrawFullscreen();
            }
            _up.Use();
            _up.Set("uSrc", 0);
            _gl.Enable(EnableCap.Blend);
            _gl.BlendFunc(BlendingFactor.One, BlendingFactor.One);
            for (int i = BloomLevels - 1; i > 0; i--)
            {
                _bloom[i - 1].Bind();
                _bloom[i].Colors[0].Bind(0);
                _up.Set("uTexel", 1f / _bloom[i].Width, 1f / _bloom[i].Height);
                DrawFullscreen();
            }
            _gl.Disable(EnableCap.Blend);
        }

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        _gl.Viewport(0, 0, (uint)_windowW, (uint)_windowH);
        _gl.ClearColor(0, 0, 0, 1);
        _gl.Clear(ClearBufferMask.ColorBufferBit);
        _final.Use();
        _lit.Colors[0].Bind(0);
        _bloom[0].Colors[0].Bind(1);
        _final.Set("uScene", 0);
        _final.Set("uBloom", 1);
        _ui.Bind(2);
        _final.Set("uUi", 2);
        _final.Set("uBloomStrength", Settings.Bloom ? Settings.BloomStrength : 0f);
        _final.Set("uScale", Scale);
        _final.Set("uWindow", _windowW, _windowH);
        _final.Set("uTarget", TargetW, TargetH);
        // Sub-pixel camera: shift the upscaled image by the fractional part (smooth scrolling, crisp pixels).
        _final.Set("uOffset", Margin + _fracX, Margin + _fracY);
        _final.Set("uRawView", Settings.DebugView);
        _final.Set("uUiScale", UiScale);
        _final.Set("uVisionScale", _visionEnabled ? UiScale / (float)Scale : 0f);
        if (_visionEnabled)
        {
            _final.Set("uVisionPose", _visionX, _visionY, _visionFacingX, _visionFacingY);
            _final.Set("uVisionEdge", _visionCos, _visionInvFade);
        }
        var g = Settings;
        _final.Set("uGrade", g.Contrast, g.Saturation, g.Lift, g.Split);
        _final.Set("uShadowTint", g.ShadowTintR, g.ShadowTintG, g.ShadowTintB);
        _final.Set("uHighlightTint", g.HighlightTintR, g.HighlightTintG, g.HighlightTintB);
        _final.Set("uVignette", g.Vignette);
        _final.Set("uTiltShift", g.TiltShift);
        _final.Set("uAberration", g.Aberration);
        _final.Set("uFlash", g.FlashR, g.FlashG, g.FlashB);
        DrawFullscreen();
    }

    private void DrawFullscreen()
    {
        _gl.BindVertexArray(_emptyVao);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
    }

    /// <summary>Reads the window back buffer as RGBA (top row first).</summary>
    public unsafe uint[] ReadBackBuffer(out int w, out int h)
    {
        w = _windowW;
        h = _windowH;
        var px = new uint[w * h];
        fixed (uint* p = px) _gl.ReadPixels(0, 0, (uint)w, (uint)h, PixelFormat.Rgba, PixelType.UnsignedByte, p);
        var flipped = new uint[w * h];
        for (int y = 0; y < h; y++) Array.Copy(px, (h - 1 - y) * w, flipped, y * w, w);
        for (int i = 0; i < flipped.Length; i++) flipped[i] |= 0xFF000000;
        return flipped;
    }

    public void Dispose()
    {
        _gbuf.Dispose();
        _lit.Dispose();
        foreach (var b in _bloom) b.Dispose();
        _vfxColor.Dispose();
        _vfxLight.Dispose();
        _ui.Dispose();
        _light.Dispose();
        _down.Dispose();
        _up.Dispose();
        _final.Dispose();
        _gl.DeleteVertexArray(_emptyVao);
    }
}
