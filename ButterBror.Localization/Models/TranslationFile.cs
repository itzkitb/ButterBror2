namespace ButterBror.Localization.Models;

/// <summary>
/// Structure of a translation file
/// </summary>
public class TranslationFile
{
    public TranslationMeta Meta { get; set; } = new();
    public Dictionary<string, string> Strings { get; set; } = new();
}

public class TranslationMeta
{
    public string Locale { get; set; } = string.Empty;
}