using ButterBror.Core.Abstractions;
using ButterBror.Core.Contracts;
using ButterBror.Core.Enums;
using ButterBror.Core.Interfaces;
using ButterBror.Core.Models.Commands;
using ButterBror.Data;
using Microsoft.Extensions.Logging;

namespace ButterBror.Application.Commands;

public record UserInfoCommand(string TargetUsername) : IMetadataCommand
{
    public ICommandMetadata GetMetadata() => new UserInfoCommandMetadata();

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

public class UnifiedUserInfoCommand : UnifiedCommandBase
{
    public override async Task<CommandResult> ExecuteAsync(
        ICommandExecutionContext context,
        ICommandServiceProvider serviceProvider)
    {
        try
        {
            var logger = GetLogger<UnifiedUserInfoCommand>(serviceProvider);
            var userRepository = GetService<IUserRepository>(serviceProvider);

            // Determine target username - either from arguments or use caller's username
            var targetUsername = context.Arguments.Count > 0
                ? context.Arguments[0]
                : context.User.DisplayName;

            var platform = context.Channel.Platform.ToLowerInvariant();

            var userEntity = await userRepository.FindUserAsync(platform, targetUsername);

            if (userEntity == null)
            {
                return CommandResult.Failure($"User '{targetUsername}' not found. " +
                    "Try using exact username or platform ID.");
            }

            var stats = userEntity.Statistics
                .Select(kvp => $"{kvp.Key}: {kvp.Value}")
                .ToList();

            return CommandResult.Successfully(
                $"User: {userEntity.DisplayName}\n" +
                $"Unified ID: {userEntity.UnifiedUserId}\n" +
                $"Platforms: {string.Join(", ", userEntity.PlatformIds.Keys)}\n" +
                $"Statistics:\n{string.Join("\n", stats)}",
                userEntity
            );
        }
        catch (Exception ex)
        {
            var logger = GetService<ILogger<UnifiedUserInfoCommand>>(serviceProvider);
            logger.LogError(ex, "Error handling UnifiedUserInfoCommand for user '{TargetUsername}'",
                context.Arguments.Count > 0 ? context.Arguments[0] : context.User.DisplayName);
            return CommandResult.Failure($"Error retrieving user info: {ex.Message}");
        }
    }
}