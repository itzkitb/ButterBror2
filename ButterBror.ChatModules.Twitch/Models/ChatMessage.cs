namespace ButterBror.ChatModules.Twitch.Models;

public class ChatMessage
{
    public string Username { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public string ChannelId { get; set; } = string.Empty;
    public bool IsModerator { get; internal set; }
    public bool IsBroadcaster { get; internal set; }
    public bool IsSubscriber { get; internal set; }
    public bool IsVIP { get; internal set; }
    public List<KeyValuePair<string, string>> Badges { get; internal set; } = new();
    public string Color { get; internal set; } = string.Empty;
}
