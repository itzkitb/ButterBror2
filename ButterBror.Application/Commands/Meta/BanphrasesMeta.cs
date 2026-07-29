using ButterBror.Core.Modules.Enums;
using ButterBror.Core.Modules.Interfaces;

namespace ButterBror.Application.Commands.Meta;

public class BanphrasesCommandMeta : ICommandMetadata
{
    public string Name => "banphrases";
    public List<string> Aliases => ["bp", "banphrase"];
    public int CooldownSeconds => 5;
    public List<string> RequiredPermissions => ["su:bb:banphrases"];
    public string ArgumentsHelpText => "<set|get|list|test|delete> <global|channel> [category] [pattern]";
    public string Id => "bb:builtin:banphrases";
    public PlatformCompatibilityType PlatformCompatibilityType => PlatformCompatibilityType.Blacklist;
    public List<string> PlatformCompatibilityList => [];
}