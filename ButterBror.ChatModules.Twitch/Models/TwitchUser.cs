using ButterBror.Domain;

namespace ButterBror.ChatModules.Twitch.Models;

public class TwitchUser : IPlatformUser
{
    public TwitchUser(
        string username, 
        string userId, 
        bool isModerator, 
        bool isBroadcaster,
        bool isBot = false)
    {
        Id = userId;
        DisplayName = username;
        Platform = "sillyapps:twitch";
        IsModerator = isModerator;
        IsBroadcaster = isBroadcaster;
        IsBot = isBot;
    }

    public string Id { get; }
    public string DisplayName { get; }
    public string Platform { get; }
    public bool IsModerator { get; }
    public bool IsBroadcaster { get; }
    public bool IsBot { get; }
}
