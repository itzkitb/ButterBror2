using ButterBror.Core.Attributes;
using ButterBror.Core.Contracts;
using ButterBror.Core.Enums;
using ButterBror.Core.Interfaces;
using ButterBror.Core.Models.Commands;
using ButterBror.Infrastructure.Wrappers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace ButterBror.Infrastructure.Registration;

public interface ICommandAutoRegistrar
{
    void RegisterAllCommands(IServiceCollection services);
}

public class CommandAutoRegistrar : ICommandAutoRegistrar
{
    private readonly ICommandRegistry _commandRegistry;
    private readonly ILogger<CommandAutoRegistrar> _logger;

    public CommandAutoRegistrar(ICommandRegistry commandRegistry, ILogger<CommandAutoRegistrar> logger)
    {
        _commandRegistry = commandRegistry;
        _logger = logger;
    }

    public void RegisterAllCommands(IServiceCollection services)
    {
        // Find all types with CommandAttribute
        var commandTypes = Assembly.GetCallingAssembly()
            .GetTypes()
            .Where(t => t.GetCustomAttribute<CommandAttribute>() != null);

        foreach (var commandType in commandTypes)
        {
            var attribute = commandType.GetCustomAttribute<CommandAttribute>();
            if (attribute != null)
            {
                var metadata = new CommandMetadataWrapper(attribute);
                _commandRegistry.RegisterCommandMetadata(metadata);

                _logger.LogInformation("Auto-registered command '{CommandName}' from type {TypeName}",
                    metadata.Name, commandType.Name);
            }
        }
    }
}
