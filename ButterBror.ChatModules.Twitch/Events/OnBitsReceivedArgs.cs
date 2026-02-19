namespace ButterBror.ChatModules.Twitch.Events;

public class OnBitsReceivedArgs : EventArgs
{
    public string Channel { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public int Bits { get; set; }
    public string Message { get; set; } = string.Empty;
}
