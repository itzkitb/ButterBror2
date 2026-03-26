using ButterBror.Core.Interfaces;
using ButterBror.Core.Models;
using Microsoft.Extensions.Logging;
using ButterBror.Domain.Entities;
using ButterBror.CommandModule.Commands;
using ButterBror.CommandModule.Context;

namespace ButterBror.Infrastructure.Services;

public class CommandProcessor : ICommandProcessor
{
    private readonly ICommandDispatcher _commandDispatcher;
    private readonly IUserService _userService;
    private readonly ICommandRegistry _commandRegistry;
    private readonly ILogger<CommandProcessor> _logger;
    private readonly IBanphraseService _banphraseService;
    private readonly IPermissionManager _permissionManager;

    public CommandProcessor(
        ICommandDispatcher commandDispatcher,
        IUserService userService,
        ICommandRegistry commandRegistry,
        ILogger<CommandProcessor> logger,
        IBanphraseService banphraseService,
        IPermissionManager permissionManager)
    {
        _commandDispatcher = commandDispatcher;
        _userService = userService;
        _commandRegistry = commandRegistry;
        _banphraseService = banphraseService;
        _permissionManager = permissionManager;
        _logger = logger;
    }

    public async Task<CommandResult> ProcessCommandAsync(ICommandContext context)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // S0: Getting/creating user profile
            var user = await _userService.GetOrCreateUserAsync(
                context.User.Id,
                context.Platform,
                context.User.DisplayName
            );

            // S1: Validating command
            var validationResult = await ValidateCommand(context, user);
            if (!validationResult.Success)
            {
                stopwatch.Stop();
                validationResult.ExecutionTime = stopwatch.Elapsed;
                return validationResult;
            }

            // S2: Proceed with user management and command execution
            var extendedContext = new ExtendedCommandContext(context, user.UnifiedUserId);

            // S3: Command Dispatch
            var result = await _commandDispatcher.DispatchAsync(extendedContext);

            stopwatch.Stop();
            result.ExecutionTime = stopwatch.Elapsed;

            _logger.LogInformation(
                "Command '{CommandName}' executed by user {UserId} in {ExecutionTime}ms. Success: {Success}",
                context.CommandName, user.UnifiedUserId, stopwatch.ElapsedMilliseconds, result.Success);

            // S4: Check banphrases
            var commandMeta = _commandRegistry.GetCommandMetadata(context.CommandName);
            // If any permission start with "su:" skipping check
            var skipCheck = commandMeta != null ? commandMeta.RequiredPermissions.Any((c) => c.StartsWith("su:")) : false;

            if (!skipCheck)
            {
                var banphraseResult = await _banphraseService.CheckMessageAsync(
                    context.Channel.Id,
                    context.Platform,
                    result.Message ?? string.Empty,
                    context.CancellationToken
                );

                if (!banphraseResult.Passed)
                {
                    _logger.LogInformation(
                        "Command result blocked by banphrase. Command: {Command}, User: {UserId}, Section: {Section}, Category: {Category}, Pattern: '{Pattern}', Phrase: '{Phrase}'",
                        context.CommandName,
                        user.UnifiedUserId,
                        banphraseResult.FailedSection,
                        banphraseResult.FailedCategory,
                        banphraseResult.MatchedPattern,
                        banphraseResult.MatchedPhrase
                    );

                    result.Message = "The resulting message violates moderation settings";
                    result.Success = false;
                }
            }
            else
            {
                _logger.LogInformation("Skipping the ban phrases check");
            }

            if (commandMeta != null)
            {
                // S5: Updating user statistics
                await _userService.UpdateUserStatisticsAsync(
                    user.UnifiedUserId,
                    commandMeta.Name,
                    result.Success
                );
            }
            else
            {
                _logger.LogWarning("Unable to find command meta!");
            }

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Error processing command '{CommandName}' for user {UserId}",
                context.CommandName, context.User.Id);

            return new CommandResult
            {
                Success = false,
                Message = $"Internal error: {ex.Message}",
                ExecutionTime = stopwatch.Elapsed
            };
        }
    }

    private async Task<CommandResult> ValidateCommand(ICommandContext context, UserProfile user)
    {
        var commandName = context.CommandName;

        // S0: Validating that command exists
        var commandMetadata = _commandRegistry.GetCommandMetadata(commandName);
        if (commandMetadata == null)
        {
            return CommandResult.Failure($"Command '{commandName}' not found.", sendResult:false);
        }

        // S1: Checking platform compatibility
        var platformId = context.Platform.ToLowerInvariant();
        if (!_commandRegistry.IsCommandCompatibleWithPlatform(commandName, platformId))
        {
            return CommandResult.Failure($"Command '{commandName}' is not compatible with platform '{context.Platform}'.", sendResult:false);
        }

        // S2: Validating permissions
        if (!await _commandRegistry.UserHasPermissionForCommandAsync(commandName, user.UnifiedUserId))
        {
            return CommandResult.Failure($"You don't have permission to execute command '{commandName}'.");
        }

        // S3: Cooldown check (per user)
        var lastUse = await _userService.GetCommandLastUsedAsync(commandMetadata.Id, user.UnifiedUserId);
        var betweenUses = DateTime.UtcNow - lastUse;
        if (betweenUses != null && ((TimeSpan)betweenUses).TotalSeconds < commandMetadata.CooldownSeconds)
        {
            _logger.LogDebug("Command cooldown. User: {UserId}, Command: {CommandId}, Seconds remain: {Seconds}, Command cooldown: {CooldownSeconds}",
                user.UnifiedUserId,
                commandMetadata.Id,
                ((TimeSpan)betweenUses).TotalSeconds,
                commandMetadata.CooldownSeconds
            );
            return CommandResult.Failure($"Command '{commandName}' is on cooldown. Please, wait {(commandMetadata.CooldownSeconds - ((TimeSpan)betweenUses).TotalSeconds)} seconds", sendResult:false);
        }
        _ = _userService.SetCommandLastUseAsync(commandMetadata.Id, user.UnifiedUserId, DateTime.UtcNow);

        // Yay
        _logger.LogInformation("Command '{CommandName}' passed all validations", commandName);
        return CommandResult.Successfully($"Command '{commandName}' is valid and ready for execution.");
    }
}
