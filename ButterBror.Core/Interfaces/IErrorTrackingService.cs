using ButterBror.Core.Models;
using ButterBror.Core.Modules.Commands;

namespace ButterBror.Core.Interfaces;

/// <summary>
/// Service for tracking and reporting application errors
/// </summary>
public interface IErrorTrackingService
{
    /// <summary>
    /// Log error with user context and return localized CommandResult
    /// </summary>
    /// <param name="exception">Exception to log</param>
    /// <param name="message">Custom error message</param>
    /// <param name="userId">User ID for localization</param>
    /// <param name="platform">Platform id</param>
    /// <param name="extraData">Additional context data</param>
    /// <returns>CommandResult with localized error message</returns>
    Task<(CommandResult, ErrorLogRecord)> LogErrorAsync(
        Exception exception,
        string message,
        Guid userId,
        string platform,
        params object[] extraData);
}