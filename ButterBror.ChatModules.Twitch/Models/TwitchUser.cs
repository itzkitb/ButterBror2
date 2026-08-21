using ButterBror.Domain;

namespace ButterBror.ChatModules.Twitch.Models;

public class TwitchUser(
    string username,
    string userId,
    HashSet<PlatformPermission> permissions)
    : IPlatformUser
{
    public string Id { get; } = userId;
    public string DisplayName { get; } = username;
    public string Platform { get; } = "sillyapps:twitch";
    public HashSet<PlatformPermission> Permissions { get; } = permissions;
}
