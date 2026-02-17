# ButterBror2 - Project Context

## Project Overview

**ButterBror2** is a modular, multi-platform chat bot framework built with .NET 10.0. It provides a unified architecture for creating bots that can operate across multiple platforms (Twitch, Discord, Telegram) with shared core functionality.

### Architecture

The project follows a **clean architecture** approach with clear separation of concerns:

```
ButterBror2/
├── ButterBror.Domain/          # Core domain entities, no dependencies
├── ButterBror.Core/            # Core abstractions, interfaces, models
├── ButterBror.Application/     # Application logic, commands, handlers
├── ButterBror.Infrastructure/  # External services, resilience, storage
├── ButterBror.Data/            # Data access layer, repositories
├── ButterBror.Host/            # Application entry point, DI composition
├── ButterBror.Platforms/       # Platform abstraction layer
│   ├── ButterBror.Platforms.Twitch/
│   ├── ButterBror.Platforms.Discord/
│   └── ButterBror.Platforms.Telegram/
├── ButterBror.AI/              # AI integration (placeholder)
├── ButterBror.Scripting/       # Scripting support (placeholder)
├── ButterBror.Dashboard/       # Dashboard UI (placeholder)
├── ButterBror.Localization/    # Localization resources
├── Testing/
│   ├── ButterBror.UnitTests/
│   ├── ButterBror.IntegrationTests/
│   ├── ButterBror.E2ETests/
│   └── ButterBror.PlatformTests/
└── Tools/
    ├── ButterBror.Tools.Analyzer/
    ├── ButterBror.Tools.Generator/
    ├── ButterBror.Tools.Migrator/
    └── ButterBror.Tools.Updater/
```

### Key Technologies

- **Runtime**: .NET 10.0
- **Dependency Injection**: Microsoft.Extensions.DependencyInjection
- **Messaging**: MediatR (v14.0.0)
- **Storage**: StackExchange.Redis (Redis for persistence)
- **Resilience**: Polly.Core + Microsoft.Extensions.Resilience
- **Platforms**: TwitchLib (Twitch integration)
- **Testing**: NUnit, NUnit3TestAdapter, Coverlet

### Core Concepts

1. **Command Pattern**: All bot functionality is implemented as commands
   - Commands implement `ICommand` interface
   - Metadata-driven registration with `ICommandMetadata`
   - Support for aliases, cooldowns, and platform-specific compatibility

2. **Unified Command System**: 
   - `UnifiedCommandBase` provides cross-platform command execution
   - `ICommandDispatcher` routes commands to appropriate handlers
   - `ICommandRegistry` manages global command registration

3. **Platform Abstraction**:
   - `IPlatformModule` defines platform-specific implementations
   - `PlatformModuleManager` and `PlatformModuleRegistry` manage platform lifecycle
   - Platform compatibility defined in command metadata

4. **User System**:
   - `IUserService` / `UserService` for user management
   - `IUserRepository` with Redis implementation
   - Unified user identity across platforms

## Building and Running

### Prerequisites

- .NET 10.0 SDK
- Redis server (for data persistence)

### Build Commands

```bash
# Build entire solution
dotnet build ButterBror2.slnx

# Build specific project
dotnet build ButterBror.Host/ButterBror.Host.csproj

# Build without restoring packages
dotnet build --no-restore
```

### Run Commands

```bash
# Run the bot host
dotnet run --project ButterBror.Host/ButterBror.Host.csproj

# Run with configuration
dotnet run --project ButterBror.Host/ButterBror.Host.csproj -- --config appsettings.json
```

### Test Commands

```bash
# Run all unit tests
dotnet test ButterBror.UnitTests/ButterBror.UnitTests.csproj

# Run integration tests
dotnet test ButterBror.IntegrationTests/ButterBror.IntegrationTests.csproj

# Run all tests with coverage
dotnet test --collect:"XPlat Code Coverage"
```

### Configuration

The application uses `appsettings.json` in `ButterBror.Host/` for configuration:

- **Redis Connection**: Configured via connection string (default: `localhost:6379`)
- **Twitch**: Configured via `Twitch` section (credentials, channels)

## Development Conventions

### Code Style

- **Nullable Reference Types**: Enabled (`<Nullable>enable</Nullable>`)
- **Implicit Usings**: Enabled (`<ImplicitUsings>enable</ImplicitUsings>`)
- **Latest Language Version**: Used where applicable (`<LangVersion>latest</LangVersion>`)

### Project Structure Conventions

1. **Dependencies Flow**: Domain → Core → Application → Infrastructure → Host
   - Higher layers reference lower layers only
   - `ButterBror.Domain` has no project dependencies
   - `ButterBror.Host` composes all layers via DI

2. **Interfaces in Core**: All abstractions defined in `ButterBror.Core/Interfaces/`
   - Implementations in respective layers (Infrastructure, Data, Platforms)

3. **Commands**: 
   - Command definitions in `Application/Commands/`
   - Command metadata in `Application/Commands/Meta/`
   - Platform-specific command handlers in respective platform projects

### Testing Practices

- **Unit Tests**: NUnit framework with `[Test]` attributes
- **Test Project Structure**: Separate projects for unit, integration, E2E, and platform tests
- **Coverage**: Coverlet collector for code coverage reporting

### Key Patterns

1. **Dependency Injection**: All services registered in `Program.cs`
2. **Hosted Services**: Background work via `IHostedService` (e.g., `BotHostedService`)
3. **Repository Pattern**: Data access abstracted via repositories (e.g., `IUserRepository`)
4. **Options Pattern**: Configuration via `IOptions<T>` (e.g., `TwitchConfiguration`)

### Logging

Custom console formatter (`CustomConsoleFormatter`) configured in `Program.cs`:
- Supports color output (disabled in CI/GitHub Actions)
- Configurable via `CustomConsoleFormatterOptions`

## Common Tasks

### Adding a New Command

1. Create command class in `ButterBror.Application/Commands/`
2. Implement `IMetadataCommand` or inherit from `UnifiedCommandBase`
3. Add metadata class with command configuration
4. Register in `Program.cs` using `ICommandRegistry`

### Adding a New Platform

1. Create new platform project under `ButterBror.Platforms.*`
2. Implement `IPlatformModule` interface
3. Register module in host composition root
4. Ensure platform compatibility in command metadata

### Adding a New Repository

1. Define interface in `ButterBror.Core/Interfaces/` or `ButterBror.Data/`
2. Implement in `ButterBror.Data/` (or `ButterBror.Infrastructure/`)
3. Register in DI container in `Program.cs`

## Known TODOs

- Twitch client abstraction marked with `// TODO: Need to delete this` in `Program.cs`
- Several placeholder projects: `AI`, `Scripting`, `Dashboard`
