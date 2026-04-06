namespace ButterBror.Domain.Chat;

public class ChatMessageReceivedEventArgs : EventArgs
{
    public required string ModuleId { get; init; }
    public required DateTime ReceivedAt { get; init; }
    public required string Text { get; init; }
    public required Guid UnifiedUserId { get; init; }
    public required string PlatformChatId { get; init; }
    public required string ExtraData { get; init; }
}