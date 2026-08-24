using ButterBror.Domain;

namespace ButterBror.ChatModules.Twitch.Models;

public class TwitchChat(string channelName, string channelId) : IPlatformChat
{
    public string Id { get; } = channelId;
    public string Name { get; } = channelName;
    public string Platform { get; } = "sillyapps:twitch";
}
