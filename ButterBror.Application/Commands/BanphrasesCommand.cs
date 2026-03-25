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
            var hasteBinService = GetService<IHasteBinService>(serviceProvider);
            
            if (context.Arguments.Count < 2)
            {
                return CommandResult.Failure(
                    "Usage: !banphrases <set|get|list|test|delete> <global|channel> [category] [pattern]");
            }
            
            var action = context.Arguments[0].ToLowerInvariant();
            var section = context.Arguments[1].ToLowerInvariant();
            
            switch (action)
            {
                case "set":
                    return await HandleSetAsync(context, banphraseService, hasteBinService, logger);
                case "get":
                    return await HandleGetAsync(context, banphraseService, hasteBinService, logger);
                case "list":
                    return await HandleListAsync(context, banphraseService, hasteBinService, logger);
                case "test":
                    return await HandleTestAsync(context, banphraseService, logger);
                case "delete":
                    return await HandleDeleteAsync(context, banphraseService, logger);
                default:
                    return CommandResult.Failure($"Unknown action: {action}. Use: set, get, list, test, delete");
            }
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
        IHasteBinService hasteBinService,
        ILogger logger)
    {
        if (context.Arguments.Count < 4)
        {
            return CommandResult.Failure("Usage: !banphrases set <global|channel> <category> <pattern|hastebin-url>");
        }
        
        var section = context.Arguments[1].ToLowerInvariant();
        var categoryName = context.Arguments[2];
        var patternOrUrl = string.Join(" ", context.Arguments.Skip(3));
        
        string regexPattern;
        
        // Check if it's a Hastebin URL
        if (patternOrUrl.StartsWith("https://hastebin.dev/", StringComparison.OrdinalIgnoreCase) ||
            patternOrUrl.StartsWith("http://hastebin.dev/", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                regexPattern = await hasteBinService.GetTextAsync(patternOrUrl, context.CancellationToken);
                logger.LogInformation("Loaded banphrases from Hastebin: {Url}", patternOrUrl);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to fetch pattern from Hastebin: {Url}", patternOrUrl);
                return CommandResult.Failure($"Failed to fetch pattern from Hastebin: {ex.Message}");
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
            return CommandResult.Failure("Failed to set banphrase category. Check regex pattern syntax.");
        }
        
        var patternCount = CountRegexAlternatives(regexPattern);
        return CommandResult.Successfully(
            $"Setted {patternCount} {section} banphrases from your regex in category '{categoryName}'");
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
        
            // Count | only at top level (not inside groups or character classes)
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
        IHasteBinService hasteBinService,
        ILogger logger)
    {
        if (context.Arguments.Count < 3)
        {
            return CommandResult.Failure("Usage: !banphrases get <global|channel> <category>");
        }
        
        var section = context.Arguments[1].ToLowerInvariant();
        var categoryName = context.Arguments[2];
        var platform = context.Channel.Platform;
        var channelId = context.Channel.Id;
        
        var pattern = await banphraseService.GetCategoryAsync(section, platform, channelId, categoryName);
        
        if (string.IsNullOrEmpty(pattern))
        {
            return CommandResult.Failure($"Category '{categoryName}' not found in section '{section}'");
        }
        
        try
        {
            var url = await hasteBinService.UploadTextAsync(pattern, context.CancellationToken);
            return CommandResult.Successfully(
                $"Here's the regex for banned phrases in the '{section}' section of the '{categoryName}' category: {url}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to upload pattern to Hastebin");
            return CommandResult.Failure("Failed to upload pattern to Hastebin");
        }
    }
    
    private async Task<CommandResult> HandleListAsync(
        ICommandExecutionContext context,
        IBanphraseService banphraseService,
        IHasteBinService hasteBinService,
        ILogger logger)
    {
        var section = context.Arguments.Count > 1 ? context.Arguments[1].ToLowerInvariant() : "global";
        var platform = context.Channel.Platform;
        var channelId = context.Channel.Id;
        
        var categories = await banphraseService.ListCategoriesAsync(section, platform, channelId);
        
        if (categories.Count == 0)
        {
            return CommandResult.Successfully($"No categories found in section '{section}'");
        }
        
        // Get patterns for each category
        var listContent = new System.Text.StringBuilder();
        listContent.AppendLine($"List of categories in the '{section}' section:");
        
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
            var url = await hasteBinService.UploadTextAsync(listText, context.CancellationToken);
            return CommandResult.Successfully(
                $"Here are all the categories in the '{section}' section: {url}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to upload list to Hastebin");
            return CommandResult.Failure("Failed to upload list to Hastebin");
        }
    }
    
    private async Task<CommandResult> HandleTestAsync(
        ICommandExecutionContext context,
        IBanphraseService banphraseService,
        ILogger logger)
    {
        if (context.Arguments.Count < 3)
        {
            return CommandResult.Failure("Usage: !banphrases test <global|channel> <message>");
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
        
        var result = await banphraseService.CheckMessageAsync(testChannelId, testPlatform, message, context.CancellationToken);
        
        if (result.Passed)
        {
            return CommandResult.Successfully("The message passed the filter!");
        }
        else
        {
            var response = $"The message didn't pass the filter! Section: '{result.FailedSection}', category: '{result.FailedCategory}'";

            if (!string.IsNullOrEmpty(result.MatchedPhrase))
            {
                var displayPhrase = result.MatchedPhrase.Length > 50
                    ? result.MatchedPhrase[..50] + "..."
                    : result.MatchedPhrase;
                response += $", matched: '{displayPhrase}'";
            }

            if (!string.IsNullOrEmpty(result.MatchedPattern))
            {
                var displayPattern = result.MatchedPattern.Length > 50
                    ? result.MatchedPattern[..50] + "..."
                    : result.MatchedPattern;
                response += $", regex: '{displayPattern}'";
            }

            return CommandResult.Failure(response);
        }
    }
    
    private async Task<CommandResult> HandleDeleteAsync(
        ICommandExecutionContext context,
        IBanphraseService banphraseService,
        ILogger logger)
    {
        if (context.Arguments.Count < 3)
        {
            return CommandResult.Failure("Usage: !banphrases delete <global|channel> <category>");
        }
        
        var section = context.Arguments[1].ToLowerInvariant();
        var categoryName = context.Arguments[2];
        var platform = context.Channel.Platform;
        var channelId = context.Channel.Id;
        
        var success = await banphraseService.DeleteCategoryAsync(section, platform, channelId, categoryName);
        
        if (!success)
        {
            return CommandResult.Failure($"Failed to delete category '{categoryName}'");
        }
        
        return CommandResult.Successfully($"Deleted banphrase category '{categoryName}' from section '{section}'");
    }
}