namespace ButterBror.ChatModule;

public interface IChatModuleRegistry
{
    void RegisterModule(IChatModule module);
    IEnumerable<IChatModule> GetModules();
}
