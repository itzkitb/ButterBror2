using ButterBror.ChatModules.Abstractions;
using Microsoft.Extensions.Logging;

namespace ButterBror.Infrastructure.Services;

public class PlatformModuleRegistry : IChatModuleRegistry
{
    private readonly List<IChatModule> _modules = new();
    private readonly ILogger<PlatformModuleRegistry> _logger;

    public PlatformModuleRegistry(ILogger<PlatformModuleRegistry> logger)
    {
        _logger = logger;
    }

    public void RegisterModule(IChatModule module)
    {
        if (_modules.Any(m => m.PlatformName.Equals(module.PlatformName, StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogWarning("Module with platform name '{PlatformName}' is already registered", module.PlatformName);
            return;
        }

        _modules.Add(module);
        _logger.LogInformation("Registered platform module: {PlatformName}", module.PlatformName);
    }

    public IEnumerable<IChatModule> GetModules() => _modules.AsReadOnly();
}