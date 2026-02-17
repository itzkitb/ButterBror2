using ButterBror.Core.Attributes;
using ButterBror.Core.Interfaces;
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
    private readonly IServiceProvider _serviceProvider;

    public CommandAutoRegistrar(ICommandRegistry commandRegistry, ILogger<CommandAutoRegistrar> logger, IServiceProvider serviceProvider)
    {
        _commandRegistry = commandRegistry;
        _logger = logger;
        _serviceProvider = serviceProvider;
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
                
                // Register command with factory
                var factory = () => (ICommand)ActivatorUtilities.CreateInstance(_serviceProvider, commandType);
                _commandRegistry.RegisterGlobalCommand(attribute.Name, factory, metadata);

                _logger.LogInformation("Auto-registered command '{CommandName}' from type {TypeName}",
                    metadata.Name, commandType.Name);
            }
        }
    }
}
