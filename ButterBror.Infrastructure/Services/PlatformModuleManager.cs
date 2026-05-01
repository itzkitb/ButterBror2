using ButterBror.ChatModule;
using ButterBror.CommandModule.CommandModule;
using ButterBror.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ButterBror.Infrastructure.Services;

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
    private IBotCore? _core;

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
        _core = core;

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
                _logger.LogError(ex, "Failed to initialize built-in platform module. name='{PlatformName}'", module.ModuleId);
            }
        }

        // Load and initialize modules from DLL files
        await LoadAndInitializeChatModulesAsync(core, cancellationToken);
        await LoadAndInitializeCommandModulesAsync(cancellationToken);
    }

    private async Task LoadAndInitializeCommandModulesAsync(CancellationToken cancellationToken)
    {
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
                    "Initialized command module. id='{ModuleId}', version={Version}, commands={CommandCount}",
                    module.ModuleId,
                    module.Version,
                    module.ExportedCommands.Count
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize command module. id={ModuleId}", module.ModuleId);
            }
        }
    }

    private async Task LoadAndInitializeChatModulesAsync(IBotCore core, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Loading chat modules...");

        var chatModules = await _chatModuleLoader.LoadModulesAsync(cancellationToken);

        foreach (var module in chatModules)
        {
            try
            {
                await InitializeModuleAsync(module, core);
                _loadedChatModules.Add(module);
                _logger.LogInformation(
                    "Initialized chat module. id='{ModuleId}', version={Version}, commands={CommandCount}",
                    module.ModuleId,
                    module.Version,
                    module.ExportedCommands.Count
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize chat module: {PlatformName}", module.ModuleId);
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
                module.ModuleId,
                exportedCommand.Factory,
                exportedCommand.Metadata
            );
        }

        await module.InitializeAsync(core);
        _moduleRegistry.RegisterModule(module);
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
                _logger.LogInformation("Shutdown platform module. id='{ModuleId}'", module.ModuleId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error shutting down platform module. id='{ModuleId}'", module.ModuleId);
            }
        }

        // Shutdown loaded chat modules
        foreach (var module in _loadedChatModules)
        {
            try
            {
                await module.ShutdownAsync();
                _logger.LogInformation("Shutdown chat module. id='{ModuleId}'", module.ModuleId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error shutting down chat module. id='{ModuleId}'", module.ModuleId);
            }
        }

        // Shutdown loaded command modules
        foreach (var module in _loadedCommandModules)
        {
            try
            {
                await module.ShutdownAsync();
                _logger.LogInformation("Shutdown command module. id='{ModuleId}'", module.ModuleId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error shutting down command module. id='{ModuleId}'", module.ModuleId);
            }
        }

        await _chatModuleLoader.UnloadModulesAsync(cancellationToken);
        await _commandModuleLoader.UnloadModulesAsync(cancellationToken);
    }

    public async Task<string> ReloadChatModuleAsync(string moduleId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Reloading chat module. id='{PlatformName}'", moduleId);

        // Find module in loaded chat modules
        var existingModule = _loadedChatModules.FirstOrDefault(m => m.ModuleId.Equals(moduleId, StringComparison.OrdinalIgnoreCase));
        if (existingModule == null)
        {
            var error = $"Chat module not found in loaded modules. id='{moduleId}'";
            _logger.LogError(error);
            return error;
        }

        if (_core == null)
        {
            var error = "Bot core is not initialized";
            _logger.LogError(error);
            return error;
        }

        try
        {
            // Shutdown module
            await existingModule.ShutdownAsync();
            _logger.LogDebug("Shutdown chat module. id='{PlatformName}'", moduleId);

            // Unregister commands
            _commandRegistry.UnregisterModuleCommands(moduleId);
            _logger.LogDebug("Unregistered commands for module. id='{PlatformName}'", moduleId);

            // Unregister from module registry
            _moduleRegistry.UnregisterModule(moduleId);
            _logger.LogDebug("Unregistered module from registry. id='{PlatformName}'", moduleId);

            // Remove from loaded modules
            _loadedChatModules.Remove(existingModule);

            // Reload module from ZIP
            var newModules = await _chatModuleLoader.ReloadModuleAsync(moduleId, cancellationToken);

            if (newModules.Count == 0)
            {
                var error = $"Module not found. id='{moduleId}'";
                _logger.LogError(error);
                return error;
            }

            // Initialize new modules
            foreach (var module in newModules)
            {
                await InitializeModuleAsync(module, _core);
                _loadedChatModules.Add(module);
            }

            var result = $"Reloaded chat module. id='{moduleId}'";
            _logger.LogInformation(result);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reload chat module. id='{ModuleId}'", moduleId);
            return $"Failed to reload chat module '{moduleId}': {ex.Message}";
        }
    }

    public async Task<string> ReloadCommandModuleAsync(string moduleId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Reloading command module. id='{ModuleId}'", moduleId);

        // Find module in loaded command modules
        var existingModule = _loadedCommandModules.FirstOrDefault(m => m.ModuleId.Equals(moduleId, StringComparison.OrdinalIgnoreCase));
        if (existingModule == null)
        {
            var error = $"Command module not found in loaded modules. id='{moduleId}'";
            _logger.LogError(error);
            return error;
        }

        try
        {
            // Unregister commands
            _commandRegistry.UnregisterModuleCommands(moduleId);
            _logger.LogDebug("Unregistered commands for module. id='{ModuleId}'", moduleId);

            // Remove from loaded modules
            _loadedCommandModules.Remove(existingModule);

            // Reload module from ZIP
            var newModules = await _commandModuleLoader.ReloadModuleAsync(moduleId, cancellationToken);

            if (newModules.Count == 0)
            {
                var error = $"Module not found. id='{moduleId}'";
                _logger.LogError(error);
                return error;
            }

            // Register commands from new modules
            foreach (var module in newModules)
            {
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
                    "Reloaded command module. id='{ModuleId}', version={Version}, commands_count={CommandCount}",
                    module.ModuleId,
                    module.Version,
                    module.ExportedCommands.Count
                );
            }

            var result = $"Reloaded command module. id='{moduleId}'";
            _logger.LogInformation(result);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reload command module. id='{ModuleId}'", moduleId);
            return $"Failed to reload command module '{moduleId}': {ex.Message}";
        }
    }

    public IChatModule? GetModule(string platformName)
    {
        return _moduleRegistry.GetModules()
            .Concat(_loadedChatModules.OfType<IChatModule>())
            .FirstOrDefault(m => m.ModuleId.Equals(platformName, StringComparison.OrdinalIgnoreCase));
    }
}
