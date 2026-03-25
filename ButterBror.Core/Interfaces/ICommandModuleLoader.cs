using ButterBror.CommandModule.CommandModule;

namespace ButterBror.Core.Interfaces;

public interface ICommandModuleLoader
{
    Task<IReadOnlyList<ICommandModule>> LoadModulesAsync(CancellationToken cancellationToken = default);
    Task UnloadModulesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reloads a single command module by module ID
    /// </summary>
    Task<IReadOnlyList<ICommandModule>> ReloadModuleAsync(string moduleId, CancellationToken cancellationToken = default);
}
