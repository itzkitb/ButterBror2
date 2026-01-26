using ButterBror.Core.Interfaces;

namespace ButterBror.Platforms.Twitch.Services;

public class TwitchChannel : IPlatformChannel
{
    public TwitchChannel(string channelName)
    {
        Id = channelName.ToLowerInvariant();
        Name = channelName;
        Platform = "Twitch";
    }

    public string Id { get; }
    public string Name { get; }
    public string Platform { get; }
}