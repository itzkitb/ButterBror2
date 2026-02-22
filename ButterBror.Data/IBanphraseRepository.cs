namespace ButterBror.Data;

/// <summary>
/// Repository for banphrase categories storage
/// </summary>
public interface IBanphraseRepository
{
    // Global categories
    Task<IReadOnlyList<string>> GetGlobalCategoryNamesAsync();
    Task<string?> GetGlobalCategoryAsync(string categoryName);
    Task SetGlobalCategoryAsync(string categoryName, string regexPattern);
    Task DeleteGlobalCategoryAsync(string categoryName);
    
    // Channel-specific categories
    Task<IReadOnlyList<string>> GetChannelCategoryNamesAsync(string platform, string channelId);
    Task<string?> GetChannelCategoryAsync(string platform, string channelId, string categoryName);
    Task SetChannelCategoryAsync(string platform, string channelId, string categoryName, string regexPattern);
    Task DeleteChannelCategoryAsync(string platform, string channelId, string categoryName);
    
    // Bulk operations
    Task<IReadOnlyDictionary<string, string>> GetAllGlobalCategoriesAsync();
    Task<IReadOnlyDictionary<string, string>> GetAllChannelCategoriesAsync(string platform, string channelId);
}