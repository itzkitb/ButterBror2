using ButterBror.CommandModule.Commands;
using ButterBror.CommandModule.Enums;

namespace ButterBror.Application.Commands.Meta;

public class LocaleCommandMeta : ICommandMetadata
{
    public string Name => "locale";
    public List<string> Aliases => new() { "lang", "language" };
    public int CooldownSeconds => 5;
    public List<string> RequiredPermissions => new();
    public string ArgumentsHelpText => "set <locale> [url] | list | delete <locale> | view <locale> | reload";
    public string Id => "sillyapps:locale";
    public PlatformCompatibilityType PlatformCompatibilityType => PlatformCompatibilityType.Whitelist;
    public List<string> PlatformCompatibilityList => new() { "sillyapps:twitch", "sillyapps:discord", "sillyapps:telegram" };
}