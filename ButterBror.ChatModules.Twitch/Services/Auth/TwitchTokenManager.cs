using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ButterBror.ChatModules.Twitch.Interfaces;
using ButterBror.ChatModules.Twitch.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ButterBror.ChatModules.Twitch.Services.Auth;

public sealed class TwitchTokenManager(
    IOptions<TwitchConfiguration> options,
    IHttpClientFactory httpClientFactory,
    ITwitchBotCredentialStore credentialStore,
    ILogger<TwitchTokenManager> logger) : ITwitchTokenManager
{
    private static readonly TimeSpan RefreshWindow = TimeSpan.FromMinutes(5);
    private readonly TwitchConfiguration _configuration = options.Value;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private TwitchTokenState _current = new(null, DateTimeOffset.MinValue, null);

    public TwitchTokenState Current => Volatile.Read(ref _current);
    public event EventHandler<TwitchTokenState>? StateChanged;
    public event EventHandler? CredentialRefreshFailed;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var credential = await credentialStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (credential is not null)
        {
            await SetBotCredentialAsync(credential, cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask<string> GetAppAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var state = Current;
        if (string.IsNullOrWhiteSpace(state.AppAccessToken) || state.AppAccessTokenExpiresAt - DateTimeOffset.UtcNow <= RefreshWindow)
            await RefreshAppTokenAsync(cancellationToken).ConfigureAwait(false);

        return Current.AppAccessToken!;
    }

    public async ValueTask<string> GetUserAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var credential = Current.BotCredential;
        if (credential is null)
            throw new InvalidOperationException("twitch bot credential is unavailable");
        if (credential.ExpiresAtUtc > DateTimeOffset.UtcNow + RefreshWindow)
            return credential.AccessToken ??
                   throw new InvalidOperationException("twitch bot credential is unavailable");
        
        await RefreshBotCredentialAsync(cancellationToken).ConfigureAwait(false);
        credential = Current.BotCredential;

        return credential?.AccessToken ?? throw new InvalidOperationException("twitch bot credential is unavailable");
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (Current.BotCredential is { } credential && credential.ExpiresAtUtc <= DateTimeOffset.UtcNow + RefreshWindow)
        {
            try
            {
                await RefreshBotCredentialAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await ClearBotCredentialAsync(cancellationToken).ConfigureAwait(false);
                CredentialRefreshFailed?.Invoke(this, EventArgs.Empty);
                logger.LogError(exception, "[tw:tm] bot credential refresh failed; waiting for twitchauth.json");
            }
        }

        if (Current.AppAccessToken is null || Current.AppAccessTokenExpiresAt <= DateTimeOffset.UtcNow + RefreshWindow)
            await RefreshAppTokenAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SetBotCredentialAsync(TwitchBotCredential credential, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(credential.AccessToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(credential.RefreshToken);
        var normalized = credential with { AccessToken = Normalize(credential.AccessToken) };
        var current = Current;
        await credentialStore.SaveAsync(normalized, cancellationToken).ConfigureAwait(false);
        var next = current with { BotCredential = normalized };
        Volatile.Write(ref _current, next);
        if (normalized.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            await RefreshAsync(cancellationToken).ConfigureAwait(false);
            return;
        }
        if (!string.Equals(current.BotCredential?.AccessToken, normalized.AccessToken, StringComparison.Ordinal))
            StateChanged?.Invoke(this, next);
    }

    public async Task ClearBotCredentialAsync(CancellationToken cancellationToken = default)
    {
        await credentialStore.ClearAsync(cancellationToken).ConfigureAwait(false);
        var current = Current;
        Volatile.Write(ref _current, current with { BotCredential = null });
        if (current.BotCredential is not null)
            StateChanged?.Invoke(this, Current);
    }

    private async Task RefreshBotCredentialAsync(CancellationToken cancellationToken)
    {
        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = Current.BotCredential ?? throw new InvalidOperationException("twitch bot credential is unavailable");
            if (current.ExpiresAtUtc > DateTimeOffset.UtcNow + RefreshWindow)
                return;

            using var response = await httpClientFactory.CreateClient("twitch-token").PostAsync(
                "oauth2/token",
                new FormUrlEncodedContent([
                    new KeyValuePair<string, string>("client_id", _configuration.ClientId),
                    new KeyValuePair<string, string>("client_secret", _configuration.ClientSecret),
                    new KeyValuePair<string, string>("refresh_token", current.RefreshToken),
                    new KeyValuePair<string, string>("grant_type", "refresh_token")
                ]),
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var token = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("twitch returned an empty token response");
            await SetBotCredentialAsync(new TwitchBotCredential(
                Normalize(token.AccessToken),
                string.IsNullOrWhiteSpace(token.RefreshToken) ? current.RefreshToken : token.RefreshToken,
                DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn)), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task RefreshAppTokenAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_configuration.ClientId) || string.IsNullOrWhiteSpace(_configuration.ClientSecret))
            throw new InvalidOperationException("Twitch ClientId and ClientSecret are required for an App Access Token.");

        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Current.AppAccessTokenExpiresAt > DateTimeOffset.UtcNow + RefreshWindow)
                return;
            using var response = await httpClientFactory.CreateClient("twitch-token").PostAsync(
                "oauth2/token",
                new FormUrlEncodedContent([
                    new("client_id", _configuration.ClientId),
                    new("client_secret", _configuration.ClientSecret),
                    new("grant_type", "client_credentials")
                ]),
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var token = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Twitch returned an empty app token response.");
            var next = Current with
            {
                AppAccessToken = token.AccessToken,
                AppAccessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn)
            };
            Volatile.Write(ref _current, next);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private static string Normalize(string token) => token.StartsWith("oauth:", StringComparison.OrdinalIgnoreCase)
        ? token["oauth:".Length..]
        : token;

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken);
}
