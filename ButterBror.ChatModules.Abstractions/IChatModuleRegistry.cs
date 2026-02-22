namespace ButterBror.ChatModules.Abstractions;

public interface IChatModuleRegistry
{
    void RegisterModule(IChatModule module);
    IEnumerable<IChatModule> GetModules();
}
