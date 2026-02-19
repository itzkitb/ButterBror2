using System.Text.Json.Serialization;

namespace ButterBror.ChatModules.Abstractions;

/// <summary>
/// Chat module manifest
/// </summary>
public class ModuleManifest
{
    /// <summary>
    /// Module master DLL
    /// </summary>
    [JsonPropertyName("mainDll")]
    public string MainDll { get; set; } = string.Empty;

    /// <summary>
    /// Module name
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Module version
    /// </summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Module description
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Author of the module
    /// </summary>
    [JsonPropertyName("author")]
    public string? Author { get; set; }
}
