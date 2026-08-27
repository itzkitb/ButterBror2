using ButterBror.ChatModules.Twitch.Events;

namespace ButterBror.ChatModules.Twitch.Interfaces;

public interface ITwitchChatTransport : IAsyncDisposable
{
    string Name { get; }
    bool IsConnected { get; }
    IReadOnlyCollection<string> ConnectedChannels { get; }
    event EventHandler<OnMessageReceivedArgs>? MessageReceived;
    event EventHandler<OnUserStateChangedArgs>? UserStateChanged;
    Task ConnectAsync(CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);
    Task JoinChannelAsync(string channel, CancellationToken cancellationToken = default);
    Task JoinChannelViaIrcAsync(string channel, CancellationToken cancellationToken = default);
    Task LeaveChannelAsync(string channel, CancellationToken cancellationToken = default);
    Task RefreshChannelAsync(string channel, CancellationToken cancellationToken = default);
    Task SendMessageAsync(string channel, string message, string? replyToMessageId = null, CancellationToken cancellationToken = default);
}