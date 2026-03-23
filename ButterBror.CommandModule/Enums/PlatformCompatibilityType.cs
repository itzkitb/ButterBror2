namespace ButterBror.CommandModule.Enums;

/// <summary>
/// Defines the type of platform compatibility check for commands
/// </summary>
public enum PlatformCompatibilityType
{
    /// <summary>
    /// Only platforms in the list can execute the command (whitelist)
    /// </summary>
    Whitelist,
    
    /// <summary>
    /// Platforms in the list are excluded from executing the command (blacklist)
    /// </summary>
    Blacklist
}