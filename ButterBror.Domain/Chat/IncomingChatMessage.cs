namespace ButterBror.Domain.Chat;

/// <summary>
/// Raw message data passed by a chat module to the bot core
/// </summary>
public record IncomingChatMessage(
    DateTime ReceivedAt,
    string Text,
    string PlatformUserId,
    string PlatformUserName,
    string PlatformChatId,
    string ExtraData
);