#if TICKHAPPENS_EOS
using System.Text.Json;
using TickHappens.Localization;

namespace TickHappens.Game.Online;

internal static class LocalEosConfig
{
    public static EosConfig Load()
    {
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TickHappens");
        // A developer override wins; release packages carry the product's client configuration as eos.json.
        string? path = Environment.GetEnvironmentVariable("TICKHAPPENS_EOS_CONFIG");
        if (path is null)
        {
            path = Path.Combine(root, "eos.local.json");
            if (!File.Exists(path)) path = Path.Combine(AppContext.BaseDirectory, "eos.json");
        }
        if (!File.Exists(path)) throw new LocalizedException("online.missingConfig");

        using var json = JsonDocument.Parse(File.ReadAllText(path));
        string Read(string property) =>
            json.RootElement.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(value.GetString())
                ? value.GetString()!.Trim()
                : throw new InvalidDataException($"EOS configuration field {property} is required.");

        string cache = Environment.GetEnvironmentVariable("TICKHAPPENS_EOS_CACHE") ?? Path.Combine(root, "eos-cache");
        Directory.CreateDirectory(cache);
        return new EosConfig(Read("ProductId"), Read("SandboxId"), Read("DeploymentId"), Read("ClientId"), Read("ClientSecret"), cache);
    }

    public static bool ForceRelays => Environment.GetEnvironmentVariable("TICKHAPPENS_EOS_FORCE_RELAY") == "1";
}
#endif
