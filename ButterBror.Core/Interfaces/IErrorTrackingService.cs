using ButterBror.Core.Models.Commands;

namespace ButterBror.Core.Interfaces;

/// <summary>
/// Service for tracking and reporting application errors
/// </summary>
public interface IErrorTrackingService
{
    /// <summary>
    /// Log error without user context
    /// </summary>
    /// <param name="exception">Exception to log</param>
    /// <param name="message">Custom error message</param>
    /// <param name="extraData">Additional context data</param>
    void LogError(Exception exception, string message, params object[] extraData);

    /// <summary>
    /// Log error with user context and return localized CommandResult
    /// </summary>
    /// <param name="exception">Exception to log</param>
    /// <param name="message">Custom error message</param>
    /// <param name="userId">User ID for localization</param>
    /// <param name="extraData">Additional context data</param>
    /// <returns>CommandResult with localized error message</returns>
    Task<CommandResult> LogErrorAsync(
        Exception exception,
        string message,
        string userId,
        string platform,
        params object[] extraData);
}