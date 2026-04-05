using System;

namespace ButterBror.Data;

public interface ICustomDataRepository
{
    Task SetDataAsync(string key, string value, TimeSpan? expiry = null);
    Task<string?> GetDataAsync(string key);
    Task<bool> DeleteDataAsync(string key);
}
