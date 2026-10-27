using System.Text;
using ButterBror.Core.Interfaces;
using ButterBror.Core.Modules;
using ButterBror.Core.Modules.Commands;
using ButterBror.Core.Modules.Interfaces;

namespace ButterBror.Application.Commands;

public class ModuleCommand : ICommand
{
    public async Task<CommandResult> ExecuteAsync(
        CommandContext context,
        ICommandServiceProvider serviceProvider)
    {
        var localization = serviceProvider.GetService<ILocaleService>();
        var moduleManager = serviceProvider.GetService<IPlatformModuleManager>();

        if (context.Arguments.Count < 1)
        {
            return CommandResult.Failure(
                await localization.GetStringAsync("command.module.usage", context.Locale));
        }

        var sub = context.Arguments[0].ToLowerInvariant();

        return sub switch
        {
            "reload" => await HandleReloadAsync(context, moduleManager, localization),
            "disable" => await HandleDisableAsync(context, moduleManager, localization),
            "enable" => await HandleEnableAsync(context, moduleManager, localization),
            "reloadall" => await HandleSimpleAsync(
                moduleManager.ReloadAllModulesAsync,
                "command.module.reloadall.success",
                context, localization),
            "disableall" => await HandleSimpleAsync(
                moduleManager.DisableAllModulesAsync,
                "command.module.disableall.success",
                context, localization),
            "enableall" => await HandleSimpleAsync(
                moduleManager.EnableAllModulesAsync,
                "command.module.enableall.success",
                context, localization),
            "list" => await HandleListAsync(context, moduleManager, localization),
            "status" => await HandleStatusAsync(context, moduleManager, localization),
            "loadfromdisk" => await HandleLoadFromDiskAsync(context, moduleManager, localization),
            _ => CommandResult.Failure(
                await localization.GetStringAsync("command.module.unknown", context.Locale))
        };
    }

    private static Task<CommandResult> HandleSimpleAsync(
        Func<CancellationToken, Task<string>> action,
        string successKey,
        CommandContext context,
        ILocaleService localization)
        => HandleSimpleCoreAsync(action, successKey, context, localization);

    private static async Task<CommandResult> HandleSimpleCoreAsync(
        Func<CancellationToken, Task<string>> action,
        string successKey,
        CommandContext context,
        ILocaleService localization)
    {
        var result = await action(context.CancellationToken);
        return await MapResultAsync(result, successKey, context, localization);
    }

    private static async Task<CommandResult> HandleReloadAsync(
        CommandContext context,
        IPlatformModuleManager manager,
        ILocaleService localization)
    {
        if (context.Arguments.Count < 2)
            return CommandResult.Failure(
                await localization.GetStringAsync("command.module.reload.usage", context.Locale));

        var id = context.Arguments[1];
        var status = await manager.GetModuleStatusAsync(id, context.CancellationToken);

        string result;
        if (status == null)
        {
            // try load from disk if not loaded
            result = await manager.LoadModuleFromDiskAsync(id, context.CancellationToken);
        }
        else if (status.Kind == "chat")
        {
            result = await manager.ReloadChatModuleAsync(id, context.CancellationToken);
        }
        else
        {
            result = await manager.ReloadCommandModuleAsync(id, context.CancellationToken);
        }

        return await MapResultAsync(result, "command.module.reload.success", context, localization, id);
    }

    private static async Task<CommandResult> HandleDisableAsync(
        CommandContext context,
        IPlatformModuleManager manager,
        ILocaleService localization)
    {
        if (context.Arguments.Count < 2)
            return CommandResult.Failure(
                await localization.GetStringAsync("command.module.disable.usage", context.Locale));

        var id = context.Arguments[1];
        var result = await manager.DisableModuleAsync(id, context.CancellationToken);
        return await MapResultAsync(result, "command.module.disable.success", context, localization, id);
    }

    private static async Task<CommandResult> HandleEnableAsync(
        CommandContext context,
        IPlatformModuleManager manager,
        ILocaleService localization)
    {
        if (context.Arguments.Count < 2)
            return CommandResult.Failure(
                await localization.GetStringAsync("command.module.enable.usage", context.Locale));

        var id = context.Arguments[1];
        var result = await manager.EnableModuleAsync(id, context.CancellationToken);
        return await MapResultAsync(result, "command.module.enable.success", context, localization, id);
    }

    private static async Task<CommandResult> HandleLoadFromDiskAsync(
        CommandContext context,
        IPlatformModuleManager manager,
        ILocaleService localization)
    {
        if (context.Arguments.Count < 2)
            return CommandResult.Failure(
                await localization.GetStringAsync("command.module.loadfromdisk.usage", context.Locale));

        var id = context.Arguments[1];
        var result = await manager.LoadModuleFromDiskAsync(id, context.CancellationToken);
        return await MapResultAsync(result, "command.module.loadfromdisk.success", context, localization, id);
    }

    private static async Task<CommandResult> HandleListAsync(
        CommandContext context,
        IPlatformModuleManager manager,
        ILocaleService localization)
    {
        var modules = await manager.GetLoadedModulesAsync(context.CancellationToken);
        if (modules.Count == 0)
            return CommandResult.Successfully(
                await localization.GetStringAsync("command.module.list.empty", context.Locale));

        var sb = new StringBuilder();
        sb.AppendLine(await localization.GetStringAsync("command.module.list.title", context.Locale));
        foreach (var m in modules.OrderBy(m => m.Kind).ThenBy(m => m.ModuleId))
        {
            sb.AppendLine(FormatModule(m, context.Locale, localization));
        }

        return CommandResult.Successfully(sb.ToString().TrimEnd());
    }

    private static async Task<CommandResult> HandleStatusAsync(
        CommandContext context,
        IPlatformModuleManager manager,
        ILocaleService localization)
    {
        if (context.Arguments.Count < 2)
            return CommandResult.Failure(
                await localization.GetStringAsync("command.module.status.usage", context.Locale));

        var id = context.Arguments[1];
        var info = await manager.GetModuleStatusAsync(id, context.CancellationToken);
        if (info == null)
            return CommandResult.Failure(
                await localization.GetStringAsync("command.module.not_found", context.Locale, id));

        return CommandResult.Successfully(FormatModule(info, context.Locale, localization));
    }

    private static string FormatModule(ModuleInfo info, string locale, ILocaleService localization)
    {
        var state = info.IsEnabled ? "enabled" : "disabled";
        var version = info.Version?.ToString() ?? "?";
        var line = $"[{info.Kind}] {info.ModuleId} v{version} - {state} ({info.ExportedCommandsCount} cmds)";
        return line;
    }

    private static async Task<CommandResult> MapResultAsync(
        string result,
        string successKey,
        CommandContext context,
        ILocaleService localization,
        string? id = null)
    {
        return result switch
        {
            "success" => CommandResult.Successfully(
                await localization.GetStringAsync(successKey, context.Locale, id ?? "")),
            "error:not_found" => CommandResult.Failure(
                await localization.GetStringAsync("command.module.not_found", context.Locale, id ?? "")),
            "error:not_found_local" => CommandResult.Failure(
                await localization.GetStringAsync("command.module.not_found_local", context.Locale, id ?? "")),
            _ => CommandResult.Failure(
                await localization.GetStringAsync("command.module.exception", context.Locale))
        };
    }
}
