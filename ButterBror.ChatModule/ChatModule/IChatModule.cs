using ButterBror.Core.Contracts;
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
    string PlatformName { get; }
    IReadOnlyList<ModuleCommandExport> ExportedCommands { get; }
    
    /// <summary>
    /// Module initialization
    /// </summary>
    void InitializeWithServices(IServiceProvider serviceProvider);
    Task InitializeAsync(IBotCore core);
    Task ShutdownAsync();
}
