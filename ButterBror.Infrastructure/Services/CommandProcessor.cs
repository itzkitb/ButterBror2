using ButterBror.Core.Interfaces;
using ButterBror.Core.Messaging;
using ButterBror.Core.Modules.Commands;
using ButterBror.Core.Modules.Interfaces;
using Microsoft.Extensions.Logging;
using ButterBror.Domain.Entities;

namespace ButterBror.Infrastructure.Services;

public class CommandProcessor(
    ICommandDispatcher commandDispatcher,
    IUserService userService,
    IChatService chatService,
    ICommandRegistry commandRegistry,
    ILogger<CommandProcessor> logger,
    IBanphraseService banphraseService,
    ILocalizationService localization,
    IErrorTrackingService errorTrackingService,
    IRestrictionService restrictionService)
    : ICommandProcessor
{
    public async Task<CommandResult> ProcessCommandAsync(CommandContext context)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var userId = Guid.Empty;

        try
        {
            // S0: Getting/creating user profile
            var user = await userService.GetOrCreateUserAsync(
                context.PlatformUser.Id,
                context.PlatformId,
                context.PlatformUser.DisplayName
            );
            userId = user.UnifiedId;
            
            // s1: getting/creating chat
            var chat = await chatService.GetOrCreateChatAsync(
                context.Chat.Id,
                context.Chat.Platform,
                context.Chat.Name
            );

            // S1. Find a command
            var commandMeta = commandRegistry.GetCommandMetadata(context.CommandName, true);
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
            context.ExtendContext(user, chat);

            // S4: Checking user block status
            var userStatus = await restrictionService.CheckUserBlockStatusAsync(
                context.Chat.Platform,
                context.User.UnifiedId,
                context.CancellationToken);

            if (userStatus.IsBlocked)
            {
                var blockMessage = await localization.GetStringAsync("core.bot.user_blocked", context.Locale);
                return CommandResult.Failure(blockMessage, sendResult: userStatus.ShouldNotify);
            }

            // S5. Checking command block status
            var blockStatus = await restrictionService.CheckCommandStatusAsync(
                context.Chat.Platform,
                context.Chat.Id,
                commandMeta.Id,
                context.CancellationToken);

            if (blockStatus != CommandBlockStatus.Allowed)
            {
                var reasonMessageKey = blockStatus switch
                {
                    CommandBlockStatus.BlockedGlobally => "core.bot.command.blocked_global",
                    CommandBlockStatus.BlockedOnPlatform => "core.bot.command.blocked_platform",
                    CommandBlockStatus.BlockedInChat => "core.bot.command.blocked_chat",
                    _ => "core.bot.command.blocked"
                };

                var message = await localization.GetStringAsync(reasonMessageKey, context.Locale, commandMeta.Id);
                return CommandResult.Failure(message);
            }

            // S6: Command Dispatch
            var result = await commandDispatcher.DispatchAsync(context);

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
                    context.ChatInfo.UnifiedId,
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
                        new Message(await localization.GetStringAsync("core.bot.banphrase", context.Locale));
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
                commandMeta.Id,
                result.Success
            );

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var errorLog = await errorTrackingService.LogErrorAsync(ex, "command error", userId, context.PlatformId, context);
            logger.LogError(ex,
                "Error processing command. name='{CommandName}', uid='{UserId}', error_code='{ErrorCode}'",
                context.CommandName, userId, errorLog.Item2.Hash);
            
            return errorLog.Item1;
        }
    }
    
    private async Task<CommandResult> ValidateCommand(CommandContext context, ICommandMetadata meta, UserProfile user)
    {
        var commandName = context.CommandName;

        // S0: Checking platform compatibility
        var platformId = context.PlatformId.ToLowerInvariant();
        if (!commandRegistry.IsCommandCompatibleWithPlatform(meta.Id, platformId))
        {
            return CommandResult.Failure(
                await localization.GetStringAsync("core.bot.command.compatibility", user.PreferredLocale,
                    commandName,
                    context.PlatformId),
                sendResult:false);
        }

        // S1: Validating permissions
        if (!await commandRegistry.UserHasPermissionForCommandAsync(meta.Id, user.UnifiedId))
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
