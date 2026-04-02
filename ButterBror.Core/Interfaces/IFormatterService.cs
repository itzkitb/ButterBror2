
namespace ButterBror.Core.Interfaces;

/// <summary>
/// Service for text formating
/// </summary>
public interface IFormatterService
{
    /// <summary>
    /// Formating TimeSpan to string with localization
    /// </summary>
    /// <param name="ts"></param>
    /// <param name="locale"></param>
    /// <returns></returns>
    Task<string> FormatTimeSpanAsync(TimeSpan ts, string locale);

    /// <summary>
    /// Formating UTC DateTime to string with localization
    /// </summary>
    /// <param name="utcDate"></param>
    /// <param name="locale"></param>
    /// <returns></returns>
    Task<string> FormatUtcDateAsync(DateTime utcDate, string locale);

    /// <summary>
    /// Formating local DateTime to string with localization
    /// </summary>
    /// <param name="localDate"></param>
    /// <param name="locale"></param>
    /// <returns></returns>
    Task<string> FormatLocalDateAsync(DateTime localDate, string locale);
    
    /// <summary>
    /// Formating DateTime for different time zones to string with localization
    /// </summary>
    /// <param name="remoteDate"></param>
    /// <param name="timeZoneId"></param>
    /// <param name="locale"></param>
    /// <returns></returns>
    Task<string> FormatRegionalDateAsync(DateTime remoteDate, string timeZoneId, string locale);
}