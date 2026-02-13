using System;

namespace ButterBror.Platforms.Twitch.Events;

public class OnNewSubscriberArgs : EventArgs
{
    public string Channel { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string SubscriptionPlan { get; set; } = string.Empty;
    public int Months { get; set; }
}