namespace ButterBror.Core.Models;

/// <summary>
/// Represents a single log entry forwarded to the dashboard.
/// </summary>
public class LogEntry
{
    /// <summary>
    /// Log creation timestamp
    /// </summary>
    public DateTime Timestamp { get; set; }
    
    /// <summary>
    /// Log level
    /// </summary>
    public string Level { get; set; } = string.Empty;
    
    /// <summary>
    /// Category (class + namespace) from which the log was created
    /// </summary>
    public string Category { get; set; } = string.Empty;
    
    /// <summary>
    /// Message
    /// </summary>
    public string Message { get; set; } = string.Empty;
    
    /// <summary>
    /// Exception if an error was recorded
    /// </summary>
    public string? Exception { get; set; }
}
