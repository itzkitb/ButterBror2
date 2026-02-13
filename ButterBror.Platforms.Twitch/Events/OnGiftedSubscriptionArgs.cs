using System;

namespace ButterBror.Platforms.Twitch.Events;

public class OnGiftedSubscriptionArgs : EventArgs
{
    public string Channel { get; set; } = string.Empty;
    public string GifterUsername { get; set; } = string.Empty;
    public string RecipientUsername { get; set; } = string.Empty;
    public string SubscriptionPlan { get; set; } = string.Empty;
}