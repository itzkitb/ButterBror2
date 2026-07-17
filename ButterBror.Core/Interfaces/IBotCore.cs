using ButterBror.Core.Modules.Commands;
using ButterBror.Core.Modules.Interfaces;
using ButterBror.Domain.Chat;

namespace ButterBror.Core.Interfaces;

public interface IBotCore
{
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    Task<CommandResult> ProcessCommandAsync(ICommandContext context);
    event EventHandler<ChatMessageReceivedEventArgs>? OnChatMessageReceived;
    Task RaiseMessageReceivedAsync(
        string moduleId,
        IncomingChatMessage message,
        string platform,
        CancellationToken ct = default);
}
