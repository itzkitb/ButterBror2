using ButterBror.Core.Contracts;
using ButterBror.Core.Enums;
using ButterBror.Core.Interfaces;
using ButterBror.Core.Models.Commands;
using Microsoft.Extensions.DependencyInjection;

namespace ButterBror.Core.Registration;

public static class CommandRegistration
{
    public static IServiceCollection AddCommands(this IServiceCollection services)
    {
        // NOTE: Commands should be registered in Program.cs or respective modules
        // to avoid circular dependencies between Core and Application layers
        return services;
    }

    /// <summary>
    /// Registers all global commands in the registry
    /// </summary>
    /// <remarks>
    /// This method must be called from the host project, which has references to all modules.
    /// Command factories are created directly at the call site.
    /// </remarks>
    public static void RegisterGlobalCommand<TCommand>(
        ICommandRegistry registry,
        string commandName,
        Func<TCommand> factory,
        ICommandMetadata metadata) where TCommand : Interfaces.ICommand
    {
        registry.RegisterGlobalCommand(commandName, () => factory(), metadata);
    }
}