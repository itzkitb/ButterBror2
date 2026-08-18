using ButterBror.Domain.Entities;

namespace ButterBror.Core.Interfaces;

/// <summary>
/// Service for working with chats
/// </summary>
public interface IChatService
{
    /// <summary>
    /// Get and|or create a chat
    /// </summary>
    /// <param name="platformId">Chat ID on this platform</param>
    /// <param name="platform">Platform</param>
    /// <param name="title">Chat title</param>
    /// <param name="extraData">Extra</param>
    /// <returns>User</returns>
    Task<ChatInfo> GetOrCreateChatAsync(string platformId, string platform, string title);
}