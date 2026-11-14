using SDL3;
using Silk.NET.OpenGL;

namespace TickHappens.Engine.Platform;

/// <summary>SDL3 window with an OpenGL 4.3 core context, plus keyboard state.</summary>
public sealed class GameWindow : IDisposable
{
    public nint Handle { get; }
    public GL Gl { get; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public bool QuitRequested { get; private set; }

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
        SDL.GLSetSwapInterval(vsync ? 1 : 0);
        Gl = GL.GetApi(name => SDL.GLGetProcAddress(name));
        UpdateSize();
    }

    public void PumpEvents()
    {
        Array.Clear(_pressed);
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
    public void SetTitle(string title) => SDL.SetWindowTitle(Handle, title);
    public void Present() => SDL.GLSwapWindow(Handle);
    public void RequestQuit() => QuitRequested = true;

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
    A = 4, D = 7, E = 8, N = 17, P = 19, Q = 20, R = 21, S = 22, W = 26,
    D1 = 30, D2 = 31, D3 = 32, D4 = 33, D5 = 34, D6 = 35,
    Escape = 41, Tab = 43, Space = 44,
    F1 = 58, F2 = 59, F3 = 60, F4 = 61, F5 = 62, F12 = 69,
    Right = 79, Left = 80, Down = 81, Up = 82,
}
