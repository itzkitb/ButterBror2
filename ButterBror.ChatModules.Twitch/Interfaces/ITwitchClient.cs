using ButterBror.ChatModules.Twitch.Events;
using TwitchLib.Client.Events;

namespace ButterBror.ChatModules.Twitch.Models;

/// <summary>
/// Implementation of a twitch client
/// </summary>
public interface ITwitchClient
{
    /// <summary>
    /// Occurs when a chat message is received
    /// </summary>
    event EventHandler<Events.OnMessageReceivedArgs>? OnMessageReceived;

    /// <summary>
    /// Occurs when the client successfully connects to Twitch
    /// </summary>
    event EventHandler<OnConnectedEventArgs>? OnConnected;

    /// <summary>
    /// Occurs when the client disconnects from Twitch
    /// </summary>
    event EventHandler<OnDisconnectedArgs>? OnDisconnected;

    /// <summary>
    /// Occurs when a user joins a chat channel
    /// </summary>
    event EventHandler<Events.OnUserJoinedArgs>? OnUserJoined;

    /// <summary>
    /// Occurs when a user leaves a chat channel
    /// </summary>
    event EventHandler<Events.OnUserLeftArgs>? OnUserLeft;

    /// <summary>
    /// Occurs when a new subscription is received
    /// </summary>
    event EventHandler<Events.OnNewSubscriberArgs>? OnNewSubscriber;

    /// <summary>
    /// Occurs when a subscription is gifted to a user
    /// </summary>
    event EventHandler<Events.OnGiftedSubscriptionArgs>? OnGiftedSubscription;

    /// <summary>
    /// Occurs when a raid notification is received
    /// </summary>
    event EventHandler<Events.OnRaidNotificationArgs>? OnRaidNotification;

    /// <summary>
    /// Occurs when bits are cheered/received in a channel
    /// </summary>
    event EventHandler<OnBitsReceivedArgs>? OnBitsReceived;

    /// <summary>
    /// Occurs when broadcaster authorization is received
    /// </summary>
    event EventHandler<BroadcasterAuthReceivedArgs>? OnBroadcasterAuthReceived;

    /// <summary>
    /// Gets the set of channels the client is currently connected to
    /// </summary>
    HashSet<string> ConnectedChannels { get; }

    /// <summary>
    /// Gets a value indicating whether the client is currently connected to Twitch
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Check if the client is connected to a specific channel
    /// </summary>
    bool IsJoined(string channel);

    /// <summary>
    /// Try deleting the channel from the list
    /// </summary>
    Task<bool> TryRemoveChannelAsync(string channel);

    /// <summary>
    /// Add channel to list
    /// </summary>
    Task AddChannelAsync(string channel);

    /// <summary>
    /// Connect a client
    /// </summary>
    Task ConnectAsync(string username, string oauthToken, string clientId);

    /// <summary>
    /// Disconnect the client
    /// </summary>
    Task DisconnectAsync();

    /// <summary>
    /// Join the specified channel using the specified transport
    /// </summary>
    Task JoinChannelAsync(string channel);
    Task JoinBotChannelAsync(string channel);

    /// <summary>
    /// Leave the specified channel
    /// </summary>
    Task LeaveChannelAsync(string channel);

    /// <summary>
    /// Send a message to a specific channel
    /// </summary>
    Task SendMessageAsync(string channel, string message, bool convertChannelId = true);

    /// <summary>
    /// Reply to a message in a specific channel
    /// </summary>
    Task SendReplyAsync(string channel, string replyToMessageId, string message, bool convertChannelId = true);

    /// <summary>
    /// Whisper a message into the ear of one of the chatters
    /// </summary>
    Task SendWhisperAsync(string recipientUserId, string message);
    
    /// <summary>
    /// Invalidate the cached settings for a specific channel
    /// </summary>
    void InvalidateChannelSettingsCache(string channelId);

    /// <summary>
    /// Set the broadcaster token for a specific channel
    /// </summary>
    void SetBroadcasterToken(string channelId, string token);

    /// <summary>
    /// Get the broadcaster token for a specific channel if set
    /// </summary>
    string? GetBroadcasterToken(string channelId);

    /// <summary>
    /// Get the unique Twitch channel ID by channel name
    /// </summary>
    Task<string?> GetChannelIdAsync(string channelName);
    Task<TwitchManagedChannel?> ResolveChannelAsync(string channelOrId, CancellationToken cancellationToken = default);
    Task<TwitchManagedChannel?> ValidateBotTokenAsync(string accessToken, CancellationToken cancellationToken = default);
    Task UpgradeToEventSubAsync(string channelId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validate the provided broadcaster OAuth token
    /// </summary>
    Task<bool> ValidateBroadcasterTokenAsync(string token);

    /// <summary>
    /// Clear the IRC fallback connection state for a specific channel
    /// </summary>
    void ClearIrcFallback(string channelId);

    Task RefreshChannelAsync(string channelId);
}