using ButterBror.Core.Interfaces;
using ButterBror.Core.Modules.Commands;
using ButterBror.Domain;
using ButterBror.Domain.Chat;
using Microsoft.Extensions.Logging;

namespace ButterBror.Dashboard.Services;

/// <summary>
/// Executes admin commands from the dashboard
/// </summary>
public class AdminCommandExecutor(
    IBotCore core,
    ILogger<AdminCommandExecutor> logger)
{
    /// <summary>
    /// Parse and execute a command line from the dashboard
    /// </summary>
    public async Task<string> ExecuteAsync(
        string commandLine,
        CancellationToken ct = default)
    {
        var tokens = commandLine.Trim()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (tokens.Length == 0)
            return "Empty command";

        string commandName;
        List<string> args;
        string commandPlatform;

        if (tokens[0].Contains(':') && tokens.Length >= 2)
        {
            commandPlatform = tokens[0];
            commandName = tokens[1];
            args = tokens.Skip(2).ToList();
        }
        else
        {
            commandPlatform = "dashboard";
            commandName = tokens[0];
            args = tokens.Skip(1).ToList();
        }

        logger.LogInformation(
            "[D:] executing command. command='{Command}', args=['{Args}'], platform='{Platform}'",
            commandName, string.Join("', '", args), commandPlatform);

        var user = new DashboardAdminUser();
        var channel = new DashboardAdminChannel(commandPlatform);
        var message = new ChatMessage(
            DateTime.UtcNow,
            string.Join(" ", args),
            user.Id,
            user.DisplayName,
            channel.Id,
            channel.Name,
            null
        );
        var context = new CommandContext(
            commandName, 
            commandPlatform,
            args,
            user,
            channel,
            message,
            ct);
        var result = await core.ProcessCommandAsync(context);
        
        return result.Message?.RawText ?? "Empty result";
    }
}

internal class DashboardAdminUser : IPlatformUser
{
    public string Id => "dashboard-admin";
    public string DisplayName => "Dashboard Admin";
    public string Platform => "dashboard";
    public bool IsModerator => true;
    public bool IsBroadcaster => true;
}

internal class DashboardAdminChannel(string platform) : IPlatformChannel
{
    public string Id => "dashboard";
    public string Name => "Dashboard";
    public string Platform { get; } = platform;
}
