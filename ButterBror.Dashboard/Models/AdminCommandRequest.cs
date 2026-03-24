namespace ButterBror.Dashboard.Models;

/// <summary>
/// Request to execute an admin command from the dashboard
/// </summary>
public class AdminCommandRequest
{
    /// <summary>
    /// Full command line, e.g.: "sillyapps:twitch join forsen"
    /// First token = moduleId (or empty for global), rest = commandName + args
    /// </summary>
    public string CommandLine { get; set; } = string.Empty;
}
