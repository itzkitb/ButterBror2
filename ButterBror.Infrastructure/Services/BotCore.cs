using ButterBror.Core.Interfaces;
using ButterBror.Core.Modules.Commands;
using ButterBror.Core.Modules.Interfaces;
using ButterBror.Core.Scopes;
using ButterBror.Domain.Chat;
using Microsoft.Extensions.Logging;

namespace ButterBror.Infrastructure.Services;

public class BotCore(
    IPlatformModuleManager moduleManager,
    ICommandProcessor commandProcessor,
    IBotStatsService statsService,
    ILocalizationService localizationService,
    IBanphraseService banphraseService,
    ICommandRegistry commandRegistry,
    IPermissionManager permissionManager,
    IDeviceStatsService deviceStatsService,
    IDashboardService dashboardService,
    IUserService userService,
    IChatService chatService,
    ILogger<BotCore> logger)
    : IBotCore
{
    private readonly CancellationTokenSource _cts = new();
    public event EventHandler<ChatMessageReceivedEventArgs>? OnChatMessageReceived;

    public async Task StartAsync(CancellationToken ct = default)
    {
        await using var _ = new InitializationScope(logger, "bot core", true);
        
        await Task.WhenAll(
            userService.InitializeAsync(ct),
            moduleManager.InitializeAsync(this, ct),
            commandRegistry.InitializeAsync(ct),
            statsService.StartAsync(ct),
            localizationService.InitializeAsync(ct),
            banphraseService.ReloadGlobalCategoriesAsync(),
            InitDashboardAdmin(),
            deviceStatsService.InitializeAsync(ct),
            dashboardService.StartAsync(ct)
        );
    }

    private async Task InitDashboardAdmin()
    {
        try
        {
            await using (new InitializationScope(logger, "dashboard admin"))
            {
                var adminUser = await userService.GetOrCreateUserAsync(
                    platformId: "admin",
                    platform: "dashboard",
                    displayName: "Admin"
                );

                await permissionManager.AddPermissionAsync(adminUser.UnifiedId, "su:*");
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "failed to init dashboard admin user");
        }
    }
    
    public async Task<CommandResult> ProcessCommandAsync(CommandContext context) => 
        await commandProcessor.ProcessCommandAsync(context);

    public async Task StopAsync(CancellationToken ct = default)
    {
        await using var _ = new StopScope(logger, "bot core", true);

        await _cts.CancelAsync();
        await Task.WhenAll(
            moduleManager.ShutdownAsync(ct),
            statsService.StopAsync(ct),
            deviceStatsService.ShutdownAsync(ct),
            dashboardService.StopAsync(ct)
        );
    }

    public async Task RaiseMessageReceivedAsync(
        string moduleId,
        ChatMessage message,
        CancellationToken ct = default)
    {
        var user = await userService.GetOrCreateUserAsync(
            message.PlatformUserId,
            moduleId,
            message.PlatformUserName);

        var chat = await chatService.GetOrCreateChatAsync(
            message.PlatformChatId,
            moduleId,
            message.PlatformChatName);
        
        OnChatMessageReceived?.Invoke(this, new ChatMessageReceivedEventArgs
        {
            Text = message.Text,
            ModuleId = moduleId,
            Message = message,
            User = user,
            Chat = chat,
            ExtraData = message.ExtraData,
            ReceivedAt = message.ReceivedAt,
            UnifiedUserId = user.UnifiedId,
            PlatformChatId = message.PlatformChatId,
            PlatformChatName = message.PlatformChatName
        });
    }
}