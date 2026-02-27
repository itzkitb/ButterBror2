using System.Text.Json;
using ButterBror.Core.Interfaces;

namespace ButterBror.ChatModules.Twitch.Services;

/// <summary>
/// A service to load configuration from a Twitch.json file
/// </summary>
public class TwitchConfigurationService
{
    private readonly IAppDataPathProvider _appDataPathProvider;

    public TwitchConfigurationService(IAppDataPathProvider appDataPathProvider)
    {
        _appDataPathProvider = appDataPathProvider;
    }

    /// <summary>
    /// Loads the configuration from the Twitch.json file
    /// </summary>
    public TwitchConfiguration LoadConfiguration()
    {
        var appDataPath = _appDataPathProvider.GetAppDataPath();
        var configPath = Path.Combine(appDataPath, "Twitch.json");

        if (!File.Exists(configPath)) return new TwitchConfiguration();

        var json = File.ReadAllText(configPath);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        var config = JsonSerializer.Deserialize<TwitchConfiguration>(json, options) 
            ?? new TwitchConfiguration();

        return config;
    }
}
