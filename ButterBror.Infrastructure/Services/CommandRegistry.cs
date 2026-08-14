using ButterBror.Core.Interfaces;
using ButterBror.Core.Modules.Enums;
using ButterBror.Core.Modules.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ButterBror.Infrastructure.Services;

public class CommandRegistry : ICommandRegistry
{
    private readonly HashSet<CommandEntry> _commands = new();
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CommandRegistry> _logger;

    private record CommandEntry(
        Func<ICommand> Factory,
        ICommandMetadata Metadata,
        string ModuleId
    );
    
    public CommandRegistry(IServiceProvider serviceProvider, ILogger<CommandRegistry> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public void RegisterGlobalCommand(Func<ICommand> factory, ICommandMetadata metadata)
    {
        RegisterCommand(factory, metadata, "global");
    }

    public void RegisterModuleCommand(string moduleId, Func<ICommand> factory, ICommandMetadata metadata)
    {
        if (moduleId == "global") throw new ArgumentException("moduleId cannot be 'global'");

        RegisterCommand(factory, metadata, moduleId);
    }

    private void RegisterCommand(Func<ICommand> factory, ICommandMetadata metadata, string moduleId)
    {
        _commands.Add(new CommandEntry(factory, metadata, moduleId));
    }

    public Func<ICommand>? GetCommandFactory(string id, bool idIsName = false)
    {
        var entry = idIsName ? GetCommandByIdInternal(id) : GetCommandByNameInternal(id);
        if (entry == null)
            return null;
        return entry.Factory;
    }

    public ICommandMetadata? GetCommandMetadata(string id, bool idIsName = false)
    {
        var entry = idIsName ? GetCommandByIdInternal(id) : GetCommandByNameInternal(id);
        if (entry == null)
            return null;
        return entry.Metadata;
    }

    public bool ContainsCommand(string id, bool idIsName = false)
    {
        var entry = idIsName ? GetCommandByIdInternal(id) : GetCommandByNameInternal(id);
        return entry != null;
    }

    public string GetCommandModuleId(string id, bool idIsName = false)
    {
        var entry = idIsName ? GetCommandByIdInternal(id) : GetCommandByNameInternal(id);
        if (entry == null)
            return null;
        return entry.ModuleId;
    }

    public IEnumerable<ICommandMetadata> GetRegisteredCommands()
    {
        var meta = _commands.Select(c => c.Metadata);
        return meta;
    }

    public bool IsCommandCompatibleWithPlatform(string id, string platformId, bool idIsName = false)
    {
        var entry = idIsName ? GetCommandByIdInternal(id) : GetCommandByNameInternal(id);
        if (entry == null)
            return false;

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

    public async Task<bool> UserHasPermissionForCommandAsync(string id, Guid unifiedUserId, bool idIsName = false)
    {
        var entry = idIsName ? GetCommandByIdInternal(id) : GetCommandByNameInternal(id);
        if (entry == null)
            return false;

        var metadata = entry.Metadata;

        // If command requires no permissions, allow
        if (metadata.RequiredPermissions.Count == 0)
        {
            return true;
        }

        // Use scoped PermissionManager for permission check
        using var scope = _serviceProvider.CreateScope();
        var permissionManager = scope.ServiceProvider.GetRequiredService<IPermissionManager>();

        // Check if user has any of the required permissions using PermissionManager
        foreach (var requiredPerm in metadata.RequiredPermissions)
        {
            if (await permissionManager.HasPermissionAsync(unifiedUserId, requiredPerm))
            {
                return true;
            }
        }

        return false;
    }

    public void UnregisterModuleCommands(string moduleId)
    {
        var keysToRemove = _commands
            .Where(entry => entry.ModuleId.Equals(moduleId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var key in keysToRemove)
        {
            _commands.Remove(key);
        }

        if (keysToRemove.Count > 0)
        {
            _logger.LogDebug("Unregistered {Count} command(s) for module '{ModuleId}'", keysToRemove.Count, moduleId);
        }
    }

    private CommandEntry? GetCommandByNameInternal(string name)
    {
        foreach (var c in _commands)
        {
            var meta = c.Metadata;
            if (meta.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                meta.Aliases.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                return c;
            }

            if (meta.RegexAliases.Count > 0)
            {
                foreach (var r in meta.RegexAliases)
                {
                    if (r.IsMatch(name))
                    {
                        return c;
                    }
                }
            }
        }

        return null;
    }
    
    private CommandEntry? GetCommandByIdInternal(string id)
    {
        foreach (var c in _commands)
        {
            var meta = c.Metadata;
            if (meta.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
            {
                return c;
            }
        }

        return null;
    }
}
