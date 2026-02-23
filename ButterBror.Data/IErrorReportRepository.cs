using ButterBror.Domain.Entities;

namespace ButterBror.Data;

/// <summary>
/// Repository for error report storage
/// </summary>
public interface IErrorReportRepository
{
    /// <summary>
    /// Save error report to DB
    /// </summary>
    Task SaveAsync(ErrorReport report);

    /// <summary>
    /// Get error report by ID
    /// </summary>
    Task<ErrorReport?> GetByIdAsync(string errorId);

    /// <summary>
    /// Get error reports by user ID
    /// </summary>
    Task<IReadOnlyList<ErrorReport>> GetByUserIdAsync(Guid userId);
}