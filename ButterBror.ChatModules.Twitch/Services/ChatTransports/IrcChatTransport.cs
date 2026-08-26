using ButterBror.ChatModules.Twitch.Events;
using ButterBror.ChatModules.Twitch.Interfaces;
using ButterBror.ChatModules.Twitch.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TwitchLib.Client.Models;
using TwitchLib.Communication.Clients;
using TwitchLib.Communication.Models;
using TwitchChatMessage = ButterBror.ChatModules.Twitch.Models.ChatMessage;

namespace ButterBror.ChatModules.Twitch.Services.ChatTransports;

public sealed class IrcChatTransport : ITwitchChatTransport
{
    private readonly TwitchConfiguration _configuration;
    private readonly ITwitchTokenManager _tokenManager;
    private readonly ILogger<IrcChatTransport> _logger;
    private readonly TwitchLib.Client.TwitchClient _client;

    public IrcChatTransport(
        IOptions<TwitchConfiguration> options,
        ITwitchTokenManager tokenManager,
        ILogger<IrcChatTransport> logger)
    {
        _configuration = options.Value;
        _tokenManager = tokenManager;
        _logger = logger;
        _client = CreateClient();
        _client.OnMessageReceived += OnMessageReceived;
    }

    public string Name => "irc";
    public bool IsConnected => _client.IsConnected;
    public IReadOnlyCollection<string> ConnectedChannels => _client.JoinedChannels.Select(channel => channel.Channel).ToArray();
    public event EventHandler<OnMessageReceivedArgs>? MessageReceived;
    public event EventHandler<BroadcasterAuthReceivedArgs>? BroadcasterAuthReceived;

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (_client.IsConnected)
            await _client.DisconnectAsync().ConfigureAwait(false);
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_client.IsConnected)
            return;

        var token = await _tokenManager.GetUserAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("[tw] connecting irc");
        _client.Initialize(new ConnectionCredentials(_configuration.BotUsername, $"oauth:{token}", disableUsernameCheck: true));
        await _client.ConnectAsync().ConfigureAwait(false);
    }

    public Task JoinChannelAsync(string channel, CancellationToken cancellationToken = default) => _client.JoinChannelAsync(channel);
    public Task JoinChannelViaIrcAsync(string channel, CancellationToken cancellationToken = default) => _client.JoinChannelAsync(channel);
    public Task LeaveChannelAsync(string channel, CancellationToken cancellationToken = default) => _client.LeaveChannelAsync(channel);
    public Task RefreshChannelAsync(string channel, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task SendMessageAsync(string channel, string message, string? replyToMessageId = null, CancellationToken cancellationToken = default) =>
        replyToMessageId is null
            ? _client.SendMessageAsync(channel, message)
            : _client.SendReplyAsync(channel, replyToMessageId, message);

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        _client.OnMessageReceived -= OnMessageReceived;
    }

    private static TwitchLib.Client.TwitchClient CreateClient()
    {
        var options = new ClientOptions(new ReconnectionPolicy(3000, 10000, int.MaxValue));
        return new TwitchLib.Client.TwitchClient(new WebSocketClient(options));
    }

    private Task OnMessageReceived(object? sender, TwitchLib.Client.Events.OnMessageReceivedArgs args)
    {
        var message = args.ChatMessage;
        MessageReceived?.Invoke(this, new OnMessageReceivedArgs
        {
            ChatMessage = new TwitchChatMessage
            {
                Username = message.Username,
                UserId = message.UserId,
                MessageId = message.Id,
                Message = message.Message,
                Channel = message.Channel,
                ChannelId = message.RoomId,
                IsModerator = message.UserDetail.IsModerator,
                IsBroadcaster = message.IsBroadcaster,
                IsSubscriber = message.UserDetail.IsSubscriber,
                IsVip = message.UserDetail.IsVip,
                Badges = message.Badges,
                Color = message.HexColor
            }
        });
        return Task.CompletedTask;
    }
}
