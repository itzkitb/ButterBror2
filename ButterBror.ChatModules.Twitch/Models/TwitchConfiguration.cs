namespace ButterBror.ChatModules.Twitch.Models;

public class TwitchConfiguration
{
    public string BotUsername { get; set; } = "coolBotName";
    public string BotUserId { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = "https://tupid.lol/ba";
    public string AuthApiBaseUrl { get; set; } = "https://api.tupid.lol";
    public string BotApiToken { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public string CommandPrefix { get; set; } = "!";
    public TwitchReplyMode ReplyMode { get; set; } = TwitchReplyMode.Mention;
    public TwitchNotificationSettings Notifications { get; set; } = new();
}

public class TwitchNotificationSettings
{
    public bool Enabled { get; set; } = true;
    public string DefaultChannel { get; set; } = string.Empty;
    public List<string> GlobalChannels { get; set; } = [];
    public TwitchNotificationEventSettings IrcConnect { get; set; } = new();
    public TwitchNotificationEventSettings IrcReconnect { get; set; } = new();
    public TwitchNotificationEventSettings EventSubConnect { get; set; } = new();
    public TwitchNotificationEventSettings EventSubReconnect { get; set; } = new();
    public TwitchNotificationEventSettings ChannelJoin { get; set; } = new();
    public TwitchNotificationEventSettings ChannelPart { get; set; } = new();
    public TwitchNotificationEventSettings ChannelAdd { get; set; } = new();
    public TwitchNotificationEventSettings ChannelRemove { get; set; } = new();
}

public class TwitchNotificationEventSettings
{
    public bool Enabled { get; set; } = true;
    public List<string> Channels { get; set; } = [];
}