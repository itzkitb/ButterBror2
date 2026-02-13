using ButterBror.Core.Contracts;
using ButterBror.Core.Enums;
using ButterBror.Core.Interfaces;
using ButterBror.Core.Models.Commands;
using ButterBror.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ButterBror.Core.Registration;

public static class CommandRegistration
{
    public static IServiceCollection AddCommands(this IServiceCollection services)
    {
        // NOTE: Commands should be registered in Program.cs or respective modules
        // to avoid circular dependencies between Core and Application layers
        return services;
    }

    public static void RegisterAllCommands(ICommandRegistry registry)
    {
        // Register command metadata - creating temporary metadata object
        var metadata = CreateUserInfoMetadata();
        registry.RegisterCommandMetadata(metadata);
        
        // Add more commands here as they are converted
    }
    
    private static ICommandMetadata CreateUserInfoMetadata()
    {
        return new UserInfoCommandMetadata();
    }
    
    private class UserInfoCommandMetadata : ICommandMetadata
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
}