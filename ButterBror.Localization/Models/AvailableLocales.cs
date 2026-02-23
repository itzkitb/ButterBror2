namespace ButterBror.Localization.Models;

/// <summary>
/// Registry of all available locales and their configuration
/// </summary>
public class AvailableLocales
{
    public string DefaultLocale { get; set; } = "EN_US";
    public Dictionary<string, LocaleMetadata> Locales { get; set; } = new();
}