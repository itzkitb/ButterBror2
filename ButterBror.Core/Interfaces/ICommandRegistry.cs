using ButterBror.CommandModule.Commands;

namespace ButterBror.Core.Interfaces;

public interface ICommandRegistry
{
    // Registration methods
    void RegisterGlobalCommand(string commandName, Func<ICommand> factory, ICommandMetadata metadata);
    void RegisterModuleCommand(string commandName, string moduleId, Func<ICommand> factory, ICommandMetadata metadata);

    // Command retrieval methods
    Func<ICommand>? GetCommandFactory(string commandName);
    ICommandMetadata? GetCommandMetadata(string commandName);

    // Query methods
    bool ContainsCommand(string commandName);
    string GetCommandModuleId(string commandName);
    IEnumerable<string> GetRegisteredCommands();
    bool IsCommandCompatibleWithPlatform(string commandName, string platformId);
    Task<bool> UserHasPermissionForCommandAsync(string commandName, Guid unifiedUserId);

    /// <summary>
    /// Removes all commands registered under the given moduleId
    /// </summary>
    void UnregisterModuleCommands(string moduleId);

    // Legacy methods for backward compatibility
    bool TryGetUnifiedCommand(string name, out ICommand? command);
    IEnumerable<string> GetRegisteredCommandNames();
    ICommandMetadata? GetCommand(string name);
}