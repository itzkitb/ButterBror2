namespace ButterBror.ChatModules.Twitch.Models;

public class TwitchMessageExtra
{
    public bool IsModerator { get; internal set; }
    public bool IsBroadcaster { get; internal set; }
    public bool IsSubscriber { get; internal set; }
    public bool IsVIP { get; internal set; }
    public string? Color { get; internal set; }
    public string? Channel { get; internal set; }
    public string? ChannelId { get; internal set; }
    public List<KeyValuePair<string, string>>? Badges { get; internal set; }
}