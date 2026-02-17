using ButterBror.Core.Contracts;
using ButterBror.Core.Enums;
using ButterBror.Core.Interfaces;
using ButterBror.Core.Models.Commands;
using Microsoft.Extensions.Logging;

namespace ButterBror.Infrastructure.Services;

public class CommandRegistry : ICommandRegistry
{
    private readonly Dictionary<string, Core.Interfaces.ICommand> _commands = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ICommandMetadata> _metadata = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<CommandRegistry> _logger;

    public CommandRegistry(ILogger<CommandRegistry> logger)
    {
        _logger = logger;
    }

    // Unified command methods
    public void RegisterCommand(string name, Core.Interfaces.ICommand command)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Command name cannot be null or empty", nameof(name));
        }

        if (command == null)
        {
            throw new ArgumentNullException(nameof(command));
        }

        _commands[name] = command;
        _logger.LogInformation("Registered unified command: {CommandName}", name);
    }

    public bool TryGetUnifiedCommand(string name, out Core.Interfaces.ICommand command)
    {
        return _commands.TryGetValue(name, out command!);
    }

    public IEnumerable<string> GetRegisteredCommandNames()
    {
        return _commands.Keys.ToList();
    }

    // Metadata methods for validation
    public ICommandMetadata? GetCommand(string name)
    {
        _metadata.TryGetValue(name, out var metadata);
        return metadata;
    }

    public bool IsCommandCompatibleWithPlatform(string commandName, string platformId)
    {
        if (!_metadata.TryGetValue(commandName, out var metadata))
        {
            return false;
        }

        _logger.LogDebug(platformId);
        _logger.LogDebug(string.Join(", ", metadata.PlatformCompatibilityList));
        _logger.LogDebug(metadata.PlatformCompatibilityType.ToString());

        // Check platform compatibility based on metadata
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
        if (!_metadata.TryGetValue(commandName, out var metadata))
        {
            return false;
        }

        // Command requires no permissions
        if (metadata.RequiredPermissions.Count == 0)
        {
            return true;
        }

        // Check if user has any of the required permissions
        return metadata.RequiredPermissions.Any(requiredPerm =>
            userPermissions.Contains(requiredPerm, StringComparer.OrdinalIgnoreCase));
    }

    public void RegisterCommandMetadata(ICommandMetadata metadata)
    {
        _metadata[metadata.Name] = metadata;

        // Also register aliases
        foreach (var alias in metadata.Aliases)
        {
            _metadata[alias] = metadata;
        }

        _logger.LogInformation("Registered command metadata: {CommandName}", metadata.Name);
    }
}
