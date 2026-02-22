using ButterBror.Core.Interfaces;

namespace ButterBror.ChatModules.Twitch.Services;

public class TwitchChannel : IPlatformChannel
{
    public TwitchChannel(string channelName, string channelId)
    {
        Id = channelId;
        Name = channelName;
        Platform = "sillyapps:twitch";
    }

    public string Id { get; }
    public string Name { get; }
    public string Platform { get; }
}
