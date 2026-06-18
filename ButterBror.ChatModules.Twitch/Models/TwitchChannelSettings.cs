namespace ButterBror.ChatModules.Twitch.Models;

public class TwitchChannelSettings
{
    public bool AllowOnline { get; set; } = true;
    public bool AllowOffline { get; set; } = true;
}

public class StreamStatusInfo
{
    public bool IsOnline { get; set; }
    public DateTime LastChecked { get; set; }
}