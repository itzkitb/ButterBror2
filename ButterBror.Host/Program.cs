using ButterBror.Application.Commands;
using ButterBror.Application.Services;
using ButterBror.Core.Interfaces;
using ButterBror.Host;
using ButterBror.Host.Logging;
using ButterBror.Infrastructure.Configuration;
using ButterBror.Infrastructure.Data;
using ButterBror.Infrastructure.Resilience;
using ButterBror.Infrastructure.Services;
using ButterBror.Infrastructure.Storage;
using ButterBror.Platforms.Twitch.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

var builder = Host.CreateApplicationBuilder(args);

// Logging
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
builder.Services.AddSingleton<ICommandDispatcher, CommandDispatcher>();
builder.Services.AddSingleton<IPlatformModuleManager, PlatformModuleManager>();
builder.Services.AddSingleton<IPlatformModuleRegistry, PlatformModuleRegistry>();

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

// Core
builder.Services.AddSingleton<IBotCore, BotCoreService>();

// Twitch
// TODO: Need to delete this
builder.Services.AddSingleton<ITwitchClient, TwitchLibClient>();
builder.Services.AddSingleton<ICommandParser, TwitchCommandParser>();
builder.Services.Configure<TwitchConfiguration>(builder.Configuration.GetSection("Twitch"));

// Background service
builder.Services.AddHostedService<BotHostedService>();

var host = builder.Build();
await host.RunAsync();