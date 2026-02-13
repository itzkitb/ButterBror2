using System;
using ButterBror.Platforms.Twitch.Models;

namespace ButterBror.Platforms.Twitch.Events;

public class OnMessageReceivedArgs : EventArgs
{
    public ChatMessage ChatMessage { get; set; } = new ChatMessage();
}
