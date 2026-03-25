using System.Text;
using ButterBror.CommandModule.Commands;
using ButterBror.CommandModule.Context;
using ButterBror.Core.Interfaces;
using ButterBror.Data;
using ButterBror.Localization.Models;
using ButterBror.Localization.Services;
using Microsoft.Extensions.Logging;

namespace ButterBror.Application.Commands;

/// <summary>
/// Unified command for locale management
/// </summary>
public class LocaleCommand : CommandBase
{
    public override async Task<CommandResult> ExecuteAsync(
        ICommandExecutionContext context,
        ICommandServiceProvider serviceProvider)
    {
        try
        {
            var logger = GetLogger<LocaleCommand>(serviceProvider);
            var localizationService = GetService<ILocalizationService>(serviceProvider);
            var userRepository = GetService<IUserRepository>(serviceProvider);

            if (context.Arguments.Count == 0)
            {
                return CommandResult.Failure("Usage: !locale set <locale> [url] | list | delete <locale> | view <locale> | reload");
            }

            var action = context.Arguments[0].ToLowerInvariant();

            return action switch
            {
                "set" => await HandleSetAsync(context, serviceProvider, localizationService, userRepository, logger),
                "list" => await HandleListAsync(serviceProvider),
                "delete" => await HandleDeleteAsync(context, serviceProvider, localizationService, userRepository, logger),
                "view" => await HandleViewAsync(context, serviceProvider, localizationService, userRepository, logger),
                "reload" => await HandleReloadAsync(context, serviceProvider, localizationService, userRepository, logger),
                _ => CommandResult.Failure($"Unknown action. Use: set, list, delete, view, reload")
            };
        }
        catch (Exception ex)
        {
            var errorTracking = GetService<IErrorTrackingService>(serviceProvider);
            return await errorTracking.LogErrorAsync(
                ex,
                "Failed to execute LocaleCommand",
                context.User.Id,
                context.Channel.Platform,
                context);
        }
    }

    private async Task<CommandResult> HandleSetAsync(
        ICommandExecutionContext context,
        ICommandServiceProvider serviceProvider,
        ILocalizationService localizationService,
        IUserRepository userRepository,
        ILogger logger)
    {
        if (context.Arguments.Count < 2)
        {
            return CommandResult.Failure("Usage: !locale set <locale> [url]");
        }

        var targetLocale = context.Arguments[1];
        var user = await userRepository.GetByPlatformIdAsync(context.User.Platform, context.User.Id);
        
        if (user == null)
            return CommandResult.Failure("User profile not found");

        var resolvedLocale = localizationService.ResolveLocale(targetLocale);
        if (resolvedLocale == null)
        {
            var message = await localizationService.GetStringAsync(
                "commands.locale.set.invalid", "EN_US", targetLocale);
            return CommandResult.Failure(message);
        }

        if (context.Arguments.Count >= 3)
        {
            return await HandleAdminSetAsync(context, serviceProvider, localizationService, userRepository, logger, resolvedLocale);
        }

        user.PreferredLocale = resolvedLocale;
        await userRepository.CreateOrUpdateAsync(user);

        var successMessage = await localizationService.GetStringAsync(
            "commands.locale.set.success", resolvedLocale, resolvedLocale);

        logger.LogInformation("User {UserId} changed locale to {Locale}", user.UnifiedUserId, resolvedLocale);
        return CommandResult.Successfully(successMessage, user);
    }

