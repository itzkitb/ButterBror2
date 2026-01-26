namespace ButterBror.Core.Interfaces;

public interface IPlatformModuleRegistry
{
    void RegisterModule(IPlatformModule module);
    IEnumerable<IPlatformModule> GetModules();
}
