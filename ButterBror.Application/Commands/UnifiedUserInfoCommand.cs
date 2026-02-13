using ButterBror.Core.Abstractions;
using ButterBror.Core.Interfaces;
using ButterBror.Core.Models.Commands;
using ButterBror.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ButterBror.Application.Commands;

public class UnifiedUserInfoCommand : UnifiedCommandBase
{
    public override async Task<CommandResult> ExecuteAsync(
        IPlatformChannel channel, 
        List<string> arguments, 
        IPlatformUser user, 
        IServiceProvider services)
    {
        try
        {
            var logger = GetLogger<UnifiedUserInfoCommand>(services);
            var userRepository = GetService<IUserRepository>(services);

            // Determine target username - either from arguments or use caller's username
            var targetUsername = arguments.Count > 0 
                ? arguments[0] 
                : user.DisplayName;

            var platform = channel.Platform.ToLowerInvariant(); // Assuming channel has platform info
            
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
            var logger = GetService<ILogger<UnifiedUserInfoCommand>>(services);
            logger.LogError(ex, "Error handling UnifiedUserInfoCommand for user '{TargetUsername}'", 
                arguments.Count > 0 ? arguments[0] : user.DisplayName);
            return CommandResult.Failure($"Error retrieving user info: {ex.Message}");
        }
    }
}