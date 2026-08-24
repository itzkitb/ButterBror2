using ButterBror.Domain.Entities;

namespace ButterBror.Data.Interfaces;

/// <summary>
/// Repository for storing chat data
/// </summary>
public interface IChatRepository
{
    /// <summary>
    /// Get chat by unified ID
    /// </summary>
    /// <param name="unifiedId">Unified chat ID</param>
    /// <returns>Chat info or null</returns>
    Task<ChatInfo?> GetByUnifiedIdAsync(Guid unifiedId);
    
    /// <summary>
    /// Get chat based on platform data
    /// </summary>
    /// <param name="platform">Platform name</param>
    /// <param name="platformId">Chat id on the platform</param>
    /// <returns>Chat info or null</returns>
    Task<ChatInfo?> GetByPlatformIdAsync(string platform, string platformId);
    
    /// <summary>
    /// Get chat based on title
    /// </summary>
    /// <param name="platform">Platform name</param>
    /// <param name="title">Chat title</param>
    /// <returns>Chat info or null</returns>
    Task<ChatInfo?> GetByTitleAsync(string platform, string title);
    
    /// <summary>
    /// Create or update chat details
    /// </summary>
    /// <param name="chat">Chat info</param>
    /// <returns>Chat info</returns>
    Task<ChatInfo> CreateOrUpdateAsync(ChatInfo chat);
    
    /// <summary>
    /// Check if this chat exists
    /// </summary>
    /// <param name="unifiedId">Unified chat ID</param>
    /// <returns>Is there a chat</returns>
    Task<bool> ChatExistsAsync(Guid unifiedId);
}