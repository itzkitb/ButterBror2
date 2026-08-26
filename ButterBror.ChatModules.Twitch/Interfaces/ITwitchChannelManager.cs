namespace ButterBror.ChatModules.Twitch.Models;

public interface ITwitchChannelManager
{
    /// <summary>
    /// Gets the list of managed channels
    /// </summary>
    Task<List<TwitchManagedChannel>> GetChannelsAsync();

    /// <summary>
    /// Adds a channel to the managed channels list
    /// </summary>
    Task AddChannelAsync(TwitchManagedChannel channel);

    /// <summary>
    /// Removes a channel from the managed channels list
    /// </summary>
    Task RemoveChannelAsync(string channel);
}