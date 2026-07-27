using System.Text.Json.Serialization;

namespace ButterBror.ChatModules.Twitch.Models;

internal sealed class BroadcasterAuthPayload
{
    [JsonPropertyName("channel")]
    public string Channel { get; set; } = string.Empty;

    [JsonPropertyName("token")]
    public string Token { get; set; } = string.Empty;
}