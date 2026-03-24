using ButterBror.CommandModule.Commands;
using ButterBror.CommandModule.Context;
using ButterBror.Core.Interfaces;
using ButterBror.Domain;
using Microsoft.Extensions.Logging;

namespace ButterBror.Dashboard.Services;

/// <summary>
/// Executes admin commands from the dashboard
/// </summary>
public class AdminCommandExecutor
{
    private readonly ICommandDispatcher _dispatcher;
    private readonly ILogger<AdminCommandExecutor> _logger;

    public AdminCommandExecutor(
        ICommandDispatcher dispatcher,
        ILogger<AdminCommandExecutor> logger)
    {
        _dispatcher = dispatcher;
        _logger = logger;
    }

    /// <summary>
    /// Parse and execute a command line from the dashboard
    /// </summary>
    public async Task<CommandResult> ExecuteAsync(
        string commandLine,
        CancellationToken ct = default)
    {
        var tokens = commandLine.Trim()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (tokens.Length == 0)
            return CommandResult.Failure("Empty command");

        string commandName;
        string[] args;
        string commandPlatform;

        if (tokens[0].Contains(':') && tokens.Length >= 2)
        {
            commandPlatform = tokens[0];
            commandName = tokens[1];
            args = tokens.Skip(2).ToArray();
        }
        else
        {
            commandPlatform = "dashboard";
            commandName = tokens[0];
            args = tokens.Skip(1).ToArray();
        }

        _logger.LogInformation(
            "[Dashboard] Executing admin command: {Command} args=[{Args}] platform={Platform}",
            commandName, string.Join(", ", args), commandPlatform);

        var context = new DashboardCommandContext(commandName, args, commandPlatform);
        context.CancellationToken = ct;

        return await _dispatcher.DispatchAsync(context);
    }
}

/// <summary>
/// Synthetic command context for dashboard-initiated admin commands
/// </summary>
internal class DashboardCommandContext : ICommandContext
{
    public string CommandName { get; }
    public string[] Arguments { get; }
    public IPlatformUser User { get; }
    public IPlatformChannel Channel { get; }
    public DateTime ExecutedAt { get; } = DateTime.UtcNow;
    public string Platform { get; }
    public Guid CorrelationId { get; } = Guid.NewGuid();
    public CancellationToken CancellationToken { get; set; }

    public DashboardCommandContext(string commandName, string[] args, string commandPlatform)
    {
        CommandName = commandName;
        Arguments = args;
        Platform = commandPlatform;
        User = new DashboardAdminUser();
        Channel = new DashboardAdminChannel(commandPlatform);
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

internal class DashboardAdminChannel : IPlatformChannel
{
    public string Id => "dashboard";
    public string Name => "Dashboard";
    public string Platform { get; }

    public DashboardAdminChannel(string platform) => Platform = platform;
}
