using System.Text.Json.Serialization;

namespace ButterBror.CommandModule.CommandModule;

/// <summary>
/// Command module manifest
/// </summary>
public class CommandModuleManifest
{
    [JsonPropertyName("mainDll")]
    public string MainDll { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("author")]
    public string? Author { get; set; }
}