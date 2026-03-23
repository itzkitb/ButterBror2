using ButterBror.CommandModule.Commands;
using ButterBror.CommandModule.Enums;

namespace ButterBror.Application.Commands.Meta;

public class BanphrasesCommandMeta : ICommandMetadata
{
    public string Name => "banphrases";
    public List<string> Aliases => new() { "bp", "banphrase" };
    public int CooldownSeconds => 5;
    public List<string> RequiredPermissions => new() { "su:banphrases" };
    public string ArgumentsHelpText => "<set|get|list|test|delete> <global|channel> [category] [pattern]";
    public string Id => "sillyapps:banphrases";
    public PlatformCompatibilityType PlatformCompatibilityType => PlatformCompatibilityType.Whitelist;
    public List<string> PlatformCompatibilityList => new() { "sillyapps:twitch", "sillyapps:discord", "sillyapps:telegram" };
}