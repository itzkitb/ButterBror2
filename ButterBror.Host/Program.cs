using ButterBror.Application.Commands;
using ButterBror.Application.Commands.Meta;
using ButterBror.ChatModule;
using ButterBror.Core.Interfaces;
using ButterBror.Dashboard;
using ButterBror.Dashboard.Services;
using ButterBror.Data;
using ButterBror.Domain;
using ButterBror.Host;
using ButterBror.Host.Logging;
using ButterBror.Infrastructure.Resilience;
using ButterBror.Infrastructure.Services;
using ButterBror.Infrastructure.Storage;
using ButterBror.Localization.Services;
using ButterBror.Modules.Loader;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Text;

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding  = Encoding.UTF8;

var builder = Host.CreateApplicationBuilder(args);

// Logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options =>
{
    options.FormatterName = CustomConsoleFormatter.FormatterName;
});
builder.Services.Configure<CustomConsoleFormatterOptions>(options =>
{
    if (Environment.GetEnvironmentVariable("CI") == "true" ||
        Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true")
    {
        options.UseColors = false;
    }

    // options.UseTrueColor = true;
});
builder.Logging.AddConsoleFormatter<CustomConsoleFormatter, CustomConsoleFormatterOptions>();

// Filter informational logs
builder.Logging.AddFilter("Polly", LogLevel.Warning);
builder.Logging.AddFilter("Polly.Core", LogLevel.Warning);
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);

// Version checker
builder.Services.AddSingleton<IBotCoreInfo, BotCoreInfo>();

// Dashboard
builder.Services.Configure<DashboardOptions>(builder.Configuration.GetSection("Dashboard"));
builder.Services.AddSingleton<IDashboardBridge, DashboardBridge>();
builder.Services.AddSingleton<MetricsCollector>();
builder.Services.AddSingleton<AdminCommandExecutor>();
builder.Services.AddSingleton<RedisExplorerService>();
builder.Services.AddSingleton<FileManagerService>();
builder.Services.AddHostedService<DashboardServer>();

// Drive statistics service
builder.Services.AddSingleton<IDeviceStatsService, DeviceStatsService>();
builder.Services.AddHostedService<DeviceStatsHostedService>();

// Services
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<ICommandProcessor, CommandProcessor>();
builder.Services.AddSingleton<AppDataStorageProvider>();
builder.Services.AddSingleton<IAppDataPathProvider>(sp => sp.GetRequiredService<AppDataStorageProvider>());
builder.Services.AddScoped<IFormatterService, FormatterService>();

// Bot Stats Service
builder.Services.AddSingleton<IBotStatsService, BotStatsService>();

// Use the new unified command dispatcher
builder.Services.AddSingleton<ICommandDispatcher, CommandDispatcher>();
builder.Services.AddSingleton<IPlatformModuleManager, PlatformModuleManager>();
builder.Services.AddSingleton<IChatModuleRegistry, PlatformModuleRegistry>();
builder.Services.AddSingleton<ICommandRegistry, CommandRegistry>();

// Module Loaders
builder.Services.AddSingleton<IChatModuleLoader, ChatModuleLoader>();
builder.Services.AddSingleton<ICommandModuleLoader, CommandModuleLoader>();

// Redis
var redisConfig = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
    ConnectionMultiplexer.Connect(redisConfig));

// Repositories
builder.Services.RegisterResilienceStrategies();
builder.Services.AddScoped<IUserRepository, RedisUserRepository>();
builder.Services.AddScoped<ICommandUsageRepository, RedisCommandUsageRepository>();
builder.Services.AddSingleton<ICustomDataRepository, RedisCustomDataRepository>();

// Permission Manager
builder.Services.AddScoped<IPermissionManager, PermissionManager>();

// Core
builder.Services.AddSingleton<IBotCore, BotCoreService>();

// Background service
builder.Services.AddHostedService<BotHostedService>();

// HasteBin Service
builder.Services.AddHttpClient<IHasteBinService, HasteBinService>()
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
    });

// Banphrase Service
builder.Services.AddScoped<IBanphraseRepository, BanphraseRepository>();
builder.Services.AddSingleton<IBanphraseService, BanphraseService>();

// Error Tracking Service
builder.Services.AddScoped<IErrorReportRepository, ErrorReportRepository>();
builder.Services.AddScoped<IErrorTrackingService, ErrorTrackingService>();

// Localization Service
builder.Services.AddSingleton<TranslationFileLoader>();
builder.Services.AddSingleton<LocaleRegistryService>();
builder.Services.AddSingleton<ILocalizationService, LocalizationService>();

var host = builder.Build();

var logger = host.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("· - —==≡ ButterBror is starting ≡==- — ·");

var coreInfoService = host.Services.GetRequiredService<IBotCoreInfo>();
coreInfoService.Initialize();

// Register Dashboard logger provider after build
var bridge = host.Services.GetRequiredService<IDashboardBridge>();
var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
loggerFactory.AddProvider(new DashboardLoggerProvider(bridge));

// Initialize BotStatsService
var statsService = host.Services.GetRequiredService<IBotStatsService>();
await statsService.InitializeAsync(CancellationToken.None);

// Register graceful shutdown for BotStatsService
var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStopping.Register(async () =>
{
    await statsService.FlushAsync(CancellationToken.None);
});

// Initialize dashboard admin user in Redis
using (var scope = host.Services.CreateScope())
{
    try
    {
        var userService = scope.ServiceProvider.GetRequiredService<IUserService>();
        var permManager = scope.ServiceProvider.GetRequiredService<IPermissionManager>();

        // Create or retrieve the dashboard-admin user
        var adminUser = await userService.GetOrCreateUserAsync(
            platformId: "dashboard-admin",
            platform: "dashboard",
            displayName: "Dashboard Admin"
        );

        // Grant super-admin permissions if not already granted
        await permManager.AddPermissionAsync(adminUser.UnifiedUserId, "su:*");

        logger.LogInformation(
            "Dashboard admin initialized. unified_uid='{UserId}'",
            adminUser.UnifiedUserId);
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Failed to initialize dashboard admin user (Redis may not be ready yet)");
    }
}

// Register all commands after services are built
using (var scope = host.Services.CreateScope())
{
    var commandRegistry = scope.ServiceProvider.GetRequiredService<ICommandRegistry>();

    commandRegistry.RegisterGlobalCommand(
        "userinfo",
        () => new UserInfoCommand(),
        new UserInfoMeta()
    );
    commandRegistry.RegisterGlobalCommand(
        "banphrases",
        () => new BanphrasesCommand(),
        new BanphrasesCommandMeta()
    );
    commandRegistry.RegisterGlobalCommand(
        "locale",
        () => new LocaleCommand(),
        new LocaleCommandMeta()
    );
    commandRegistry.RegisterGlobalCommand(
        "reloadmodule",
        () => new ReloadModuleCommand(),
        new ReloadModuleMeta()
    );

    // Load global banphrase categories on startup
    var banphraseService = scope.ServiceProvider.GetRequiredService<IBanphraseService>();
    await banphraseService.ReloadGlobalCategoriesAsync();

    // Initializing lang
    var localizationService = scope.ServiceProvider.GetRequiredService<ILocalizationService>();
    if (localizationService is LocalizationService impl)
    {
        await impl.InitializeAsync(CancellationToken.None);
    }
}

await host.RunAsync();