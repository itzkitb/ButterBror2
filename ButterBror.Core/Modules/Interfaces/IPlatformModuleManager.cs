using ButterBror.Core.Interfaces;
using ButterBror.Core.Modules;

namespace ButterBror.Core.Modules.Interfaces;

public interface IPlatformModuleManager
{
    Task InitializeAsync(IBotCore core, CancellationToken cancellationToken = default);
    Task ShutdownAsync(CancellationToken cancellationToken = default);
    IChatModule? GetModule(string platformName);

    /// <summary>
    /// Reloads a single chat module by platform name
    /// </summary>
    Task<string> ReloadChatModuleAsync(string platformName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reloads a single command module by module ID
    /// </summary>
    Task<string> ReloadCommandModuleAsync(string moduleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a snapshot of all loaded modules (chat and command)
    /// </summary>
    Task<IReadOnlyList<ModuleInfo>> GetLoadedModulesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets status of a single module (searches chat and command)
    /// </summary>
    Task<ModuleInfo?> GetModuleStatusAsync(string moduleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disables a module (unregisters its commands and shuts it down)
    /// </summary>
    Task<string> DisableModuleAsync(string moduleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enables a previously disabled module
    /// </summary>
    Task<string> EnableModuleAsync(string moduleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reloads a module from disk (or loads it if not loaded yet)
    /// </summary>
    Task<string> LoadModuleFromDiskAsync(string moduleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reloads all loaded modules
    /// </summary>
    Task<string> ReloadAllModulesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Disables all modules
    /// </summary>
    Task<string> DisableAllModulesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enables all disabled modules
    /// </summary>
    Task<string> EnableAllModulesAsync(CancellationToken cancellationToken = default);
}
