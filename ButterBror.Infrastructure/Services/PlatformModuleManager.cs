using ButterBror.Core.Interfaces;
using ButterBror.Core.Modules;
using ButterBror.Core.Modules.Interfaces;
using ButterBror.Core.Scopes;
using Microsoft.Extensions.Logging;

namespace ButterBror.Infrastructure.Services;

public class PlatformModuleManager(
    IChatModuleRegistry moduleRegistry,
    ICommandRegistry commandRegistry,
    IChatModuleLoader chatModuleLoader,
    ICommandModuleLoader commandModuleLoader,
    ILogger<PlatformModuleManager> logger)
    : IPlatformModuleManager
{
    private readonly List<IChatModule> _loadedChatModules = [];
    private readonly List<ICommandModule> _loadedCommandModules = [];

    public async Task InitializeAsync(IBotCore core, CancellationToken ct = default)
    {
        await using (new InitializationScope(logger, "platform module manager"))
        {
            await Task.WhenAll(
                LoadAndInitializeChatModulesAsync(ct),
                LoadAndInitializeCommandModulesAsync(ct)
            );
        }
    }

    private async Task LoadAndInitializeCommandModulesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var commandModules = await commandModuleLoader.LoadModulesAsync(cancellationToken);

            foreach (var module in commandModules)
            {
                try
                {
                    await using (new InitializationScope(logger, $"command module '{module.ModuleId}'"))
                    {
                        foreach (var exportedCommand in module.ExportedCommands)
                        {
                            commandRegistry.RegisterModuleCommand(
                                module.ModuleId,
                                exportedCommand.Factory,
                                exportedCommand.Metadata
                            );
                        }

                        _loadedCommandModules.Add(module);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "failed to initialize command module. id={ModuleId}", module.ModuleId);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "failed to load chat modules");
        }
    }

    private async Task LoadAndInitializeChatModulesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var chatModules = await chatModuleLoader.LoadModulesAsync(cancellationToken);

            foreach (var module in chatModules)
            {
                try
                {
                    await using (new InitializationScope(logger, $"chat module '{module.ModuleId}'"))
                    {
                        foreach (var exportedCommand in module.ExportedCommands)
                        {
                            commandRegistry.RegisterModuleCommand(
                                module.ModuleId,
                                exportedCommand.Factory,
                                exportedCommand.Metadata
                            );
                        }

                        _loadedChatModules.Add(module);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "failed to initialize chat module: {PlatformName}", module.ModuleId);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "failed to load chat modules");
        }
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        await using var _ = new StopScope(logger, "platform module manager");

        // shutdown built-in modules
        foreach (var module in moduleRegistry.GetModules())
        {
            try
            {
                await using (new InitializationScope(logger, $"platform module '{module.ModuleId}'"))
                {
                    await module.ShutdownAsync();
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "error shutting down platform module. id='{ModuleId}'", module.ModuleId);
            }
        }

        // shutdown loaded chat modules
        foreach (var module in _loadedChatModules)
        {
            try
            {
                await using (new InitializationScope(logger, $"chat module '{module.ModuleId}'"))
                {
                    await module.ShutdownAsync();
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "error shutting down chat module. id='{ModuleId}'", module.ModuleId);
            }
        }

        // shutdown loaded command modules
        foreach (var module in _loadedCommandModules)
        {
            try
            {
                await using (new InitializationScope(logger, $"command module '{module.ModuleId}'"))
                {
                    await module.ShutdownAsync();
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "error shutting down command module. id='{ModuleId}'", module.ModuleId);
            }
        }

        await chatModuleLoader.UnloadModulesAsync(cancellationToken);
        await commandModuleLoader.UnloadModulesAsync(cancellationToken);
    }

    public async Task<string> ReloadChatModuleAsync(string moduleId, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("reloading chat module. id='{PlatformName}'", moduleId);

        // find module in loaded chat modules
        var existingModule = _loadedChatModules.FirstOrDefault(m => m.ModuleId.Equals(moduleId, StringComparison.OrdinalIgnoreCase));
        if (existingModule == null)
        {
            logger.LogError("chat module not found in loaded modules. id='{ModuleId}'", moduleId);
            return "error:not_found";
        }

        try
        {
            // shutdown module
            await existingModule.ShutdownAsync();
            logger.LogDebug("shutdown chat module. id='{PlatformName}'", moduleId);

            // unregister commands
            commandRegistry.UnregisterModuleCommands(moduleId);
            logger.LogDebug("unregistered commands for module. id='{PlatformName}'", moduleId);

            // unregister from module registry
            moduleRegistry.UnregisterModule(moduleId);
            logger.LogDebug("unregistered module from registry. id='{PlatformName}'", moduleId);

            // remove from loaded modules
            _loadedChatModules.Remove(existingModule);

            // reload module from ZIP
            var newModules = await chatModuleLoader.ReloadModuleAsync(moduleId, cancellationToken);

            if (newModules.Count == 0)
            {
                logger.LogError("the module was not found in the files. id='{ModuleId}'", moduleId);
                return "error:not_found_local";
            }

            // initialize new modules
            foreach (var module in newModules)
            {
                foreach (var exportedCommand in module.ExportedCommands)
                {
                    commandRegistry.RegisterModuleCommand(
                        module.ModuleId,
                        exportedCommand.Factory,
                        exportedCommand.Metadata
                    );
                }

                _loadedChatModules.Add(module);
                logger.LogInformation(
                    "reloaded chat module. id='{ModuleId}', version={Version}, commands_count={CommandCount}",
                    module.ModuleId,
                    module.Version,
                    module.ExportedCommands.Count
                );
            }
            
            var result = $"reloaded chat module. id='{moduleId}'";
            logger.LogInformation(result);
            return "success";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "failed to reload chat module. id='{ModuleId}'", moduleId);
            return "error:exception";
        }
    }

    public async Task<string> ReloadCommandModuleAsync(string moduleId, CancellationToken cancellationToken = default)
    {
        // find module in loaded command modules
        var existingModule = _loadedCommandModules.FirstOrDefault(m => m.ModuleId.Equals(moduleId, StringComparison.OrdinalIgnoreCase));
        if (existingModule == null)
        {
            logger.LogError("command module not found in loaded modules. id='{ModuleId}'", moduleId);
            return "error:not_found";
        }

        try
        {
            // unregister commands
            commandRegistry.UnregisterModuleCommands(moduleId);
            logger.LogDebug("unregistered commands for module. id='{ModuleId}'", moduleId);

            // remove from loaded modules
            _loadedCommandModules.Remove(existingModule);

            // reload module from ZIP
            var newModules = await commandModuleLoader.ReloadModuleAsync(moduleId, cancellationToken);

            if (newModules.Count == 0)
            {
                logger.LogError("the module was not found in the files.. id='{ModuleId}'", moduleId);
                return "error:not_found_local";
            }

            // register commands from new modules
            foreach (var module in newModules)
            {
                foreach (var exportedCommand in module.ExportedCommands)
                {
                    commandRegistry.RegisterModuleCommand(
                        module.ModuleId,
                        exportedCommand.Factory,
                        exportedCommand.Metadata
                    );
                }

                _loadedCommandModules.Add(module);
                logger.LogInformation(
                    "reloaded command module. id='{ModuleId}', version={Version}, commands_count={CommandCount}",
                    module.ModuleId,
                    module.Version,
                    module.ExportedCommands.Count
                );
            }
            
            logger.LogInformation("reloaded command module. id='{moduleId}'", moduleId);
            return "success";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "failed to reload command module. id='{ModuleId}'", moduleId);
            return "error:exception";
        }
    }

    public IChatModule? GetModule(string platformName)
    {
        return moduleRegistry.GetModules()
            .Concat(_loadedChatModules.OfType<IChatModule>())
            .FirstOrDefault(m => m.ModuleId.Equals(platformName, StringComparison.OrdinalIgnoreCase));
    }
}
