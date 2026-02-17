using ButterBror.Application.Commands;
using ButterBror.Application.Commands.Meta;
using ButterBror.Core.Contracts;
using ButterBror.Core.Enums;
using ButterBror.Core.Interfaces;
using ButterBror.Core.Registration;
using ButterBror.Data;
using ButterBror.Domain;
using ButterBror.Host;
using ButterBror.Host.Logging;
using ButterBror.Infrastructure;
using ButterBror.Infrastructure.Configuration;
using ButterBror.Infrastructure.Resilience;
using ButterBror.Infrastructure.Services;
using ButterBror.Infrastructure.Storage;
using ButterBror.Platforms.Twitch.Models;
using ButterBror.Platforms.Twitch.Services;
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

// Services
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<ICommandProcessor, CommandProcessor>();
builder.Services.AddSingleton<ConfigService>();
builder.Services.AddSingleton<AppDataStorageProvider>();
builder.Services.AddSingleton<IPlatformModule, TwitchModule>();
// Use the new unified command dispatcher adapter
builder.Services.AddSingleton<IUnifiedCommandDispatcher, UnifiedCommandDispatcher>();
builder.Services.AddSingleton<ICommandDispatcher, UnifiedCommandDispatcherAdapter>();
builder.Services.AddSingleton<IPlatformModuleManager, PlatformModuleManager>();
builder.Services.AddSingleton<IPlatformModuleRegistry, PlatformModuleRegistry>();
builder.Services.AddSingleton<ICommandRegistry, CommandRegistry>();
builder.Services.AddSingleton<IUnifiedCommandRegistry, UnifiedCommandRegistry>();

// Redis
var redisConfig = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
    ConnectionMultiplexer.Connect(redisConfig));

// MediatR
builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssembly(typeof(UserInfoCommand).Assembly);
});

// Repositories
builder.Services.RegisterResilienceStrategies();
builder.Services.AddScoped<IUserRepository, RedisUserRepository>();
builder.Services.AddScoped<ICommandUsageRepository, RedisCommandUsageRepository>();

// Permission Manager
builder.Services.AddPermissionManager();

// Core
builder.Services.AddSingleton<IBotCore, BotCoreService>();

// Register commands
builder.Services.AddCommands();

// Twitch
// TODO: Need to delete this
builder.Services.AddSingleton<ITwitchClient, TwitchLibClient>();
builder.Services.Configure<TwitchConfiguration>(builder.Configuration.GetSection("Twitch"));

// Background service
builder.Services.AddHostedService<BotHostedService>();

var host = builder.Build();

// Register all commands after services are built
using (var scope = host.Services.CreateScope())
{
    var commandRegistry = scope.ServiceProvider.GetRequiredService<IUnifiedCommandRegistry>();

    // Register global command: !userinfo
    commandRegistry.RegisterGlobalCommand(
        "userinfo",
        () => new UnifiedUserInfoCommand(),
        new UserInfoMeta()
    );
}

await host.RunAsync();