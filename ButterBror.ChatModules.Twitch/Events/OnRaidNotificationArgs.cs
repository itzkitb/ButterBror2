namespace ButterBror.ChatModules.Twitch.Events;

public class OnRaidNotificationArgs : EventArgs
{
    public string Channel { get; set; } = string.Empty;
    public string RaiderUsername { get; set; } = string.Empty;
    public int ViewerCount { get; set; }
}
