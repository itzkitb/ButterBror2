using ButterBror.ChatModules.Twitch.Events;
using TwitchLib.Client.Events;

namespace ButterBror.ChatModules.Twitch.Models;

public interface ITwitchClient
{
    event EventHandler<Events.OnMessageReceivedArgs>? OnMessageReceived;
    event EventHandler<OnConnectedEventArgs>? OnConnected;
    event EventHandler<OnDisconnectedArgs>? OnDisconnected;
    event EventHandler<Events.OnUserJoinedArgs>? OnUserJoined;
    event EventHandler<Events.OnUserLeftArgs>? OnUserLeft;
    event EventHandler<Events.OnNewSubscriberArgs>? OnNewSubscriber;
    event EventHandler<Events.OnGiftedSubscriptionArgs>? OnGiftedSubscription;
    event EventHandler<Events.OnRaidNotificationArgs>? OnRaidNotification;
    event EventHandler<OnBitsReceivedArgs>? OnBitsReceived;

    HashSet<string> ConnectedChannels { get; }

    bool IsConnected { get; }

    /// <summary>
    /// Check if the client is connected to a specific channel
    /// </summary>
    bool IsJoined(string channel);

    /// <summary>
    /// Connect a client
    /// </summary>
    Task ConnectAsync(string username, string oauthToken, string clientId);

    /// <summary>
    /// Disconnect the client
    /// </summary>
    Task DisconnectAsync();

    /// <summary>
    /// Join the specified channel
    /// </summary>
    Task JoinChannelAsync(string channel);

    /// <summary>
    /// Leave the specified channel
    /// </summary>
    Task LeaveChannelAsync(string channel);

    /// <summary>
    /// Send a message to a specific channel
    /// </summary>
    Task SendMessageAsync(string channel, string message);

    /// <summary>
    /// Reply to a message in a specific channel
    /// </summary>
    Task SendReplyAsync(string channel, string replyToMessageId, string message);
}
