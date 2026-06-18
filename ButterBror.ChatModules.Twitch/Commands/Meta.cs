using ButterBror.CommandModule.Commands;
using ButterBror.CommandModule.Enums;

namespace ButterBror.ChatModules.Twitch.Commands;

// Metadata for join command
internal class JoinChannelCommandMetadata : ICommandMetadata
{
    public string Name => "join";
    public List<string> Aliases => ["jc"];
    public int CooldownSeconds => 1;
    public List<string> RequiredPermissions => ["su:twitch:join"];
    public string ArgumentsHelpText => "<channel>";
    public string Id => "sillyapps:twitch:join";
    public PlatformCompatibilityType PlatformCompatibilityType => PlatformCompatibilityType.Whitelist;
    public List<string> PlatformCompatibilityList => ["sillyapps:twitch"];
}

// Metadata for part command
internal class PartChannelCommandMetadata : ICommandMetadata
{
    public string Name => "part";
    public List<string> Aliases => ["pc", "leave"];
    public int CooldownSeconds => 1;
    public List<string> RequiredPermissions => ["su:twitch:part"];
    public string ArgumentsHelpText => "<channel>";
    public string Id => "sillyapps:twitch:part";
    public PlatformCompatibilityType PlatformCompatibilityType => PlatformCompatibilityType.Whitelist;
    public List<string> PlatformCompatibilityList => ["sillyapps:twitch"];
}

// Metadata for setprefix command
internal class SetPrefixCommandMetadata : ICommandMetadata
{
    public string Name => "setprefix";
    public List<string> Aliases => ["prefix", "sp"];
    public int CooldownSeconds => 2;
    public List<string> RequiredPermissions => ["su:twitch:prefix"];
    public string ArgumentsHelpText => "<prefix>";
    public string Id => "sillyapps:twitch:setprefix";
    public PlatformCompatibilityType PlatformCompatibilityType => PlatformCompatibilityType.Whitelist;
    public List<string> PlatformCompatibilityList => ["sillyapps:twitch"];
}

// Metadata for auth command
internal class AuthCommandMetadata : ICommandMetadata
{
    public string Name => "auth";
    public List<string> Aliases => ["oauth", "login"];
    public int CooldownSeconds => 10;
    public List<string> RequiredPermissions => new List<string>();
    public string ArgumentsHelpText => "";
    public string Id => "sillyapps:twitch:auth";
    public PlatformCompatibilityType PlatformCompatibilityType => PlatformCompatibilityType.Whitelist;
    public List<string> PlatformCompatibilityList => ["sillyapps:twitch"];
}

// Metadata for addchannel command
internal class AddChannelCommandMetadata : ICommandMetadata
{
    public string Name => "addchannel";
    public List<string> Aliases => ["ac"];
    public int CooldownSeconds => 1;
    public List<string> RequiredPermissions => ["su:twitch:addchannel"];
    public string ArgumentsHelpText => "<channel>";
    public string Id => "sillyapps:twitch:addchannel";
    public PlatformCompatibilityType PlatformCompatibilityType => PlatformCompatibilityType.Whitelist;
    public List<string> PlatformCompatibilityList => ["sillyapps:twitch"];
}

// Metadata for deletechannel command
internal class DeleteChannelCommandMetadata : ICommandMetadata
{
    public string Name => "deletechannel";
    public List<string> Aliases => ["dc"];
    public int CooldownSeconds => 1;
    public List<string> RequiredPermissions => ["su:twitch:deletechannel"];
    public string ArgumentsHelpText => "<channel>";
    public string Id => "sillyapps:twitch:deletechannel";
    public PlatformCompatibilityType PlatformCompatibilityType => PlatformCompatibilityType.Whitelist;
    public List<string> PlatformCompatibilityList => ["sillyapps:twitch"];
}

// Metadata for channel settings command
internal class ChannelSettingsCommandMetadata : ICommandMetadata
{
    public string Name => "twitchset";
    public List<string> Aliases => ["ts"];
    public int CooldownSeconds => 1;
    public List<string> RequiredPermissions => ["su:twitch:settings"];
    public string ArgumentsHelpText => "<online|offline> <true|false>";
    public string Id => "sillyapps:twitch:settings";
    public PlatformCompatibilityType PlatformCompatibilityType => PlatformCompatibilityType.Whitelist;
    public List<string> PlatformCompatibilityList => ["sillyapps:twitch"];
}