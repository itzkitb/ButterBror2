namespace ButterBror.Domain.Entities;

/// <summary>
/// Error report entity for tracking application errors
/// </summary>
public class ErrorReport
{
    public string ErrorId { get; set; } = string.Empty;
    public string ExceptionType { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? StackTrace { get; set; }
    public string? InnerException { get; set; }
    public Dictionary<string, object?> ExtraData { get; set; } = new();
    public Guid? UserId { get; set; }
    public string? Platform { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsResolved { get; set; }
}