using Microsoft.Extensions.DependencyInjection;

namespace ButterBror.Core.Interfaces;

public interface ICommandTypeRegistry
{
    void RegisterCommandType(string name, Type type);
    Type? GetCommandType(string name);
    bool ContainsCommand(string name);
}