> [!WARNING]
> The project is under active development. Many features will change as development progresses

# 🥪 ButterBror2

![GitHub contributors](https://img.shields.io/github/contributors/itzkitb/butterbror2)
![GitHub last commit](https://img.shields.io/github/last-commit/itzkitb/butterbror2)
![Bot status](https://img.shields.io/website?url=https%3A%2F%2Fbot.tupid.lol%2Fhealth&label=twitch.tv%2Fbutterbror)
![GitHub top language](https://img.shields.io/github/languages/top/itzkitb/butterbror2)
![CodeQL](https://github.com/itzkitb/ButterBror2/actions/workflows/github-code-scanning/codeql/badge.svg)
![GitHub Repo stars](https://img.shields.io/github/stars/itzkitb/butterbror2)


ButterBror2 is a modular, multi-platform chatbot framework built on .NET 10. It is designed around a plugin architecture - chat integrations (Twitch, Discord, Telegram) and commands are distributed as self-contained `.pag` packages and loaded at runtime without recompiling the host application

---

## ✅ Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or later
- Python 3.10+ (only required to build `.pag` module packages with `scripts/module-compilator.py`)
- Git
- Windows, Linux, or macOS

---

## 🚀 Installation

Currently, the recommended way to run ButterBror2 is directly from source

**1. Clone the repository**

```bash
git clone https://github.com/itzkitb/butterbror2.git
cd butterbror2
```

**2. Run the host**

```bash
dotnet run --project ButterBror.Host/ButterBror.Host.csproj
```

Or, if you prefer to build a binary first:

```bash
dotnet build ButterBror.Host/ButterBror.Host.csproj -c Release
```

The compiled output will be placed in `ButterBror.Host/bin/Release/net10.0/`

---

## 🎮 Usage

Once the host is running, it will scan the platform-specific AppData directory for installed `.pag` modules:

| OS | Path |
|----|------|
| Windows | `%APPDATA%\SillyApps\ButterBror2\` |
| Linux / macOS | `~/.local/share/SillyApps/ButterBror2\` |

Chat modules are loaded from the `Chat/` subdirectory and command modules from `Command/`. Any `.pag` file placed in the correct folder will be picked up automatically on the next startup or reload

---

## 🗂 Project Structure

The solution is organized into the following groups:

**Core / Host**

- `ButterBror.Host` - application entry point; wires up DI, loads modules
- `ButterBror.Core` - interfaces and abstractions consumed across all layers
- `ButterBror.Domain` - domain entities
- `ButterBror.Application` - built-in commands
- `ButterBror.Infrastructure` - service implementations, repositories, storage
- `ButterBror.Data` - data access layer
- `ButterBror.Localization` - translation file loading and locale resolution
- `ButterBror.AI` - chatgpt to answer your questions in chat (WIP)
- `ButterBror.Scripting` - scripting support (WIP)
- `ButterBror.Dashboard` - dashboard bridge

**SDK (for authors)**

- `ButterBror.ChatModule` - `IChatModule` interface and chat module loader
- `ButterBror.CommandModule` - `ICommandModule`, `ICommand`, `CommandBase`, metadata and context contracts

**Built-in Chat Modules**

- `ButterBror.ChatModules.Twitch`
- `ButterBror.ChatModules.Discord`
- `ButterBror.ChatModules.Telegram`
- `ButterBror.ChatModules.Loader` - runtime loader for `.pag` chat modules

**Tools**

- `ButterBror.Tools.Analyzer`
- `ButterBror.Tools.Generator`
- `ButterBror.Tools.Migrator`
- `ButterBror.Tools.Updater`

**Tests**

- `ButterBror.UnitTests`, `ButterBror.IntegrationTests`, `ButterBror.PlatformTests`, `ButterBror.E2ETests`

---

## 🧩 Creating and Installing Modules

Modules are packaged as `.pag` files - ZIP archives with a `.pag` extension that contain compiled DLL assemblies and a `module.manifest.json` descriptor. The build script `scripts/module-compilator.py` handles the full build-and-package pipeline

### Setting Up the SDK Reference

Before writing a module, you need to make the ButterBror2 SDK projects available to your module's `.csproj`. The recommended approach is to add the main repository as a **Git submodule** inside a `libs/` folder in your module's repository. This way you always have access to the correct SDK source without manually copying files

**1. Add ButterBror2 as a submodule**

```bash
git submodule add https://github.com/itzkitb/butterbror2.git libs/butterbror2
git submodule update --init --recursive
```

Your module project layout will look like this:

```
MyModule/
├── libs/
│   └── butterbror2/ - ButterBror2 repository (submodule)
│       ├── ButterBror.ChatModule/
│       ├── ButterBror.CommandModule/
│       └── ...
├── MyModule/
│   └── MyModule.csproj
└── MyModule.sln
```

**2. Update the submodule** at any time to pull in the latest SDK changes:

```bash
git submodule update --remote libs/butterbror2
```

If you're using **Visual Studio Code** or other IDE, it will look like this:

<img width="465" height="251" alt="image" src="https://github.com/user-attachments/assets/7769d161-b25e-4235-984b-5027df758d04" />

**3. Reference the SDK project** from your `.csproj` using the relative path into `libs/`:

```xml
<!-- For a chat module -->
<ItemGroup>
  <ProjectReference Include="../libs/butterbror2/ButterBror.ChatModule/ButterBror.ChatModule.csproj" />
</ItemGroup>

<!-- For a command module -->
<ItemGroup>
  <ProjectReference Include="../libs/butterbror2/ButterBror.CommandModule/ButterBror.CommandModule.csproj" />
</ItemGroup>
```

> When running `scripts/module-compilator.py`, all core ButterBror assemblies are automatically excluded from the `.pag` file, since they will already be in context when the module is loaded

---

### Chat Modules

A chat module integrates ButterBror2 with a messaging platform (e.g. Twitch, Discord). To create one:

**1. Create a new .NET class library project** that references `ButterBror.ChatModule`:

```xml
<ItemGroup>
  <ProjectReference Include="../ButterBror.ChatModule/ButterBror.ChatModule.csproj" />
</ItemGroup>
```

**2. Implement the `IChatModule` interface:**

```csharp
using ButterBror.ChatModule;
using ButterBror.Core.Interfaces;
using ButterBror.CommandModule.Commands;

public class MyPlatformModule : IChatModule
{
    public string PlatformName => "myauthor:myplatform";

    public IReadOnlyList<ModuleCommandExport> ExportedCommands => Array.Empty<ModuleCommandExport>();

    // Optional: provide default translations bundled with the module
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> DefaultTranslations =>
        new Dictionary<string, IReadOnlyDictionary<string, string>>();

    public void InitializeWithServices(IServiceProvider serviceProvider)
    {
        // Resolve any services you need from the DI container here
    }

    public Task InitializeAsync(IBotCore core)
    {
        // Connect to the platform and start listening
        return Task.CompletedTask;
    }

    public Task ShutdownAsync()
    {
        // Disconnect and clean up
        return Task.CompletedTask;
    }
}
```

`PlatformName` must be unique across all loaded modules and follow the `"author:platform"` convention.

---

### Command Modules

A command module exposes one or more chat commands. To create one:

**1. Create a new .NET class library project** that references `ButterBror.CommandModule`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="../libs/butterbror2/ButterBror.CommandModule/ButterBror.CommandModule.csproj" />
  </ItemGroup>

</Project>
```

**2. Implement your command:**

```csharp
using ButterBror.CommandModule.Commands;
using ButterBror.CommandModule.Context;

public class HelloCommand : ICommand
{
    public override Task<CommandResult> ExecuteAsync(
        ICommandExecutionContext context,
        ICommandServiceProvider serviceProvider)
    {
        return Task.FromResult(
            CommandResult.Successfully($"Hello, {context.User.DisplayName}!"));
    }
}
```

**3. Create a metadata for the command:**

```csharp
using ButterBror.CommandModule.Commands;
using ButterBror.CommandModule.Enums;

public class PingCommandMeta : ICommandMetadata
{
    public string Name => "hello";
    public List<string> Aliases => ["hi", "yo", "wrrr"];
    public int CooldownSeconds => 10;
    public List<string> RequiredPermissions => [];
    public string ArgumentsHelpText => "";
    public string Id => "author:bot:hello";
    public PlatformCompatibilityType PlatformCompatibilityType => PlatformCompatibilityType.Blacklist;
    public List<string> PlatformCompatibilityList => [];
}
```

**4. Implement `ICommandModule`** to register your commands:

```csharp
using ButterBror.CommandModule.CommandModule;
using ButterBror.CommandModule.Commands;

public class MyCommandModule : ICommandModule
{
    public string ModuleId => "author:bot";
    public string Version => "1.0.0";

    public IReadOnlyList<CommandModuleExport> ExportedCommands =>
    [
        new CommandModuleExport(
            CommandName: "hello",
            Factory: () => new HelloCommand(),
            Metadata: new HelloCommandMetadata()
        )
    ];

    public void InitializeWithServices(IServiceProvider serviceProvider) { }
    public Task ShutdownAsync() => Task.CompletedTask;
}
```

---

### Building a .pag File

Use the included Python build script to compile the project and package it into a `.pag` archive:

```bash
# Build a chat module (Release, installs automatically)
python3 scripts/module-compilator.py \
    --type chat \
    --csproj MyModule/MyModule.csproj

# Build a command module (Release, installs automatically)
python3 scripts/module-compilator.py \
    --type command \
    --csproj MyCommandModule/MyCommandModule.csproj

# Build without installing; save the .pag to ./dist/
python3 scripts/module-compilator.py \
    --type chat \
    --csproj MyModule/MyModule.csproj \
    --no-install \
    --output ./dist

# Debug build, skip install
python3 scripts/module-compilator.py \
    --type command \
    --csproj MyModule/MyModule.csproj \
    --config Debug \
    --no-install

# Dry-run: see what would happen without doing anything
python3 scripts/module-compilator.py \
    --type chat \
    --csproj MyModule/MyModule.csproj \
    --dry-run
```

**All available options:**

| Flag | Description |
|------|-------------|
| `--type` / `-t` | Module type: `chat` or `command` (required) |
| `--csproj` / `-p` | Path to the `.csproj` file (required) |
| `--config` / `-c` | Build configuration: `Debug` or `Release` (default: `Release`) |
| `--framework` / `-f` | Target framework (default: `net10.0`) |
| `--version` | Version written into the manifest (default: `1.0.0`) |
| `--author` | Author written into the manifest |
| `--output` / `-o` | Output directory for the `.pag` file (default: `<project_root>/dist/`) |
| `--no-install` | Skip auto-install to AppData; only produce the `.pag` file |
| `--dry-run` / `-n` | Show what would happen without executing anything |
| `--exclude` | Additional assembly prefix(es) to exclude from the archive (repeatable) |

The script performs four steps internally: `dotnet build` > collect files > generate `module.manifest.json` > pack into `.pag`. Core ButterBror assemblies are automatically excluded from the archive since they are already present in the host.

The generated `module.manifest.json` inside the archive has the following structure:

```json
{
  "mainDll": "MyModule.dll",
  "name": "MyModule",
  "version": "1.0.0",
  "description": "My custom module for ButterBror2",
  "author": "MyName"
}
```

---

### Installing a .pag File

If you used `--no-install` or want to install a pre-built package manually, copy the `.pag` file to the appropriate AppData directory:

| Module type | Windows | Linux / macOS |
|-------------|---------|---------------|
| Chat | `%APPDATA%\SillyApps\ButterBror2\Chat\` | `~/.local/share/SillyApps/ButterBror2/Chat/` |
| Command | `%APPDATA%\SillyApps\ButterBror2\Command\` | `~/.local/share/SillyApps/ButterBror2/Command/` |

After placing the file, restart the host (or trigger a hot-reload if supported) and the module will be discovered automatically

---

## 🌐 Localization

ButterBror2 has a built-in localization system. Translation files are stored in the `Localization/` subdirectory of the AppData folder, alongside an `Available.json` registry that maps locale codes to file paths and aliases

By default, the registry is bootstrapped with `EN_US` and `RU_RU`. Module authors can bundle default translations directly in their `IChatModule.DefaultTranslations` or `ICommandModule.DefaultTranslations` property - these are merged into the localization cache at load time and act as fallbacks when no translation file exists for a given key

---

## 🤝 Contributing

Contributions are welcome! To get started:

1. Fork the repository on GitHub
2. Create a new branch from `main` for your changes: `git checkout -b feat/your-feature`
3. Make your changes and add or update tests where relevant. The test suite uses **NUnit 4** and targets `net10.0`
4. Run the tests to make sure nothing is broken: `dotnet test`
5. Commit using [Conventional Commits](https://www.conventionalcommits.org/) (`feat:`, `fix:`, `docs:`, etc.)
6. Open a Pull Request against `main`

Please keep PRs focused and include a clear description of what was changed and why

---

## 📄 License

This project is licensed under the **MIT License**. See [LICENSE.txt](LICENSE.txt) for the full text

Copyright © 2026 SillyApps (aka. ItzKITb)

---

## 💬 Support

If you encounter any issues or have questions:

- Create an issue in the [GitHub repository](https://github.com/itzkitb/zapret-cli/issues)
- Twitch: [https://twitch.tv/itzkitb](https://twitch.tv/itzkitb)
- Email: [itzkitb@gmail.com](mailto:itzkitb@gmail.com)
- Donate: [My Boosty](https://boosty.to/itzkitb)

---

*SillyApps :P*
