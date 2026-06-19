using ButterBror.CommandModule.Commands;
using ButterBror.CommandModule.Context;
using ButterBror.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace ButterBror.Application.Commands;

public class BanphrasesCommand : CommandBase
{
    public override async Task<CommandResult> ExecuteAsync(
        ICommandExecutionContext context,
        ICommandServiceProvider serviceProvider)
    {
        try
        {
            var logger = GetLogger<BanphrasesCommand>(serviceProvider);
            var banphraseService = GetService<IBanphraseService>(serviceProvider);
            var hasteBinService = GetService<IPasteBinService>(serviceProvider);
            var localization = GetService<ILocalizationService>(serviceProvider);
            
            if (context.Arguments.Count < 2)
            {
                return CommandResult.Failure(
                    await localization.GetStringAsync("command.banphrases.usage", context.Locale));
            }
            
            var action = context.Arguments[0].ToLowerInvariant();
            var section = context.Arguments[1].ToLowerInvariant();
            
            return action switch
            {
                "set" => await HandleSetAsync(context, banphraseService, hasteBinService, logger, localization),
                "get" => await HandleGetAsync(context, banphraseService, hasteBinService, logger, localization),
                "list" => await HandleListAsync(context, banphraseService, hasteBinService, logger, localization),
                "test" => await HandleTestAsync(context, banphraseService, logger, localization),
                "delete" => await HandleDeleteAsync(context, banphraseService, logger, localization),
                _ => CommandResult.Failure(
                        await localization.GetStringAsync("command.banphrases.unknown", context.Locale))
            };
        }
        catch (Exception ex)
        {
            var errorTracking = GetService<IErrorTrackingService>(serviceProvider);
            return await errorTracking.LogErrorAsync(
                ex,
                "Failed to execute BanphrasesCommand",
                context.User.Id,
                context.Channel.Platform,
                context);
        }
    }
    
    private async Task<CommandResult> HandleSetAsync(
        ICommandExecutionContext context,
        IBanphraseService banphraseService,
        IPasteBinService pasteBinService,
        ILogger logger,
        ILocalizationService localization)
    {
        if (context.Arguments.Count < 4)
        {
            return CommandResult.Failure(
                await localization.GetStringAsync("command.banphrases.set.usage", context.Locale));
        }
        
        var section = context.Arguments[1].ToLowerInvariant();
        var categoryName = context.Arguments[2];
        var patternOrUrl = string.Join(" ", context.Arguments.Skip(3));
        
        string regexPattern;
        
        // S0: Check if it's a Pastebin URL
        if (patternOrUrl.StartsWith("https://sourceb.in", StringComparison.OrdinalIgnoreCase) ||
            patternOrUrl.StartsWith("sourceb.in", StringComparison.OrdinalIgnoreCase) || 
            patternOrUrl.StartsWith("https://cdn.sourceb.in", StringComparison.OrdinalIgnoreCase) ||
            patternOrUrl.StartsWith("cdn.sourceb.in", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                regexPattern = await pasteBinService.GetTextAsync(patternOrUrl, context.CancellationToken);
                logger.LogInformation("Loaded banphrases from Pastebin: {Url}", patternOrUrl);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to fetch pattern from Pastebin: {Url}", patternOrUrl);
                return CommandResult.Failure(
                    await localization.GetStringAsync("command.banphrases.set.fail", context.Locale));
            }
        }
        else
        {
            regexPattern = patternOrUrl;
        }
        
        var platform = context.Channel.Platform;
        var channelId = context.Channel.Id;
        
        var success = await banphraseService.SetCategoryAsync(
            section,
            platform,
            channelId,
            categoryName,
            regexPattern);
        
        if (!success)
        {
            return CommandResult.Failure(
                await localization.GetStringAsync("command.banphrases.set.regex_fail", context.Locale));
        }
        
        var patternCount = CountRegexAlternatives(regexPattern);
        return CommandResult.Successfully(
            await localization.GetStringAsync("command.banphrases.set.success", context.Locale,
                patternCount,
                section,
                categoryName));
    }
    
    private static int CountRegexAlternatives(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return 0;
    
        int count = 1;
        int groupDepth = 0;
        bool inCharacterClass = false;
        bool escaped = false;
    
        for (int i = 0; i < pattern.Length; i++)
        {
            char c = pattern[i];
        
            if (escaped)
            {
                escaped = false;
                continue;
            }
        
            if (c == '\\')
            {
                escaped = true;
                continue;
            }
        
            if (c == '[' && !inCharacterClass)
            {
                inCharacterClass = true;
                continue;
            }
        
            if (c == ']' && inCharacterClass)
            {
                inCharacterClass = false;
                continue;
            }
        
            if (inCharacterClass)
                continue;
        
            if (c == '(')
            {
                groupDepth++;
                continue;
            }
        
            if (c == ')')
            {
                groupDepth--;
                continue;
            }
            
            if (c == '|' && groupDepth == 0)
            {
                count++;
            }
        }
    
        return count;
    }

