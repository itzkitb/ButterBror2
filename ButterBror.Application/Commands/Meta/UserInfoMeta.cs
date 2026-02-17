using ButterBror.Core.Contracts;
using ButterBror.Core.Enums;

namespace ButterBror.Application.Commands.Meta;

public class UserInfoMeta : ICommandMetadata
{
    public string Name => "userinfo";
    public List<string> Aliases => new List<string> { "ui", "whois" };
    public int CooldownSeconds => 10;
    public List<string> RequiredPermissions => new List<string>();
    public string ArgumentsHelpText => "<username>";
    public string Id => "sillyapps:userinfo";
    public PlatformCompatibilityType PlatformCompatibilityType => PlatformCompatibilityType.Whitelist;
    public List<string> PlatformCompatibilityList => new List<string> { "sillyapps:twitch", "sillyapps:discord", "sillyapps:telegram" };
}