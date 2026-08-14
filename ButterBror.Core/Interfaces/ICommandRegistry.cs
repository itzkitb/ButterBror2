

using ButterBror.Core.Modules.Interfaces;

namespace ButterBror.Core.Interfaces;

public interface ICommandRegistry
{
    // Registration methods
    void RegisterGlobalCommand(Func<ICommand> factory, ICommandMetadata metadata);
    void RegisterModuleCommand(string moduleId, Func<ICommand> factory, ICommandMetadata metadata);

    // Command retrieval methods
    Func<ICommand>? GetCommandFactory(string id, bool idIsName = false);
    ICommandMetadata? GetCommandMetadata(string id, bool idIsName = false);

    // Query methods
    bool ContainsCommand(string id, bool idIsName = false);
    string GetCommandModuleId(string id, bool idIsName = false);
    IEnumerable<ICommandMetadata> GetRegisteredCommands();
    bool IsCommandCompatibleWithPlatform(string id, string platformId, bool idIsName = false);
    Task<bool> UserHasPermissionForCommandAsync(string id, Guid unifiedUserId, bool idIsName = false);

    /// <summary>
    /// Removes all commands registered under the given moduleId
    /// </summary>
    void UnregisterModuleCommands(string moduleId);
}