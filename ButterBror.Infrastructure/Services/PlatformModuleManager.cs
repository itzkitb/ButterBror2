using ButterBror.ChatModule;
using ButterBror.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ButterBror.Infrastructure.Services;

public interface IPlatformModuleManager
{
    Task InitializeAsync(IBotCore core, CancellationToken cancellationToken = default);
    Task ShutdownAsync(CancellationToken cancellationToken = default);
    IChatModule? GetModule(string platformName);
}

public class PlatformModuleManager : IPlatformModuleManager
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IChatModuleRegistry _moduleRegistry;
    private readonly ICommandRegistry _commandRegistry;
    private readonly IChatModuleLoader _chatModuleLoader;
    private readonly ILogger<PlatformModuleManager> _logger;
    private readonly ICommandModuleLoader _commandModuleLoader;
    private readonly List<IChatModule> _loadedChatModules = new();
    private readonly List<ICommandModule> _loadedCommandModules = new();

    public PlatformModuleManager(
        IServiceProvider serviceProvider,
        IChatModuleRegistry moduleRegistry,
        ICommandRegistry commandRegistry,
        IChatModuleLoader chatModuleLoader,
        ICommandModuleLoader commandModuleLoader,
        ILogger<PlatformModuleManager> logger)
    {
        _serviceProvider = serviceProvider;
        _moduleRegistry = moduleRegistry;
        _commandRegistry = commandRegistry;
        _chatModuleLoader = chatModuleLoader;
        _commandModuleLoader = commandModuleLoader;
        _logger = logger;
    }

    public async Task InitializeAsync(IBotCore core, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Initializing platform modules...");

        // Initialize built-in modules from DI container
        var builtInModules = _serviceProvider.GetServices<IChatModule>();
        foreach (var module in builtInModules)
        {
            try
            {
                await InitializeModuleAsync(module, core);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize built-in platform module: {PlatformName}", module.PlatformName);
            }
        }

        // Load and initialize modules from DLL files
        await LoadAndInitializeChatModulesAsync(core, cancellationToken);
        await LoadAndInitializeCommandModulesAsync(cancellationToken);
    }

    private async Task LoadAndInitializeCommandModulesAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Loading command modules from AppData/Commands...");
        var commandModules = await _commandModuleLoader.LoadModulesAsync(cancellationToken);
        
        foreach (var module in commandModules)
        {
            try
            {
                // Register exported commands from module
                foreach (var exportedCommand in module.ExportedCommands)
                {
                    _commandRegistry.RegisterModuleCommand(
                        exportedCommand.CommandName,
                        module.ModuleId,
                        exportedCommand.Factory,
                        exportedCommand.Metadata
                    );
                }
                
                _loadedCommandModules.Add(module);
                _logger.LogInformation(
                    "Initialized command module: {ModuleId} v{Version} with {CommandCount} commands",
                    module.ModuleId,
                    module.Version,
                    module.ExportedCommands.Count
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize command module: {ModuleId}", module.ModuleId);
            }
        }
    }

    private async Task LoadAndInitializeChatModulesAsync(IBotCore core, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Loading chat modules from AppData/Chat...");

        var chatModules = await _chatModuleLoader.LoadModulesAsync(cancellationToken);

        foreach (var module in chatModules)
        {
            try
            {
                await InitializeModuleAsync(module, core);
                _loadedChatModules.Add(module);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize chat module: {PlatformName}", module.PlatformName);
            }
        }
    }

    private async Task InitializeModuleAsync(IChatModule module, IBotCore core)
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

    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Shutting down platform modules...");

        // Shutdown built-in modules
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

        // Shutdown loaded chat modules
        foreach (var module in _loadedChatModules)
        {
            try
            {
                await module.ShutdownAsync();
                _logger.LogInformation("Shutdown chat module: {PlatformName}", module.PlatformName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error shutting down chat module: {PlatformName}", module.PlatformName);
            }
        }

        // Shutdown loaded command modules
        foreach (var module in _loadedCommandModules)
        {
            try
            {
                await module.ShutdownAsync();
                _logger.LogInformation("Shutdown command module: {ModuleId}", module.ModuleId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error shutting down command module: {ModuleId}", module.ModuleId);
            }
        }

        await _chatModuleLoader.UnloadModulesAsync(cancellationToken);
        await _commandModuleLoader.UnloadModulesAsync(cancellationToken);
    }

    public IChatModule? GetModule(string platformName)
    {
        return _moduleRegistry.GetModules()
            .Concat(_loadedChatModules.OfType<IChatModule>())
            .FirstOrDefault(m => m.PlatformName.Equals(platformName, StringComparison.OrdinalIgnoreCase));
    }
}
