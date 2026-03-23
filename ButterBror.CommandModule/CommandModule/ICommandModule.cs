using ButterBror.CommandModule.Commands;

namespace ButterBror.CommandModule.CommandModule;

/// <summary>
/// Information about exported command from module
/// </summary>
public record CommandModuleExport(
    string CommandName,
    Func<ICommand> Factory,
    ICommandMetadata Metadata
);

/// <summary>
/// Interface for dynamically loaded command modules
/// </summary>
public interface ICommandModule
{
    string ModuleId { get; }
    string Version { get; }
    IReadOnlyList<CommandModuleExport> ExportedCommands { get; }
    
    /// <summary>
    /// Module initialization
    /// </summary>
    void InitializeWithServices(IServiceProvider serviceProvider);
    
    /// <summary>
    /// Module shutdown
    /// </summary>
    Task ShutdownAsync();
}