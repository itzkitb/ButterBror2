using ButterBror.Core.Contracts;
using ButterBror.Core.Enums;
using ButterBror.Core.Interfaces;
using ButterBror.Core.Models.Commands;

namespace ButterBror.Core.Interfaces;

public interface ICommandRegistry
{
    // Unified command methods
    void RegisterCommand(string name, IUnifiedCommand command);
    bool TryGetUnifiedCommand(string name, out IUnifiedCommand command);
    IEnumerable<string> GetRegisteredCommandNames();

    // Metadata methods for validation
    ICommandMetadata? GetCommand(string name);
    bool IsCommandCompatibleWithPlatform(string commandName, string platformId);
    bool UserHasPermissionForCommand(string commandName, List<string> userPermissions);
    void RegisterCommandMetadata(ICommandMetadata metadata);
}