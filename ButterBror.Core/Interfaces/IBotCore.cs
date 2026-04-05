using ButterBror.CommandModule.Commands;
using ButterBror.CommandModule.Context;
using ButterBror.Core.Models;
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
