using ButterBror.Core.Interfaces;

namespace ButterBror.ChatModule;

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
}