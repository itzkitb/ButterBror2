using ButterBror.Core.Modules.Enums;
using ButterBror.Core.Modules.Interfaces;

namespace ButterBror.Application.Commands.Meta;

public class ModuleMeta : ICommandMetadata
{
    public string Name => "module";
    public List<string> Aliases => ["modules", "mod"];
    public int CooldownSeconds => 0;
    public List<string> RequiredPermissions => ["su:bb:modules"];
    public string ArgumentsHelpText => "<reload|disable|enable|reloadall|disableall|enableall|list|status|loadfromdisk> [moduleId]";
    public string Id => "bb:builtin:module";
    public PlatformCompatibilityType PlatformCompatibilityType => PlatformCompatibilityType.Blacklist;
    public List<string> PlatformCompatibilityList => [];
}
