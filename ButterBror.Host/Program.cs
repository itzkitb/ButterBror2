using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using ButterBror.Core;
using ButterBror.Core.Interfaces;
using ButterBror.Core.Modules.Interfaces;
using ButterBror.Core.Storage;
using ButterBror.Dashboard;
using ButterBror.Dashboard.Services;
using ButterBror.Data.Interfaces;
using ButterBror.Data.Repositories;
using ButterBror.Domain;
using ButterBror.Host;
using ButterBror.Host.Logging;
using ButterBror.Infrastructure.Resilience;
using ButterBror.Infrastructure.Services;
using ButterBror.Localization.Services;
using ButterBror.Modules.Loader;

// ><> console setup
Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding  = Encoding.UTF8;

var builder = Host.CreateApplicationBuilder(args);

// ><> configs
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

// log level filters
builder.Logging.AddFilter("Polly", LogLevel.Warning);
builder.Logging.AddFilter("Polly.Core", LogLevel.Warning);
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
builder.Logging.AddFilter("TwitchLib.Api", LogLevel.Warning);

// ><> services

// ^ core & infrastructure
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IBotCoreInfo, BotCoreInfo>();
builder.Services.AddSingleton<IBotCore, BotCore>();
builder.Services.AddSingleton<IConfigurationService, ConfigurationService>();
builder.Services.AddSingleton<IAppDataPathProvider, AppDataStorageProvider>();
builder.Services.AddSingleton<IDynamicServiceProvider>(sp => new DynamicServiceProvider(sp));

// ^ database
var redisConfig = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379,allowAdmin=true,abortConnect=false";
builder.Services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(redisConfig));

// ^ resilience & repos
builder.Services.RegisterResilienceStrategies();
builder.Services.AddScoped<IUserRepository, RedisUserRepository>();
builder.Services.AddScoped<IChatRepository, RedisChatRepository>();
builder.Services.AddScoped<ICommandUsageRepository, RedisCommandUsageRepository>();
builder.Services.AddSingleton<ICustomDataRepository, RedisCustomDataRepository>();
builder.Services.AddScoped<IBanphraseRepository, BanphraseRepository>();
builder.Services.AddScoped<IErrorReportRepository, ErrorReportRepository>();
builder.Services.AddSingleton<IDataRepository, DataRepository>();

// ^ users
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IPermissionManager, PermissionManager>();

// ^ chats
builder.Services.AddScoped<IChatService, ChatService>();

// ^ commands & modules
builder.Services.AddScoped<ICommandProcessor, CommandProcessor>();
builder.Services.AddSingleton<ICommandDispatcher, CommandDispatcher>();
builder.Services.AddSingleton<IPlatformModuleManager, PlatformModuleManager>();
builder.Services.AddSingleton<IChatModuleRegistry, PlatformModuleRegistry>();
builder.Services.AddSingleton<ICommandRegistry, CommandRegistry>();

builder.Services.AddSingleton<IChatModuleLoader, ChatModuleLoader>();
builder.Services.AddSingleton<ICommandModuleLoader, CommandModuleLoader>();

// ^ domain & feature
builder.Services.AddScoped<IFormatterService, FormatterService>();
builder.Services.AddSingleton<IBotStatsService, BotStatsService>();
builder.Services.AddSingleton<IBanphraseService, BanphraseService>();
builder.Services.AddScoped<IErrorTrackingService, ErrorTrackingService>();
builder.Services.AddSingleton<IRestrictionService, RestrictionService>();

// ^ localization
builder.Services.AddSingleton<TranslationFileLoader>();
builder.Services.AddSingleton<LocaleRegistryService>();
builder.Services.AddSingleton<ILocalizationService, LocalizationService>();

// ^ external integrations
builder.Services.AddHttpClient<IPasteBinService, PasteBinService>()
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
    });

// ^ dashboard
builder.Services.Configure<DashboardOptions>(builder.Configuration.GetSection("Dashboard"));
builder.Services.AddSingleton<IDashboardBridge, DashboardBridge>();
builder.Services.AddSingleton<MetricsCollector>();
builder.Services.AddSingleton<AdminCommandExecutor>();
builder.Services.AddSingleton<RedisExplorerService>();
builder.Services.AddSingleton<FileManagerService>();
builder.Services.AddSingleton<IDeviceStatsService, DeviceStatsService>();
builder.Services.AddSingleton<IDashboardService, DashboardServer>();

// ^ hosted
builder.Services.AddHostedService<BotHostedService>();

// ^ json
builder.Services.AddSingleton(new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
});

// ><> build & post-build
var host = builder.Build();

// s0: logger & core info
var logger = host.Services.GetRequiredService<ILogger<Program>>();
var appdataPathProvider = host.Services.GetRequiredService<IAppDataPathProvider>();
var coreInfoService = host.Services.GetRequiredService<IBotCoreInfo>();
logger.LogInformation("- —==≡ butterbror is starting ≡==- —");
coreInfoService.Initialize();
logger.LogInformation("default path: {Path}", appdataPathProvider.GetAppDataPath());

// s1: dashboard logger provider
var bridge = host.Services.GetRequiredService<IDashboardBridge>();
var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
loggerFactory.AddProvider(new DashboardLoggerProvider(bridge));

// ><> hello, world!
await host.RunAsync();
// ><> bye