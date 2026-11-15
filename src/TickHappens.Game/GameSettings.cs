using System.Text.Json;
using TickHappens.Engine.Platform;
using TickHappens.Game.Sim;
using TickHappens.Localization;

namespace TickHappens.Game;

internal enum RunAction { Up, Down, Left, Right, Dash, Chest1, Chest2, Chest3 }

internal sealed class GameSettings
{
    private static readonly string Pathname = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TickHappens", "game-settings.json");
    private static readonly Key[] DefaultKeys = [Key.W, Key.S, Key.A, Key.D, Key.Space, Key.D1, Key.D2, Key.D3];
    private static readonly Key[] Arrows = [Key.Up, Key.Down, Key.Left, Key.Right];
    internal static readonly Key[] BindableKeys = Enum.GetValues<Key>().Where(key =>
        key is >= Key.A and <= Key.Z and not Key.P
            or >= Key.D1 and <= Key.D0 or Key.Space or Key.Backspace or Key.Semicolon or Key.Period
            or >= Key.Right and <= Key.Up or Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift).ToArray();

    public int Master { get; set; } = 100;
    public int Music { get; set; } = 100;
    public int Effects { get; set; } = 100;
    public Key[] Keys { get; set; } = [.. DefaultKeys];

    public static GameSettings Load()
    {
        try
        {
            using var file = File.OpenRead(Pathname);
            var settings = JsonSerializer.Deserialize<GameSettings>(file) ?? new GameSettings();
            settings.Master = Math.Clamp(settings.Master, 0, 100);
            settings.Music = Math.Clamp(settings.Music, 0, 100);
            settings.Effects = Math.Clamp(settings.Effects, 0, 100);
            if (settings.Keys?.Length != DefaultKeys.Length || settings.Keys.Distinct().Count() != DefaultKeys.Length ||
                settings.Keys.Any(key => !BindableKeys.Contains(key)))
                settings.ResetKeys();
            return settings;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return new GameSettings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Pathname)!);
            string temp = Pathname + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, JsonSerializer.Serialize(this));
                File.Move(temp, Pathname, overwrite: true);
            }
            finally { File.Delete(temp); }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(Localizer.Get("settings.saveError"));
        }
    }

    public void Bind(int action, Key key)
    {
        int previous = Array.IndexOf(Keys, key);
        if (previous >= 0) Keys[previous] = Keys[action];
        Keys[action] = key;
    }

    public void ResetKeys() => Keys = [.. DefaultKeys];

    public RunInput ReadInput(GameWindow window)
    {
        int choice = window.WasPressed(Keys[(int)RunAction.Chest1]) ? 1 :
            window.WasPressed(Keys[(int)RunAction.Chest2]) ? 2 :
            window.WasPressed(Keys[(int)RunAction.Chest3]) ? 3 : 0;
        var dash = Keys[(int)RunAction.Dash];
        return new RunInput((Held(RunAction.Right) ? 1 : 0) - (Held(RunAction.Left) ? 1 : 0),
            (Held(RunAction.Down) ? 1 : 0) - (Held(RunAction.Up) ? 1 : 0),
            window.IsDown(dash) || window.WasPressed(dash), choice);

        bool Held(RunAction action)
        {
            int i = (int)action;
            return window.IsDown(Keys[i]) ||
                (Keys[i] == DefaultKeys[i] && window.IsDown(Arrows[i]) && Array.IndexOf(Keys, Arrows[i]) < 0);
        }
    }

    public static string KeyLabel(Key key) => key switch
    {
        >= Key.D1 and <= Key.D0 => "1234567890"[key - Key.D1].ToString(),
        Key.Space => Localizer.Get("settings.keySpace"),
        Key.LeftShift => "L SHIFT",
        Key.RightShift => "R SHIFT",
        Key.LeftCtrl => "L CTRL",
        Key.RightCtrl => "R CTRL",
        Key.Semicolon => ";",
        Key.Period => ".",
        _ => key.ToString().ToUpperInvariant(),
    };
}
