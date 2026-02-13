using ButterBror.Core.Contracts;
using ButterBror.Core.Enums;
using ButterBror.Core.Models.Commands;
using MediatR;

namespace ButterBror.Core.Abstractions;

/// <summary>
/// Base class for commands that implements both command functionality and metadata
/// </summary>
public abstract class BaseCommand : ICommand, ICommandMetadata
{
    public abstract string Name { get; }
    public abstract List<string> Aliases { get; }
    public abstract int CooldownSeconds { get; }
    public abstract List<string> RequiredPermissions { get; }
    public abstract string ArgumentsHelpText { get; }
    public abstract string Id { get; }
    public abstract PlatformCompatibilityType PlatformCompatibilityType { get; }
    public abstract List<string> PlatformCompatibilityList { get; }
    
    /// <summary>
    /// Executes the command asynchronously
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Command result</returns>
    public abstract Task<CommandResult> ExecuteAsync(CancellationToken cancellationToken = default);
}