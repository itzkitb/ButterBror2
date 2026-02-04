namespace ButterBror.Platforms.Twitch.Services;

public class TwitchConfiguration
{
    public string BotUsername { get; set; } = "butterbror_bot";
    public string OauthToken { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
}