using ButterBror.Core.Interfaces;

namespace ButterBror.ChatModule;

/// <summary>
/// Loader of chat modules
/// </summary>
public interface IChatModuleLoader
{
    /// <summary>
    /// Load all modules
    /// </summary>
    Task<IReadOnlyList<IChatModule>> LoadModulesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Unload all loaded modules
    /// </summary>
    Task UnloadModulesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reloads a single chat module by ZIP file name or module name from manifest
    /// </summary>
    Task<IReadOnlyList<IChatModule>> ReloadModuleAsync(string moduleName, CancellationToken cancellationToken = default);
}