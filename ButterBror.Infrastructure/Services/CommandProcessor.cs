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

public class CommandProcessor(
    ICommandDispatcher commandDispatcher,
    IUserService userService,
    ICommandRegistry commandRegistry,
    ILogger<CommandProcessor> logger,
    IBanphraseService banphraseService,
    ILocalizationService localization,
    IErrorTrackingService errorTrackingService,
    IRestrictionService restrictionService)
    : ICommandProcessor
{
    public async Task<CommandResult> ProcessCommandAsync(ICommandContext context)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        string unifiedUserId = "unknown";
        ExtendedCommandContext? eContext = null;

        try
        {
            // S0: Getting/creating user profile
            var user = await userService.GetOrCreateUserAsync(
                context.User.Id,
                context.Platform,
                context.User.DisplayName
            );
            unifiedUserId = user.UnifiedId.ToString();

            // S1. Find a command
            var commandMeta = commandRegistry.GetCommandMetadata(context.CommandName);
            if (commandMeta == null)
            {
                return CommandResult.Failure(
                    await localization.GetStringAsync("core.bot.command.not_found", user.PreferredLocale,
                        context.CommandName),
                    sendResult: false);
            }

            // S2: Validating command
            var validationResult = await ValidateCommand(context, commandMeta, user);
            if (!validationResult.Success)
            {
                stopwatch.Stop();
                validationResult.ExecutionTime = stopwatch.Elapsed;
                return validationResult;
            }

            // S3: Proceed with user management and command execution
            eContext = new ExtendedCommandContext(context, user);

            // S4: Checking user block status
            var userStatus = await restrictionService.CheckUserBlockStatusAsync(
                eContext.Channel.Platform,
                eContext.UnifiedUserId,
                eContext.CancellationToken);

            if (userStatus.IsBlocked)
            {
                var blockMessage = await localization.GetStringAsync("core.bot.user_blocked", eContext.Locale);
                return CommandResult.Failure(blockMessage, sendResult: userStatus.ShouldNotify);
            }

            // S5. Checking command block status
            var blockStatus = await restrictionService.CheckCommandStatusAsync(
                eContext.Channel.Platform,
                eContext.Channel.Id,
                commandMeta.Id,
                eContext.CancellationToken);

            if (blockStatus != CommandBlockStatus.Allowed)
            {
                var reasonMessageKey = blockStatus switch
                {
                    CommandBlockStatus.BlockedGlobally => "core.bot.command.blocked_global",
                    CommandBlockStatus.BlockedOnPlatform => "core.bot.command.blocked_platform",
                    CommandBlockStatus.BlockedInChat => "core.bot.command.blocked_chat",
                    _ => "core.bot.command.blocked"
                };

                var message = await localization.GetStringAsync(reasonMessageKey, eContext.Locale, commandMeta.Id);
                return CommandResult.Failure(message);
            }

            // S6: Command Dispatch
            var result = await commandDispatcher.DispatchAsync(eContext);

            stopwatch.Stop();
            result.ExecutionTime = stopwatch.Elapsed;

            logger.LogInformation(
                "Command executed by user. name='{CommandName}' uid='{UserId}' execution_time={ExecutionTime} success={Success}",
                context.CommandName, user.UnifiedId, stopwatch.ElapsedMilliseconds, result.Success);

            // S7: Check banphrases
            // If any permission start with "su:" skipping check
            var skipCheck = commandMeta.RequiredPermissions.Any((c) => c.StartsWith("su:"));

            if (!skipCheck)
            {
                var banphraseResult = await banphraseService.CheckMessageAsync(
                    context.Channel.Id,
                    context.Platform,
                    result.Message?.RawText ?? string.Empty,
                    context.CancellationToken
                );

                if (!banphraseResult.Passed)
                {
                    logger.LogInformation(
                        "Command result blocked by banphrase. command='{Command}', uid='{UserId}', section='{Section}', category='{Category}', pattern='{Pattern}', phrase='{Phrase}'",
                        context.CommandName,
                        user.UnifiedId,
                        banphraseResult.FailedSection,
                        banphraseResult.FailedCategory,
                        banphraseResult.MatchedPattern,
                        banphraseResult.MatchedPhrase
                    );

                    result.Message =
                        new Message(await localization.GetStringAsync("core.bot.banphrase", eContext.Locale));
                    result.Success = false;
                }
            }
            else
            {
                logger.LogWarning("Skipping the ban phrases check");
            }

            // S8: Updating user statistics
            await userService.UpdateUserStatisticsAsync(
                user.UnifiedId,
                commandMeta.Name,
                result.Success
            );

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var errorHash = GenerateExceptionHash(ex);
            logger.LogError(ex,
                "Error processing command. name='{CommandName}', uid='{UserId}', error_code='{ErrorCode}'",
                context.CommandName, unifiedUserId, errorHash);

            errorTrackingService.LogError(ex, "The exception was not caught at the command level",
                eContext ?? context);

            return new CommandResult
            {
                Success = false,
                Message = new Message(
                    $"🚨 | An internal error has occurred ▹ The developers are already aware of it ▹ Error code: {errorHash}"),
                ExecutionTime = stopwatch.Elapsed,
                SendResult = true
            };
        }
    }

    public static string GenerateExceptionHash(Exception ex)
    {
        // S0: Receive class
        var targetMethod = ex.TargetSite;
        string className = targetMethod?.DeclaringType?.Name ?? "UnknownClass";

        // S1: Generating an abbreviation
        string abbreviation = GetPascalCaseAbbreviation(className);

        // S2: Calculate a hash
        string input = $"{ex.GetType().FullName}\n{ex.StackTrace}";
        using var sha256 = SHA256.Create();
        byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
        string hash = Convert.ToHexString(bytes)[..8];

        // S3: Final
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
    
    private async Task<CommandResult> ValidateCommand(ICommandContext context, ICommandMetadata meta, UserProfile user)
    {
        var commandName = context.CommandName;

        // S0: Checking platform compatibility
        var platformId = context.Platform.ToLowerInvariant();
        if (!commandRegistry.IsCommandCompatibleWithPlatform(commandName, platformId))
        {
            return CommandResult.Failure(
                await localization.GetStringAsync("core.bot.command.compatibility", user.PreferredLocale,
                    commandName,
                    context.Platform),
                sendResult:false);
        }

        // S1: Validating permissions
        if (!await commandRegistry.UserHasPermissionForCommandAsync(commandName, user.UnifiedId))
        {
            return CommandResult.Failure(
                await localization.GetStringAsync("core.bot.command.permission", user.PreferredLocale));
        }

        // S2: Cooldown check
        var lastUse = await userService.GetCommandLastUsedAsync(meta.Id, user.UnifiedId);
        var betweenUses = DateTime.UtcNow - lastUse;
        if (betweenUses != null && ((TimeSpan)betweenUses).TotalSeconds < meta.CooldownSeconds)
        {
            logger.LogDebug("Command cooldown. uid='{UserId}', cid='{CommandId}', remain={Seconds}, cooldown={CooldownSeconds}",
                user.UnifiedId,
                meta.Id,
                ((TimeSpan)betweenUses).TotalSeconds,
                meta.CooldownSeconds
            );
            return CommandResult.Failure(
                await localization.GetStringAsync("core.bot.command.cooldown", user.PreferredLocale,
                    commandName,
                    meta.CooldownSeconds - ((TimeSpan)betweenUses).TotalSeconds),
                sendResult:false);
        }
        _ = userService.SetCommandLastUseAsync(meta.Id, user.UnifiedId, DateTime.UtcNow);
        
        // S3: Yay
        logger.LogInformation("Command passed all validations. name='{CommandName}'", commandName);
        return CommandResult.Successfully("Yay");
    }
}
