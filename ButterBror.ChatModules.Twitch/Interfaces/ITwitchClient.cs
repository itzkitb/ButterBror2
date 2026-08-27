using ButterBror.ChatModules.Twitch.Models;

namespace ButterBror.ChatModules.Twitch.Interfaces;

/// <summary>
/// High-level orchestration interface for Twitch interactions.
/// Handles message queuing, caching, and API routing, delegating raw transport to ITwitchChatTransport.
/// </summary>
public interface ITwitchClient
{
    #region Events
    /// <summary>
    /// Occurs when a chat message is received.
    /// </summary>
    event EventHandler<Events.OnMessageReceivedArgs>? OnMessageReceived;

    /// <summary>
    /// Occurs when the client successfully connects to Twitch.
    /// </summary>
    event EventHandler<Events.OnConnectedArgs>? OnConnected;

    /// <summary>
    /// Occurs when the client disconnects from Twitch.
    /// </summary>
    event EventHandler<Events.OnDisconnectedArgs>? OnDisconnected;
    
    // Note: Subscription, raid, and bits events have been removed from this high-level interface.
    // If required, they should be exposed via ITwitchChatTransport (EventSub implementation).
    #endregion

    #region Properties
    /// <summary>
    /// Gets the set of channels the client is currently connected to.
    /// </summary>
    IReadOnlyCollection<string> ConnectedChannels { get; }

    /// <summary>
    /// Gets a value indicating whether the client is currently connected to Twitch.
    /// </summary>
    bool IsConnected { get; }
    #endregion

    #region Channel Management
    /// <summary>
    /// Check if the client is connected to a specific channel.
    /// </summary>
    bool IsJoined(string channel);

    /// <summary>
    /// Add channel to the managed list and join if connected.
    /// </summary>
    Task AddChannelAsync(string channel);

    /// <summary>
    /// Try removing the channel from the managed list and leave if joined.
    /// </summary>
    Task<bool> TryRemoveChannelAsync(string channel);

    /// <summary>
    /// Join the specified channel using the primary transport.
    /// </summary>
    Task JoinChannelAsync(string channel);

    /// <summary>
    /// Force join the specified channel via IRC transport (fallback mechanism).
    /// </summary>
    Task JoinBotChannelAsync(string channel);

    /// <summary>
    /// Leave the specified channel.
    /// </summary>
    Task LeaveChannelAsync(string channel);

    /// <summary>
    /// Refresh the transport connection for a specific channel (e.g., upgrade from IRC to EventSub).
    /// </summary>
    Task RefreshChannelAsync(string channelId);

    /// <summary>
    /// Upgrade a channel's subscription to EventSub.
    /// </summary>
    Task UpgradeToEventSubAsync(string channelId, CancellationToken cancellationToken = default);
    #endregion

    #region Connection Management
    /// <summary>
    /// Connect the client and initialize all managed channels.
    /// </summary>
    Task ConnectAsync(string username, string oauthToken, string clientId);

    /// <summary>
    /// Disconnect the client from all transports.
    /// </summary>
    Task DisconnectAsync();
    #endregion

    #region Messaging
    /// <summary>
    /// Send a message to a specific channel (queued and rate-limited).
    /// </summary>
    Task SendMessageAsync(string channel, string message, bool convertChannelId = true);

    /// <summary>
    /// Reply to a specific message in a channel (queued and rate-limited).
    /// </summary>
    Task SendReplyAsync(string channel, string replyToMessageId, string message, bool convertChannelId = true);

    /// <summary>
    /// Send a whisper to a specific user via Helix API.
    /// </summary>
    Task SendWhisperAsync(string recipientUserId, string message);
    #endregion

    #region Tokens & Resolution
    /// <summary>
    /// Set the broadcaster token for a specific channel.
    /// </summary>
    void SetBroadcasterToken(string channelId, string token);

    /// <summary>
    /// Get the broadcaster token for a specific channel if set.
    /// </summary>
    string? GetBroadcasterToken(string channelId);

    /// <summary>
    /// Validate the provided broadcaster OAuth token via Twitch API.
    /// </summary>
    Task<bool> ValidateBroadcasterTokenAsync(string token);

    /// <summary>
    /// Get the unique Twitch channel ID by channel name or ID.
    /// </summary>
    Task<string?> GetChannelIdAsync(string channelName);

    /// <summary>
    /// Resolve a channel name or ID into a managed channel object.
    /// </summary>
    Task<TwitchManagedChannel?> ResolveChannelAsync(string channelOrId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validate a bot token and return the associated user information.
    /// </summary>
    Task<TwitchManagedChannel?> ValidateBotTokenAsync(string accessToken, CancellationToken cancellationToken = default);
    #endregion

    #region Cache Management
    /// <summary>
    /// Invalidate the cached settings for a specific channel.
    /// </summary>
    void InvalidateChannelSettingsCache(string channelId);
    #endregion
}