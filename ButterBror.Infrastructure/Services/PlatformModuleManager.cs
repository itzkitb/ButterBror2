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
    IServiceProvider serviceProvider,
    ILogger<PlatformModuleManager> logger)
    : IPlatformModuleManager
{
    private readonly List<IChatModule> _loadedChatModules = [];
    private readonly List<ICommandModule> _loadedCommandModules = [];
    private readonly HashSet<string> _disabledModuleIds = new(StringComparer.OrdinalIgnoreCase);

    public async Task InitializeAsync(IBotCore core, CancellationToken ct = default)
    {
        await using (new InitializationScope(logger, "module manager"))
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
            logger.LogError(ex, "failed to load command modules");
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

        foreach (var module in moduleRegistry.GetModules())
        {
            try
            {
                await using (new StopScope(logger, $"platform module '{module.ModuleId}'"))
                {
                    await module.ShutdownAsync();
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "error shutting down platform module. id='{ModuleId}'", module.ModuleId);
            }
        }

        foreach (var module in _loadedChatModules)
        {
            try
            {
                await using (new StopScope(logger, $"chat module '{module.ModuleId}'"))
                {
                    await module.ShutdownAsync();
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "error shutting down chat module. id='{ModuleId}'", module.ModuleId);
            }
        }

        foreach (var module in _loadedCommandModules)
        {
            try
            {
                await using (new StopScope(logger, $"command module '{module.ModuleId}'"))
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

        var existingModule = _loadedChatModules.FirstOrDefault(m => m.ModuleId.Equals(moduleId, StringComparison.OrdinalIgnoreCase));
        if (existingModule == null)
        {
            logger.LogError("chat module not found in loaded modules. id='{ModuleId}'", moduleId);
            return "error:not_found";
        }

        try
        {
            await existingModule.ShutdownAsync();
            logger.LogDebug("shutdown chat module. id='{PlatformName}'", moduleId);

            commandRegistry.UnregisterModuleCommands(moduleId);
            logger.LogDebug("unregistered commands for module. id='{PlatformName}'", moduleId);

            moduleRegistry.UnregisterModule(moduleId);
            logger.LogDebug("unregistered module from registry. id='{PlatformName}'", moduleId);

            _loadedChatModules.Remove(existingModule);

            var newModules = await chatModuleLoader.ReloadModuleAsync(moduleId, cancellationToken);

            if (newModules.Count == 0)
            {
                logger.LogError("the module was not found in the files. id='{ModuleId}'", moduleId);
                return "error:not_found_local";
            }

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

            _disabledModuleIds.Remove(moduleId);
            logger.LogInformation("reloaded chat module. id='{moduleId}'", moduleId);
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
        var existingModule = _loadedCommandModules.FirstOrDefault(m => m.ModuleId.Equals(moduleId, StringComparison.OrdinalIgnoreCase));
        if (existingModule == null)
        {
            logger.LogError("command module not found in loaded modules. id='{ModuleId}'", moduleId);
            return "error:not_found";
        }

        try
        {
            commandRegistry.UnregisterModuleCommands(moduleId);
            logger.LogDebug("unregistered commands for module. id='{ModuleId}'", moduleId);

            _loadedCommandModules.Remove(existingModule);

            var newModules = await commandModuleLoader.ReloadModuleAsync(moduleId, cancellationToken);

            if (newModules.Count == 0)
            {
                logger.LogError("the module was not found in the files. id='{ModuleId}'", moduleId);
                return "error:not_found_local";
            }

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

            _disabledModuleIds.Remove(moduleId);
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

    public Task<IReadOnlyList<ModuleInfo>> GetLoadedModulesAsync(CancellationToken cancellationToken = default)
    {
        var result = moduleRegistry.GetModules().Select(module => new ModuleInfo(module.ModuleId, module.Version,
            "chat", IsLoaded: true, IsEnabled: !_disabledModuleIds.Contains(module.ModuleId),
            module.ExportedCommands.Count)).ToList();

        foreach (var module in _loadedChatModules.Where(module =>
                     !result.Any(r => r.ModuleId.Equals(module.ModuleId, StringComparison.OrdinalIgnoreCase))))
        {
            result.Add(new ModuleInfo(
                module.ModuleId,
                module.Version,
                "chat",
                IsLoaded: true,
                IsEnabled: !_disabledModuleIds.Contains(module.ModuleId),
                module.ExportedCommands.Count
            ));
        }

        result.AddRange(_loadedCommandModules.Select(module => new ModuleInfo(module.ModuleId, module.Version,
            "command", IsLoaded: true, IsEnabled: !_disabledModuleIds.Contains(module.ModuleId),
            module.ExportedCommands.Count)));

        return Task.FromResult<IReadOnlyList<ModuleInfo>>(result);
    }

    public async Task<ModuleInfo?> GetModuleStatusAsync(string moduleId, CancellationToken cancellationToken = default)
    {
        var all = await GetLoadedModulesAsync(cancellationToken);
        return all.FirstOrDefault(m => m.ModuleId.Equals(moduleId, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<string> DisableModuleAsync(string moduleId, CancellationToken cancellationToken = default)
    {
        var chatModule = _loadedChatModules.FirstOrDefault(m => m.ModuleId.Equals(moduleId, StringComparison.OrdinalIgnoreCase));
        if (chatModule != null)
        {
            if (_disabledModuleIds.Contains(chatModule.ModuleId))
                return "success";

            try
            {
                commandRegistry.UnregisterModuleCommands(chatModule.ModuleId);
                await chatModule.ShutdownAsync();
                _disabledModuleIds.Add(chatModule.ModuleId);
                logger.LogInformation("disabled chat module. id='{ModuleId}'", chatModule.ModuleId);
                return "success";
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "failed to disable chat module. id='{ModuleId}'", chatModule.ModuleId);
                return "error:exception";
            }
        }

        var cmdModule = _loadedCommandModules.FirstOrDefault(m => m.ModuleId.Equals(moduleId, StringComparison.OrdinalIgnoreCase));
        if (cmdModule != null)
        {
            if (_disabledModuleIds.Contains(cmdModule.ModuleId))
                return "success";

            try
            {
                commandRegistry.UnregisterModuleCommands(cmdModule.ModuleId);
                await cmdModule.ShutdownAsync();
                _disabledModuleIds.Add(cmdModule.ModuleId);
                logger.LogInformation("disabled command module. id='{ModuleId}'", cmdModule.ModuleId);
                return "success";
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "failed to disable command module. id='{ModuleId}'", cmdModule.ModuleId);
                return "error:exception";
            }
        }

        logger.LogError("module not found for disable. id='{ModuleId}'", moduleId);
        return "error:not_found";
    }

    public async Task<string> EnableModuleAsync(string moduleId, CancellationToken cancellationToken = default)
    {
        var chatModule = _loadedChatModules.FirstOrDefault(m => m.ModuleId.Equals(moduleId, StringComparison.OrdinalIgnoreCase));
        if (chatModule != null)
        {
            if (!_disabledModuleIds.Contains(chatModule.ModuleId))
                return "success";

            try
            {
                await chatModule.InitializeAsync(serviceProvider);

                foreach (var exportedCommand in chatModule.ExportedCommands)
                {
                    commandRegistry.RegisterModuleCommand(
                        chatModule.ModuleId,
                        exportedCommand.Factory,
                        exportedCommand.Metadata
                    );
                }

                _disabledModuleIds.Remove(chatModule.ModuleId);
                logger.LogInformation("enabled chat module. id='{ModuleId}'", chatModule.ModuleId);
                return "success";
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "failed to enable chat module. id='{ModuleId}'", chatModule.ModuleId);
                return "error:exception";
            }
        }

        var cmdModule = _loadedCommandModules.FirstOrDefault(m => m.ModuleId.Equals(moduleId, StringComparison.OrdinalIgnoreCase));
        if (cmdModule != null)
        {
            if (!_disabledModuleIds.Contains(cmdModule.ModuleId))
                return "success";

            try
            {
                await cmdModule.InitializeAsync(serviceProvider);

                foreach (var exportedCommand in cmdModule.ExportedCommands)
                {
                    commandRegistry.RegisterModuleCommand(
                        cmdModule.ModuleId,
                        exportedCommand.Factory,
                        exportedCommand.Metadata
                    );
                }

                _disabledModuleIds.Remove(cmdModule.ModuleId);
                logger.LogInformation("enabled command module. id='{ModuleId}'", cmdModule.ModuleId);
                return "success";
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "failed to enable command module. id='{ModuleId}'", cmdModule.ModuleId);
                return "error:exception";
            }
        }

        logger.LogError("module not found for enable. id='{ModuleId}'", moduleId);
        return "error:not_found";
    }

    public async Task<string> LoadModuleFromDiskAsync(string moduleId, CancellationToken cancellationToken = default)
    {
        // s0: already loaded - just reload
        var isChatLoaded = _loadedChatModules.Any(m => m.ModuleId.Equals(moduleId, StringComparison.OrdinalIgnoreCase));
        if (isChatLoaded)
            return await ReloadChatModuleAsync(moduleId, cancellationToken);

        var isCmdLoaded = _loadedCommandModules.Any(m => m.ModuleId.Equals(moduleId, StringComparison.OrdinalIgnoreCase));
        if (isCmdLoaded)
            return await ReloadCommandModuleAsync(moduleId, cancellationToken);

        // s1: try loading chat module from disk
        try
        {
            var chatModules = await chatModuleLoader.ReloadModuleAsync(moduleId, cancellationToken);
            if (chatModules.Count > 0)
            {
                foreach (var module in chatModules)
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
                    logger.LogInformation("loaded chat module from disk. id='{ModuleId}'", module.ModuleId);
                }

                return "success";
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "chat module not found on disk, trying command. id='{ModuleId}'", moduleId);
        }

        // s2: try loading command module from disk
        try
        {
            var commandModules = await commandModuleLoader.ReloadModuleAsync(moduleId, cancellationToken);
            if (commandModules.Count > 0)
            {
                foreach (var module in commandModules)
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
                    logger.LogInformation("loaded command module from disk. id='{ModuleId}'", module.ModuleId);
                }

                return "success";
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "command module not found on disk. id='{ModuleId}'", moduleId);
        }

        logger.LogError("module not found on disk. id='{ModuleId}'", moduleId);
        return "error:not_found_local";
    }

    public async Task<string> ReloadAllModulesAsync(CancellationToken cancellationToken = default)
    {
        var chatIds = _loadedChatModules.Select(m => m.ModuleId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var cmdIds = _loadedCommandModules.Select(m => m.ModuleId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        foreach (var id in chatIds)
        {
            await ReloadChatModuleAsync(id, cancellationToken);
        }

        foreach (var id in cmdIds)
        {
            await ReloadCommandModuleAsync(id, cancellationToken);
        }

        return "success";
    }

    public async Task<string> DisableAllModulesAsync(CancellationToken cancellationToken = default)
    {
        var chatIds = _loadedChatModules.Select(m => m.ModuleId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var cmdIds = _loadedCommandModules.Select(m => m.ModuleId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        foreach (var id in chatIds)
        {
            await DisableModuleAsync(id, cancellationToken);
        }

        foreach (var id in cmdIds)
        {
            await DisableModuleAsync(id, cancellationToken);
        }

        return "success";
    }

    public async Task<string> EnableAllModulesAsync(CancellationToken cancellationToken = default)
    {
        var ids = _disabledModuleIds.ToList();
        foreach (var id in ids)
        {
            await EnableModuleAsync(id, cancellationToken);
        }

        return "success";
    }
}
