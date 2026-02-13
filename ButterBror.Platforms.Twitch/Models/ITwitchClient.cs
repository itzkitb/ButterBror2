using System;
using TwitchLib.Client.Events;

namespace ButterBror.Platforms.Twitch.Models;

public interface ITwitchClient
{
    event EventHandler<Events.OnMessageReceivedArgs> OnMessageReceived;
    event EventHandler<OnConnectedEventArgs> OnConnected;
    event EventHandler<OnDisconnectedArgs> OnDisconnected;

    Task ConnectAsync(string username, string oauthToken, string clientId, string channel);
    Task DisconnectAsync();
}
