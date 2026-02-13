using System;

namespace ButterBror.Platforms.Twitch.Events;

public class OnUserLeftArgs : EventArgs
{
    public string Channel { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
}
