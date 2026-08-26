namespace ButterBror.ChatModules.Twitch.Services;

public sealed record TwitchBotCredential(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAtUtc);