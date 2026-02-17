using ButterBror.Core.Contracts;
using ButterBror.Core.Enums;

namespace ButterBror.Core.Interfaces;

/// <summary>
/// Реестр команд с поддержкой глобальных и модульных команд
/// </summary>
public interface IUnifiedCommandRegistry
{
    void RegisterGlobalCommand(string commandName, Func<ICommand> factory, ICommandMetadata metadata);
    void RegisterModuleCommand(string commandName, string moduleId, Func<ICommand> factory, ICommandMetadata metadata);
    Func<ICommand>? GetCommandFactory(string commandName);
    ICommandMetadata? GetCommandMetadata(string commandName);
    bool ContainsCommand(string commandName);
    string GetCommandModuleId(string commandName);
    IEnumerable<string> GetRegisteredCommands();
    bool IsCommandCompatibleWithPlatform(string commandName, string platformId);
    bool UserHasPermissionForCommand(string commandName, List<string> userPermissions);
}
