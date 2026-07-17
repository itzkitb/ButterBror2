using System.Text;
using ButterBror.Core.Interfaces;
using ButterBror.Core.Modules.Commands;
using ButterBror.Core.Modules.Interfaces;
using ButterBror.Data;
using ButterBror.Localization.Models;
using ButterBror.Localization.Services;
using Microsoft.Extensions.Logging;

namespace ButterBror.Application.Commands;

/// <summary>
/// Unified command for locale management
/// </summary>
public class LocaleCommand : ICommand
{
    private static string _defaultLocale = "EN_US";
    public async Task<CommandResult> ExecuteAsync(
        ICommandExecutionContext context,
        ICommandServiceProvider serviceProvider)
    {
        try
        {
            var logger = serviceProvider.GetService<Logger<LocaleCommand>>();
            var localization = serviceProvider.GetService<ILocalizationService>();
            var userRepository = serviceProvider.GetService<IUserRepository>();

            if (context.Arguments.Count == 0)
            {
                return CommandResult.Failure(
                    await localization.GetStringAsync("command.locale.usage", _defaultLocale));
            }

            var action = context.Arguments[0].ToLowerInvariant();

            return action switch
            {
                "set" => await HandleSetAsync(context, serviceProvider, localization, userRepository, logger),
                "list" => await HandleListAsync(serviceProvider, localization),
                "delete" => await HandleDeleteAsync(context, serviceProvider, localization, userRepository, logger),
                "view" => await HandleViewAsync(context, serviceProvider, localization, userRepository, logger),
                "reload" => await HandleReloadAsync(context, serviceProvider, localization, userRepository, logger),
                _ => CommandResult.Failure(
                    await localization.GetStringAsync("command.locale.unknown", _defaultLocale))
            };
        }
        catch (Exception ex)
        {
            var errorTracking = serviceProvider.GetService<IErrorTrackingService>();
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
        ILocalizationService localization,
        IUserRepository userRepository,
        ILogger logger)
    {
        if (context.Arguments.Count < 2)
        {
            return CommandResult.Failure(
                await localization.GetStringAsync("command.locale.set.using", _defaultLocale));
        }

        var targetLocale = context.Arguments[1];
        var user = await userRepository.GetByPlatformIdAsync(context.User.Platform, context.User.Id);
        
        if (user == null)
            return CommandResult.Failure(
                await localization.GetStringAsync("command.locale.set.user_not_found", _defaultLocale));

        var resolvedLocale = localization.ResolveLocale(targetLocale);
        if (resolvedLocale == null)
        {
            var message = await localization.GetStringAsync(
                "command.locale.set.invalid", _defaultLocale,
                targetLocale);
            return CommandResult.Failure(message);
        }

        if (context.Arguments.Count >= 3)
        {
            return await HandleAdminSetAsync(context, serviceProvider, localization, userRepository, logger, resolvedLocale);
        }

        user.PreferredLocale = resolvedLocale;
        await userRepository.CreateOrUpdateAsync(user);

        var successMessage = await localization.GetStringAsync("command.locale.set.success", resolvedLocale);

        logger.LogDebug("User {UserId} changed locale to {Locale}", user.UnifiedUserId, resolvedLocale);
        return CommandResult.Successfully(successMessage, user);
    }

    private async Task<CommandResult> HandleAdminSetAsync(
        ICommandExecutionContext context,
        ICommandServiceProvider serviceProvider,
        ILocalizationService localization,
        IUserRepository userRepository,
        ILogger logger,
        string resolvedLocale)
    {
        var user = await userRepository.GetByPlatformIdAsync(context.User.Platform, context.User.Id);
        if (user == null || !await CheckAdminPermissionAsync(user.UnifiedUserId, serviceProvider))
            return CommandResult.Failure(
                await localization.GetStringAsync("command.locale.set.admin.permission", context.Locale));

        if (context.Arguments.Count < 3)
            return CommandResult.Failure(
                await localization.GetStringAsync("command.locale.set.admin.usage", context.Locale));

        var hasteUrl = context.Arguments[2];

        var pasteBinService = serviceProvider.GetService<IPasteBinService>();
        var fileLoader = serviceProvider.GetService<TranslationFileLoader>();
        var registry = serviceProvider.GetService<LocaleRegistryService>();

        var jsonContent = await pasteBinService.GetTextAsync(hasteUrl, context.CancellationToken);
        var translation = System.Text.Json.JsonSerializer.Deserialize<TranslationFile>(
            jsonContent, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        
        if (translation?.Strings == null)
            return CommandResult.Failure(
                await localization.GetStringAsync("command.locale.set.admin.invalid", context.Locale));

        var fileName = $"{resolvedLocale}.json";
        await fileLoader.SaveTranslationAsync(fileName, translation, context.CancellationToken);
        
        var aliases = new[] { resolvedLocale.ToLower(), resolvedLocale.ToLowerInvariant() }.Distinct().ToList();
        await registry.RegisterLocaleAsync(resolvedLocale, fileName, aliases, context.CancellationToken);
        await localization.ReloadAsync(context.CancellationToken);

        logger.LogInformation("Updated locale {Locale} from HasteBin by admin {AdminId}", 
            resolvedLocale, user.UnifiedUserId);
        
        return CommandResult.Successfully(
            await localization.GetStringAsync("command.locale.set.admin.success", context.Locale,
                resolvedLocale));
    }

    private async Task<CommandResult> HandleListAsync(
        ICommandServiceProvider serviceProvider,
        ILocalizationService localization)
    {
        var registry = serviceProvider.GetService<LocaleRegistryService>();
        var defaultLocale = registry.GetDefaultLocale();
        var locales = registry.GetAllLocales();

        var sb = new StringBuilder();
        sb.AppendLine(
            await localization.GetStringAsync("command.locale.list", _defaultLocale, defaultLocale));
        
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
        ILocalizationService localization,
        IUserRepository userRepository,
        ILogger logger)
    {
        var user = await userRepository.GetByPlatformIdAsync(context.User.Platform, context.User.Id);
        if (user == null || !await CheckAdminPermissionAsync(user.UnifiedUserId, serviceProvider))
            return CommandResult.Failure(
                await localization.GetStringAsync("command.locale.delete.permission", context.Locale));

        if (context.Arguments.Count < 2)
            return CommandResult.Failure(
                await localization.GetStringAsync("command.locale.delete.usage", context.Locale));

        var registry = serviceProvider.GetService<LocaleRegistryService>();
        var resolved = localization.ResolveLocale(context.Arguments[1]);
        
        if (resolved == null)
            return CommandResult.Failure(
                await localization.GetStringAsync("command.locale.delete.unknown", context.Locale,
                    context.Arguments[1]));

        if (resolved.Equals(registry.GetDefaultLocale(), StringComparison.OrdinalIgnoreCase))
            return CommandResult.Failure(
                await localization.GetStringAsync("command.locale.delete.default", context.Locale));

        var success = await registry.UnregisterLocaleAsync(resolved, context.CancellationToken);
        if (success)
        {
            await localization.ReloadAsync(context.CancellationToken);
            logger.LogInformation("Deleted locale {Locale} by admin {AdminId}", resolved, user.UnifiedUserId);
            return CommandResult.Successfully(
                await localization.GetStringAsync("command.locale.delete.success", context.Locale,
                    resolved));
        }

        return CommandResult.Failure(
            await localization.GetStringAsync("command.locale.delete.fail", context.Locale,
                resolved));
    }

    private async Task<CommandResult> HandleViewAsync(
        ICommandExecutionContext context,
        ICommandServiceProvider serviceProvider,
        ILocalizationService localization,
        IUserRepository userRepository,
        ILogger logger)
    {
        var user = await userRepository.GetByPlatformIdAsync(context.User.Platform, context.User.Id);
        if (user == null || !await CheckAdminPermissionAsync(user.UnifiedUserId, serviceProvider))
            return CommandResult.Failure(
                await localization.GetStringAsync("command.locale.view.permission", context.Locale));

        if (context.Arguments.Count < 2)
            return CommandResult.Failure(
                await localization.GetStringAsync("command.locale.view.usage", context.Locale));

        var resolved = localization.ResolveLocale(context.Arguments[1]);
        if (resolved == null)
            return CommandResult.Failure(
                await localization.GetStringAsync("command.locale.view.unknown", context.Locale,
                    context.Arguments[1]));

        var fileLoader = serviceProvider.GetService<TranslationFileLoader>();
        var pasteBinService = serviceProvider.GetService<IPasteBinService>();

        var fileName = $"{resolved}.json";
        var path = fileLoader.GetTranslationFilePath(fileName);
        
        if (!File.Exists(path))
            return CommandResult.Failure(
                await localization.GetStringAsync("command.locale.view.not_found", context.Locale,
                    fileName));

        var content = await File.ReadAllTextAsync(path, context.CancellationToken);
        var url = await pasteBinService.UploadTextAsync(content, context.CancellationToken);
        
        logger.LogInformation("Uploaded locale {Locale} to HasteBin: {Url}", resolved, url);
        return CommandResult.Successfully(
            await localization.GetStringAsync("command.locale.view.success", context.Locale,
                resolved,
                url));
    }

    private async Task<CommandResult> HandleReloadAsync(
        ICommandExecutionContext context,
        ICommandServiceProvider serviceProvider,
        ILocalizationService localization,
        IUserRepository userRepository,
        ILogger logger)
    {
        var user = await userRepository.GetByPlatformIdAsync(context.User.Platform, context.User.Id);
        if (user == null || !await CheckAdminPermissionAsync(user.UnifiedUserId, serviceProvider))
            return CommandResult.Failure(
                await localization.GetStringAsync("command.locale.reload.permission", context.Locale));

        await localization.ReloadAsync(context.CancellationToken);
        logger.LogInformation("Localization cache reloaded by admin {AdminId}", user.UnifiedUserId);
        return CommandResult.Successfully(
            await localization.GetStringAsync("command.locale.reload.success", context.Locale));
    }

    private async Task<bool> CheckAdminPermissionAsync(Guid unifiedUserId, ICommandServiceProvider serviceProvider)
    {
        var permissionManager = serviceProvider.GetService<IPermissionManager>();
        return await permissionManager.HasPermissionAsync(unifiedUserId, "su:lang");
    }
}