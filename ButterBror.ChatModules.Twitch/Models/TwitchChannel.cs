using ButterBror.Domain;

namespace ButterBror.ChatModules.Twitch.Models;

public class TwitchChannel(string channelName, string channelId) : IPlatformChannel
{
    public string Id { get; } = channelId;
    public string Name { get; } = channelName;
    public string Platform { get; } = "sillyapps:twitch";
}
