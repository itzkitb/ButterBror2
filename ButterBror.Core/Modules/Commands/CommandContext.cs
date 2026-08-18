using ButterBror.Core.Modules.Interfaces;
using ButterBror.Domain;
using ButterBror.Domain.Chat;
using ButterBror.Domain.Entities;

namespace ButterBror.Core.Modules.Commands;

public class CommandContext(
    string commandName,
    string platformId,
    List<string> arguments,
    IPlatformUser platformUser,
    IPlatformChannel chat,
    ChatMessage originalMessage,
    CancellationToken cancellationToken)
{
    public string CommandName { get; } = commandName;
    public string PlatformId { get; } = platformId;
    public string Locale { get; private set; } = "EN_US";
    public List<string> Arguments { get; } = arguments;
    public IPlatformUser PlatformUser { get; } = platformUser;
    public IPlatformChannel Chat { get; } = chat;
    public UserProfile User { get; private set; } = null!;
    public ChatInfo ChatInfo { get; private set; } = null!;
    public ChatMessage OriginalMessage { get; } = originalMessage;
    public DateTime ExecutedAt { get; } = DateTime.UtcNow;
    public Guid CorrelationId { get; } = Guid.NewGuid();
    public CancellationToken CancellationToken { get; set; } = cancellationToken;

    public void ExtendContext(UserProfile user, ChatInfo chat)
    {
        User = user;
        Locale = user.PreferredLocale;
        ChatInfo = chat;
    }
}