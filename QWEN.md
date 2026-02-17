# ButterBror2 - Многофункциональный чат-бот для стриминговых платформ

## Обзор проекта

**ButterBror2** — это современный **.NET 10.0** чат-бот для Twitch, Discord и Telegram, построенный на модульной архитектуре с использованием паттернов **CQRS** (через MediatR) и **внедрения зависимостей**.

### Ключевые особенности

- **Модульная архитектура** — платформы изолированы в отдельных модулях
- **Унифицированная система команд** — единый интерфейс для всех платформ
- **CQRS с MediatR** — разделение операций чтения и записи
- **Redis для хранения** — быстрое хранение пользователей и статистики
- **Resilience-политики** — устойчивость к сбоям через Polly
- **Система прав доступа** — гибкое управление разрешениями
- **Cooldown команд** — ограничение частоты выполнения

## Структура решения

```
ButterBror2/
├── ButterBror.Core/              # Ядро: интерфейсы, модели, сервисы
│   ├── Abstractions/             # Базовые абстракции (ICommandMetadata и др.)
│   ├── Contracts/                # Контракты (ICommandMetadata)
│   ├── Enums/                    # Перечисления (PlatformCompatibilityType)
│   ├── Interfaces/               # Интерфейсы (IPlatformModule, IBotCore)
│   ├── Models/                   # Модели данных
│   ├── Registration/             # Регистрация сервисов
│   ├── Services/                 # Сервисы ядра (CommandRegistry, UnifiedCommandDispatcher)
│   └── Wrappers/                 # Обёртки
│
├── ButterBror.Domain/            # Доменные сущности
│   ├── Entities/                 # Сущности (UserProfile)
│   └── Repositories/             # Интерфейсы репозиториев
│
├── ButterBror.Application/       # Бизнес-логика
│   ├── Commands/                 # Команды (UserInfoCommand, UnifiedUserInfoCommand)
│   ├── Services/                 # Сервисы (BotCoreService, CommandProcessor, UserService)
│   └── Interfaces/               # Интерфейсы прикладного уровня
│
├── ButterBror.Data/              # Реализации репозиториев (Redis)
│   ├── RedisUserRepository.cs
│   └── RedisCommandUsageRepository.cs
│
├── ButterBror.Infrastructure/    # Инфраструктурные компоненты
│   ├── Configuration/            # Конфигурация
│   ├── Resilience/               # Resilience-стратегии Polly
│   ├── Services/                 # Инфраструктурные сервисы
│   └── Storage/                  # Хранилища данных
│
├── ButterBror.Host/              # Точка входа и хостинг
│   ├── Logging/                  # Кастомный логгер (CustomConsoleFormatter)
│   ├── Program.cs                # Точка входа
│   └── BotHostedService.cs       # Фоновый сервис
│
├── ButterBror.Platforms.*        # Модули платформ
│   ├── ButterBror.Platforms.Twitch/      # Twitch интеграция (TwitchLib)
│   ├── ButterBror.Platforms.Discord/     # Discord интеграция
│   ├── ButterBror.Platforms.Telegram/    # Telegram интеграция
│   └── ButterBror.Platforms/             # Общие интерфейсы платформ
│
├── ButterBror.Tools.*            # Инструменты разработки
│   ├── Analyzer/                 # Статический анализ
│   ├── Generator/                # Генерация кода
│   ├── Migrator/                 # Миграции данных
│   └── Updater/                  # Обновления
│
├── ButterBror.AI/                # AI компоненты (в разработке)
├── ButterBror.Scripting/         # Скриптовый движок (в разработке)
├── ButterBror.Dashboard/         # Веб-дашборд (в разработке)
├── ButterBror.Localization/      # Локализация
│
└── Testing/                      # Тестовые проекты
    ├── ButterBror.UnitTests/           # Модульные тесты (NUnit)
    ├── ButterBror.IntegrationTests/    # Интеграционные тесты
    ├── ButterBror.E2ETests/            # E2E тесты
    └── ButterBror.PlatformTests/       # Тесты платформ
```

## Сборка и запуск

### Предварительные требования

- **.NET 10.0 SDK** или новее
- **Redis** сервер (локально или удалённо)

### Сборка проекта

```bash
dotnet build ButterBror2.slnx
```

### Запуск проекта

```bash
dotnet run --project ButterBror.Host/ButterBror.Host.csproj
```

### Запуск тестов

```bash
# Все тесты
dotnet test

# Конкретный проект тестов
dotnet test ButterBror.UnitTests/ButterBror.UnitTests.csproj
dotnet test ButterBror.IntegrationTests/ButterBror.IntegrationTests.csproj
```

## Конфигурация

