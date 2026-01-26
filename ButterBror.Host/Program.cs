// ButterBror.Host/Program.cs
using ButterBror.Application.Commands;
using ButterBror.Application.Services;
using ButterBror.Core.Interfaces;
using ButterBror.Core.Models.Commands;
using ButterBror.Host;
using ButterBror.Infrastructure.Configuration;
using ButterBror.Infrastructure.Data;
using ButterBror.Infrastructure.Services;
using ButterBror.Infrastructure.Storage;
using ButterBror.Platforms.Twitch.Services;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

var builder = Host.CreateApplicationBuilder(args);

// Добавляем логирование
builder.Logging.AddConsole();

// Регистрация сервисов
builder.Services.AddSingleton<AppDataStorageProvider>();
builder.Services.AddSingleton<ConfigService>();

// Redis
var redisConfig = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
    ConnectionMultiplexer.Connect(redisConfig));

// MediatR
builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssembly(typeof(UserInfoCommand).Assembly);
});

// Репозитории
builder.Services.AddScoped<IUserRepository, RedisUserRepository>();

// Сервисы пользователей
builder.Services.AddScoped<IUserService, UserService>();

// Новые сервисы с разделенной ответственностью
builder.Services.AddSingleton<IPlatformModuleManager, PlatformModuleManager>();
builder.Services.AddScoped<ICommandProcessor, CommandProcessor>();
builder.Services.AddSingleton<ICommandDispatcher, CommandDispatcher>();

// Ядро бота
builder.Services.AddSingleton<IBotCore, BotCoreService>();

// Регистрация модулей платформ
builder.Services.AddSingleton<IPlatformModuleRegistry, PlatformModuleRegistry>();
builder.Services.AddSingleton<IPlatformModule, TwitchModule>();

// Twitch конкретные сервисы
builder.Services.AddSingleton<ITwitchClient, TwitchLibClient>();
builder.Services.AddSingleton<ICommandParser, TwitchCommandParser>();

// Конфигурация Twitch
builder.Services.Configure<TwitchConfiguration>(builder.Configuration.GetSection("Twitch"));

// Background сервис для запуска бота
builder.Services.AddHostedService<BotHostedService>();

var host = builder.Build();
await host.RunAsync();