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
            // s0: get/create user profile
            var user = await userService.GetOrCreateUserAsync(
                context.PlatformUser.Id,
                context.PlatformId,
                context.PlatformUser.DisplayName
            );
            userId = user.UnifiedId;
            
            // s1: get/create chat
            var chat = await chatService.GetOrCreateChatAsync(
                context.Chat.Id,
                context.Chat.Platform,
                context.Chat.Name
            );

            // s2: find a command
            var commandMeta = commandRegistry.GetCommandMetadata(context.CommandName, true);
            if (commandMeta == null)
            {
                return CommandResult.Failure(
                    await localization.GetStringAsync("core.bot.command.not_found", user.PreferredLocale,
                        context.CommandName),
                    sendResult: false);
            }

            // s3: validating command
            var validationResult = await ValidateCommand(context, commandMeta, user);
            if (!validationResult.Success)
            {
                stopwatch.Stop();
                validationResult.ExecutionTime = stopwatch.Elapsed;
                return validationResult;
            }

            // s4: proceed with user management and command execution
            context.ExtendContext(user, chat);

            // s5: checking user block status
            var userStatus = await restrictionService.CheckUserBlockStatusAsync(
                context.Chat.Platform,
                context.User.UnifiedId,
                context.CancellationToken);

            if (userStatus.IsBlocked)
            {
                var blockMessage = await localization.GetStringAsync("core.bot.user_blocked", context.Locale);
                return CommandResult.Failure(blockMessage, sendResult: userStatus.ShouldNotify);
            }

            // s6. checking command block status
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

            // s7: command Dispatch
            var result = await commandDispatcher.DispatchAsync(context);

            stopwatch.Stop();
            result.ExecutionTime = stopwatch.Elapsed;

            logger.LogInformation(
                "command executed. name='{CommandName}', uid='{UserId}', time={ExecutionTime}ms, success={Success}",
                context.CommandName, user.UnifiedId, stopwatch.ElapsedMilliseconds, result.Success);

            // s8: check banphrases
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
                        "command result blocked by banphrase. command='{Command}', uid='{UserId}', section='{Section}', category='{Category}', pattern='{Pattern}', phrase='{Phrase}'",
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
                logger.LogWarning("skipping the ban phrases check. user has 'su:' permission");
            }

            // s9: update user statistics
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
            var errorLog = 
                await errorTrackingService.LogErrorAsync(ex, 
                    "command error", 
                    userId, 
                    context.PlatformId, context);
            
            return errorLog.Item1;
        }
    }
    
    private async Task<CommandResult> ValidateCommand(CommandContext context, ICommandMetadata meta, UserProfile user)
    {
        var commandName = context.CommandName;

        // s0: checking platform compatibility
        var platformId = context.PlatformId.ToLowerInvariant();
        if (!commandRegistry.IsCommandCompatibleWithPlatform(meta.Id, platformId))
        {
            logger.LogInformation("command is not compatible. uid='{UserId}', cid='{CommandId}', platform={Platform}",
                user.UnifiedId,
                meta.Id,
                platformId
            );
            
            return CommandResult.Failure(
                await localization.GetStringAsync("core.bot.command.compatibility", user.PreferredLocale,
                    commandName,
                    context.PlatformId),
                sendResult: false);
        }

        // s1: validating permissions
        if (!await commandRegistry.UserHasPermissionForCommandAsync(meta.Id, user.UnifiedId))
        {
            logger.LogInformation("command is not available for this user. uid='{UserId}', cid='{CommandId}', platform={Platform}",
                user.UnifiedId,
                meta.Id,
                platformId
            );
            
            return CommandResult.Failure(
                await localization.GetStringAsync("core.bot.command.permission", user.PreferredLocale));
        }

        // s2: cooldown check
        var lastUse = await userService.GetCommandLastUsedAsync(meta.Id, user.UnifiedId);
        var betweenUses = DateTime.UtcNow - lastUse;
        if (betweenUses != null && ((TimeSpan)betweenUses).TotalSeconds < meta.CooldownSeconds)
        {
            logger.LogInformation("command cooldown. uid='{UserId}', cid='{CommandId}', remain={Seconds}, cooldown={CooldownSeconds}",
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
        
        // s3: yay
        return CommandResult.Successfully("yay");
    }
}
