namespace ButterBror.ChatModules.Twitch.Events;

public sealed class TransportConnectionEventArgs : EventArgs
{
    public string TransportName { get; init; } = string.Empty;
}