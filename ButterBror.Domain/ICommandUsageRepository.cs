using System;

namespace ButterBror.Domain;

public interface ICommandUsageRepository
{
    Task<DateTime?> GetLastUsedAsync(string commandId, Guid userId);
    Task SetLastUsedAsync(string commandId, Guid userId, DateTime timestamp);
}