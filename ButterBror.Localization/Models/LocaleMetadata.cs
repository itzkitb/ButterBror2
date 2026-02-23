namespace ButterBror.Localization.Models;

/// <summary>
/// Metadata for a single locale configuration
/// </summary>
public class LocaleMetadata
{
    public List<string> Aliases { get; set; } = new();
    public string FilePath { get; set; } = string.Empty;
}