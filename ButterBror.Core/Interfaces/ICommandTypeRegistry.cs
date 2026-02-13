using Microsoft.Extensions.DependencyInjection;

namespace ButterBror.Core.Interfaces;

public interface ICommandTypeRegistry
{
    void RegisterCommandType(string name, Type type);
    Type? GetCommandType(string name);
    bool ContainsCommand(string name);
}

public class CommandTypeRegistry : ICommandTypeRegistry
{
    private readonly Dictionary<string, Type> _commandTypes = new(StringComparer.OrdinalIgnoreCase);

    public void RegisterCommandType(string name, Type type)
    {
        _commandTypes[name] = type;
    }

    public Type? GetCommandType(string name)
    {
        return _commandTypes.TryGetValue(name, out var type) ? type : null;
    }

    public bool ContainsCommand(string name)
    {
        return _commandTypes.ContainsKey(name);
    }
}