using ButterBror.Application.Commands;
using ButterBror.Application.Commands.Meta;
using ButterBror.ChatModules.Abstractions;
using ButterBror.ChatModules.Loader;
using ButterBror.Core.Interfaces;
using ButterBror.Data;
using ButterBror.Domain;
using ButterBror.Host;
using ButterBror.Host.Logging;
using ButterBror.Infrastructure.Resilience;
using ButterBror.Infrastructure.Services;
using ButterBror.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

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

// Services
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<ICommandProcessor, CommandProcessor>();
builder.Services.AddSingleton<AppDataStorageProvider>();
builder.Services.AddSingleton<IAppDataPathProvider>(sp => sp.GetRequiredService<AppDataStorageProvider>());
// Use the new unified command dispatcher
builder.Services.AddSingleton<ICommandDispatcher, CommandDispatcher>();
builder.Services.AddSingleton<IPlatformModuleManager, PlatformModuleManager>();
builder.Services.AddSingleton<IChatModuleRegistry, PlatformModuleRegistry>();
builder.Services.AddSingleton<ICommandRegistry, CommandRegistry>();

// Chat Module Loader
builder.Services.AddSingleton<IChatModuleLoader, ChatModuleLoader>();

// Redis
var redisConfig = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
    ConnectionMultiplexer.Connect(redisConfig));

// Repositories
builder.Services.RegisterResilienceStrategies();
builder.Services.AddScoped<IUserRepository, RedisUserRepository>();
builder.Services.AddScoped<ICommandUsageRepository, RedisCommandUsageRepository>();

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

var host = builder.Build();

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
    
    // Load global banphrase categories on startup
    var banphraseService = scope.ServiceProvider.GetRequiredService<IBanphraseService>();
    await banphraseService.ReloadGlobalCategoriesAsync();
}

await host.RunAsync();