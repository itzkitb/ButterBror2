using System.Text.Json;
using System.Text.Json.Serialization;
using ButterBror.Core.Interfaces;

namespace ButterBror.Infrastructure.Services;

public class ConfigurationService : IConfigurationService
{
    private readonly IAppDataPathProvider _appDataPathProvider;
    private readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };
    
    public ConfigurationService(IAppDataPathProvider appDataPathProvider)
    {
        _appDataPathProvider = appDataPathProvider;
    }
    
    public async Task<T?> LoadConfigurationAsync<T>(string key)
    {
        var appDataPath = _appDataPathProvider.GetAppDataPath();
        var configPath = Path.Combine(appDataPath, $"{key}.json");

        if (!File.Exists(configPath)) return default;

        var json = await File.ReadAllTextAsync(configPath);
        var config = JsonSerializer.Deserialize<T>(json, _options) 
                     ?? default;

        return config;
    }

    public async Task SaveConfigurationAsync<T>(string key, T value)
    {
        if (value == null)
        {
            throw new ArgumentNullException(nameof(value), "The configuration cannot be null");
        }

        var appDataPath = _appDataPathProvider.GetAppDataPath();
        
        if (!Directory.Exists(appDataPath))
        {
            Directory.CreateDirectory(appDataPath);
        }

        var configPath = Path.Combine(appDataPath, $"{key}.json");
        
        using var stream = File.Create(configPath);
        await JsonSerializer.SerializeAsync(stream, value, _options);
    }
}