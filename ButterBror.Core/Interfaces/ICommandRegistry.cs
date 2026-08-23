

using ButterBror.Core.Modules.Interfaces;

namespace ButterBror.Core.Interfaces;

/// <summary>
/// Command registry
/// </summary>
public interface ICommandRegistry
{
    /// <summary>
    /// Initialize service
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns></returns>
    Task InitializeAsync(CancellationToken cancellationToken = default);
    
    // ><> registration methods
    
    /// <summary>
    /// Register global command
    /// </summary>
    /// <param name="factory">Command factory</param>
    /// <param name="metadata">Command metadata</param>
    void RegisterGlobalCommand(Func<ICommand> factory, ICommandMetadata metadata);
    
    /// <summary>
    /// Register module commands
    /// </summary>
    /// <param name="moduleId">Module ID</param>
    /// <param name="factory">Command factory</param>
    /// <param name="metadata">Command metadata</param>
    void RegisterModuleCommand(string moduleId, Func<ICommand> factory, ICommandMetadata metadata);

    // ><> command retrieval methods
    
    /// <summary>
    /// Get command factory
    /// </summary>
    /// <param name="id">Command ID</param>
    /// <param name="idIsName">Is the input ID the name of the command?</param>
    /// <returns></returns>
    Func<ICommand>? GetCommandFactory(string id, bool idIsName = false);
    
    /// <summary>
    /// Get command metadata
    /// </summary>
    /// <param name="id">Command ID</param>
    /// <param name="idIsName">Is the input ID the name of the command?</param>
    /// <returns></returns>
    ICommandMetadata? GetCommandMetadata(string id, bool idIsName = false);

    // ><> query methods
    
    /// <summary>
    /// Is such a command contained in the registry?
    /// </summary>
    /// <param name="id">Command ID</param>
    /// <param name="idIsName">Is the input ID the name of the command?</param>
    /// <returns></returns>
    bool ContainsCommand(string id, bool idIsName = false);
    
    /// <summary>
    /// Get the ID of the module in which the command is registered
    /// </summary>
    /// <param name="id">Command ID</param>
    /// <param name="idIsName">Is the input ID the name of the command?</param>
    /// <returns></returns>
    string? GetCommandModuleId(string id, bool idIsName = false);
    
    /// <summary>
    /// Get a complete list of commands that are registered in the registry
    /// </summary>
    /// <returns>List of command metadata</returns>
    IEnumerable<ICommandMetadata> GetRegisteredCommands();
    
    /// <summary>
    /// Is it possible to execute this command in this chat module?
    /// </summary>
    /// <param name="id">Command ID</param>
    /// <param name="platformId">User ID</param>
    /// <param name="idIsName">Is the input ID the name of the command?</param>
    /// <returns></returns>
    bool IsCommandCompatibleWithPlatform(string id, string platformId, bool idIsName = false);
    
    /// <summary>
    /// Does the user have permission to execute this command?
    /// </summary>
    /// <param name="id">Command ID</param>
    /// <param name="unifiedUserId">User ID</param>
    /// <param name="idIsName">Is the input ID the name of the command?</param>
    /// <returns></returns>
    Task<bool> UserHasPermissionForCommandAsync(string id, Guid unifiedUserId, bool idIsName = false);

    /// <summary>
    /// Removes all commands registered under the given moduleId
    /// </summary>
    /// <param name="moduleId">Module ID</param>
    void UnregisterModuleCommands(string moduleId);
}