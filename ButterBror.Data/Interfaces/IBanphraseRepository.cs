using ButterBror.Core.Models;

namespace ButterBror.Data.Interfaces;

/// <summary>
/// Repository for banphrase categories storage
/// </summary>
public interface IBanphraseRepository
{
    // ><> Global categories
    
    /// <summary>
    /// Get global ban phrase categories
    /// </summary>
    /// <returns>Category record</returns>
    Task<IReadOnlyList<BanphraseRecord>> GetGlobalCategoriesAsync();
    
    /// <summary>
    /// Get the global ban phrase category
    /// </summary>
    /// <param name="categoryName">Category name</param>
    /// <returns>Category record</returns>
    Task<BanphraseRecord?> GetGlobalCategoryAsync(string categoryName);
    
    /// <summary>
    /// Set global ban phrase category
    /// </summary>
    /// <param name="categoryName">Category name</param>
    /// <param name="regexPattern">Regex pattern</param>
    /// <returns></returns>
    Task SetGlobalCategoryAsync(string categoryName, string regexPattern);
    
    /// <summary>
    /// Delete global ban phrase category
    /// </summary>
    /// <param name="categoryName">Category name</param>
    /// <returns></returns>
    Task DeleteGlobalCategoryAsync(string categoryName);
    
    // ><> Channel-specific categories
    
    /// <summary>
    /// Get channel ban phrase categories
    /// </summary>
    /// <param name="chatId">Chat ID</param>
    /// <returns>Category record</returns>
    Task<IReadOnlyList<BanphraseRecord>> GetChannelCategoriesAsync(Guid chatId);
    
    /// <summary>
    /// Get channel ban phrase category
    /// </summary>
    /// <param name="chatId">Chat ID</param>
    /// <param name="categoryName">Category name</param>
    /// <returns>Category record</returns>
    Task<BanphraseRecord?> GetChannelCategoryAsync(Guid chatId, string categoryName);
    
    /// <summary>
    /// Set the channel ban phrase category
    /// </summary>
    /// <param name="chatId">Chat ID</param>
    /// <param name="categoryName">Category name</param>
    /// <param name="regexPattern">Regex pattern</param>
    /// <returns></returns>
    Task SetChannelCategoryAsync(Guid chatId, string categoryName, string regexPattern);
    
    /// <summary>
    /// Delete a channel's ban phrase category
    /// </summary>
    /// <param name="chatId">Chat ID</param>
    /// <param name="categoryName">Category name</param>
    /// <returns></returns>
    Task DeleteChannelCategoryAsync(Guid chatId, string categoryName);
}