using ButterBror.Core.Modules;
using ButterBror.Core.Modules.Interfaces;

namespace ButterBror.Core.Interfaces;

/// <summary>
/// Command module loader
/// </summary>
public interface ICommandModuleLoader
{
    /// <summary>
    /// Load all modules
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns></returns>
    Task<IReadOnlyList<ICommandModule>> LoadModulesAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Unload all command modules
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns></returns>
    Task UnloadModulesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reloads a command module by module ID
    /// </summary>
    /// <param name="moduleId">ID of the module to be reloaded</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns></returns>
    Task<IReadOnlyList<ICommandModule>> ReloadModuleAsync(string moduleId, CancellationToken cancellationToken = default);
}
