namespace ButterBror.Core.Interfaces;

/// <summary>
/// Result of banphrase check
/// </summary>
public record BanphraseCheckResult(
    bool Passed,
    string? FailedCategory = null,
    string? FailedSection = null,
    string? MatchedPattern = null,
    string? MatchedPhrase = null
);

/// <summary>
/// Service for checking messages against banphrase patterns
/// </summary>
public interface IBanphraseService
{
    /// <summary>
    /// Check message against all applicable banphrase categories
    /// </summary>
    Task<BanphraseCheckResult> CheckMessageAsync(
        Guid chatId,
        string message,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Set banphrase category (global or channel-specific)
    /// </summary>
    Task<bool> SetCategoryAsync(
        string section,
        Guid chatId,
        string categoryName,
        string regexPattern);
    
    /// <summary>
    /// Get banphrase category pattern
    /// </summary>
    Task<string?> GetCategoryAsync(
        string section,
        Guid chatId,
        string categoryName);
    
    /// <summary>
    /// List all categories in section
    /// </summary>
    Task<IReadOnlyList<string>> ListCategoriesAsync(
        string section,
        Guid chatId);
    
    /// <summary>
    /// Delete banphrase category
    /// </summary>
    Task<bool> DeleteCategoryAsync(
        string section,
        Guid chatId,
        string categoryName);
    
    /// <summary>
    /// Reload global categories from Redis
    /// </summary>
    Task ReloadGlobalCategoriesAsync();
}