using ButterBror.CommandModule.Commands;
using ButterBror.Core.Interfaces;

namespace ButterBror.ChatModule;

/// <summary>
/// Information about the module's exported command
/// </summary>
public record ModuleCommandExport(
    string CommandName,
    Func<ICommand> Factory,
    ICommandMetadata Metadata
);

public interface IChatModule
{
    /// <summary>
    /// The module identifier by which the system will recognize it
    /// </summary>
    string ModuleId { get; }
    
    /// <summary>
    /// Module version
    /// </summary>
    Version Version { get; }
    
    /// <summary>
    /// Built-in commands in this module
    /// </summary>
    IReadOnlyList<ModuleCommandExport> ExportedCommands { get; }

    /// <summary>
    /// Built-in default translations for this module
    /// </summary>
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> DefaultTranslations => 
        new Dictionary<string, IReadOnlyDictionary<string, string>>();

    /// <summary>
    /// Module initialization
    /// </summary>
    /// <param name="serviceProvider"></param>
    Task InitializeAsync(IServiceProvider serviceProvider);
    
    /// <summary>
    /// Shutdown module. Called by the system when the module is restarting or the bot is turning off
    /// </summary>
    Task ShutdownAsync();

    /// <summary>
    /// Send a message to the chat
    /// </summary>
    /// <param name="chatId">Chat ID</param>
    /// <param name="message">Message</param>
    /// <param name="replyId">ID of the message to which the reply is sent</param>
    /// <param name="data">Additional data</param>
    /// <exception cref="NotImplementedException">The method may not be implemented in the class. Recommend checking its presence through the CanSendMessage field</exception>
    Task SendMessageAsync(string chatId, string message, string? replyId = null, dynamic? data = null)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Module flags
    /// </summary>
    List<ChatFlags> Flags { get; }
}
