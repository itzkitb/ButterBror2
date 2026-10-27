using ButterBror.Core.Modules.Enums;
using ButterBror.Core.Modules.Interfaces;

namespace ButterBror.ChatModules.Twitch.Commands;

// commands metadata
// ><> join
internal class JoinChannelCommandMetadata : ICommandMetadata
{
    public string Name => "join";
    public List<string> Aliases => ["jc"];
    public int CooldownSeconds => 1;
    public List<string> RequiredPermissions => ["su:twitch:join"];
    public string ArgumentsHelpText => "<channel>";
    public string Id => "tupidapps:twitch:join";
    public PlatformCompatibilityType PlatformCompatibilityType => PlatformCompatibilityType.Whitelist;
    public List<string> PlatformCompatibilityList => ["tupidapps:twitch"];
}

// ><> part
internal class PartChannelCommandMetadata : ICommandMetadata
{
    public string Name => "part";
    public List<string> Aliases => ["pc", "leave"];
    public int CooldownSeconds => 1;
    public List<string> RequiredPermissions => ["su:twitch:part"];
    public string ArgumentsHelpText => "<channel>";
    public string Id => "tupidapps:twitch:part";
    public PlatformCompatibilityType PlatformCompatibilityType => PlatformCompatibilityType.Whitelist;
    public List<string> PlatformCompatibilityList => ["tupidapps:twitch"];
}

// ><> set prefix
internal class SetPrefixCommandMetadata : ICommandMetadata
{
    public string Name => "setprefix";
    public List<string> Aliases => ["prefix", "sp"];
    public int CooldownSeconds => 2;
    public List<string> RequiredPermissions => [];
    public string ArgumentsHelpText => "<prefix>";
    public string Id => "tupidapps:twitch:setprefix";
    public PlatformCompatibilityType PlatformCompatibilityType => PlatformCompatibilityType.Whitelist;
    public List<string> PlatformCompatibilityList => ["tupidapps:twitch"];
}

// ><> auth
internal class AuthCommandMetadata : ICommandMetadata
{
    public string Name => "auth";
    public List<string> Aliases => ["oauth", "login"];
    public int CooldownSeconds => 10;
    public List<string> RequiredPermissions => new List<string>();
    public string ArgumentsHelpText => "";
    public string Id => "tupidapps:twitch:auth";
    public PlatformCompatibilityType PlatformCompatibilityType => PlatformCompatibilityType.Whitelist;
    public List<string> PlatformCompatibilityList => ["tupidapps:twitch"];
}

// ><> add channel
internal class AddChannelCommandMetadata : ICommandMetadata
{
    public string Name => "addchannel";
    public List<string> Aliases => ["ac"];
    public int CooldownSeconds => 1;
    public List<string> RequiredPermissions => ["su:twitch:addchannel"];
    public string ArgumentsHelpText => "<channel>";
    public string Id => "tupidapps:twitch:addchannel";
    public PlatformCompatibilityType PlatformCompatibilityType => PlatformCompatibilityType.Whitelist;
    public List<string> PlatformCompatibilityList => ["tupidapps:twitch"];
}

// ><> delete channel
internal class DeleteChannelCommandMetadata : ICommandMetadata
{
    public string Name => "deletechannel";
    public List<string> Aliases => ["dc"];
    public int CooldownSeconds => 1;
    public List<string> RequiredPermissions => ["su:twitch:deletechannel"];
    public string ArgumentsHelpText => "<channel>";
    public string Id => "tupidapps:twitch:deletechannel";
    public PlatformCompatibilityType PlatformCompatibilityType => PlatformCompatibilityType.Whitelist;
    public List<string> PlatformCompatibilityList => ["tupidapps:twitch"];
}

// ><> twitch settings
internal class ChannelSettingsCommandMetadata : ICommandMetadata
{
    public string Name => "twitchset";
    public List<string> Aliases => ["ts"];
    public int CooldownSeconds => 1;
    public List<string> RequiredPermissions => [];
    public string ArgumentsHelpText => "<online|offline> <true|false>";
    public string Id => "tupidapps:twitch:settings";
    public PlatformCompatibilityType PlatformCompatibilityType => PlatformCompatibilityType.Whitelist;
    public List<string> PlatformCompatibilityList => ["tupidapps:twitch"];
}