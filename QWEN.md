# ButterBror2 - Project Context

## Project Overview

**ButterBror2** is a modular chat bot framework built on **.NET 10.0**. It provides a flexible architecture for creating chat bots that can connect to multiple platforms (Twitch, Discord, Telegram) with support for dynamically loadable chat modules.

### Architecture

The solution follows a **clean architecture** approach with clear separation of concerns:

```
ButterBror2/
├── Core Layers
│   ├── ButterBror.Domain          # Domain entities and core abstractions
│   ├── ButterBror.Core            # Core services (MediatR-based)
│   ├── ButterBror.Application     # Application logic, commands, handlers
│   └── ButterBror.Data            # Data access layer (Redis repositories)
│
├── Infrastructure
│   ├── ButterBror.Infrastructure  # General infrastructure services
│   ├── ButterBror.AI              # AI integration layer
│   ├── ButterBror.Scripting       # Scripting support
│   └── ButterBror.Localization    # Localization resources
│
├── Platform Implementations
│   ├── ButterBror.Platforms       # Platform abstraction layer
│   ├── ButterBror.Platforms.Twitch
│   ├── ButterBror.Platforms.Discord
│   └── ButterBror.Platforms.Telegram
│
├── Chat Modules (Dynamic Plugins)
│   ├── ButterBror.ChatModules.Abstractions  # Module API contracts
│   ├── ButterBror.ChatModules.Loader        # Module loading infrastructure
│   └── ButterBror.ChatModules.Twitch        # Twitch-specific modules
│
├── Applications
│   ├── ButterBror.Host            # Main bot host (CLI application)
│   └── ButterBror.Dashboard       # Web dashboard (Blazor)
│
├── Tooling
│   ├── ButterBror.Tools.Analyzer
│   ├── ButterBror.Tools.Generator
│   ├── ButterBror.Tools.Migrator
│   └── ButterBror.Tools.Updater
│
└── Testing
    ├── ButterBror.UnitTests       # Unit tests (NUnit)
    ├── ButterBror.IntegrationTests
    ├── ButterBror.PlatformTests
    └── ButterBror.E2ETests        # End-to-end tests
```

### Key Technologies

| Technology | Purpose |
|------------|---------|
| .NET 10.0 | Runtime framework |
| MediatR 14.x | CQRS / Command handling |
| Microsoft.Extensions.Hosting | Dependency injection & hosting |
| StackExchange.Redis | Redis data storage |
| Polly | Resilience patterns (retry, circuit breaker) |
| NUnit | Testing framework |
| Blazor | Web dashboard UI |

## Building and Running

### Prerequisites

- **.NET 10.0 SDK** or later
- **Redis** server running (default: `localhost:6379`)
- **Bash** (for packaging scripts)
- **zip** utility (for module packaging)

### Build Commands

```bash
# Build entire solution
dotnet build ButterBror2.slnx

# Build specific project
dotnet build ButterBror.Host/ButterBror.Host.csproj

# Build in Release configuration
dotnet build -c Release
```

### Running the Bot

```bash
# Run the main host
dotnet run --project ButterBror.Host/ButterBror.Host.csproj

# Or after publishing
dotnet ButterBror.Host/bin/Release/net10.0/ButterBror.Host.dll
```

### Running Tests

```bash
# Run all unit tests
dotnet test ButterBror.UnitTests/ButterBror.UnitTests.csproj

# Run integration tests
dotnet test ButterBror.IntegrationTests/ButterBror.IntegrationTests.csproj

# Run all tests in solution
dotnet test
```

### Running the Dashboard

```bash
# Run the Blazor dashboard
dotnet run --project ButterBror.Dashboard/ButterBror.Dashboard.csproj
```

### Packaging Chat Modules

The project includes a script to package the Twitch chat module:

```bash
# Package Twitch module (creates ZIP in user's Chat directory)
./scripts/copy-chat-modules.sh
```

The script:
1. Builds the `ButterBror.ChatModules.Twitch` project in Release mode
2. Copies output files (excluding core ButterBror assemblies)
3. Creates a `module.manifest.json` manifest file
4. Packages everything into a ZIP archive

**Target directories:**
- Linux/macOS: `~/.local/share/SillyApps/ButterBror2/Chat/`
- Windows: `%APPDATA%/SillyApps/ButterBror2/Chat/`

Override with `BB_INSTALL_HOME` environment variable.

## Configuration

### Redis Connection

The bot requires a Redis connection. Configure via connection string:

```bash
# Environment variable
export ConnectionStrings__Redis="localhost:6379"

# Or in appsettings.json (ButterBror.Host/)
{
  "ConnectionStrings": {
    "Redis": "localhost:6379"
  }
}
```

### Environment Variables

| Variable | Description |
|----------|-------------|
| `CI` | Set to `true` to disable colored console output |
| `GITHUB_ACTIONS` | Auto-detected for CI environments |
| `BB_INSTALL_HOME` | Override target home directory for module installation |

## Development Conventions

### Code Style

- **Nullable reference types**: Enabled (`<Nullable>enable</Nullable>`)
- **Implicit usings**: Enabled (`<ImplicitUsings>enable</ImplicitUsings>`)
- **Latest C# language version**: Used across projects

### Testing Practices

- **NUnit** is the primary testing framework
- Test projects follow naming convention: `<ProjectName>.Tests` or `<ProjectName>Tests`
- Tests use the `NUnit.Framework` namespace via global using

### Project Structure Conventions

1. **Domain layer** (`ButterBror.Domain`): Contains entities, value objects, and domain interfaces - no external dependencies
2. **Core layer** (`ButterBror.Core`): Implements core business logic using MediatR pattern
3. **Application layer** (`ButterBror.Application`): Command/query handlers, DTOs, application services
4. **Infrastructure layer**: External service implementations (Redis, HTTP clients, etc.)
5. **Platform layers**: Platform-specific implementations behind common abstractions

### Dependency Injection

Services are registered in `Program.cs` using the standard Microsoft.Extensions.DependencyInjection pattern:

```csharp
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddSingleton<ICommandRegistry, CommandRegistry>();
builder.Services.AddHostedService<BotHostedService>();
```

### Command Pattern

Commands follow the MediatR pattern:
- Commands implement `IRequest<T>` from MediatR
- Handlers implement `IRequestHandler<TCommand, TResult>`
- Commands are registered via `ICommandRegistry` for runtime dispatch

## Key Files

| File | Description |
|------|-------------|
| `ButterBror2.slnx` | Solution file defining project structure |
| `ButterBror.Host/Program.cs` | Application entry point, DI configuration |
| `scripts/copy-chat-modules.sh` | Module packaging script |
| `.gitignore` | Visual Studio / .NET standard ignore rules |

## License

MIT License - see `LICENSE.txt` for full terms.
