namespace ButterBror.ChatModules.Twitch.Events;

public class OnUserJoinedArgs : EventArgs
{
    public string Channel { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
}
