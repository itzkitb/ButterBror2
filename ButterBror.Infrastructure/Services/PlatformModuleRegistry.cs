using ButterBror.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace ButterBror.Infrastructure.Services;

public class PlatformModuleRegistry : IPlatformModuleRegistry
{
    private readonly List<IPlatformModule> _modules = new();
    private readonly ILogger<PlatformModuleRegistry> _logger;

    public PlatformModuleRegistry(ILogger<PlatformModuleRegistry> logger)
    {
        _logger = logger;
    }

    public void RegisterModule(IPlatformModule module)
    {
        if (_modules.Any(m => m.PlatformName.Equals(module.PlatformName, StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogWarning("Module with platform name '{PlatformName}' is already registered", module.PlatformName);
            return;
        }

        _modules.Add(module);
        _logger.LogInformation("Registered platform module: {PlatformName}", module.PlatformName);
    }

    public IEnumerable<IPlatformModule> GetModules() => _modules.AsReadOnly();
}