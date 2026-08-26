namespace ButterBror.ChatModules.Twitch.Services;

public sealed record TwitchTokenState(
    string? AppAccessToken,
    DateTimeOffset AppAccessTokenExpiresAt,
    TwitchBotCredential? BotCredential)
{
    public bool HasUserToken => BotCredential is not null;
}

public interface ITwitchTokenManager
{
    TwitchTokenState Current { get; }
    event EventHandler<TwitchTokenState>? StateChanged;
    event EventHandler? CredentialRefreshFailed;
    Task InitializeAsync(CancellationToken cancellationToken = default);
    ValueTask<string> GetAppAccessTokenAsync(CancellationToken cancellationToken = default);
    ValueTask<string> GetUserAccessTokenAsync(CancellationToken cancellationToken = default);
    Task RefreshAsync(CancellationToken cancellationToken = default);
    Task SetBotCredentialAsync(TwitchBotCredential credential, CancellationToken cancellationToken = default);
    Task ClearBotCredentialAsync(CancellationToken cancellationToken = default);
}
