using ButterBror.Core.Interfaces;
using ButterBror.Core.Models;
using ButterBror.Core.Models.Commands;
using ButterBror.Core.Abstractions;
using ButterBror.Core.Services;
using MediatR;
using Microsoft.Extensions.Logging;
using ButterBror.Data;
using ButterBror.Domain.Entities;

namespace ButterBror.Application.Services;

public interface ICommandProcessor
{
    Task<CommandResult> ProcessCommandAsync(ICommandContext context, IMediator mediator = null);
}

public class CommandProcessor : ICommandProcessor
{
    private readonly ICommandDispatcher _commandDispatcher;
    private readonly IUserService _userService;
    private readonly ICommandRegistry _commandRegistry;
    private readonly ILogger<CommandProcessor> _logger;
    private readonly IUserRepository _userRepository;

    public CommandProcessor(
        ICommandDispatcher commandDispatcher,
        IUserService userService,
        ICommandRegistry commandRegistry,
        ILogger<CommandProcessor> logger,
        IUserRepository userRepository)
    {
        _commandDispatcher = commandDispatcher;
        _userService = userService;
        _commandRegistry = commandRegistry;
        _userRepository = userRepository;
        _logger = logger;
    }

    public async Task<CommandResult> ProcessCommandAsync(ICommandContext context, IMediator mediator = null)
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

            // If validation passes, proceed with user management and command execution

            var extendedContext = new ExtendedCommandContext(context, user.UnifiedUserId);

            // Command Dispatch
            var result = await _commandDispatcher.DispatchAsync(extendedContext);

            stopwatch.Stop();
            result.ExecutionTime = stopwatch.Elapsed;

            _logger.LogInformation(
                "Command '{CommandName}' executed by user {UserId} in {ExecutionTime}ms. Result: {Success}",
                context.CommandName, user.UnifiedUserId, stopwatch.ElapsedMilliseconds, result.Success);

            // Updating user statistics
            await _userService.UpdateUserStatisticsAsync(
                user.UnifiedUserId,
                context.CommandName,
                result.Success
            );

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
        var commandMetadata = _commandRegistry.GetCommand(commandName);
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
        var userPermissions = user.Permissions;
        if (!_commandRegistry.UserHasPermissionForCommand(commandName, userPermissions))
        {
            return CommandResult.Failure($"You don't have permission to execute command '{commandName}'.");
        }

        // S3: Cooldown check
        var lastUse = await _userService.GetCommandLastUsedAsync(commandMetadata.Id);
        var betweenUses = DateTime.UtcNow - lastUse;
        if (betweenUses != null && ((TimeSpan)betweenUses).Seconds < commandMetadata.CooldownSeconds)
        {
            return CommandResult.Failure($"Command '{commandName}' is on cooldown for '{user.DisplayName}'.", sendResult:false);
        }
        _ = _userService.SetCommandLastUseAsync(commandMetadata.Id, DateTime.UtcNow);

        // Yay
        _logger.LogInformation("Command '{CommandName}' passed all validations", commandName);
        return CommandResult.Successfully($"Command '{commandName}' is valid and ready for execution.");
    }
}