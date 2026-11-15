using SDL3;
using Silk.NET.OpenGL;
using System.Runtime.InteropServices;

namespace TickHappens.Engine.Platform;

/// <summary>SDL3 window with an OpenGL 4.3 core context, plus keyboard state.</summary>
public sealed class GameWindow : IDisposable
{
    public nint Handle { get; }
    public GL Gl { get; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public bool QuitRequested { get; private set; }
    public (float X, float Y)? LeftClick { get; private set; }
    public string TextInput { get; private set; } = "";
    public bool Vsync { get; private set; }

    private readonly nint _context;
    private readonly bool[] _down = new bool[512];
    private readonly bool[] _pressed = new bool[512];

    public GameWindow(string title, int width, int height, bool vsync)
    {
        if (!SDL.Init(SDL.InitFlags.Video | SDL.InitFlags.Gamepad))
            throw new InvalidOperationException("SDL_Init failed: " + SDL.GetError());
        SDL.GLSetAttribute(SDL.GLAttr.ContextMajorVersion, 4);
        SDL.GLSetAttribute(SDL.GLAttr.ContextMinorVersion, 3);
        SDL.GLSetAttribute(SDL.GLAttr.ContextProfileMask, (int)SDL.GLProfile.Core);
        SDL.GLSetAttribute(SDL.GLAttr.DoubleBuffer, 1);
        SDL.GLSetAttribute(SDL.GLAttr.DepthSize, 0);

        Handle = SDL.CreateWindow(title, width, height, SDL.WindowFlags.OpenGL | SDL.WindowFlags.Resizable | SDL.WindowFlags.HighPixelDensity);
        if (Handle == 0) throw new InvalidOperationException("SDL_CreateWindow failed: " + SDL.GetError());
        _context = SDL.GLCreateContext(Handle);
        if (_context == 0) throw new InvalidOperationException("OpenGL 4.3 core context unavailable: " + SDL.GetError());
        SDL.GLMakeCurrent(Handle, _context);
        SetVsync(vsync);
        Gl = GL.GetApi(name => SDL.GLGetProcAddress(name));
        UpdateSize();
    }

    public void PumpEvents()
    {
        Array.Clear(_pressed);
        LeftClick = null;
        TextInput = "";
        while (SDL.PollEvent(out var e))
        {
            switch ((SDL.EventType)e.Type)
            {
                case SDL.EventType.Quit:
                case SDL.EventType.WindowCloseRequested:
                    QuitRequested = true;
                    break;
                case SDL.EventType.KeyDown:
                {
                    int sc = (int)e.Key.Scancode;
                    if (sc is >= 0 and < 512)
                    {
                        if (!e.Key.Repeat) _pressed[sc] = true;
                        _down[sc] = true;
                    }
                    break;
                }
                case SDL.EventType.KeyUp:
                {
                    int sc = (int)e.Key.Scancode;
                    if (sc is >= 0 and < 512) _down[sc] = false;
                    break;
                }
                case SDL.EventType.MouseButtonDown when e.Button.Button == 1:
                    SDL.GetWindowSize(Handle, out int w, out int h);
                    LeftClick = (e.Button.X / Math.Max(w, 1), e.Button.Y / Math.Max(h, 1));
                    break;
                case SDL.EventType.TextInput:
                    TextInput += Marshal.PtrToStringUTF8(e.Text.Text);
                    break;
            }
        }
        UpdateSize();
    }

    private void UpdateSize()
    {
        SDL.GetWindowSizeInPixels(Handle, out int w, out int h);
        Width = Math.Max(1, w);
        Height = Math.Max(1, h);
    }

    public bool IsDown(Key k) => _down[(int)k];
    public bool WasPressed(Key k) => _pressed[(int)k];
    public char PressedCodeCharacter()
    {
        for (int i = 4; i <= 29; i++) if (_pressed[i]) return (char)('A' + i - 4);
        for (int i = 30; i <= 39; i++) if (_pressed[i]) return "1234567890"[i - 30];
        return '\0';
    }
    public void SetTitle(string title) => SDL.SetWindowTitle(Handle, title);
    public void Present() => SDL.GLSwapWindow(Handle);
    public void RequestQuit() => QuitRequested = true;
    public void SetTextInput(bool enabled)
    {
        if (enabled) SDL.StartTextInput(Handle);
        else SDL.StopTextInput(Handle);
    }

    public void SetVsync(bool enabled)
    {
        if (SDL.GLSetSwapInterval(enabled ? 1 : 0)) Vsync = enabled;
    }

    public void Dispose()
    {
        SDL.GLDestroyContext(_context);
        SDL.DestroyWindow(Handle);
        SDL.Quit();
    }
}

/// <summary>USB HID scancodes (layout-independent physical keys).</summary>
public enum Key
{
    A = 4, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
    D1 = 30, D2 = 31, D3 = 32, D4 = 33, D5 = 34, D6 = 35, D7 = 36, D8 = 37, D9 = 38, D0 = 39,
    Return = 40, Escape = 41, Backspace = 42, Tab = 43, Space = 44,
    Semicolon = 51, Period = 55,
    F1 = 58, F2 = 59, F3 = 60, F4 = 61, F5 = 62, F6 = 63, F7 = 64, F12 = 69,
    Right = 79, Left = 80, Down = 81, Up = 82,
    LeftCtrl = 224, LeftShift, RightCtrl = 228, RightShift,
}
