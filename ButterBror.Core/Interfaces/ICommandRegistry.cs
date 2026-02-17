using ButterBror.Core.Contracts;
using ButterBror.Core.Enums;
using ButterBror.Core.Interfaces;
using ButterBror.Core.Models.Commands;

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

    // Legacy methods for backward compatibility
    void RegisterCommand(string name, ICommand command);
    bool TryGetUnifiedCommand(string name, out ICommand command);
    IEnumerable<string> GetRegisteredCommandNames();
    ICommandMetadata? GetCommand(string name);
    void RegisterCommandMetadata(ICommandMetadata metadata);
}