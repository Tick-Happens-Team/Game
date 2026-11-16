using System.Globalization;
using System.Text.Json;

namespace TickHappens.Localization;

public static class Localizer
{
    private const string DefaultLanguage = "en";
    private static readonly Dictionary<string, string> English = Load(DefaultLanguage);
    private static readonly Dictionary<string, string> Russian = Load("ru");
    private static readonly string Settings = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TickHappens", "language.txt");

    public static IReadOnlyList<string> Languages { get; } = Array.AsReadOnly(new[] { "en", "ru" });
    public static string Language { get; private set; } = DefaultLanguage;
    private static CultureInfo Culture => CultureInfo.GetCultureInfo(Language);

    public static void Initialize()
    {
        string? saved = null;
        try { if (File.Exists(Settings)) saved = File.ReadAllText(Settings).Trim(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        string? requested = Environment.GetEnvironmentVariable("TICKHAPPENS_LANGUAGE");
        Language = ResolveLanguage(Languages.Contains(requested) ? requested : saved,
            CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
    }

    public static string ResolveLanguage(string? preference, string systemLanguage) =>
        Languages.Contains(preference) ? preference! : Languages.Contains(systemLanguage) ? systemLanguage : DefaultLanguage;

    public static bool Select(string language)
    {
        if (!Languages.Contains(language)) throw new ArgumentOutOfRangeException(nameof(language));
        Language = language;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Settings)!);
            File.WriteAllText(Settings, language);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    public static string Get(string key) => Find(key) ?? throw new KeyNotFoundException(key);
    public static string Format(string key, params object[] values) => string.Format(Culture, Get(key), values);
    public static string Number(float value, string format) => value.ToString(format, Culture);
    public static string Content(string type, string id, string field, string fallback) =>
        Find($"content.{type}.{id}.{field}") ?? fallback;

    private static string? Find(string key) =>
        Language == "ru" && Russian.TryGetValue(key, out var localized) ? localized : English.GetValueOrDefault(key);

    private static Dictionary<string, string> Load(string language)
    {
        using var stream = typeof(Localizer).Assembly.GetManifestResourceStream(language + ".json")
            ?? throw new InvalidDataException(language);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? throw new InvalidDataException(language);
    }
}
