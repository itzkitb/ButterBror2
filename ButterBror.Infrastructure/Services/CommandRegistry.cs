using System.Text.RegularExpressions;
using ButterBror.Application.Commands;
using ButterBror.Application.Commands.Meta;
using ButterBror.Core.Interfaces;
using ButterBror.Core.Modules.Enums;
using ButterBror.Core.Modules.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ButterBror.Infrastructure.Services;

public class CommandRegistry(IServiceProvider serviceProvider, ILogger<CommandRegistry> logger)
    : ICommandRegistry
{
    private readonly Dictionary<string, CommandEntry> _commandsById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CommandEntry> _commandsByName = new(StringComparer.OrdinalIgnoreCase);
    
    private readonly List<RegexCommandEntry> _regexCommands = [];

    private record CommandEntry(
        Func<ICommand> Factory,
        ICommandMetadata Metadata,
        string ModuleId
    );

    private record RegexCommandEntry(
        Regex Pattern,
        CommandEntry Entry
    );

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            RegisterGlobalCommand(
                () => new UserInfoCommand(serviceProvider),
                new UserInfoMeta()
            );
            RegisterGlobalCommand(
                () => new BanphrasesCommand(),
                new BanphrasesCommandMeta()
            );
            RegisterGlobalCommand(
                () => new LocaleCommand(),
                new LocaleCommandMeta()
            );
            RegisterGlobalCommand(
                () => new ReloadModuleCommand(),
                new ReloadModuleMeta()
            );
            RegisterGlobalCommand(
                () => new BlockCommand(),
                new BlockCommandMeta()
            );
            return Task.CompletedTask;
        }
        catch (Exception exception)
        {
            return Task.FromException(exception);
        }
    }

    public void RegisterGlobalCommand(Func<ICommand> factory, ICommandMetadata metadata)
    {
        RegisterCommand(factory, metadata, "global");
    }

    public void RegisterModuleCommand(string moduleId, Func<ICommand> factory, ICommandMetadata metadata)
    {
        if (moduleId == "global")
            throw new ArgumentException("moduleId cannot be 'global'");
        RegisterCommand(factory, metadata, moduleId);
    }

    private void RegisterCommand(Func<ICommand> factory, ICommandMetadata metadata, string moduleId)
    {
        var entry = new CommandEntry(factory, metadata, moduleId);
        
        _commandsById[metadata.Id] = entry;
        _commandsByName[metadata.Name] = entry;
        foreach (var alias in metadata.Aliases)
        {
            _commandsByName[alias] = entry;
        }

        if (metadata.RegexAliases.Count <= 0)
            return;
        
        foreach (var regex in metadata.RegexAliases)
        {
            _regexCommands.Add(new RegexCommandEntry(regex, entry));
        }
    }

    private CommandEntry? GetCommandByNameInternal(string name)
    {
        if (_commandsByName.TryGetValue(name, out var exactMatch))
        {
            return exactMatch;
        }
        
        foreach (var regexEntry in _regexCommands.Where(regexEntry => regexEntry.Pattern.IsMatch(name)))
        {
            _commandsByName[name] = regexEntry.Entry;
            return regexEntry.Entry;
        }

        return null;
    }
    
    private CommandEntry? GetCommandByIdInternal(string id)
    {
        _commandsById.TryGetValue(id, out var entry);
        return entry;
    }
    
    private CommandEntry? ResolveEntry(string identifier, bool idIsName)
    {
        return idIsName ? GetCommandByNameInternal(identifier) : GetCommandByIdInternal(identifier);
    }

    public Func<ICommand>? GetCommandFactory(string id, bool idIsName = false) 
        => ResolveEntry(id, idIsName)?.Factory;

    public ICommandMetadata? GetCommandMetadata(string id, bool idIsName = false) 
        => ResolveEntry(id, idIsName)?.Metadata;

    public bool ContainsCommand(string id, bool idIsName = false) 
        => ResolveEntry(id, idIsName) != null;

    public string? GetCommandModuleId(string id, bool idIsName = false) 
        => ResolveEntry(id, idIsName)?.ModuleId;

    public IEnumerable<ICommandMetadata> GetRegisteredCommands()
    {
        return _commandsById.Values.Select(c => c.Metadata).Distinct();
    }

    public bool IsCommandCompatibleWithPlatform(string id, string platformId, bool idIsName = false)
    {
        var entry = ResolveEntry(id, idIsName);
        if (entry == null) return false;

        var metadata = entry.Metadata;
        return metadata.PlatformCompatibilityType switch
        {
            PlatformCompatibilityType.Whitelist => metadata.PlatformCompatibilityList.Contains(platformId,
                StringComparer.OrdinalIgnoreCase),
            PlatformCompatibilityType.Blacklist => !metadata.PlatformCompatibilityList.Contains(platformId,
                StringComparer.OrdinalIgnoreCase),
            _ => false
        };
    }

    public async Task<bool> UserHasPermissionForCommandAsync(string id, Guid unifiedUserId, bool idIsName = false)
    {
        var entry = ResolveEntry(id, idIsName);
        if (entry == null) return false;

        var metadata = entry.Metadata;
        if (metadata.RequiredPermissions.Count == 0) return true;

        using var scope = serviceProvider.CreateScope();
        var permissionManager = scope.ServiceProvider.GetRequiredService<IPermissionManager>();

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
        var removedCount = 0;

        var idsToRemove = _commandsById
            .Where(x => x.Value.ModuleId.Equals(moduleId, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Key).ToList();
            
        foreach (var key in idsToRemove)
        {
            _commandsById.Remove(key);
            removedCount++;
        }

        var namesToRemove = _commandsByName
            .Where(x => x.Value.ModuleId.Equals(moduleId, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Key).ToList();
            
        foreach (var key in namesToRemove)
        {
            _commandsByName.Remove(key);
        }
        
        _regexCommands.RemoveAll(x => x.Entry.ModuleId.Equals(moduleId, StringComparison.OrdinalIgnoreCase));

        if (removedCount > 0)
        {
            logger.LogDebug("Unregistered {Count} command(s) for module '{ModuleId}'", removedCount, moduleId);
        }
    }
}