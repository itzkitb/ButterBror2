using ButterBror.Core.Interfaces;
using ButterBror.Core.Modules;
using ButterBror.Core.Modules.Interfaces;
using Microsoft.Extensions.Logging;

namespace ButterBror.Infrastructure.Services;

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

    public async Task InitializeAsync(IBotCore core, CancellationToken ct = default)
    {
        _core = core;
        
        await Task.WhenAll(
            LoadAndInitializeChatModulesAsync(core, ct),
            LoadAndInitializeCommandModulesAsync(ct)
        );
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
        var chatModules = await _chatModuleLoader.LoadModulesAsync(cancellationToken);

        foreach (var module in chatModules)
        {
            try
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
            return "error:not_found";
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
                var error = $"The module was not found in the files. id='{moduleId}'";
                _logger.LogError(error);
                return "error:not_found_local";
            }

            // Initialize new modules
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

                _loadedChatModules.Add(module);
                _logger.LogInformation(
                    "Reloaded chat module. id='{ModuleId}', version={Version}, commands_count={CommandCount}",
                    module.ModuleId,
                    module.Version,
                    module.ExportedCommands.Count
                );
            }
            
            var result = $"Reloaded chat module. id='{moduleId}'";
            _logger.LogInformation(result);
            return "success";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reload chat module. id='{ModuleId}'", moduleId);
            return "error:exception";
        }
    }

    public async Task<string> ReloadCommandModuleAsync(string moduleId, CancellationToken cancellationToken = default)
    {
        // Find module in loaded command modules
        var existingModule = _loadedCommandModules.FirstOrDefault(m => m.ModuleId.Equals(moduleId, StringComparison.OrdinalIgnoreCase));
        if (existingModule == null)
        {
            var error = $"Command module not found in loaded modules. id='{moduleId}'";
            _logger.LogError(error);
            return "error:not_found";
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
                var error = $"The module was not found in the files.. id='{moduleId}'";
                _logger.LogError(error);
                return "error:not_found_local";
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
            return "success";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reload command module. id='{ModuleId}'", moduleId);
            return "error:exception";
        }
    }

    public IChatModule? GetModule(string platformName)
    {
        return _moduleRegistry.GetModules()
            .Concat(_loadedChatModules.OfType<IChatModule>())
            .FirstOrDefault(m => m.ModuleId.Equals(platformName, StringComparison.OrdinalIgnoreCase));
    }
}
