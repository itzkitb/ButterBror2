using System.Text.Json.Serialization;
using ButterBror.Core.Interfaces;
using ButterBror.Core.Models.Commands;

namespace ButterBror.Core.Interfaces;

/// <summary>
/// Command execution context with required services
/// </summary>
public interface ICommandExecutionContext
{
    IPlatformChannel Channel { get; }
    List<string> Arguments { get; }
    IPlatformUser User { get; }
    
    [JsonIgnore]
    CancellationToken CancellationToken { get; }
}

/// <summary>
/// Service provider for command
/// </summary>
public interface ICommandServiceProvider
{
    T GetService<T>() where T : notnull;
    T? GetService<T>(string? key = null) where T : notnull;
}

/// <summary>
/// Unified command interface that receives only essential data
/// </summary>
public interface ICommand
{
    Task<CommandResult> ExecuteAsync(
        ICommandExecutionContext context,
        ICommandServiceProvider serviceProvider);
}