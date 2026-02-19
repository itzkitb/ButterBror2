using ButterBror.Core.Interfaces;

namespace ButterBror.ChatModules.Twitch.Services;

public class TwitchUser : IPlatformUser
{
    public TwitchUser(string username, string userId, bool isModerator, bool isBroadcaster)
    {
        Id = userId;
        DisplayName = username;
        Platform = "sillyapps:twitch";
        IsModerator = isModerator;
        IsBroadcaster = isBroadcaster;
    }

    public string Id { get; }
    public string DisplayName { get; }
    public string Platform { get; }
    public bool IsModerator { get; }
    public bool IsBroadcaster { get; }
}
