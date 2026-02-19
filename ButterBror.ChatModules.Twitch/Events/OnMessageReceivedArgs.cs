using ButterBror.ChatModules.Twitch.Models;

namespace ButterBror.ChatModules.Twitch.Events;

public class OnMessageReceivedArgs : EventArgs
{
    public ChatMessage ChatMessage { get; set; } = new();
}
