namespace ButterBror.Core.Interfaces;

/// <summary>
/// Service for user configurations
/// </summary>
public interface IConfigurationService
{
    /// <summary>
    /// Load custom configuration
    /// </summary>
    /// <param name="key">Configuration file name ({AppData}/SillyApps/ButterBror2/{Config}.json)</param>
    /// <typeparam name="T">Data type in configuration</typeparam>
    /// <returns>Configuration data</returns>
    Task<T?> LoadConfigurationAsync<T>(string key);
    
    /// <summary>
    /// Save custom configuration
    /// </summary>
    /// <param name="key">Configuration file name ({AppData}/SillyApps/ButterBror2/{Config}.json)</param>
    /// <param name="value">Data</param>
    /// <typeparam name="T">Data type in configuration</typeparam>
    /// <returns></returns>
    Task SaveConfigurationAsync<T>(string key, T value);
}