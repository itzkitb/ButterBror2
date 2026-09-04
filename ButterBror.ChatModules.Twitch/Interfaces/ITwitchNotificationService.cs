namespace ButterBror.ChatModules.Twitch.Interfaces;

public interface ITwitchNotificationService
{
    void SetClient(ITwitchClient client);
    Task NotifyChannelJoinedAsync(string channel, string executor, CancellationToken cancellationToken = default);
    Task NotifyChannelPartedAsync(string channel, string executor, CancellationToken cancellationToken = default); 
    Task NotifyChannelAddedAsync(string channel, string executor = "system", CancellationToken cancellationToken = default);
    Task NotifyChannelRemovedAsync(string channel, string executor, CancellationToken cancellationToken = default);
}