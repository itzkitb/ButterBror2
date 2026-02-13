using ButterBror.Core.Enums;

namespace ButterBror.Core.Contracts;

/// <summary>
/// Interface defining metadata for commands in the unified command system
/// </summary>
public interface ICommandMetadata
{
    /// <summary>
    /// Name of the command
    /// </summary>
    string Name { get; }
    
    /// <summary>
    /// Aliases for the command (alternative names to trigger the command)
    /// </summary>
    List<string> Aliases { get; }
    
    /// <summary>
    /// Cooldown period for the command per user in seconds
    /// </summary>
    int CooldownSeconds { get; }
    
    /// <summary>
    /// Required permissions for the command (user must have all listed permissions to execute)
    /// </summary>
    List<string> RequiredPermissions { get; }
    
    /// <summary>
    /// Help text showing command arguments and usage
    /// Example: "[action:clear|models] | [model:<name>] [history:ignore] <query>"
    /// </summary>
    string ArgumentsHelpText { get; }
    
    /// <summary>
    /// Unique ID for the command in format "author:command_id"
    /// Used for statistics and identification purposes
    /// </summary>
    string Id { get; }
    
    /// <summary>
    /// Type of platform compatibility check (whitelist/blacklist)
    /// </summary>
    PlatformCompatibilityType PlatformCompatibilityType { get; }
    
    /// <summary>
    /// List of platform IDs that are either allowed (whitelist) or denied (blacklist)
    /// Format: "author:platform_id"
    /// </summary>
    List<string> PlatformCompatibilityList { get; }
}