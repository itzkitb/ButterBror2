using ButterBror.Domain.Entities;

namespace ButterBror.Data;

public interface IChatRepository
{
    Task<ChatInfo?> GetByUnifiedIdAsync(Guid unifiedId);
    Task<ChatInfo?> GetByPlatformIdAsync(string platform, string platformId);
    Task<ChatInfo> CreateOrUpdateAsync(ChatInfo chat);
    Task<bool> ChatExistsAsync(Guid unifiedId);
}