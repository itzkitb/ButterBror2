
namespace ButterBror.Core.Interfaces;

/// <summary>
/// Service for text formating
/// </summary>
public interface IFormatterService
{
    /// <summary>
    /// Formating TimeSpan to string with localization
    /// </summary>
    /// <param name="ts">Time interval for formatting</param>
    /// <param name="locale">Locale for formatting (example EN_US)</param>
    /// <returns>Localized text</returns>
    Task<string> FormatTimeSpanAsync(TimeSpan ts, string locale);

    /// <summary>
    /// Formating UTC DateTime to string with localization
    /// </summary>
    /// <param name="utcDate">Date & time in UTC</param>
    /// <param name="locale">Locale for formatting (example EN_US)</param>
    /// <returns>Localized text</returns>
    Task<string> FormatUtcDateAsync(DateTime utcDate, string locale);

    /// <summary>
    /// Formating local DateTime to string with localization
    /// </summary>
    /// <param name="localDate">Date & time</param>
    /// <param name="locale">Locale for formatting (example EN_US)</param>
    /// <returns>Localized text</returns>
    Task<string> FormatLocalDateAsync(DateTime localDate, string locale);
    
    /// <summary>
    /// Formating DateTime for different time zones to string with localization
    /// </summary>
    /// <param name="remoteDate">Date & time</param>
    /// <param name="timeZoneId">Zone ID</param>
    /// <param name="locale">Locale for formatting (example EN_US)</param>
    /// <returns>Localized text</returns>
    Task<string> FormatRegionalDateAsync(DateTime remoteDate, string timeZoneId, string locale);
}