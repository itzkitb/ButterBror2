namespace ButterBror.ChatModules.Twitch.Models;

public interface ITwitchChannelManager
{
    /// <summary>
    /// Gets the list of managed channels
    /// </summary>
    Task<List<string>> GetChannelsAsync();

    /// <summary>
    /// Adds a channel to the managed channels list
    /// </summary>
    Task AddChannelAsync(string channel);

    /// <summary>
    /// Removes a channel from the managed channels list
    /// </summary>
    Task RemoveChannelAsync(string channel);
}