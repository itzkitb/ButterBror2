namespace ButterBror.Data.Interfaces;

public interface IDataRepository
{
    /// <summary>
    /// Write data to the database
    /// </summary>
    /// <param name="key">Data key</param>
    /// <param name="value">Data</param>
    /// <param name="expiry">Expires</param>
    /// <returns></returns>
    Task SetDataAsync(string key, string value, TimeSpan? expiry = null);
    
    /// <summary>
    /// Get data from the database
    /// </summary>
    /// <param name="key">Data key</param>
    /// <returns>Data for this key</returns>
    Task<string?> GetDataAsync(string key);
    
    /// <summary>
    /// Delete data by key
    /// </summary>
    /// <param name="key">Data key</param>
    /// <returns>Success</returns>
    Task<bool> DeleteDataAsync(string key);
    
    /// <summary>
    /// Scan the database
    /// </summary>
    /// <param name="pattern">Scanning pattern ("example:*")</param>
    /// <returns>Data</returns>
    Task<IReadOnlyDictionary<string, string>> ScanAsync(string pattern);
}