using ButterBror.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace ButterBror.Core.Services;

public class CommandFactory : ICommandFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly Dictionary<string, Type> _commandTypes = new(StringComparer.OrdinalIgnoreCase);

    public CommandFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        InitializeCommandTypes();
    }

    private void InitializeCommandTypes()
    {
        // Register command types here
        _commandTypes["userinfo"] = typeof(IUnifiedCommand); // This won't work directly
        
        // We'll register specific implementations elsewhere
    }

    public T? GetCommand<T>(string name) where T : class
    {
        // This approach won't work well with generic commands
        // So we'll use a different approach
        return null;
    }
}

public interface ICommandFactory
{
    T? GetCommand<T>(string name) where T : class;
}