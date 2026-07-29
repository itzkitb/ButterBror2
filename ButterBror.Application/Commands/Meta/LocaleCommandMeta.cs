using ButterBror.Core.Modules.Enums;
using ButterBror.Core.Modules.Interfaces;

namespace ButterBror.Application.Commands.Meta;

public class LocaleCommandMeta : ICommandMetadata
{
    public string Name => "locale";
    public List<string> Aliases => ["lang", "language"];
    public int CooldownSeconds => 5;
    public List<string> RequiredPermissions => [];
    public string ArgumentsHelpText => "set <locale> [url] | list | delete <locale> | view <locale> | reload";
    public string Id => "bb:builtin:locale";
    public PlatformCompatibilityType PlatformCompatibilityType => PlatformCompatibilityType.Blacklist;
    public List<string> PlatformCompatibilityList => [];
}