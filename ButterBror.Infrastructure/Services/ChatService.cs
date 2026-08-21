using ButterBror.Core.Interfaces;
using ButterBror.Data;
using ButterBror.Data.Interfaces;
using ButterBror.Domain.Entities;

namespace ButterBror.Infrastructure.Services;

public class ChatService(IChatRepository chatRepository) : IChatService
{
    public async Task<ChatInfo> GetOrCreateChatAsync(string platformId, string platform, string title)
    {
        var chat = await chatRepository.GetByPlatformIdAsync(platform, platformId);

        if (chat != null)
        {
            chat.Title = title;
            await chatRepository.CreateOrUpdateAsync(chat);
            return chat;
        }

        var newChat = new ChatInfo
        {
            UnifiedId = Guid.CreateVersion7(),
            Title = title,
            CreatedAt = DateTime.UtcNow,
            PlatformId = platformId,
            Platform = platform
        };

        return await chatRepository.CreateOrUpdateAsync(newChat);
    }
}