using ButterBror.Domain;

namespace ButterBror.ChatModules.Twitch.Models;

public class TwitchUser(
    string username,
    string userId,
    bool isModerator,
    bool isBroadcaster,
    bool isBot = false)
    : IPlatformUser
{
    public string Id { get; } = userId;
    public string DisplayName { get; } = username;
    public string Platform { get; } = "sillyapps:twitch";
    public bool IsModerator { get; } = isModerator;
    public bool IsBroadcaster { get; } = isBroadcaster;
    public bool IsBot { get; } = isBot;
}
