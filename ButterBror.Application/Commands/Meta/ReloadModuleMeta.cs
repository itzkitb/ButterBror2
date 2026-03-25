using ButterBror.CommandModule.Commands;
using ButterBror.CommandModule.Enums;

namespace ButterBror.Application.Commands.Meta;

public class ReloadModuleMeta : ICommandMetadata
{
    public string Name => "reloadmodule";
    public List<string> Aliases => new() { "rlmod", "reloadmod" };
    public int CooldownSeconds => 0;
    public List<string> RequiredPermissions => new() { "su:modules" };
    public string ArgumentsHelpText => "<chat|command> <moduleId>";
    public string Id => "sillyapps:reloadmodule";
    public PlatformCompatibilityType PlatformCompatibilityType => PlatformCompatibilityType.Whitelist;
    public List<string> PlatformCompatibilityList => new()
        { "sillyapps:twitch", "sillyapps:discord", "sillyapps:telegram", "dashboard" };
}
