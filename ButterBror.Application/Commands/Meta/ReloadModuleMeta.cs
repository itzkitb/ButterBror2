using ButterBror.Core.Modules.Enums;
using ButterBror.Core.Modules.Interfaces;

namespace ButterBror.Application.Commands.Meta;

public class ReloadModuleMeta : ICommandMetadata
{
    public string Name => "reloadmodule";
    public List<string> Aliases => ["rlmod", "reloadmod"];
    public int CooldownSeconds => 0;
    public List<string> RequiredPermissions => ["su:bb:modules"];
    public string ArgumentsHelpText => "<chat|command> <moduleId>";
    public string Id => "bb:builtin:reloadmodule";
    public PlatformCompatibilityType PlatformCompatibilityType => PlatformCompatibilityType.Blacklist;
    public List<string> PlatformCompatibilityList => [];
}
