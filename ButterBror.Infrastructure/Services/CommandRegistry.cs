using ButterBror.Core.Contracts;
using ButterBror.Core.Enums;
using ButterBror.Core.Interfaces;

namespace ButterBror.Infrastructure.Services;

public class CommandRegistry : ICommandRegistry
{
    private readonly Dictionary<string, CommandEntry> _commands = new(StringComparer.OrdinalIgnoreCase);

    private record CommandEntry(
        Func<ICommand> Factory,
        ICommandMetadata Metadata,
        string ModuleId
    );

    public void RegisterGlobalCommand(string commandName, Func<ICommand> factory, ICommandMetadata metadata)
    {
        RegisterCommand(commandName, factory, metadata, "global");
    }

    public void RegisterModuleCommand(string commandName, string moduleId, Func<ICommand> factory, ICommandMetadata metadata)
    {
        if (moduleId == "global") throw new ArgumentException("moduleId cannot be 'global'");

        RegisterCommand(commandName, factory, metadata, moduleId);
    }

    private void RegisterCommand(string commandName, Func<ICommand> factory, ICommandMetadata metadata, string moduleId)
    {
        _commands[commandName] = new CommandEntry(factory, metadata, moduleId);

        // Aliases register
        foreach (var alias in metadata.Aliases)
        {
            _commands[alias] = new CommandEntry(factory, metadata, moduleId);
        }
    }

    public Func<ICommand>? GetCommandFactory(string commandName)
    {
        return _commands.TryGetValue(commandName, out var entry) ? entry.Factory : null;
    }

    public ICommandMetadata? GetCommandMetadata(string commandName)
    {
        return _commands.TryGetValue(commandName, out var entry) ? entry.Metadata : null;
    }

    public bool ContainsCommand(string commandName)
    {
        return _commands.ContainsKey(commandName);
    }

    public string GetCommandModuleId(string commandName)
    {
        return _commands.TryGetValue(commandName, out var entry) ? entry.ModuleId : "unknown";
    }

    public IEnumerable<string> GetRegisteredCommands()
    {
        return _commands.Keys.ToList();
    }

    public bool IsCommandCompatibleWithPlatform(string commandName, string platformId)
    {
        if (!_commands.TryGetValue(commandName, out var entry))
        {
            return false;
        }

        var metadata = entry.Metadata;

        // Check platform compatibility from metadata
        switch (metadata.PlatformCompatibilityType)
        {
            case PlatformCompatibilityType.Whitelist:
                return metadata.PlatformCompatibilityList.Contains(platformId, StringComparer.OrdinalIgnoreCase);
            case PlatformCompatibilityType.Blacklist:
                return !metadata.PlatformCompatibilityList.Contains(platformId, StringComparer.OrdinalIgnoreCase);
            default:
                return false;
        }
    }

    public bool UserHasPermissionForCommand(string commandName, List<string> userPermissions)
    {
        if (!_commands.TryGetValue(commandName, out var entry))
        {
            return false;
        }

        var metadata = entry.Metadata;

        // If command requires no permissions, allow
        if (metadata.RequiredPermissions.Count == 0)
        {
            return true;
        }

        // Check if user has any of the required permissions
        return metadata.RequiredPermissions.Any(requiredPerm =>
            userPermissions.Contains(requiredPerm, StringComparer.OrdinalIgnoreCase));
    }

    // Legacy methods for backward compatibility
    public void RegisterCommand(string name, ICommand command)
    {
        // Legacy method - not used in new architecture
    }

    public bool TryGetUnifiedCommand(string name, out ICommand command)
    {
        command = null;
        return false;
    }

    public IEnumerable<string> GetRegisteredCommandNames()
    {
        return _commands.Keys.ToList();
    }

    public ICommandMetadata? GetCommand(string name)
    {
        return GetCommandMetadata(name);
    }

    public void RegisterCommandMetadata(ICommandMetadata metadata)
    {
        // For backward compatibility with CommandAutoRegistrar
        // In new architecture, metadata is registered together with command factory
    }
}
