using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using ButterBror.Application;
using ButterBror.Application.Commands;
using ButterBror.Application.Commands.Meta;
using ButterBror.Core;
using ButterBror.Core.Interfaces;
using ButterBror.Core.Modules.Interfaces;
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

// ><> Console setup
Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding  = Encoding.UTF8;

var builder = Host.CreateApplicationBuilder(args);

// ><> Configs
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

// Log Level Filters
builder.Logging.AddFilter("Polly", LogLevel.Warning);
builder.Logging.AddFilter("Polly.Core", LogLevel.Warning);
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);

// ><> Services

// ^ Core & Infrastructure
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IBotCoreInfo, BotCoreInfo>();
builder.Services.AddSingleton<IBotCore, BotCoreService>();
builder.Services.AddSingleton<IConfigurationService, ConfigurationService>();
builder.Services.AddSingleton<AppDataStorageProvider>();
builder.Services.AddSingleton<IAppDataPathProvider>(sp => sp.GetRequiredService<AppDataStorageProvider>());
builder.Services.AddSingleton<IDynamicServiceProvider>(sp => new DynamicServiceProvider(sp));

// ^ Database
var redisConfig = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379,allowAdmin=true,abortConnect=false";
builder.Services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(redisConfig));

// ^ Resilience & Repos
builder.Services.RegisterResilienceStrategies();
builder.Services.AddScoped<IUserRepository, RedisUserRepository>();
builder.Services.AddScoped<ICommandUsageRepository, RedisCommandUsageRepository>();
builder.Services.AddSingleton<ICustomDataRepository, RedisCustomDataRepository>();
builder.Services.AddScoped<IBanphraseRepository, BanphraseRepository>();
builder.Services.AddScoped<IErrorReportRepository, ErrorReportRepository>();

// ^ Users
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IPermissionManager, PermissionManager>();

// ^ Commands & Modules
builder.Services.AddScoped<ICommandProcessor, CommandProcessor>();
builder.Services.AddSingleton<ICommandDispatcher, CommandDispatcher>();
builder.Services.AddSingleton<IPlatformModuleManager, PlatformModuleManager>();
builder.Services.AddSingleton<IChatModuleRegistry, PlatformModuleRegistry>();
builder.Services.AddSingleton<ICommandRegistry, CommandRegistry>();

builder.Services.AddSingleton<IChatModuleLoader, ChatModuleLoader>();
builder.Services.AddSingleton<ICommandModuleLoader, CommandModuleLoader>();

// ^ Domain & Feature
builder.Services.AddScoped<IFormatterService, FormatterService>();
builder.Services.AddSingleton<IBotStatsService, BotStatsService>();
builder.Services.AddSingleton<IBanphraseService, BanphraseService>();
builder.Services.AddScoped<IErrorTrackingService, ErrorTrackingService>();
builder.Services.AddSingleton<IRestrictionService, RestrictionService>();

// ^ Localization
builder.Services.AddSingleton<TranslationFileLoader>();
builder.Services.AddSingleton<LocaleRegistryService>();
builder.Services.AddSingleton<ILocalizationService, LocalizationService>();

// ^ External Integrations
builder.Services.AddHttpClient<IPasteBinService, PasteBinService>()
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
    });

// ^ Dashboard
builder.Services.Configure<DashboardOptions>(builder.Configuration.GetSection("Dashboard"));
builder.Services.AddSingleton<IDashboardBridge, DashboardBridge>();
builder.Services.AddSingleton<MetricsCollector>();
builder.Services.AddSingleton<AdminCommandExecutor>();
builder.Services.AddSingleton<RedisExplorerService>();
builder.Services.AddSingleton<FileManagerService>();
builder.Services.AddSingleton<IDeviceStatsService, DeviceStatsService>();

// ^ Hosted
builder.Services.AddHostedService<DashboardServer>();
builder.Services.AddHostedService<DeviceStatsHostedService>();
builder.Services.AddHostedService<BotHostedService>();

// ><> Build & Post-build
var host = builder.Build();

// S0: Logger & Core Info
var logger = host.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("· - —==≡ ButterBror is starting ≡==- — ·");

var coreInfoService = host.Services.GetRequiredService<IBotCoreInfo>();
coreInfoService.Initialize();

// S1: Dashboard Logger Provider
var bridge = host.Services.GetRequiredService<IDashboardBridge>();
var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
loggerFactory.AddProvider(new DashboardLoggerProvider(bridge));

// S2: Stats Init & Graceful Shutdown Setup
var statsService = host.Services.GetRequiredService<IBotStatsService>();
await statsService.InitializeAsync(CancellationToken.None);

var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStopping.Register(async void () =>
{
    await statsService.FlushAsync(CancellationToken.None);
});

// S3: Admin User
using (var scope = host.Services.CreateScope())
{
    try
    {
        var userService = scope.ServiceProvider.GetRequiredService<IUserService>();
        var permManager = scope.ServiceProvider.GetRequiredService<IPermissionManager>();

        var adminUser = await userService.GetOrCreateUserAsync(
            platformId: "dashboard-admin",
            platform: "dashboard",
            displayName: "Dashboard Admin"
        );

        await permManager.AddPermissionAsync(adminUser.UnifiedId, "su:*");

        logger.LogInformation(
            "Initialized dashboard admin. unified_uid='{UserId}'",
            adminUser.UnifiedId);
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Failed to initialize dashboard admin user (Redis may not be ready yet)");
    }
}

// S4: Global Commands & Localization Setup
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
    commandRegistry.RegisterGlobalCommand(
        "block",
        () => new BlockCommand(),
        new BlockCommandMeta()
    );

    // Load global banphrase categories
    var banphraseService = scope.ServiceProvider.GetRequiredService<IBanphraseService>();
    await banphraseService.ReloadGlobalCategoriesAsync();

    // Init Localization
    var localizationService = scope.ServiceProvider.GetRequiredService<ILocalizationService>();
    if (localizationService is LocalizationService impl)
    {
        await impl.InitializeAsync(CancellationToken.None);
    }
    localizationService.RegisterModuleTranslations("butterbror:system", Localization.DefaultTranslations);
}

// ><> Hello, world!
await host.RunAsync();
// ><> Bye.