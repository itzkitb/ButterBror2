namespace ButterBror.ChatModules.Twitch.Events;

public sealed class OnUserStateChangedArgs : EventArgs
{
    public string Channel { get; init; } = string.Empty;
    public bool IsModerator { get; init; }
    public bool IsVip { get; init; }
}