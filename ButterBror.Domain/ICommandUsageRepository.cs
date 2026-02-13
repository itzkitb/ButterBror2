using System;

namespace ButterBror.Domain;

public interface ICommandUsageRepository
{
    Task<DateTime?> GetLastUsedAsync(string commandId);
    Task SetLastUsedAsync(string commandId, DateTime timestamp);
}