namespace ButterBror.Core.Interfaces;

/// <summary>
/// Provides localized strings with fallback support
/// </summary>
public interface ILocalizationService
{
    /// <summary>
    /// Initialize service
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <returns></returns>
    Task InitializeAsync(CancellationToken ct = default);
    
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
    string? ResolveLocale(string locale, bool fixNull = true);

    /// <summary>
    /// Registers built-in default translations for a module
    /// </summary>
    /// <param name="moduleId">Unique identifier of the module</param>
    /// <param name="translations">
    /// Dictionary: locale code -> (translation key -> translation value)
    /// </param>
    void RegisterModuleTranslations(
        string moduleId,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> translations);
}