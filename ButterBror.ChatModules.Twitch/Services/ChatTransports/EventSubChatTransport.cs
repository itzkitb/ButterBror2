using System.Text;
using System.Text.Json;
using ButterBror.ChatModules.Twitch.Events;
using ButterBror.ChatModules.Twitch.Interfaces;
using ButterBror.ChatModules.Twitch.Models;
using ButterBror.Data.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TwitchLib.Api;
using TwitchLib.Api.Core.Enums;
using TwitchLib.Api.Helix.Models.Channels.SendChatMessage;
using TwitchLib.EventSub.Core.EventArgs.Channel;
using TwitchLib.EventSub.Websockets;

namespace ButterBror.ChatModules.Twitch.Services.ChatTransports;

public sealed class EventSubChatTransport : ITwitchChatTransport
{
    private readonly TwitchAPI _api;
    private readonly EventSubWebsocketClient _client;
    private readonly ITwitchTokenManager _tokens;
    private readonly ICustomDataRepository _repository;
    private readonly ILogger<EventSubChatTransport> _logger;
    private readonly TwitchConfiguration _configuration;
    private readonly HashSet<string> _subscriptions = new(StringComparer.OrdinalIgnoreCase);
    private string _botUserId = string.Empty;

    public EventSubChatTransport(
        TwitchAPI api,
        EventSubWebsocketClient client,
        ITwitchTokenManager tokens,
        ICustomDataRepository repository,
        IOptions<TwitchConfiguration> options,
        ILogger<EventSubChatTransport> logger)
    {
        _api = api;
        _client = client;
        _tokens = tokens;
        _repository = repository;
        _logger = logger;
        _configuration = options.Value;
        client.ChannelChatMessage += OnChatMessage;
        client.UserWhisperMessage += OnWhisperMessage;
    }

    public string Name => "eventsub";
    public bool IsConnected { get; private set; }
    public IReadOnlyCollection<string> ConnectedChannels => _subscriptions.ToArray();
    public event EventHandler<OnMessageReceivedArgs>? MessageReceived;
    public event EventHandler<BroadcasterAuthReceivedArgs>? BroadcasterAuthReceived;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        _botUserId = _configuration.BotUserId;
        _api.Settings.ClientId = _configuration.ClientId;
        _api.Settings.AccessToken = await _tokens.GetUserAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        await _client.ConnectAsync(new Uri("wss://eventsub.wss.twitch.tv/ws")).ConfigureAwait(false);
        IsConnected = true;
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
            return;
        await _client.DisconnectAsync().ConfigureAwait(false);
        IsConnected = false;
        _subscriptions.Clear();
    }

    public async Task SubscribeChannelAsync(string channel, CancellationToken cancellationToken = default)
    {
        var normalizedChannel = channel.TrimStart('#').ToLowerInvariant();
        if (_subscriptions.Contains(normalizedChannel))
            return;

        var broadcaster = await _api.Helix.Users.GetUsersAsync(logins: [channel]).ConfigureAwait(false);
        var broadcasterId = broadcaster.Users.FirstOrDefault()?.Id
            ?? throw new InvalidOperationException($"сhannel #{channel} was not found");
        var broadcasterToken = await _repository.GetDataAsync($"twitch:broadcaster_token:{broadcasterId}").ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(broadcasterToken))
            throw new InvalidOperationException("broadcaster authorization is unavailable");

        _api.Settings.ClientId = _configuration.ClientId;
            var botAccessToken = await _tokens.GetUserAccessTokenAsync(cancellationToken).ConfigureAwait(false);
            _api.Settings.AccessToken = botAccessToken;
        try
        {
            await _api.Helix.EventSub.CreateEventSubSubscriptionAsync(
                "channel.chat.message", "1",
                new Dictionary<string, string>
                {
                    ["broadcaster_user_id"] = broadcasterId,
                    ["user_id"] = _botUserId
                },
                EventSubTransportMethod.Websocket,
                _client.SessionId,
                accessToken: botAccessToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception) when (exception.Message.Contains("subscription already exists", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("[tw] eventsub subscription already exists for #{Channel}", normalizedChannel);
        }
        _subscriptions.Add(normalizedChannel);
    }

    public Task JoinChannelAsync(string channel, CancellationToken cancellationToken = default) =>
        SubscribeChannelAsync(channel, cancellationToken);

    public Task JoinChannelViaIrcAsync(string channel, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("eventsub transport cannot join via irc");

    public Task RefreshChannelAsync(string channel, CancellationToken cancellationToken = default) =>
        SubscribeChannelAsync(channel, cancellationToken);

    public Task LeaveChannelAsync(string channel, CancellationToken cancellationToken = default)
    {
        _subscriptions.Remove(channel.TrimStart('#').ToLowerInvariant());
        return Task.CompletedTask;
    }

    public async Task SendMessageAsync(string channel, string message, string? replyToMessageId = null, CancellationToken cancellationToken = default)
    {
        _api.Settings.ClientId = _configuration.ClientId;
        _api.Settings.AccessToken = await _tokens.GetAppAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        var users = await _api.Helix.Users.GetUsersAsync(logins: [channel]).ConfigureAwait(false);
        var broadcasterId = users.Users.FirstOrDefault()?.Id ?? throw new InvalidOperationException($"channel #{channel} was not found.");
        var senderId = string.IsNullOrWhiteSpace(_botUserId) ? _configuration.BotUserId : _botUserId;
        await _api.Helix.Chat.SendChatMessage(new SendChatMessageRequest
        {
            BroadcasterId = broadcasterId,
            SenderId = senderId,
            Message = message,
            ReplyParentMessageId = replyToMessageId
        }, _api.Settings.AccessToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        IsConnected = false;
        _client.ChannelChatMessage -= OnChatMessage;
        _client.UserWhisperMessage -= OnWhisperMessage;
        try
        {
            await _client.DisconnectAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "[tw:es] disconnect failed");
        }
    }

    private Task OnChatMessage(object? sender, ChannelChatMessageArgs args)
    {
        var message = args.Payload.Event;
        MessageReceived?.Invoke(this, new OnMessageReceivedArgs
        {
            ChatMessage = new ChatMessage
            {
                Username = message.ChatterUserName,
                UserId = message.ChatterUserId,
                MessageId = message.MessageId,
                Message = message.Message.Text,
                Channel = message.BroadcasterUserLogin,
                ChannelId = message.BroadcasterUserId,
                IsModerator = message.IsModerator,
                IsBroadcaster = message.IsBroadcaster,
                IsSubscriber = message.IsSubscriber,
                IsVip = message.IsVip,
                Color = message.Color
            }
        });
        return Task.CompletedTask;
    }

    private Task OnWhisperMessage(object? sender, TwitchLib.EventSub.Core.EventArgs.User.UserWhisperMessageArgs args)
    {
        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(args.Payload.Event.Whisper.Text.Trim()));
            var payload = JsonSerializer.Deserialize<BroadcasterAuthPayload>(json);
            if (payload is { Channel.Length: > 0, Token.Length: > 0 })
            {
                BroadcasterAuthReceived?.Invoke(this, new BroadcasterAuthReceivedArgs
                {
                    UserId = args.Payload.Event.FromUserId,
                    Username = args.Payload.Event.FromUserName,
                    Channel = payload.Channel,
                    Token = payload.Token
                });
            }
        }
        catch (FormatException)
        {
            _logger.LogDebug("[tw:es] ignored non-bootstrap whisper");
        }
        catch (JsonException)
        {
            _logger.LogWarning("[tw:es] ignored malformed bootstrap whisper");
        }
        return Task.CompletedTask;
    }
}
