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
}
