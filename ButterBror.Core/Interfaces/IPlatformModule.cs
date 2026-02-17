using ButterBror.Core.Contracts;
using ButterBror.Core.Models.Commands;

namespace ButterBror.Core.Interfaces;

/// <summary>
/// Information about the module's exported command
/// </summary>
public record ModuleCommandExport(
    string CommandName,
    Func<ICommand> Factory,
    ICommandMetadata Metadata
);

public interface IPlatformModule
{
    string PlatformName { get; }
    IReadOnlyList<ModuleCommandExport> ExportedCommands { get; }
    
    Task InitializeAsync(IBotCore core);
    Task HandleIncomingMessageAsync(IMessage message);
    Task ShutdownAsync();
}