    private async Task<CommandResult> HandleAdminSetAsync(
        ICommandExecutionContext context,
        ICommandServiceProvider serviceProvider,
        ILocalizationService localizationService,
        IUserRepository userRepository,
        ILogger logger,
        string resolvedLocale)
    {
        var user = await userRepository.GetByPlatformIdAsync(context.User.Platform, context.User.Id);
        if (user == null || !await CheckAdminPermissionAsync(user.UnifiedUserId, serviceProvider))
            return CommandResult.Failure("Permission denied. Required: su:lang");

        if (context.Arguments.Count < 3)
            return CommandResult.Failure("Usage: !locale set <locale> <hastebin_url>");

        var hasteUrl = context.Arguments[2];

        var hasteBinService = GetService<IHasteBinService>(serviceProvider);
        var fileLoader = GetService<TranslationFileLoader>(serviceProvider);
        var registry = GetService<LocaleRegistryService>(serviceProvider);

        var jsonContent = await hasteBinService.GetTextAsync(hasteUrl, context.CancellationToken);
        var translation = System.Text.Json.JsonSerializer.Deserialize<TranslationFile>(
            jsonContent, 
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        
        if (translation?.Strings == null)
            return CommandResult.Failure("Invalid translation format: missing 'strings' object");

        var fileName = $"{resolvedLocale}.json";
        await fileLoader.SaveTranslationAsync(fileName, translation, context.CancellationToken);
        
        var aliases = new[] { resolvedLocale.ToLower(), resolvedLocale.ToLowerInvariant() }.Distinct().ToList();
        await registry.RegisterLocaleAsync(resolvedLocale, fileName, aliases, context.CancellationToken);
        await localizationService.ReloadAsync(context.CancellationToken);

        logger.LogInformation("Updated locale {Locale} from HasteBin by admin {AdminId}", 
            resolvedLocale, user.UnifiedUserId);
        
        return CommandResult.Successfully($"Locale {resolvedLocale} updated successfully");
    }

    private async Task<CommandResult> HandleListAsync(ICommandServiceProvider serviceProvider)
    {
        var registry = GetService<LocaleRegistryService>(serviceProvider);
        var defaultLocale = registry.GetDefaultLocale();
        var locales = registry.GetAllLocales();

        var sb = new StringBuilder();
        sb.AppendLine($"Available Locales (default: {defaultLocale}):");
        
        foreach (var locale in locales)
        {
            var meta = registry.GetLocaleMetadata(locale);
            var aliases = meta?.Aliases.Any() == true 
                ? $" ({string.Join(", ", meta.Aliases)})" 
                : "";
            sb.AppendLine($"{locale}{aliases}; ");
        }

        return CommandResult.Successfully(sb.ToString());
    }

    private async Task<CommandResult> HandleDeleteAsync(
        ICommandExecutionContext context,
        ICommandServiceProvider serviceProvider,
        ILocalizationService localizationService,
        IUserRepository userRepository,
        ILogger logger)
    {
        var user = await userRepository.GetByPlatformIdAsync(context.User.Platform, context.User.Id);
        if (user == null || !await CheckAdminPermissionAsync(user.UnifiedUserId, serviceProvider))
            return CommandResult.Failure("Permission denied. Required: su:lang");

        if (context.Arguments.Count < 2)
            return CommandResult.Failure("Usage: !locale delete <locale>");

        var registry = GetService<LocaleRegistryService>(serviceProvider);
        var resolved = localizationService.ResolveLocale(context.Arguments[1]);
        
        if (resolved == null)
            return CommandResult.Failure($"Unknown locale: {context.Arguments[1]}");

        if (resolved.Equals(registry.GetDefaultLocale(), StringComparison.OrdinalIgnoreCase))
            return CommandResult.Failure("Cannot delete the default locale");

        var success = await registry.UnregisterLocaleAsync(resolved, context.CancellationToken);
        if (success)
        {
            await localizationService.ReloadAsync(context.CancellationToken);
            logger.LogInformation("Deleted locale {Locale} by admin {AdminId}", resolved, user.UnifiedUserId);
            return CommandResult.Successfully($"Locale {resolved} deleted");
        }

        return CommandResult.Failure($"Failed to delete locale {resolved}");
    }

    private async Task<CommandResult> HandleViewAsync(
        ICommandExecutionContext context,
        ICommandServiceProvider serviceProvider,
        ILocalizationService localizationService,
        IUserRepository userRepository,
        ILogger logger)
    {
        var user = await userRepository.GetByPlatformIdAsync(context.User.Platform, context.User.Id);
        if (user == null || !await CheckAdminPermissionAsync(user.UnifiedUserId, serviceProvider))
            return CommandResult.Failure("Permission denied. Required: su:lang");

        if (context.Arguments.Count < 2)
            return CommandResult.Failure("Usage: !locale view <locale>");

        var resolved = localizationService.ResolveLocale(context.Arguments[1]);
        if (resolved == null)
            return CommandResult.Failure($"Unknown locale: {context.Arguments[1]}");

        var fileLoader = GetService<TranslationFileLoader>(serviceProvider);
        var hasteBinService = GetService<IHasteBinService>(serviceProvider);

        var fileName = $"{resolved}.json";
        var path = fileLoader.GetTranslationFilePath(fileName);
        
        if (!System.IO.File.Exists(path))
            return CommandResult.Failure($"Translation file not found: {fileName}");

        var content = await System.IO.File.ReadAllTextAsync(path, context.CancellationToken);
        var url = await hasteBinService.UploadTextAsync(content, context.CancellationToken);
        
        logger.LogInformation("Uploaded locale {Locale} to HasteBin: {Url}", resolved, url);
        return CommandResult.Successfully($"Locale {resolved} uploaded: {url}");
    }

    private async Task<CommandResult> HandleReloadAsync(
        ICommandExecutionContext context,
        ICommandServiceProvider serviceProvider,
        ILocalizationService localizationService,
        IUserRepository userRepository,
        ILogger logger)
    {
        var user = await userRepository.GetByPlatformIdAsync(context.User.Platform, context.User.Id);
        if (user == null || !await CheckAdminPermissionAsync(user.UnifiedUserId, serviceProvider))
            return CommandResult.Failure("Permission denied. Required: su:lang");

        await localizationService.ReloadAsync(context.CancellationToken);
        logger.LogInformation("Localization cache reloaded by admin {AdminId}", user.UnifiedUserId);
        return CommandResult.Successfully("Localization cache reloaded successfully");
    }

    private async Task<bool> CheckAdminPermissionAsync(Guid unifiedUserId, ICommandServiceProvider serviceProvider)
    {
        var permissionManager = GetService<IPermissionManager>(serviceProvider);
        return await permissionManager.HasPermissionAsync(unifiedUserId, "su:lang");
    }
}