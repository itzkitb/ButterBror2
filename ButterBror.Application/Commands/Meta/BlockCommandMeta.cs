using ButterBror.Core.Modules.Enums;
using ButterBror.Core.Modules.Interfaces;

namespace ButterBror.Application.Commands.Meta;

public class BlockCommandMeta : ICommandMetadata
{
    public string Name => "block";
    public List<string> Aliases => ["ban", "unblock", "unban"];
    public int CooldownSeconds => 0;
    public List<string> RequiredPermissions => ["su:bb:block"];
    public string ArgumentsHelpText => "<user|global|platform|chat> [data]";
    public string Id => "bb:builtin:block";
    public PlatformCompatibilityType PlatformCompatibilityType => PlatformCompatibilityType.Blacklist;
    public List<string> PlatformCompatibilityList => [];
}