    private async Task<CommandResult> HandleGetAsync(
        ICommandExecutionContext context,
        IBanphraseService banphraseService,
        IPasteBinService pasteBinService,
        ILogger logger,
        ILocalizationService localization)
    {
        if (context.Arguments.Count < 3)
        {
            return CommandResult.Failure(
                await localization.GetStringAsync("command.banphrases.get.usage", context.Locale));
        }
        
        var section = context.Arguments[1].ToLowerInvariant();
        var categoryName = context.Arguments[2];
        var platform = context.Channel.Platform;
        var channelId = context.Channel.Id;
        
        var pattern = await banphraseService.GetCategoryAsync(section, platform, channelId, categoryName);
        
        if (string.IsNullOrEmpty(pattern))
        {
            return CommandResult.Failure(
                await localization.GetStringAsync("command.banphrases.get.category_not_found", context.Locale,
                    categoryName,
                    section));
        }
        
        try
        {
            var url = await pasteBinService.UploadTextAsync(pattern, context.CancellationToken);
            return CommandResult.Successfully(
                await localization.GetStringAsync("command.banphrases.get.success", context.Locale,
                    section,
                    categoryName,
                    url));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to upload pattern to Hastebin");
            return CommandResult.Failure(
                await localization.GetStringAsync("command.banphrases.get.fail", context.Locale));
        }
    }
    
    private async Task<CommandResult> HandleListAsync(
        ICommandExecutionContext context,
        IBanphraseService banphraseService,
        IPasteBinService pasteBinService,
        ILogger logger,
        ILocalizationService localization)
    {
        var section = context.Arguments.Count > 1 ? context.Arguments[1].ToLowerInvariant() : "global";
        var platform = context.Channel.Platform;
        var channelId = context.Channel.Id;
        
        var categories = await banphraseService.ListCategoriesAsync(section, platform, channelId);
        
        if (categories.Count == 0)
        {
            return CommandResult.Successfully(
                await localization.GetStringAsync("command.banphrases.list.not_found", context.Locale,
                    section));
        }
        
        // Get patterns for each category
        var listContent = new System.Text.StringBuilder();
        listContent.AppendLine(
            await localization.GetStringAsync("command.banphrases.list.title", context.Locale,
                section));
        
        foreach (var category in categories)
        {
            var pattern = await banphraseService.GetCategoryAsync(section, platform, channelId, category);
            var displayPattern = pattern?.Length > 50 ? pattern[..50] + "..." : pattern ?? "(empty)";
            listContent.AppendLine($"- {category}");
            listContent.AppendLine($"  Regex: {displayPattern}");
        }
        
        var listText = listContent.ToString();
        
        try
        {
            var url = await pasteBinService.UploadTextAsync(listText, context.CancellationToken);
            return CommandResult.Successfully(
                await localization.GetStringAsync("command.banphrases.list.success", context.Locale,
                    section,
                    url));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to upload list to Hastebin");
            return CommandResult.Failure(
                await localization.GetStringAsync("command.banphrases.list.fail", context.Locale));
        }
    }

    private async Task<CommandResult> HandleTestAsync(
        ICommandExecutionContext context,
        IBanphraseService banphraseService,
        ILogger logger,
        ILocalizationService localization)
    {
        if (context.Arguments.Count < 3)
        {
            return CommandResult.Failure(
                await localization.GetStringAsync("command.banphrases.test.usage", context.Locale));
        }

        var section = context.Arguments[1].ToLowerInvariant();
        var message = string.Join(" ", context.Arguments.Skip(2));
        var platform = context.Channel.Platform;
        var channelId = context.Channel.Id;

        // For testing, we need to determine which section to check
        string testSection, testPlatform, testChannelId;

        if (section == "global")
        {
            testSection = "global";
            testPlatform = platform;
            testChannelId = channelId;
        }
        else
        {
            testSection = section;
            testPlatform = platform;
            testChannelId = channelId;
        }

        var result =
            await banphraseService.CheckMessageAsync(testChannelId, testPlatform, message, context.CancellationToken);

        if (result.Passed)
        {
            return CommandResult.Successfully("");
        }

        var response = await localization.GetStringAsync("command.banphrases.test.fail", context.Locale,
            result.FailedSection ?? "none",
            result.FailedCategory ?? "none");

        if (!string.IsNullOrEmpty(result.MatchedPhrase))
        {
            var displayPhrase = result.MatchedPhrase.Length > 50
                ? result.MatchedPhrase[..50] + "..."
                : result.MatchedPhrase;
            response += await localization.GetStringAsync("command.banphrases.test.matched", context.Locale,
                displayPhrase);
        }

        if (!string.IsNullOrEmpty(result.MatchedPattern))
        {
            var displayPattern = result.MatchedPattern.Length > 50
                ? result.MatchedPattern[..50] + "..."
                : result.MatchedPattern;
            response += await localization.GetStringAsync("command.banphrases.test.regex", context.Locale,
                displayPattern);
        }

        return CommandResult.Failure(response);
    }

    private async Task<CommandResult> HandleDeleteAsync(
        ICommandExecutionContext context,
        IBanphraseService banphraseService,
        ILogger logger,
        ILocalizationService localization)
    {
        if (context.Arguments.Count < 3)
        {
            return CommandResult.Failure(
                await localization.GetStringAsync("command.banphrases.delete.usage", context.Locale));
        }
        
        var section = context.Arguments[1].ToLowerInvariant();
        var categoryName = context.Arguments[2];
        var platform = context.Channel.Platform;
        var channelId = context.Channel.Id;
        
        var success = await banphraseService.DeleteCategoryAsync(section, platform, channelId, categoryName);
        
        if (!success)
        {
            return CommandResult.Failure(
                await localization.GetStringAsync("command.banphrases.delete.fail", context.Locale,
                    categoryName));
        }
        
        return CommandResult.Successfully(
            await localization.GetStringAsync("command.banphrases.delete.success", context.Locale,
                categoryName,
                section));
    }
}