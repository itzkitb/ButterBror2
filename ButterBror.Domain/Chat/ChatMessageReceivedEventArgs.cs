using ButterBror.Domain.Entities;

namespace ButterBror.Domain.Chat;

public class ChatMessageReceivedEventArgs : EventArgs
{
    public required string ModuleId { get; init; }
    public required DateTime ReceivedAt { get; init; }
    public required ChatMessage Message { get; init; }
    public required UserProfile User { get; init; }
    public required ChatInfo Chat { get; init; }
    public required string Text { get; init; }
    public required Guid UnifiedUserId { get; init; }
    public required string PlatformChatId { get; init; }
    public required string PlatformChatName { get; init; }
    public required object? ExtraData { get; init; }
}