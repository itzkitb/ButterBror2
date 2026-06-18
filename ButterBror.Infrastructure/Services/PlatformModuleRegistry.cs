using ButterBror.ChatModule;
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
        if (_modules.Any(m => m.ModuleId.Equals(module.ModuleId, StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogWarning("Module with platform name '{PlatformName}' is already registered", module.ModuleId);
            return;
        }

        _modules.Add(module);
    }

    public IEnumerable<IChatModule> GetModules() => _modules.AsReadOnly();

    public IChatModule? GetModuleById(string moduleId)
    {
        var module = _modules.FirstOrDefault(
            m => m.ModuleId.Equals(moduleId, StringComparison.OrdinalIgnoreCase));

        if (module == null)
        {
            _logger.LogWarning("Module with id '{PlatformName}' not found", moduleId);
        }

        return module;
    }
    
    public bool UnregisterModule(string platformName)
    {
        var module = _modules.FirstOrDefault(m => m.ModuleId.Equals(platformName, StringComparison.OrdinalIgnoreCase));
        if (module == null)
        {
            _logger.LogWarning("Module with platform name '{PlatformName}' not found", platformName);
            return false;
        }

        _modules.Remove(module);
        return true;
    }
}