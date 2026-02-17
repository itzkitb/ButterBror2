using ButterBror.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ButterBror.Infrastructure.Services;

public interface IPlatformModuleManager
{
    Task InitializeAsync(IBotCore core, CancellationToken cancellationToken = default);
    Task ShutdownAsync(CancellationToken cancellationToken = default);
    IPlatformModule? GetModule(string platformName);
}

public class PlatformModuleManager : IPlatformModuleManager
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IPlatformModuleRegistry _moduleRegistry;
    private readonly IUnifiedCommandRegistry _commandRegistry;
    private readonly ILogger<PlatformModuleManager> _logger;

    public PlatformModuleManager(
        IServiceProvider serviceProvider,
        IPlatformModuleRegistry moduleRegistry,
        IUnifiedCommandRegistry commandRegistry,
        ILogger<PlatformModuleManager> logger)
    {
        _serviceProvider = serviceProvider;
        _moduleRegistry = moduleRegistry;
        _commandRegistry = commandRegistry;
        _logger = logger;
    }

    public async Task InitializeAsync(IBotCore core, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Initializing platform modules...");

        var modules = _serviceProvider.GetServices<IPlatformModule>();
        foreach (var module in modules)
        {
            try
            {
                // Register exported commands from module
                foreach (var exportedCommand in module.ExportedCommands)
                {
                    _commandRegistry.RegisterModuleCommand(
                        exportedCommand.CommandName,
                        module.PlatformName,
                        exportedCommand.Factory,
                        exportedCommand.Metadata
                    );
                }

                await module.InitializeAsync(core);
                _moduleRegistry.RegisterModule(module);
                _logger.LogInformation(
                    "Initialized platform module: {PlatformName} with {CommandCount} commands",
                    module.PlatformName,
                    module.ExportedCommands.Count
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize platform module: {PlatformName}", module.PlatformName);
            }
        }
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Shutting down platform modules...");

        foreach (var module in _moduleRegistry.GetModules())
        {
            try
            {
                await module.ShutdownAsync();
                _logger.LogInformation("Shutdown platform module: {PlatformName}", module.PlatformName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error shutting down platform module: {PlatformName}", module.PlatformName);
            }
        }
    }

    public IPlatformModule? GetModule(string platformName)
    {
        return _moduleRegistry.GetModules()
            .FirstOrDefault(m => m.PlatformName.Equals(platformName, StringComparison.OrdinalIgnoreCase));
    }
}
