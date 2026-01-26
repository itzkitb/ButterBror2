namespace ButterBror.Core.Interfaces;

public interface IPlatformModule
{
    string PlatformName { get; }
    Task InitializeAsync(IBotCore core);
    Task HandleIncomingMessageAsync(IMessage message);
    Task ShutdownAsync();
}
