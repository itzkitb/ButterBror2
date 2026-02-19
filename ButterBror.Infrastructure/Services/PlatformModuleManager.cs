using ButterBror.ChatModules.Abstractions;
using ButterBror.ChatModules.Loader;
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
    private readonly ICommandRegistry _commandRegistry;
    private readonly IChatModuleLoader _chatModuleLoader;
    private readonly ILogger<PlatformModuleManager> _logger;

    private readonly List<IChatModule> _loadedChatModules = new();

    public PlatformModuleManager(
        IServiceProvider serviceProvider,
        IPlatformModuleRegistry moduleRegistry,
        ICommandRegistry commandRegistry,
        IChatModuleLoader chatModuleLoader,
        ILogger<PlatformModuleManager> logger)
    {
        _serviceProvider = serviceProvider;
        _moduleRegistry = moduleRegistry;
        _commandRegistry = commandRegistry;
        _chatModuleLoader = chatModuleLoader;
        _logger = logger;
    }

    public async Task InitializeAsync(IBotCore core, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Initializing platform modules...");

        // Initialize built-in modules from DI container
        var builtInModules = _serviceProvider.GetServices<IPlatformModule>();
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

        // Load and initialize chat modules from DLL files
        await LoadAndInitializeChatModulesAsync(core, cancellationToken);
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

    private async Task InitializeModuleAsync(IPlatformModule module, IBotCore core)
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

        // Unload chat module assemblies
        await _chatModuleLoader.UnloadModulesAsync(cancellationToken);
    }

    public IPlatformModule? GetModule(string platformName)
    {
        return _moduleRegistry.GetModules()
            .Concat(_loadedChatModules.OfType<IPlatformModule>())
            .FirstOrDefault(m => m.PlatformName.Equals(platformName, StringComparison.OrdinalIgnoreCase));
    }
}
