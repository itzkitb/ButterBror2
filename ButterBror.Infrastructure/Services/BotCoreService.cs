using ButterBror.CommandModule.Commands;
using ButterBror.CommandModule.Context;
using ButterBror.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace ButterBror.Infrastructure.Services;

public class BotCoreService : IBotCore
{
    private readonly IPlatformModuleManager _moduleManager;
    private readonly ICommandProcessor _commandProcessor;
    private readonly IUserService _userService;
    private readonly ILogger<BotCoreService> _logger;
    private readonly CancellationTokenSource _cts = new();

    public BotCoreService(
        IPlatformModuleManager moduleManager,
        ICommandProcessor commandProcessor,
        IUserService userService,
        ILogger<BotCoreService> logger)
    {
        _moduleManager = moduleManager;
        _commandProcessor = commandProcessor;
        _userService = userService;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting bot core...");

        await _userService.InitializeAsync(cancellationToken);
        await _moduleManager.InitializeAsync(this, cancellationToken);

        _logger.LogInformation("Bot core started successfully");
    }

    public async Task<CommandResult> ProcessCommandAsync(ICommandContext context)
    {
        return await _commandProcessor.ProcessCommandAsync(context);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _cts.Cancel();
        await _moduleManager.ShutdownAsync(cancellationToken);
        _logger.LogInformation("Bot core stopped successfully");
    }
}
