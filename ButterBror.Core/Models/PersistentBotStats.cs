namespace ButterBror.Core.Models;

/// <summary>
/// Persistent bot statistics stored in JSON
/// </summary>
public class PersistentBotStats
{
    /// <summary>
    /// Total commands executed across all sessions
    /// </summary>
    public long TotalCommandsExecuted { get; set; }

    /// <summary>
    /// Total replies sent across all sessions
    /// </summary>
    public long TotalRepliesSent { get; set; }

    /// <summary>
    /// Total uptime across all sessions
    /// </summary>
    public TimeSpan TotalUptime { get; set; }
}
