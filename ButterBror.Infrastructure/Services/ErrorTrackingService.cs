using System.Security.Cryptography;
using System.Text;
using ButterBror.Core.Interfaces;
using ButterBror.Core.Models;
using ButterBror.Core.Modules.Commands;
using ButterBror.Data.Interfaces;
using ButterBror.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace ButterBror.Infrastructure.Services;

public class ErrorTrackingService(
    IErrorReportRepository repository,
    IUserRepository userRepository,
    ILocalizationService localizationService,
    ILogger<ErrorTrackingService> logger)
    : IErrorTrackingService
{
    public async Task<(CommandResult, ErrorLogRecord)> LogErrorAsync(
        Exception exception,
        string message,
        Guid userId,
        string platform,
        params object[] extraData)
    {
        var user = await userRepository.GetByUnifiedIdAsync(userId);
        var locale = user?.PreferredLocale ?? "EN_US";

        var errorId = await LogErrorInternalAsync(exception, message, user?.UnifiedId, platform, extraData);
        var errorHash = GenerateExceptionHash(exception);
        
        var localizedMessage = await localizationService.GetStringAsync(
            "core.error.report",
            locale,
            errorHash);

        logger.LogError(
            exception,
            "{message}. uid={UserId}, eid={ErrorId}, ehash={ErrorHash}",
            message,
            userId,
            errorId,
            errorHash);

        var errorRecord = new ErrorLogRecord(errorId, errorHash);
        
        return (CommandResult.Failure(localizedMessage), errorRecord);
    }

    private static string GenerateExceptionHash(Exception ex)
    {
        // s0: receive class
        var targetMethod = ex.TargetSite;
        string className = targetMethod?.DeclaringType?.Name ?? "UnknownClass";

        // s1: generating an abbreviation
        string abbreviation = GetAbbreviation(className);

        // s2: calc a hash
        string input = $"{ex.GetType().FullName}\n{ex.StackTrace}";
        using var sha256 = SHA256.Create();
        byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
        string hash = Convert.ToHexString(bytes)[..8];

        // s3: final
        return $"{abbreviation}:{hash}";
    }

    private static string GetAbbreviation(string input)
    {
        if (string.IsNullOrEmpty(input)) return "UNK";
        
        string cleanName = new string(input.Where(char.IsLetterOrDigit).ToArray());
        var upperLetters = cleanName.Where(char.IsUpper).ToArray();

        if (upperLetters.Length > 0)
        {
            return new string(upperLetters);
        }
        
        return cleanName.Length >= 3 ? cleanName[..3].ToUpper() : cleanName.ToUpper();
    }
    
    private async Task<Guid> LogErrorInternalAsync(
        Exception exception,
        string message,
        Guid? userId,
        string? platform,
        object[] extraData)
    {
        var errorId = Guid.CreateVersion7();
        var report = new ErrorReport
        {
            ErrorId = errorId,
            ExceptionType = exception.GetType().FullName ?? "Unknown",
            Message = message,
            StackTrace = exception.StackTrace,
            InnerException = exception.InnerException?.ToString(),
            ExtraData = SerializeExtraData(extraData),
            UserId = userId,
            Platform = platform,
            CreatedAt = DateTime.UtcNow
        };

        try
        {
            await repository.SaveAsync(report);
            logger.LogError(
                exception,
                "error logged. eid={ErrorId}, msg={Message}",
                errorId,
                message);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "failed to save error report. eid={ErrorId}",
                errorId);
        }

        return errorId;
    }

    /// <summary>
    /// Serialize extra data to dictionary
    /// </summary>
    private static Dictionary<string, object?> SerializeExtraData(object[] extraData)
    {
        var dict = new Dictionary<string, object?>();
        for (int i = 0; i < extraData.Length; i++)
        {
            var key = $"param_{i}";
            var value = extraData[i];
            
            if (value is KeyValuePair<string, object?> kvp)
            {
                dict[kvp.Key] = kvp.Value;
            }
            else
            {
                dict[key] = value;
            }
        }
        return dict;
    }
}