using System.Security.Cryptography;
using System.Text;
using ButterBror.Core.Interfaces;
using ButterBror.Core.Messaging;
using ButterBror.Core.Models;
using ButterBror.Core.Modules.Commands;
using ButterBror.Core.Modules.Interfaces;
using Microsoft.Extensions.Logging;
using ButterBror.Domain.Entities;

namespace ButterBror.Infrastructure.Services;

public class CommandProcessor : ICommandProcessor
{
    private readonly ICommandDispatcher _commandDispatcher;
    private readonly IUserService _userService;
    private readonly ICommandRegistry _commandRegistry;
    private readonly ILogger<CommandProcessor> _logger;
    private readonly IBanphraseService _banphraseService;
    private readonly IPermissionManager _permissionManager;
    private readonly ILocalizationService _localization;
    private readonly IErrorTrackingService _errorTrackingService;

    public CommandProcessor(
        ICommandDispatcher commandDispatcher,
        IUserService userService,
        ICommandRegistry commandRegistry,
        ILogger<CommandProcessor> logger,
        IBanphraseService banphraseService,
        IPermissionManager permissionManager,
        ILocalizationService localization,
        IErrorTrackingService errorTrackingService)
    {
        _commandDispatcher = commandDispatcher;
        _userService = userService;
        _commandRegistry = commandRegistry;
        _banphraseService = banphraseService;
        _permissionManager = permissionManager;
        _logger = logger;
        _localization = localization;
        _errorTrackingService = errorTrackingService;
    }

    public async Task<CommandResult> ProcessCommandAsync(ICommandContext context)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        string unifiedUserId = "unknown";
        ExtendedCommandContext? extendedContext = null;
        
        try
        {
            // S0: Getting/creating user profile
            var user = await _userService.GetOrCreateUserAsync(
                context.User.Id,
                context.Platform,
                context.User.DisplayName
            );
            unifiedUserId = user.UnifiedUserId.ToString();

            // S1: Validating command
            var validationResult = await ValidateCommand(context, user);
            if (!validationResult.Success)
            {
                stopwatch.Stop();
                validationResult.ExecutionTime = stopwatch.Elapsed;
                return validationResult;
            }

            // S2: Proceed with user management and command execution
            extendedContext = new ExtendedCommandContext(context, user.UnifiedUserId, user.PreferredLocale);
            
            // S3: Command Dispatch
            var result = await _commandDispatcher.DispatchAsync(extendedContext);

            stopwatch.Stop();
            result.ExecutionTime = stopwatch.Elapsed;

            _logger.LogInformation(
                "Command executed by user. name='{CommandName}' uid='{UserId}' execution_time={ExecutionTime} success={Success}",
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
                    result.Message?.RawText ?? string.Empty,
                    context.CancellationToken
                );

                if (!banphraseResult.Passed)
                {
                    _logger.LogInformation(
                        "Command result blocked by banphrase. command='{Command}', uid='{UserId}', section='{Section}', category='{Category}', pattern='{Pattern}', phrase='{Phrase}'",
                        context.CommandName,
                        user.UnifiedUserId,
                        banphraseResult.FailedSection,
                        banphraseResult.FailedCategory,
                        banphraseResult.MatchedPattern,
                        banphraseResult.MatchedPhrase
                    );

                    result.Message = new Message(await _localization.GetStringAsync("core.bot.banphrase", extendedContext.Locale));
                    result.Success = false;
                }
            }
            else
            {
                _logger.LogWarning("Skipping the ban phrases check");
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
            var errorHash = GenerateExceptionHash(ex);
            _logger.LogError(ex,
                "Error processing command. name='{CommandName}', uid='{UserId}', error_code='{ErrorCode}'",
                context.CommandName, unifiedUserId, errorHash);
            
            _errorTrackingService.LogError(ex, "The exception was not caught at the command level", extendedContext ?? context);
            
            return new CommandResult
            {
                Success = false,
                Message = new Message($"🚨 | An internal error has occurred ▹ The developers are already aware of it ▹ Error code: {errorHash}"),
                ExecutionTime = stopwatch.Elapsed
            };
        }
    }

    public static string GenerateExceptionHash(Exception ex)
    {
        if (ex == null) return "UNK:00000000";

        // S0. Receive class
        var targetMethod = ex.TargetSite;
        string className = targetMethod?.DeclaringType?.Name ?? "UnknownClass";

        // S1. Generating an abbreviation
        string abbreviation = GetPascalCaseAbbreviation(className);

        // S2. Calculate a hash
        string input = $"{ex.GetType().FullName}\n{ex.StackTrace}";
        using var sha256 = SHA256.Create();
        byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
        string hash = Convert.ToHexString(bytes)[..8];

        // S3. Final
        return $"{abbreviation}:{hash}";
    }

    private static string GetPascalCaseAbbreviation(string input)
    {
        if (string.IsNullOrEmpty(input)) return "UNK";
        
        string cleanName = new string(input.Where(char.IsLetterOrDigit).ToArray());
        var upperLetters = cleanName.Where(char.IsUpper).ToArray();

        if (upperLetters.Length > 0)
        {
            return new string(upperLetters);
        }
        
        return cleanName.Length >= 3 ? cleanName[..3].ToUpper() : cleanName.ToUpper();
    }
    
    private async Task<CommandResult> ValidateCommand(ICommandContext context, UserProfile user)
    {
        var commandName = context.CommandName;

        // S0: Validating that command exists
        var commandMetadata = _commandRegistry.GetCommandMetadata(commandName);
        if (commandMetadata == null)
        {
            return CommandResult.Failure(
                await _localization.GetStringAsync("core.bot.command.not_found", user.PreferredLocale,
                    commandName),
                sendResult:false);
        }

        // S1: Checking platform compatibility
        var platformId = context.Platform.ToLowerInvariant();
        if (!_commandRegistry.IsCommandCompatibleWithPlatform(commandName, platformId))
        {
            return CommandResult.Failure(
                await _localization.GetStringAsync("core.bot.command.compatibility", user.PreferredLocale,
                    commandName,
                    context.Platform),
                sendResult:false);
        }

        // S2: Validating permissions
        if (!await _commandRegistry.UserHasPermissionForCommandAsync(commandName, user.UnifiedUserId))
        {
            return CommandResult.Failure(
                await _localization.GetStringAsync("core.bot.command.permission", user.PreferredLocale));
        }

        // S3: Cooldown check
        var lastUse = await _userService.GetCommandLastUsedAsync(commandMetadata.Id, user.UnifiedUserId);
        var betweenUses = DateTime.UtcNow - lastUse;
        if (betweenUses != null && ((TimeSpan)betweenUses).TotalSeconds < commandMetadata.CooldownSeconds)
        {
            _logger.LogDebug("Command cooldown. uid='{UserId}', cid='{CommandId}', remain={Seconds}, cooldown={CooldownSeconds}",
                user.UnifiedUserId,
                commandMetadata.Id,
                ((TimeSpan)betweenUses).TotalSeconds,
                commandMetadata.CooldownSeconds
            );
            return CommandResult.Failure(
                await _localization.GetStringAsync("core.bot.command.cooldown", user.PreferredLocale,
                    commandName,
                    commandMetadata.CooldownSeconds - ((TimeSpan)betweenUses).TotalSeconds),
                sendResult:false);
        }
        _ = _userService.SetCommandLastUseAsync(commandMetadata.Id, user.UnifiedUserId, DateTime.UtcNow);

        // Yay
        _logger.LogInformation("Command passed all validations. name='{CommandName}'", commandName);
        return CommandResult.Successfully("");
    }
}
