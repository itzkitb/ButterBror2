using ButterBror.CommandModule.Commands;
using ButterBror.CommandModule.Enums;

namespace ButterBror.ChatModules.Twitch.Commands;

// Metadata for join command
internal class JoinChannelCommandMetadata : ICommandMetadata
{
    public string Name => "join";
    public List<string> Aliases => new List<string> { "jc" };
    public int CooldownSeconds => 5;
    public List<string> RequiredPermissions => new List<string> { "su:twitch:join" };
    public string ArgumentsHelpText => "<channel>";
    public string Id => "sillyapps:twitch:join";
    public PlatformCompatibilityType PlatformCompatibilityType => PlatformCompatibilityType.Whitelist;
    public List<string> PlatformCompatibilityList => new List<string> { "sillyapps:twitch" };
}

// Metadata for part command
internal class PartChannelCommandMetadata : ICommandMetadata
{
    public string Name => "part";
    public List<string> Aliases => new List<string> { "pc", "leave" };
    public int CooldownSeconds => 5;
    public List<string> RequiredPermissions => new List<string> { "su:twitch:part" };
    public string ArgumentsHelpText => "<channel>";
    public string Id => "sillyapps:twitch:part";
    public PlatformCompatibilityType PlatformCompatibilityType => PlatformCompatibilityType.Whitelist;
    public List<string> PlatformCompatibilityList => new List<string> { "sillyapps:twitch" };
}
