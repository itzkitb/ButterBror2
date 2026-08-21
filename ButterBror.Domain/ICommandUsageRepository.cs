using System;

namespace ButterBror.Domain;

/// <summary>
/// A repository for recording command usages
/// </summary>
public interface ICommandUsageRepository
{
    /// <summary>
    /// Get the last use of a command
    /// </summary>
    /// <param name="commandId">Command ID</param>
    /// <param name="userId">User ID</param>
    /// <returns>The date the command was last used</returns>
    Task<DateTime?> GetLastUsedAsync(string commandId, Guid userId);

    /// <summary>
    /// Set the last use of a command
    /// </summary>
    /// <param name="commandId">Command ID</param>
    /// <param name="userId">User ID</param>
    /// <param name="timestamp">Last used timestamp</param>
    /// <returns></returns>
    Task SetLastUsedAsync(string commandId, Guid userId, DateTime timestamp);
}