Файл конфигурации: `ButterBror.Host/appsettings.json`

```json
{
  "Twitch": {
    "BotUsername": "devbror",
    "OauthToken": "oauth:your_token_here",
    "Channel": "devbror",
    "ClientId": "your_client_id_here",
    "IsEnabled": true
  },
  "ConnectionStrings": {
    "Redis": "localhost:6379"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning",
      "TwitchLib": "Warning",
      "ButterBror": "Debug"
    }
  }
}
```

### Получение Twitch credentials

1. Перейдите на [Twitch Developer Console](https://dev.twitch.tv/console)
2. Создайте новое приложение
3. Получите **Client ID**
4. Сгенерируйте **OAuth Token** через [twitchapps.com/tmi](https://twitchapps.com/tmi/)

## Архитектура

### Доменная модель

**UserProfile** — основная сущность пользователя:

```csharp
public class UserProfile
{
    public Guid UnifiedUserId { get; set; }           // Уникальный ID во всех платформах
    public Dictionary<string, string> PlatformIds { get; } // platform -> platformId
    public string DisplayName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastActive { get; set; }
    public Dictionary<string, object> Statistics { get; } // Статистика команд
    public List<string> Permissions { get; }          // Права доступа
}
```

### Система команд

#### Иерархия команд

```
IUnifiedCommand (базовый интерфейс)
    ↓
IMetadataCommand (команды с метаданными)
    ↓
UserInfoCommand : IMetadataCommand, IRequest<CommandResult>
```

#### ICommandMetadata

```csharp
public interface ICommandMetadata
{
    string Name { get; }                              // Имя команды (например, "userinfo")
    List<string> Aliases { get; }                     // Алиасы (например, ["ui", "whois"])
    int CooldownSeconds { get; }                      // Задержка между вызовами
    List<string> RequiredPermissions { get; }         // Требуемые права
    string ArgumentsHelpText { get; }                 // Подсказка по аргументам
    string Id { get; }                                // Уникальный ID ("author:command")
    PlatformCompatibilityType PlatformCompatibilityType { get; }
    List<string> PlatformCompatibilityList { get; }   // Список платформ
}
```

#### Обработка команд (CQRS)

1. **Command** — запись команды (например, `UserInfoCommand`)
2. **Handler** — обработчик (например, `UserInfoCommandHandler : IRequestHandler<...>`)
3. **MediatR** — медиатор для dispatch команд

### Поток обработки команды

```
ICommandContext → CommandProcessor → Validation → ICommandDispatcher → MediatR → Handler → CommandResult
                      ↓
              UserService (статистика)
```

### Платформенная архитектура

**IPlatformModule** — интерфейс платформы:

```csharp
public interface IPlatformModule
{
    string PlatformName { get; }
    Task InitializeAsync(IBotCore core);
    Task HandleIncomingMessageAsync(IMessage message);
    Task ShutdownAsync();
}
```

**PlatformModuleManager** — управляет жизненным циклом всех платформ.

## Разработка

### Добавление новой команды

1. **Создайте класс команды** в `ButterBror.Application/Commands/`:

```csharp
using ButterBror.Core.Abstractions;
using ButterBror.Core.Enums;
using ButterBror.Core.Contracts;
using MediatR;

public record MyCommand(string Argument) : IMetadataCommand
{
    public ICommandMetadata GetMetadata() => new MyCommandMetadata();

    private class MyCommandMetadata : ICommandMetadata
    {
        public string Name => "mycommand";
        public List<string> Aliases => new() { "mc", "mycmd" };
        public int CooldownSeconds => 5;
        public List<string> RequiredPermissions => new();
        public string ArgumentsHelpText => "<argument>";
        public string Id => "sillyapps:mycommand";
        public PlatformCompatibilityType PlatformCompatibilityType => PlatformCompatibilityType.Whitelist;
        public List<string> PlatformCompatibilityList => new() { "sillyapps:twitch", "sillyapps:discord" };
    }
}

public class MyCommandHandler : IRequestHandler<MyCommand, CommandResult>
{
    private readonly IUserRepository _userRepository;
    private readonly ILogger<MyCommandHandler> _logger;

    public MyCommandHandler(IUserRepository userRepository, ILogger<MyCommandHandler> logger)
    {
        _userRepository = userRepository;
        _logger = logger;
    }

    public async Task<CommandResult> Handle(MyCommand command, CancellationToken cancellationToken)
    {
        // Логика команды
        return CommandResult.Successfully("Result message");
    }
}
```

2. **Зарегистрируйте MediatR handler** (автоматически через `AddMediatR` в Program.cs)

3. **Опционально: зарегистрируйте в реестре** (если нужно):

```csharp
// В Program.cs после builder.Build()
using (var scope = host.Services.CreateScope())
{
    var registry = scope.ServiceProvider.GetRequiredService<IUnifiedCommandRegistry>();
    registry.RegisterGlobalCommand("mycommand", () => new MyCommand(), new MyCommandMetadata());
}
```

### Добавление новой платформы

1. **Создайте новый проект** `ButterBror.Platforms.YourPlatform/`

2. **Реализуйте IPlatformModule**:

```csharp
public class YourPlatformModule : IPlatformModule
{
    public string PlatformName => "sillyapps:yourplatform";

    public async Task InitializeAsync(IBotCore core, CancellationToken cancellationToken)
    {
        // Инициализация клиента платформы
    }

    public async Task HandleIncomingMessageAsync(IMessage message)
    {
        // Обработка входящих сообщений
        var context = new CommandContext(...);
        await core.ProcessCommandAsync(context);
    }

    public async Task ShutdownAsync()
    {
        // Очистка ресурсов
    }
}
```

3. **Зарегистрируйте в DI** (в Program.cs):

```csharp
builder.Services.AddSingleton<IPlatformModule, YourPlatformModule>();
```

### Создание репозитория

1. **Определите интерфейс** в `ButterBror.Domain/`:

```csharp
public interface IYourRepository
{
    Task<YourEntity?> GetByIdAsync(Guid id);
    Task<YourEntity> CreateOrUpdateAsync(YourEntity entity);
}
```

2. **Реализуйте в Data** с использованием Redis:

```csharp
public class RedisYourRepository : IYourRepository
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisYourRepository> _logger;

    public RedisYourRepository(IConnectionMultiplexer redis, ILogger<RedisYourRepository> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task<YourEntity?> GetByIdAsync(Guid id)
    {
        var db = _redis.GetDatabase();
        var data = await db.StringGetAsync($"yourentity:{id}");
        return data.IsNullOrEmpty ? null : JsonSerializer.Deserialize<YourEntity>(data!);
    }

    public async Task<YourEntity> CreateOrUpdateAsync(YourEntity entity)
    {
        var db = _redis.GetDatabase();
        await db.StringSetAsync($"yourentity:{entity.Id}", JsonSerializer.Serialize(entity));
        return entity;
    }
}
```

3. **Зарегистрируйте в DI**:

```csharp
builder.Services.AddScoped<IYourRepository, RedisYourRepository>();
```

## Соглашения о разработке

### Стиль кода

- **C# 12+** с включёнными implicit using и nullable reference types
- **Файлово-ориентированная организация** — один класс на файл
- **Именование**: PascalCase для классов/методов, camelCase для локальных переменных
- **Асинхронность**: суффикс `Async` для асинхронных методов

### Тестирование

- **NUnit** для модульных тестов
- **Mock-объекты** для изоляции зависимостей
- **Покрытие**: критичная бизнес-логика должна быть покрыта тестами

### Логирование

- **Microsoft.Extensions.Logging** через `ILogger<T>`
- **Кастомный форматтер** `CustomConsoleFormatter` с цветным выводом
- **Уровни**: Debug для отладки, Information для обычных событий, Error для ошибок

### Зависимости

| Пакет | Назначение |
|-------|------------|
| MediatR | CQRS, медиатор команд |
| StackExchange.Redis | Redis клиент |
| Polly.Core | Resilience-политики |
| TwitchLib.* | Twitch интеграция |
| Microsoft.Extensions.* | DI, Hosting, Logging, Configuration |

## Полезные команды

```bash
# Сборка решения
dotnet build ButterBror2.slnx

# Запуск с отладкой
dotnet run --project ButterBror.Host/ButterBror.Host.csproj --launch-profile Development

# Проверка кода (если настроен анализатор)
dotnet build /warnaserror

# Очистка и пересборка
dotnet clean && dotnet build

# Публикация
dotnet publish ButterBror.Host/ButterBror.Host.csproj -c Release -o ./publish
```

## Troubleshooting

### Redis не подключается

Проверьте, что Redis запущен:
```bash
redis-cli ping  # Должен вернуть PONG
```

Или измените connection string в `appsettings.json`.

### TwitchLib ошибки авторизации

- Убедитесь, что OAuth токен начинается с `oauth:`
- Проверьте, что бот добавлен в канал как модератор
- Убедитесь, что Client ID корректен

### Команды не выполняются

- Проверьте регистрацию команд в `Program.cs`
- Убедитесь, что платформа совместима с командой (PlatformCompatibilityList)
- Проверьте права доступа пользователя

## Лицензия

MIT License — см. `LICENSE.txt`
