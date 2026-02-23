namespace ButterBror.Core.Interfaces;

/// <summary>
/// Provides localized strings with fallback support
/// </summary>
public interface ILocalizationService
{
    /// <summary>
    /// Gets a localized string by key
    /// </summary>
    /// <param name="key">Translation key in dot notation, e.g. "commands.userinfo.not_found"</param>
    /// <param name="locale">Locale code, e.g. "EN_US" (case-insensitive)</param>
    /// <param name="args">Optional arguments for string.Format</param>
    /// <returns>Localized and formatted string, or fallback if not found</returns>
    Task<string> GetStringAsync(
        string key, 
        string locale, 
        params object[] args);

    /// <summary>
    /// Reloads all translation files from disk
    /// </summary>
    Task ReloadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a locale is registered
    /// </summary>
    bool IsLocaleRegistered(string locale);

    /// <summary>
    /// Resolves locale alias to canonical code
    /// </summary>
    string ResolveLocale(string locale);
}