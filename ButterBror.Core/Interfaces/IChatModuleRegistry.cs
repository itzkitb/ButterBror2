using ButterBror.Core.Modules;

namespace ButterBror.Core.Interfaces;

public interface IChatModuleRegistry
{
    void RegisterModule(IChatModule module);
    IEnumerable<IChatModule> GetModules();
    /// <summary>
    /// Removes a module by PlatformName. Returns true if the module is found and removed
    /// </summary>
    bool UnregisterModule(string moduleId);
}