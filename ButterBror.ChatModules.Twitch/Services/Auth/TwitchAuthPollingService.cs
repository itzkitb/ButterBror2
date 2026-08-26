using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ButterBror.ChatModules.Twitch.Models;
using ButterBror.Core.Interfaces;
using ButterBror.Data.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ButterBror.ChatModules.Twitch.Services.Auth;

public sealed class TwitchAuthPollingService(
    ILocalizationService? localization,
    IHttpClientFactory httpClientFactory,
    IOptions<TwitchConfiguration> options,
    ITwitchClient twitchClient,
    ITwitchChannelManager channelManager,
    ICustomDataRepository repository,
    ILogger<TwitchAuthPollingService> logger) : BackgroundService
{
    private readonly TwitchConfiguration _configuration = options.Value;
    private int _consecutiveFailures;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await PollAsync(stoppingToken).ConfigureAwait(false);
                _consecutiveFailures = 0;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _consecutiveFailures++;
                logger.LogError(exception, "[tw:aps] auth polling failed (attempt {Attempt})", _consecutiveFailures);
                if (_consecutiveFailures >= 5)
                    logger.LogWarning("[tw:aps] auth polling has failed five consecutive times; continuing retries");
            }
        }
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_configuration.BotApiToken))
            return;

        var client = httpClientFactory.CreateClient("twitch-auth-api");
        using var response = await SendWithRetryAsync(client, () =>
        {
            var request = new HttpRequestMessage(HttpMethod.Get,
                $"{_configuration.AuthApiBaseUrl.TrimEnd('/')}/auth/pending");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _configuration.BotApiToken);
            return request;
        }, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var pending = await response.Content.ReadFromJsonAsync<PendingAuthResponse>(cancellationToken).ConfigureAwait(false);
        if (pending?.Tokens is null)
            return;

        foreach (var token in pending.Tokens)
        {
            await ProcessTokenAsync(client, token, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ProcessTokenAsync(HttpClient client, PendingAuthToken token, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(token.EffectiveAccessToken))
                throw new InvalidOperationException("pending authorization did not contain an access token");
            var channel = await twitchClient.ValidateBotTokenAsync(token.EffectiveAccessToken, cancellationToken).ConfigureAwait(false);
            if (channel is null)
                throw new InvalidOperationException("token validation returned no user");

            twitchClient.SetBroadcasterToken(channel.Id, token.EffectiveAccessToken);
            await repository.SetDataAsync(
                $"twitch:broadcaster_token:{channel.Id}",
                token.EffectiveAccessToken).ConfigureAwait(false);
            await channelManager.AddChannelAsync(channel).ConfigureAwait(false);
            if (twitchClient.IsJoined(channel.Login))
                await twitchClient.UpgradeToEventSubAsync(channel.Id, cancellationToken).ConfigureAwait(false);
            else
                await twitchClient.JoinChannelAsync(channel.Login).ConfigureAwait(false);

            if (localization != null)
                await twitchClient.SendMessageAsync(channel.Id, 
                await localization.GetStringAsync("text.twitch.auth", "EN_US"));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "[tw:aps] pending broadcaster authorization {TokenId} could not be applied", token.Id);
        }
        finally
        {
            await AcknowledgeAsync(client, token.Id, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task AcknowledgeAsync(HttpClient client, string tokenId, CancellationToken cancellationToken)
    {
        using var response = await SendWithRetryAsync(client, () =>
        {
            var request = new HttpRequestMessage(HttpMethod.Delete,
                $"{_configuration.AuthApiBaseUrl.TrimEnd('/')}/auth/tokens/{Uri.EscapeDataString(tokenId)}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _configuration.BotApiToken);
            return request;
        }, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.NotFound)
            response.EnsureSuccessStatusCode();
    }

    private static async Task<HttpResponseMessage> SendWithRetryAsync(
        HttpClient client,
        Func<HttpRequestMessage> requestFactory,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var response = await client.SendAsync(requestFactory(), cancellationToken).ConfigureAwait(false);
                if ((int)response.StatusCode < 500 || attempt >= 3)
                    return response;
                response.Dispose();
            }
            catch (HttpRequestException) when (attempt < 3)
            {
            }

            await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed record PendingAuthResponse(
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("count")] int Count,
        [property: JsonPropertyName("tokens")] IReadOnlyList<PendingAuthToken>? Tokens);

    private sealed record PendingAuthToken(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("accessToken")] string? AccessToken,
        [property: JsonPropertyName("access_token")] string? SnakeCaseAccessToken)
    {
        public string EffectiveAccessToken => AccessToken ?? SnakeCaseAccessToken ?? string.Empty;
    }
}