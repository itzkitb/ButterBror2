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
    string ModuleId { get; }
    Version Version { get; }
    IReadOnlyList<ModuleCommandExport> ExportedCommands { get; }

    /// <summary>
    /// Built-in default translations for this module
    /// </summary>
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> DefaultTranslations => 
        new Dictionary<string, IReadOnlyDictionary<string, string>>();

    /// <summary>
    /// Module initialization
    /// </summary>
    /// <param name="core"></param>
    /// <param name="serviceProvider"></param>
    Task InitializeAsync(IBotCore core, IServiceProvider serviceProvider);
    Task ShutdownAsync();
}
