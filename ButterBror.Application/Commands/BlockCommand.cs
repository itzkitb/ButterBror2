using ButterBror.Application.Commands.Meta;
using ButterBror.Core.Interfaces;
using ButterBror.Core.Modules.Commands;
using ButterBror.Core.Modules.Interfaces;
using ButterBror.Data;
using ButterBror.Data.Interfaces;

namespace ButterBror.Application.Commands;

public class BlockCommand : ICommand
{
    public async Task<CommandResult> ExecuteAsync(
        CommandContext context,
        ICommandServiceProvider serviceProvider)
    {
        var restrictionService = serviceProvider.GetService<IRestrictionService>();
        var localization = serviceProvider.GetService<ILocalizationService>();
        var userRepository = serviceProvider.GetService<IUserRepository>();
        var blockCommandId = new BlockCommandMeta().Id;

        if (context.Arguments.Count < 1)
        {
            return CommandResult.Failure(
                await localization.GetStringAsync("command.block.usage", context.Locale));
        }

        var isBlock = !context.CommandName.Equals("unblock", StringComparison.OrdinalIgnoreCase)
                      && !context.CommandName.Equals("unban", StringComparison.OrdinalIgnoreCase);
        var targetType = context.Arguments[0].ToLowerInvariant(); // user / global / platform / chat

        return targetType switch
        {
            "user" => await HandleUserBlockAsync(context, restrictionService, localization, userRepository, isBlock),
            "global" => await HandleGlobalBlockAsync(context, restrictionService, localization, isBlock,
                blockCommandId),
            "platform" => await HandlePlatformBlockAsync(context, restrictionService, localization, isBlock,
                blockCommandId),
            "chat" => await HandleChatBlockAsync(context, restrictionService, localization, isBlock, blockCommandId),
            _ => CommandResult.Failure(
                await localization.GetStringAsync("command.block.unknown_target", context.Locale))
        };
    }

    private async Task<CommandResult> HandleUserBlockAsync(
        CommandContext context,
        IRestrictionService restriction,
        ILocalizationService localization,
        IUserRepository userRepository,
        bool isBlock)
    {
        if (context.Arguments.Count < 2)
            return CommandResult.Failure(await localization.GetStringAsync("command.block.user.usage", context.Locale));

        var userName = context.Arguments[1];
        var userEntity = await userRepository.FindUserAsync(context.ChatInfo.PlatformId, userName);
        var user = context.User;
        
        if (userEntity == null)
            return CommandResult.Failure(
                await localization.GetStringAsync("command.block.user.not_found", context.Locale));
        
        if (userEntity.UnifiedId.Equals(user.UnifiedId))
            return CommandResult.Failure(
                await localization.GetStringAsync("command.block.user.block_self", context.Locale));
        
        var isGlobal = (context.Arguments.Count > 2 && context.Arguments[2].Equals("global", 
            StringComparison.OrdinalIgnoreCase)) || context.Arguments.Count <= 2;
        var targetPlatform = context.Arguments.Count > 2 ? context.Arguments[2] : "global";
        var reason = context.Arguments.Count > 3 ? string.Join(" ", context.Arguments.Skip(3)) : null;

        if (isBlock)
            await restriction.BlockUserAsync(targetPlatform, userEntity.UnifiedId, reason, isGlobal, context.CancellationToken);
        else
            await restriction.UnblockUserAsync(targetPlatform, userEntity.UnifiedId, isGlobal, context.CancellationToken);

        var locKey = isBlock ? "command.block.user.success" : "command.unblock.user.success";
        return CommandResult.Successfully(await localization.GetStringAsync(locKey, context.Locale, userName, targetPlatform));
    }

    private async Task<CommandResult> HandleGlobalBlockAsync(
        CommandContext context,
        IRestrictionService restriction,
        ILocalizationService localization,
        bool isBlock,
        string blockCommandId)
    {
        if (context.Arguments.Count < 2)
            return CommandResult.Failure(await localization.GetStringAsync("command.block.global.usage", context.Locale));

        var commandId = context.Arguments[1];

        if (commandId.Equals(blockCommandId, StringComparison.OrdinalIgnoreCase))
            return CommandResult.Failure(await localization.GetStringAsync("command.block.block_ban", context.Locale));
        
        if (isBlock)
            await restriction.BlockCommandGlobalAsync(commandId, context.CancellationToken);
        else
            await restriction.UnblockCommandGlobalAsync(commandId, context.CancellationToken);

        var locKey = isBlock ? "command.block.global.success" : "command.unblock.global.success";
        return CommandResult.Successfully(await localization.GetStringAsync(locKey, context.Locale, commandId));
    }

    private async Task<CommandResult> HandlePlatformBlockAsync(
        CommandContext context,
        IRestrictionService restriction,
        ILocalizationService localization,
        bool isBlock,
        string blockCommandId)
    {
        if (context.Arguments.Count < 3)
            return CommandResult.Failure(await localization.GetStringAsync("command.block.platform.usage", context.Locale));

        var platform = context.Arguments[1];
        var commandId = context.Arguments[2];

        if (commandId.Equals(blockCommandId, StringComparison.OrdinalIgnoreCase))
            return CommandResult.Failure(await localization.GetStringAsync("command.block.block_ban", context.Locale));
        
        if (isBlock)
            await restriction.BlockCommandPlatformAsync(platform, commandId, context.CancellationToken);
        else
            await restriction.UnblockCommandPlatformAsync(platform, commandId, context.CancellationToken);

        var locKey = isBlock ? "command.block.platform.success" : "command.unblock.platform.success";
        return CommandResult.Successfully(await localization.GetStringAsync(locKey, context.Locale, commandId, platform));
    }

    private async Task<CommandResult> HandleChatBlockAsync(
        CommandContext context,
        IRestrictionService restriction,
        ILocalizationService localization,
        bool isBlock,
        string blockCommandId)
    {
        if (context.Arguments.Count < 2)
            return CommandResult.Failure(await localization.GetStringAsync("command.block.chat.usage", context.Locale));

        var commandId = context.Arguments[1];
        var platform = context.ChatInfo.PlatformId;
        var channelId = context.Arguments.Count > 2 ? context.Arguments[2] : context.ChatInfo.PlatformId;

        if (commandId.Equals(blockCommandId, StringComparison.OrdinalIgnoreCase))
            return CommandResult.Failure(await localization.GetStringAsync("command.block.block_ban", context.Locale));
        
        if (isBlock)
            await restriction.BlockCommandChatAsync(platform, channelId, commandId, context.CancellationToken);
        else
            await restriction.UnblockCommandChatAsync(platform, channelId, commandId, context.CancellationToken);

        var locKey = isBlock ? "command.block.chat.success" : "command.unblock.chat.success";
        return CommandResult.Successfully(await localization.GetStringAsync(locKey, context.Locale, commandId, channelId));
    }
}