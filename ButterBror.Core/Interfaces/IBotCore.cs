using ButterBror.Core.Modules.Commands;
using ButterBror.Core.Modules.Interfaces;
using ButterBror.Domain.Chat;

namespace ButterBror.Core.Interfaces;

/// <summary>
/// Bot core
/// </summary>
public interface IBotCore
{
    /// <summary>
    /// Core startup
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns></returns>
    Task StartAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Core shutdown
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns></returns>
    Task StopAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Execute the command and retrieve the execution result. Recommended for third-party modules
    /// </summary>
    /// <param name="context">Command execution context. It's recommended to create a custom implementation the
    /// ICommandContext interface</param>
    /// <returns>Result of execution</returns>
    Task<CommandResult> ProcessCommandAsync(CommandContext context);
    
    /// <summary>
    /// Public event for reading new messages
    /// </summary>
    event EventHandler<ChatMessageReceivedEventArgs>? OnChatMessageReceived;
    
    /// <summary>
    /// Notify the core of a new message. Executed in chat modules upon new messages
    /// </summary>
    /// <param name="moduleId">ID of the module that received the message</param>
    /// <param name="message">The message itself</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns></returns>
    Task RaiseMessageReceivedAsync(
        string moduleId,
        ChatMessage message,
        CancellationToken ct = default);
}
