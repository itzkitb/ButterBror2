using System.Security.Cryptography;
using System.Text;
using ButterBror.CommandModule.Commands;
using ButterBror.Core.Interfaces;
using ButterBror.Data;
using ButterBror.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace ButterBror.Infrastructure.Services;

/// <summary>
/// Error tracking service with Redis storage and localization support
/// </summary>
public class ErrorTrackingService : IErrorTrackingService
{
    private readonly IErrorReportRepository _repository;
    private readonly IUserRepository _userRepository;
    private readonly ILocalizationService _localizationService;
    private readonly ILogger<ErrorTrackingService> _logger;
    private static readonly RandomNumberGenerator _rng = RandomNumberGenerator.Create();
    private const string Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
    private const int IdLength = 10;
    private const int MaxAttempts = 5;

    public ErrorTrackingService(
        IErrorReportRepository repository,
        IUserRepository userRepository,
        ILocalizationService localizationService,
        ILogger<ErrorTrackingService> logger)
    {
        _repository = repository;
        _userRepository = userRepository;
        _localizationService = localizationService;
        _logger = logger;
    }

    public void LogError(Exception exception, string message, params object[] extraData)
    {
        // Fire-and-forget for non-async calls
        _ = LogErrorInternalAsync(exception, message, null, null, extraData);
    }

    public async Task<CommandResult> LogErrorAsync(
        Exception exception,
        string message,
        string userId,
        string platform,
        params object[] extraData)
    {
        // Get user's preferred locale
        var user = await _userRepository.GetByPlatformIdAsync(platform, userId);
        var locale = user?.PreferredLocale ?? "EN_US";

        var errorId = await LogErrorInternalAsync(exception, message, user?.UnifiedUserId, platform, extraData);

        // Get localized message with error ID
        var localizedMessage = await _localizationService.GetStringAsync(
            "core.error.report",
            locale,
            errorId);

        _logger.LogInformation(
            "Error reported for user {UserId} with error ID {ErrorId}",
            userId,
            errorId);

        return CommandResult.Failure(localizedMessage);
    }

    private async Task<string> LogErrorInternalAsync(
        Exception exception,
        string message,
        Guid? userId,
        string? platform,
        object[] extraData)
    {
        var errorId = await GenerateErrorIdAsync();

        if (errorId == null)
        {
            return "ID_GENERATE_FAIL";
        }

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
            await _repository.SaveAsync(report);
            _logger.LogError(
                exception,
                "Error logged with ID {ErrorId}: {Message}",
                errorId,
                message);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to save error report {ErrorId} to Redis",
                errorId);
        }

        return errorId;
    }

    /// <summary>
    /// Generates a unique alphanumeric error ID with collision handling
    /// </summary>
    private async Task<string?> GenerateErrorIdAsync()
    {
        for (int attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var id = GenerateSecureId();
            var existing = await _repository.GetByIdAsync(id);
            
            if (existing == null)
            {
                return id;
            }

            _logger.LogWarning("Collision detected for error ID {Id}, attempt {Attempt}", id, attempt + 1);
        }

        _logger.LogError("Failed to generate unique error ID after {MaxAttempts} attempts", MaxAttempts);
        return null;
    }

    /// <summary>
    /// Generates a random string using cryptographically strong random number generator
    /// </summary>
    private string GenerateSecureId()
    {
        var sb = new StringBuilder(IdLength);
        var data = new byte[IdLength];

        _rng.GetBytes(data);

        foreach (var b in data)
        {
            // Map byte value to character set index safely
            sb.Append(Chars[b % Chars.Length]);
        }

        return sb.ToString();
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

            // Try to extract meaningful key from named objects
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