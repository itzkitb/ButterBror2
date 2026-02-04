using ButterBror.Infrastructure.Storage;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace ButterBror.Infrastructure.Configuration;

public class ConfigService
{
    private readonly AppDataStorageProvider _storageProvider;
    private readonly ILogger<ConfigService> _logger;
    private readonly Dictionary<string, object> _configCache = new();

    public ConfigService(AppDataStorageProvider storageProvider, ILogger<ConfigService> logger)
    {
        _storageProvider = storageProvider;
        _logger = logger;
    }

    public T GetConfig<T>(string configName) where T : class, new()
    {
        if (_configCache.TryGetValue(configName, out var cached))
        {
            return (T)cached;
        }

        var configPath = _storageProvider.GetConfigFilePath($"{configName}.json");

        try
        {
            if (File.Exists(configPath))
            {
                var json = File.ReadAllText(configPath);
                var config = JsonSerializer.Deserialize<T>(json) ?? new T();
                _configCache[configName] = config;
                return config;
            }

            // Create a default config
            var defaultConfig = new T();
            SaveConfig(configName, defaultConfig);
            return defaultConfig;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading config: {ConfigName}", configName);
            return new T();
        }
    }

    public void SaveConfig<T>(string configName, T config)
    {
        var configPath = _storageProvider.GetConfigFilePath($"{configName}.json");

        try
        {
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(configPath, json);
            _configCache[configName] = config;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving config: {ConfigName}", configName);
            throw;
        }
    }
}