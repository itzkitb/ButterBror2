using ButterBror.CommandModule.CommandModule;

namespace ButterBror.Core.Interfaces;

public interface ICommandModuleLoader
{
    Task<IReadOnlyList<ICommandModule>> LoadModulesAsync(CancellationToken cancellationToken = default);
    Task UnloadModulesAsync(CancellationToken cancellationToken = default);
}
