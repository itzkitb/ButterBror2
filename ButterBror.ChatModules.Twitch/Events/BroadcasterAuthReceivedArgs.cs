namespace ButterBror.ChatModules.Twitch.Events;

/// <summary>
/// Event arguments for when a broadcaster token is received via whisper
/// </summary>
public class BroadcasterAuthReceivedArgs : EventArgs
{
    public string UserId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
}