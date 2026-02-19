namespace ButterBror.ChatModules.Abstractions;

/// <summary>
/// Application Data Path Provider
/// </summary>
public interface IAppDataPathProvider
{
    /// <summary>
    /// Get the path to the application data directory
    /// </summary>
    string GetAppDataPath();
}
