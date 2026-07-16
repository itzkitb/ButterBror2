namespace ButterBror.Core.Interfaces;

public interface IConfigurationService
{
    Task<T?> LoadConfigurationAsync<T>(string key);
    Task SaveConfigurationAsync<T>(string key, T value);
}