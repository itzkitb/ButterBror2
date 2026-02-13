using ButterBror.Core.Abstractions;
using ButterBror.Core.Contracts;
using ButterBror.Core.Enums;
using ButterBror.Core.Models.Commands;
using ButterBror.Data;
using MediatR;
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

public class UserInfoCommandHandler : IRequestHandler<UserInfoCommand, CommandResult>
{
    private readonly IUserRepository _userRepository;
    private readonly ILogger<UserInfoCommandHandler> _logger;

    public UserInfoCommandHandler(IUserRepository userRepository, ILogger<UserInfoCommandHandler> logger)
    {
        _userRepository = userRepository;
        _logger = logger;
    }

    public async Task<CommandResult> Handle(UserInfoCommand command, CancellationToken cancellationToken)
    {
        try
        {
            var user = await _userRepository.FindUserAsync("twitch", command.TargetUsername);

            if (user == null)
            {
                return CommandResult.Failure($"User '{command.TargetUsername}' not found. " +
                    "Try using exact username or Twitch ID.");
            }

            var stats = user.Statistics
                .Select(kvp => $"{kvp.Key}: {kvp.Value}")
                .ToList();

            return CommandResult.Successfully(
                $"User: {user.DisplayName}\n" +
                $"Unified ID: {user.UnifiedUserId}\n" +
                $"Platforms: {string.Join(", ", user.PlatformIds.Keys)}\n" +
                $"Statistics:\n{string.Join("\n", stats)}",
                user
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling UserInfoCommand for user '{TargetUsername}'", command.TargetUsername);
            return CommandResult.Failure($"Error retrieving user info: {ex.Message}");
        }
    }
}