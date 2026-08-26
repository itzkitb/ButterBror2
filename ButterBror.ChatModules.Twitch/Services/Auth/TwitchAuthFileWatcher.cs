using System.Text.Json;
using System.Text.Json.Serialization;
using ButterBror.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace ButterBror.ChatModules.Twitch.Services.Auth;

public sealed class TwitchAuthFileWatcher(
    IAppDataPathProvider appDataPathProvider,
    ITwitchTokenManager tokenManager,
    ILogger<TwitchAuthFileWatcher> logger) : IAsyncDisposable
{
    private readonly string _path = Path.Combine(appDataPathProvider.GetAppDataPath(), "TwitchAuth.json");
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _pollTask;

    public void Start()
    {
        _pollTask ??= PollAsync(_shutdown.Token);
    }

    public async ValueTask DisposeAsync()
    {
        await _shutdown.CancelAsync().ConfigureAwait(false);
        if (_pollTask is not null)
        {
            try { await _pollTask.ConfigureAwait(false); }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        }
        _shutdown.Dispose();
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            try
            {
                await TryImportAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "[tw:afw] credential polling failed");
            }
        }
    }

    private async Task TryImportAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
            return;

        TwitchAuthFile? auth;
        try
        {
            await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.None);
            auth = await JsonSerializer.DeserializeAsync<TwitchAuthFile>(stream, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException)
        {
            return;
        }
        catch (JsonException exception)
        {
            logger.LogError(exception, "[tw:afw] twitchauth.json is invalid; deleting it");
            DeleteFile();
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        if (auth is null || string.IsNullOrWhiteSpace(auth.OAuthToken) ||
            string.IsNullOrWhiteSpace(auth.RefreshToken) || auth.Ttl <= 0)
        {
            logger.LogError("[tw:afw] twitchauth.json is missing required credential fields; deleting it");
            DeleteFile();
            return;
        }

        try
        {
            var expiresAtUtc = auth.Ttl > 10_000_000_000
                ? DateTimeOffset.FromUnixTimeMilliseconds(auth.Ttl)
                : DateTimeOffset.FromUnixTimeSeconds(auth.Ttl);

            await tokenManager.SetBotCredentialAsync(new(
                Normalize(auth.OAuthToken),
                auth.RefreshToken,
                expiresAtUtc), cancellationToken).ConfigureAwait(false);
            DeleteFile();
        }
        catch (ArgumentOutOfRangeException)
        {
            logger.LogError("[tw:afw] twitchauth.json contains an invalid expiration timestamp; deleting it");
            DeleteFile();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "[tw:afw] twitchauth.json credential import failed");
            DeleteFile();
        }
    }

    private void DeleteFile()
    {
        try { File.Delete(_path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string Normalize(string token) => token.StartsWith("oauth:", StringComparison.OrdinalIgnoreCase)
        ? token["oauth:".Length..]
        : token;

    private sealed record TwitchAuthFile(
        [property: JsonPropertyName("OAuthToken")] string OAuthToken,
        [property: JsonPropertyName("RefreshToken")] string RefreshToken,
        [property: JsonPropertyName("Ttl")] long Ttl);
}
