namespace TickHappens.Localization;

public sealed class LocalizedException(string key) : InvalidOperationException(key)
{
    public string Key { get; } = key;
}
