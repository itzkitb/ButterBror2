using ButterBror.Domain.Entities;

namespace ButterBror.Data.Interfaces;

/// <summary>
/// Repository for error report storage
/// </summary>
public interface IErrorReportRepository
{
    /// <summary>
    /// Save error report to DB
    /// </summary>
    /// <param name="report">Report</param>
    Task SaveAsync(ErrorReport report);

    /// <summary>
    /// Get error report by ID
    /// </summary>
    /// <param name="errorId">Error ID</param>
    /// <returns>Error report</returns>
    Task<ErrorReport?> GetByIdAsync(Guid errorId);

    /// <summary>
    /// Get error reports by user ID
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <returns>All error reports from this user</returns>
    Task<IReadOnlyList<ErrorReport>> GetByUserIdAsync(Guid userId);
}