using System;
using TwitchLib.Client.Events;

namespace ButterBror.Platforms.Twitch.Models;

public interface ITwitchClient
{
    event EventHandler<Events.OnMessageReceivedArgs> OnMessageReceived;
    event EventHandler<OnConnectedEventArgs> OnConnected;
    event EventHandler<OnDisconnectedArgs> OnDisconnected;

    /// <summary>
    /// Connect a client
    /// </summary>
    Task ConnectAsync(string username, string oauthToken, string clientId, string channel);

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
}
