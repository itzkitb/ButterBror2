using ButterBror.Core.Interfaces;
using ButterBror.Core.Modules;
using Microsoft.Extensions.Logging;

namespace ButterBror.Infrastructure.Services;

public class PlatformModuleRegistry(ILogger<PlatformModuleRegistry> logger) : IChatModuleRegistry
{
    private readonly List<IChatModule> _modules = [];

    public void RegisterModule(IChatModule module)
    {
        if (_modules.Any(m => m.ModuleId.Equals(module.ModuleId, StringComparison.OrdinalIgnoreCase)))
        {
            logger.LogWarning("module is already registered. id={PlatformName}", module.ModuleId);
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
            logger.LogWarning("module not found. id={PlatformName}", moduleId);
        }

        return module;
    }
    
    public bool UnregisterModule(string moduleId)
    {
        var module = _modules.FirstOrDefault(m => m.ModuleId.Equals(moduleId, StringComparison.OrdinalIgnoreCase));
        if (module == null)
        {
            logger.LogWarning("module not found. id={PlatformName}", moduleId);
            return false;
        }

        _modules.Remove(module);
        return true;
    }
}