using ButterBror.Core.Modules.Enums;
using ButterBror.Core.Modules.Interfaces;

namespace ButterBror.Application.Commands.Meta;

public class UserInfoMeta : ICommandMetadata
{
    public string Name => "userinfo";
    public List<string> Aliases => ["ui", "whois"];
    public int CooldownSeconds => 10;
    public List<string> RequiredPermissions => [];
    public string ArgumentsHelpText => "<username>";
    public string Id => "bb:builtin:userinfo";
    public PlatformCompatibilityType PlatformCompatibilityType => PlatformCompatibilityType.Blacklist;
    public List<string> PlatformCompatibilityList => [];
}