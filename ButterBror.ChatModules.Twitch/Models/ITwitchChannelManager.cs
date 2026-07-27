namespace ButterBror.ChatModules.Twitch.Models;

public interface ITwitchChannelManager
{
    Task<List<string>> GetChannelsAsync();
    Task AddChannelAsync(string channel);
    Task RemoveChannelAsync(string channel);
}