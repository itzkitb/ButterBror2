using ButterBror.Core.Interfaces;
using ButterBror.Core.Models;
using ButterBror.Core.Models.Commands;
using Microsoft.Extensions.Logging;

namespace ButterBror.Application.Services;

public interface ICommandProcessor
{
    Task<CommandResult> ProcessCommandAsync(ICommandContext context);
}

public class CommandProcessor : ICommandProcessor
{
    private readonly ICommandDispatcher _commandDispatcher;
    private readonly IUserService _userService;
    private readonly ILogger<CommandProcessor> _logger;

    public CommandProcessor(
        ICommandDispatcher commandDispatcher,
        IUserService userService,
        ILogger<CommandProcessor> logger)
    {
        _commandDispatcher = commandDispatcher;
        _userService = userService;
        _logger = logger;
    }

    public async Task<CommandResult> ProcessCommandAsync(ICommandContext context)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var user = await _userService.GetOrCreateUserAsync(
                context.User.Id,
                context.Platform,
                context.User.DisplayName
            );

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
}