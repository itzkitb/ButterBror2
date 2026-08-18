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
    Task<IReadOnlyList<string>> GetChannelCategoryNamesAsync(Guid chatId);
    Task<string?> GetChannelCategoryAsync(Guid chatId, string categoryName);
    Task SetChannelCategoryAsync(Guid chatId, string categoryName, string regexPattern);
    Task DeleteChannelCategoryAsync(Guid chatId, string categoryName);
    
    // Bulk operations
    Task<IReadOnlyDictionary<string, string>> GetAllGlobalCategoriesAsync();
    Task<IReadOnlyDictionary<string, string>> GetAllChannelCategoriesAsync(Guid chatId);
}